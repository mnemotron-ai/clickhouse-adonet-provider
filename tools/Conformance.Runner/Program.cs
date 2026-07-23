// Conformance runner (see conformance/README.md).
//   golden        <corpus> <goldenDir>  — axis-1 ceremony: *.sql corpus via the oracle's raw HTTP
//   golden-schema <corpus> <goldenDir>  — axis-2 ceremony (greenfield): *.json schema cases
//                                         through the PROVIDER; the snapshot is
//                                         human-reviewed in the PR and frozen
//   replay        <corpus> <actualDir>  — corpus (*.sql + *.json) through OUR provider
//   compare       <goldenDir> <actualDir> <policy.json> — structural comparator; exit 0 = parity
//   fixture       <file.sql>            — apply the axis-2 fixture (idempotent statements,
//                                         top-level split on ';', one HTTP request each)
// Format on both sides: TSVWithNamesAndTypes (names, server types, rows);
// for schema cases — a canonicalized DataTable dump (see DumpSchemaTable).
// Oracle: CH_URL (default http://localhost:18123), CH_CONNECTION for replay.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

internal static partial class Program
{
    private static string ChUrl => Environment.GetEnvironmentVariable("CH_URL") ?? "http://localhost:18123";
    private static string ChConnection => Environment.GetEnvironmentVariable("CH_CONNECTION")
        ?? "Host=localhost;Port=18123;Protocol=http;Username=default";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1) return Usage();
        switch (args[0])
        {
            case "golden": return await Golden(args[1], args[2]);
            case "golden-schema": return GoldenSchema(args[1], args[2]);
            case "replay": return await Replay(args[1], args[2]);
            case "compare": return Compare(args[1], args[2], args[3], args.Length > 4 ? args[4] : null);
            case "fixture": return await Fixture(args[1]);
            case "bench": return await Bench(args);
            default: return Usage();
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: conformance-runner golden <corpus> <goldenDir> | golden-schema <corpus> <goldenDir> | replay <corpus> <actualDir> | compare <goldenDir> <actualDir> <policy.json> [allowlist.txt] | fixture <file.sql> | bench [provider|raw|matrix|types] [rows] [compress|nocompress]");
        return 2;
    }

    private static IEnumerable<string> Cases(string corpusDir) =>
        Directory.EnumerateFiles(corpusDir, "*.sql").OrderBy(f => f, StringComparer.Ordinal);

    // Axis-2 cases: GetSchema JSON descriptors ({"collection": ..., "restrictions": [...]}).
    private static IEnumerable<string> SchemaCases(string corpusDir) =>
        Directory.EnumerateFiles(corpusDir, "*.json").OrderBy(f => f, StringComparer.Ordinal);

    // --- fixture: idempotent axis-2 fixture database via raw HTTP ---
    // CH HTTP accepts one statement per request; a top-level split on ';' is
    // sufficient — by contract the fixture contains no ';' inside strings.
    private static async Task<int> Fixture(string file)
    {
        using var http = new HttpClient();
        var statements = File.ReadAllText(file).Split(';')
            .Select(s => s.Trim()).Where(s => s.Length > 0);
        foreach (var statement in statements)
        {
            var resp = await http.PostAsync($"{ChUrl}/", new StringContent(statement));
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"fixture FAILED: {(await resp.Content.ReadAsStringAsync()).Trim()}");
                return 1;
            }
        }
        Console.WriteLine($"fixture {Path.GetFileName(file)} applied");
        return 0;
    }

    // --- golden: snapshot ceremony against the reference server (raw HTTP, none of our code involved) ---
    private static async Task<int> Golden(string corpusDir, string goldenDir)
    {
        Directory.CreateDirectory(goldenDir);
        using var http = new HttpClient();
        foreach (var file in Cases(corpusDir))
        {
            var sql = File.ReadAllText(file);
            var resp = await http.PostAsync($"{ChUrl}/?default_format=TSVWithNamesAndTypes", new StringContent(sql));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"golden FAILED {Path.GetFileName(file)}: {body.Trim()}");
                return 1;
            }
            File.WriteAllText(Path.Combine(goldenDir, Path.GetFileNameWithoutExtension(file) + ".golden"), body);
            Console.WriteLine($"golden {Path.GetFileNameWithoutExtension(file)}");
        }
        return 0;
    }

    // --- replay: the corpus through the provider, canonical TSV dump ---
    private static async Task<int> Replay(string corpusDir, string actualDir)
    {
        if (Directory.Exists(actualDir)) Directory.Delete(actualDir, true); // determinism: wipe first
        Directory.CreateDirectory(actualDir);

#if NETFRAMEWORK
        // .NET Framework's DbProviderFactories has no programmatic
        // RegisterFactory API (registration there is machine.config-only,
        // which register-provider.ps1 exercises separately) - use the
        // factory singleton directly.
        var factory = Mnemotron.Data.ClickHouse.ADO.ClickHouseConnectionFactory.Instance;
#else
        // The System.Data.Common consumer path: register + resolve by invariant name.
        DbProviderFactories.RegisterFactory("Mnemotron.Data.ClickHouse",
            Mnemotron.Data.ClickHouse.ADO.ClickHouseConnectionFactory.Instance);
        var factory = DbProviderFactories.GetFactory("Mnemotron.Data.ClickHouse");
#endif

        var failures = 0;
        foreach (var file in Cases(corpusDir))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var outPath = Path.Combine(actualDir, name + ".golden");
            try
            {
                using var conn = factory.CreateConnection();
                conn.ConnectionString = ChConnection;
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = File.ReadAllText(file);
                using var reader = await cmd.ExecuteReaderAsync();
                var sb = new StringBuilder();
                var n = reader.FieldCount;
                sb.AppendLine(string.Join("\t", Enumerable.Range(0, n).Select(i => Escape(reader.GetName(i)))));
                sb.AppendLine(string.Join("\t", Enumerable.Range(0, n).Select(i => Escape(reader.GetDataTypeName(i)))));
                while (await reader.ReadAsync())
                    sb.AppendLine(string.Join("\t", Enumerable.Range(0, n)
                        .Select(i => Render(reader.IsDBNull(i) ? null : reader.GetValue(i)))));
                File.WriteAllText(outPath, sb.ToString());
            }
            catch (Exception e)
            {
                // A case the provider could not serve: write an error marker -
                // compare reports it red with a readable reason.
                File.WriteAllText(outPath, $"!ERROR\t{e.GetType().Name}\t{OneLine(e.Message)}\n");
                failures++;
            }
            Console.WriteLine($"replay {name}");
        }
        failures += RunSchemaCases(corpusDir, actualDir, "replay");
        if (failures > 0) Console.Error.WriteLine($"replay: {failures} case(s) errored (see !ERROR markers)");
        return 0; // compare decides what is red
    }

    // --- golden-schema: axis-2 greenfield ceremony ---
    // The ONLY sanctioned exception to "goldens come only from the oracle":
    // schema cases run through the PROVIDER (build-plan §Fixed decisions,
    // axis-2 oracle — greenfield); the result is human-reviewed in the PR
    // against the Microsoft docs (FR-5/FR-6) and frozen. Axis 1 (`golden`)
    // does not touch *.json cases; this command does not touch *.sql goldens.
    private static int GoldenSchema(string corpusDir, string goldenDir)
    {
        Directory.CreateDirectory(goldenDir);
        var failures = RunSchemaCases(corpusDir, goldenDir, "golden-schema");
        if (failures > 0)
        {
            Console.Error.WriteLine($"golden-schema: {failures} case(s) errored");
            return 1; // the ceremony must be clean — errors are not frozen
        }
        return 0;
    }

    private sealed record SchemaCase(string Collection, string[] Restrictions);

    private static SchemaCase ParseSchemaCase(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var collection = doc.RootElement.GetProperty("collection").GetString();
        string[] restrictions = null;
        if (doc.RootElement.TryGetProperty("restrictions", out var r) && r.ValueKind == JsonValueKind.Array)
            restrictions = r.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? null : e.GetString()).ToArray();
        return new SchemaCase(collection, restrictions);
    }

    // Schema cases through connection.GetSchema(collection, restrictions) → canonical dump.
    private static int RunSchemaCases(string corpusDir, string outDir, string verb)
    {
        var failures = 0;
        foreach (var file in SchemaCases(corpusDir))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var outPath = Path.Combine(outDir, name + ".golden");
            try
            {
                var schemaCase = ParseSchemaCase(file);
                using var conn = new Mnemotron.Data.ClickHouse.ADO.ClickHouseConnection(ChConnection);
                conn.Open();
                using var table = conn.GetSchema(schemaCase.Collection, schemaCase.Restrictions);
                File.WriteAllText(outPath, DumpSchemaTable(table, schemaCase.Collection));
            }
            catch (Exception e)
            {
                File.WriteAllText(outPath, $"!ERROR\t{e.GetType().Name}\t{OneLine(e.Message)}\n");
                failures++;
            }
            Console.WriteLine($"{verb} {name}");
        }
        return failures;
    }

    // DataTable dump canonicalization (the axis-2 analogue of TSVWithNamesAndTypes):
    //   line 1 — column names; line 2 — CLR type names of the DataTable columns;
    //   then value rows (Render, DBNull → \N).
    // Harness canonicalizations (documented here; policy.json is not touched):
    //   1) row order — stable sort by the first column, Ordinal comparison
    //      (the provider is deterministic anyway; the sort insures collections with
    //      no inherent order; stability preserves provider order for equal keys);
    //   2) DataSourceProductVersion/…Normalized are masked with the <version>
    //      placeholder — the value depends on the server version and is
    //      nondeterministic across environments.
    private static string DumpSchemaTable(DataTable table, string collection)
    {
        var sb = new StringBuilder();
        var columns = table.Columns.Cast<DataColumn>().ToArray();
        sb.AppendLine(string.Join("\t", columns.Select(c => Escape(c.ColumnName))));
        sb.AppendLine(string.Join("\t", columns.Select(c => Escape(c.DataType.Name))));

        var maskVersion = string.Equals(collection, "DataSourceInformation", StringComparison.OrdinalIgnoreCase);
        var rendered = table.Rows.Cast<DataRow>()
            .Select(row => columns.Select((c, i) =>
            {
                if (maskVersion && (c.ColumnName == "DataSourceProductVersion" || c.ColumnName == "DataSourceProductVersionNormalized"))
                    return "<version>";
                var v = row[i];
                return Render(v is DBNull ? null : v);
            }).ToArray());
        foreach (var cells in rendered.OrderBy(cells => cells[0], StringComparer.Ordinal))
            sb.AppendLine(string.Join("\t", cells));
        return sb.ToString();
    }

    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ¶ ");

    // Canonical rendering of a CLR value in ClickHouse TSV style (policy = policy.json + this code).
    private static string Render(object v)
    {
        switch (v)
        {
            case null: return "\\N";
            case bool b: return b ? "true" : "false";
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            case float f: return f.ToString("R", CultureInfo.InvariantCulture);
            case decimal m: return m.ToString(CultureInfo.InvariantCulture);
            case DateTime dt: return dt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.');
            case DateTimeOffset dto: return dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.');
            case string s: return Escape(s);
            case byte[] bytes: // Array(UInt8) only: FixedString/String arrive as string
                return "[" + string.Join(",", bytes) + "]";
            case BigInteger bi: return bi.ToString(CultureInfo.InvariantCulture);
            case ITuple t: return "(" + string.Join(",", Enumerable.Range(0, t.Length).Select(i => RenderNested(t[i]))) + ")";
            case IDictionary map:
                return "{" + string.Join(",", map.Keys.Cast<object>().Select(k => $"{RenderNested(k)}:{RenderNested(map[k])}")) + "}";
            case IEnumerable e and not string:
                return "[" + string.Join(",", e.Cast<object>().Select(RenderNested)) + "]";
            default: return Escape(Convert.ToString(v, CultureInfo.InvariantCulture));
        }
    }

    // Inside composites (Array/Tuple/Map) ClickHouse quotes strings with single quotes.
    private static string RenderNested(object v) =>
        v is string s ? "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'" : Render(v);

    // Escaping exactly as ClickHouse TSV: \\ \t \n \r \0 and single quote -> \'
    private static string Escape(string s) => s
        .Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r")
        .Replace("\0", "\\0").Replace("'", "\\'");

    // --- compare: structural comparator ---
    private sealed record Policy(double Eps, List<(string Prefix, string Mode)> Modes);

    private static Policy LoadPolicy(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var eps = doc.RootElement.GetProperty("floatRelativeEpsilon").GetDouble();
        var modes = doc.RootElement.GetProperty("compareModes").EnumerateArray()
            .Select(m => (m.GetProperty("typePrefix").GetString(), m.GetProperty("mode").GetString())).ToList();
        return new Policy(eps, modes);
    }

    private static string ModeFor(string chType, Policy p)
    {
        var t = chType;
        while (true) // strip wrappers: the policy applies to the inner type
        {
            // Substring, not the range operator: net48 lacks System.Index/System.Range.
            if (t.StartsWith("Nullable(")) t = t.Substring(9, t.Length - 9 - 1);
            else if (t.StartsWith("LowCardinality(")) t = t.Substring(15, t.Length - 15 - 1);
            else break;
        }
        foreach (var (prefix, mode) in p.Modes)
            if (t.StartsWith(prefix, StringComparison.Ordinal)) return mode;
        return "exact";
    }

    private static bool CellEquals(string g, string a, string mode, double eps)
    {
        if (g == a) return true;
        switch (mode)
        {
            case "float":
                if (!double.TryParse(g, NumberStyles.Float, CultureInfo.InvariantCulture, out var dg) ||
                    !double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var da)) return false;
                if (double.IsNaN(dg) && double.IsNaN(da)) return true;
                return Math.Abs(dg - da) <= eps * Math.Max(Math.Abs(dg), Math.Abs(da));
            case "datetime": return NormDt(g) == NormDt(a);
            case "decimal": return NormFrac(g) == NormFrac(a);
            case "date": return NormDate(g) == NormDate(a);
            case "bool": return NormBool(g) == NormBool(a);
            default: return false;
        }
    }

    private static string NormDt(string s)
    {
        var t = s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
        return t.EndsWith(" 00:00:00") ? t.Substring(0, t.Length - 9) : t;
    }

    private static string NormDate(string s) => s.EndsWith(" 00:00:00") ? s.Substring(0, s.Length - 9) : s;

    private static string NormFrac(string s) => s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;

    private static string NormBool(string s) => s is "1" or "true" or "True" ? "true" : s is "0" or "false" or "False" ? "false" : s;

    // Allowlist: temporarily accepted red cases, one per line
    // ("case-id — reason [owner]"). Must shrink to empty: a PASS on an
    // allowlisted case fails the gate as stale so the list cannot rot.
    private static int Compare(string goldenDir, string actualDir, string policyPath, string allowlistPath)
    {
        var policy = LoadPolicy(policyPath);
        var allow = allowlistPath != null && File.Exists(allowlistPath)
            ? File.ReadAllLines(allowlistPath).Where(l => !l.StartsWith("#") && l.Trim().Length > 0)
                .Select(l => l.Split('—', ' ')[0].Trim()).ToHashSet()
            : [];
        var red = new List<string>();
        var allowed = 0;
        var stale = new List<string>();
        var goldens = Directory.EnumerateFiles(goldenDir, "*.golden").OrderBy(f => f, StringComparer.Ordinal).ToList();
        foreach (var gf in goldens)
        {
            var name = Path.GetFileNameWithoutExtension(gf);
            var af = Path.Combine(actualDir, name + ".golden");
            var why = CompareCase(gf, af, policy);
            if (why == null)
            {
                if (allow.Contains(name)) stale.Add(name);
                else Console.WriteLine($"PASS {name}");
            }
            else if (allow.Contains(name)) { Console.WriteLine($"ALLOW {name}: {why}"); allowed++; }
            else { Console.WriteLine($"FAIL {name}: {why}"); red.Add($"{name} — {why}"); }
        }
        Console.WriteLine($"conformance: {goldens.Count - red.Count - allowed - stale.Count}/{goldens.Count} green, {allowed} allowlisted red");
        foreach (var s in stale)
            Console.WriteLine($"STALE {s}: case is green but still allowlisted — remove the line (the list must shrink)");
        if (red.Count > 0)
        {
            Console.WriteLine("--- red cases (triage → conformance/frontier.txt) ---");
            red.ForEach(Console.WriteLine);
        }
        return red.Count > 0 || stale.Count > 0 ? 1 : 0;
    }

    private static string CompareCase(string goldenFile, string actualFile, Policy p)
    {
        if (!File.Exists(actualFile)) return "no actual output";
        var g = File.ReadAllLines(goldenFile);
        var a = File.ReadAllLines(actualFile);
        if (a.Length > 0 && a[0].StartsWith("!ERROR")) return "provider error: " + a[0].Substring(7);
        if (g.Length < 2) return "malformed golden";
        if (a.Length < 2) return "malformed actual";
        if (g[0] != a[0]) return $"column names: golden [{g[0]}] vs actual [{a[0]}]";
        if (g[1] != a[1]) return $"column types: golden [{g[1]}] vs actual [{a[1]}]";
        if (g.Length != a.Length) return $"row count: golden {g.Length - 2} vs actual {a.Length - 2}";
        var types = g[1].Split('\t');
        var modes = types.Select(t => ModeFor(t, p)).ToArray();
        for (var r = 2; r < g.Length; r++)
        {
            var gc = g[r].Split('\t');
            var ac = a[r].Split('\t');
            if (gc.Length != ac.Length) return $"row {r - 1}: cell count {gc.Length} vs {ac.Length}";
            for (var c = 0; c < gc.Length; c++)
                if (!CellEquals(gc[c], ac[c], modes[c], p.Eps))
                    return $"row {r - 1}, col '{g[0].Split('\t')[c]}' ({types[c]}): golden '{gc[c]}' vs actual '{ac[c]}'";
        }
        return null;
    }
}
