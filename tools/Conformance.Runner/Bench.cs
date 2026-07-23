// Throughput bench (manual tooling; NOT part of the conformance gate).
//   bench provider [rows] [compress|nocompress] — wide-row query through the provider,
//                                                 SSIS-style Read()+GetValues(object[]) loop
//   bench raw      [rows] [compress|nocompress] — same query over raw HTTP
//                                                 (RowBinaryWithNamesAndTypes), stream drained;
//                                                 the wire-speed ceiling, none of our code runs
//   bench matrix   [rows]                       — all four combinations, one table
// Reports rows/s (plus decompressed MB/s for raw), GC gen0/1/2 deltas and allocated bytes.
// Server: CH_URL / CH_CONNECTION, same as replay.
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

internal static partial class Program
{
    // Wide row mix mirroring SSIS-relevant column types: strings (short/wide/LowCardinality),
    // Decimal64 (System.Decimal path) and Decimal128 P=38 (ClickHouseDecimal path),
    // DateTime/DateTime64, ints, Nullable, UUID.
    // Materialized ONCE into a MergeTree table: generating this row mix on the fly
    // caps the server at ~95 MB/s (CPU-bound in repeat()/toString()/UUID gen), which
    // hides the client entirely — the first bench run proved provider == raw that way.
    // Streaming a stored table pushes the bottleneck back to the client under test.
    private const string BenchSetup = @"
CREATE TABLE IF NOT EXISTS bench_fixture.wide_{N} ENGINE = MergeTree ORDER BY id AS
SELECT
    number                                        AS id,
    toString(number)                              AS str_small,
    repeat(toString(number % 1000), 20)           AS str_wide,
    toDecimal64(number % 1000000, 4)              AS dec64,
    toDecimal128(number % 1000000, 10)            AS dec128,
    toDateTime(1700000000 + number % 86400)       AS dt,
    toDateTime64(1700000000 + number % 86400, 3)  AS dt64,
    toInt32(number % 2147483647)                  AS i32,
    number                                        AS u64,
    if(number % 10 = 0, NULL, toInt32(number % 1000)) AS nullable_i32,
    toLowCardinality(toString(number % 100))      AS lc_str,
    generateUUIDv4()                              AS uuid_col
FROM numbers({N})";

    private static async Task EnsureBenchTable(long rows)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        foreach (var sql in new[]
        {
            "CREATE DATABASE IF NOT EXISTS bench_fixture",
            BenchSetup.Replace("{N}", rows.ToString()),
        })
        {
            var resp = await http.PostAsync($"{ChUrl}/", new StringContent(sql));
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"bench setup failed: {(await resp.Content.ReadAsStringAsync()).Trim()}");
        }
    }

    private static async Task<int> Bench(string[] args)
    {
        var mode = args.Length > 1 ? args[1] : "matrix";
        var rows = args.Length > 2 ? long.Parse(args[2]) : 2_000_000;
        var compress = args.Length > 3 ? args[3] != "nocompress" : true;

#if !NET8_0_OR_GREATER
        try { AppDomain.MonitoringIsEnabled = true; } catch { /* mono may not implement it */ }
#endif
        switch (mode)
        {
            case "provider":
            case "raw":
                await BenchOne(mode, rows, compress, warmup: true);
                return 0;
            case "matrix":
                foreach (var m in new[] { "raw", "provider" })
                    foreach (var c in new[] { true, false })
                        await BenchOne(m, rows, c, warmup: true);
                return 0;
            case "types":
                return await BenchTypes(rows);
            default:
                return Usage();
        }
    }

    // Per-type isolation: one single-column table per type family, provider read loop,
    // no compression. The slow types are the optimization targets; u64 is the floor.
    private static async Task<int> BenchTypes(long rows)
    {
        var families = new (string Name, string Expr)[]
        {
            ("u64", "number"),
            ("i32", "toInt32(number % 2147483647)"),
            ("str_small", "toString(number)"),
            ("str_wide", "repeat(toString(number % 1000), 20)"),
            ("fixstr16", "toFixedString(leftPad(toString(number % 100000), 16, ' '), 16)"),
            ("dec64", "toDecimal64(number % 1000000, 4)"),
            ("dec128", "toDecimal128(number % 1000000, 10)"), // P=38 → ClickHouseDecimal (BigDecimal) path
            ("dec20_4", "CAST((number % 1000000) / 7 AS Decimal(20, 4))"), // P=20 → System.Decimal 128-bit path
            ("dt", "toDateTime(1700000000 + number % 86400)"),
            ("dt64", "toDateTime64(1700000000 + number % 86400, 3)"),
            ("nullable_i32", "if(number % 10 = 0, NULL, toInt32(number % 1000))"),
            ("lc_str", "toLowCardinality(toString(number % 100))"),
            ("uuid", "generateUUIDv4()"),
            ("enum8", "CAST(number % 3 AS Enum8('a' = 0, 'b' = 1, 'c' = 2))"),
        };
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        await http.PostAsync($"{ChUrl}/", new StringContent("CREATE DATABASE IF NOT EXISTS bench_fixture"));
        foreach (var (name, expr) in families)
        {
            var ddl = $"CREATE TABLE IF NOT EXISTS bench_fixture.t_{name}_{rows} ENGINE = MergeTree ORDER BY tuple() AS SELECT {expr} AS v FROM numbers({rows})";
            var resp = await http.PostAsync($"{ChUrl}/", new StringContent(ddl));
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"bench types setup {name} FAILED: {(await resp.Content.ReadAsStringAsync()).Trim()}");
                return 1;
            }
        }
        foreach (var (name, _) in families)
        {
            var sql = $"SELECT * FROM bench_fixture.t_{name}_{rows}";
            ReadViaProvider(sql, compress: false, limit: Math.Min(rows / 10, 200_000)); // warm-up
            GC.Collect();
            var gc0 = GC.CollectionCount(0);
            var alloc0 = AllocatedBytes();
            var sw = Stopwatch.StartNew();
            ReadViaProvider(sql, compress: false, limit: 0);
            sw.Stop();
            var alloc = AllocatedBytes() - alloc0;
            Console.WriteLine(
                $"bench type {name,-13} rows={rows:N0} elapsed={sw.Elapsed.TotalSeconds:F2}s " +
                $"rows/s={rows / sw.Elapsed.TotalSeconds:N0} GC0={GC.CollectionCount(0) - gc0}" +
                (alloc > 0 ? $" alloc/row={alloc / Math.Max(rows, 1)}B" : ""));
        }
        return 0;
    }

    private static void ReadViaProvider(string sql, bool compress, long limit)
    {
        var connString = ChConnection + ";Timeout=1800" + (compress ? "" : ";Compression=false");
        using var conn = new Mnemotron.Data.ClickHouse.ADO.ClickHouseConnection(connString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql + (limit > 0 ? $" LIMIT {limit}" : "");
        using var reader = cmd.ExecuteReader();
        var vals = new object[reader.FieldCount];
        while (reader.Read())
            reader.GetValues(vals); // SSIS consumption pattern
    }

    private static async Task BenchOne(string mode, long rows, bool compress, bool warmup)
    {
        await EnsureBenchTable(rows);
        if (warmup)
            await RunOnce(mode, rows, compress, limit: Math.Min(rows / 10, 200_000)); // JIT + connection + page cache

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
        var alloc0 = AllocatedBytes();
        var sw = Stopwatch.StartNew();

        var bytes = await RunOnce(mode, rows, compress, limit: 0);

        sw.Stop();
        var alloc = AllocatedBytes() - alloc0;
        var s = sw.Elapsed.TotalSeconds;
        Console.WriteLine(
            $"bench {mode,-8} compression={(compress ? "on " : "off")} rows={rows:N0} " +
            $"elapsed={s:F2}s rows/s={rows / s:N0}" +
            (bytes > 0 ? $" MB/s={bytes / s / 1048576:F1}" : "") +
            $" GC0={GC.CollectionCount(0) - gc0} GC1={GC.CollectionCount(1) - gc1} GC2={GC.CollectionCount(2) - gc2}" +
            (alloc > 0 ? $" alloc={alloc / 1048576.0:F0}MB alloc/row={alloc / Math.Max(rows, 1)}B" : ""));
    }

    // Returns decompressed body bytes for raw mode, 0 for provider mode (stream is internal there).
    private static async Task<long> RunOnce(string mode, long rows, bool compress, long limit)
    {
        var sql = $"SELECT * FROM bench_fixture.wide_{rows}";
        if (mode == "provider")
        {
            ReadViaProvider(sql, compress, limit);
            return 0;
        }
        else
        {
            sql += limit > 0 ? $" LIMIT {limit}" : "";
            using var handler = new HttpClientHandler();
            if (compress)
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            var url = $"{ChUrl}/?default_format=RowBinaryWithNamesAndTypes&enable_http_compression={(compress ? "true" : "false")}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(sql) };
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync();
            var buf = new byte[1 << 20];
            long total = 0;
            int n;
            while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                total += n;
            return total;
        }
    }

    private static long AllocatedBytes()
    {
#if NET8_0_OR_GREATER
        return GC.GetTotalAllocatedBytes(precise: true);
#else
        try { return AppDomain.MonitoringIsEnabled ? AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize : 0; }
        catch { return 0; }
#endif
    }
}
