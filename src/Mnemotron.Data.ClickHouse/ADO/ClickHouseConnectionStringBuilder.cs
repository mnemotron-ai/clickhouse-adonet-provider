using System;
using System.Data.Common;
using System.Globalization;

namespace Mnemotron.Data.ClickHouse.ADO;

public class ClickHouseConnectionStringBuilder : DbConnectionStringBuilder
{
    public ClickHouseConnectionStringBuilder()
    {
    }

    public ClickHouseConnectionStringBuilder(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public string Database
    {
        get => GetStringOrDefault("Database", ClickHouseEnvironment.Database);
        set => this["Database"] = value;
    }

    public string Username
    {
        get => GetStringOrDefault("Username", ClickHouseEnvironment.Username);
        set => this["Username"] = value;
    }

    public string Password
    {
        get => GetStringOrDefault("Password", ClickHouseEnvironment.Password);
        set => this["Password"] = value;
    }

    /// <summary>
    /// Gets or sets the column size reported by GetSchemaTable for unbounded
    /// String columns. Bounded sizes keep ADO.NET consumers (SSIS buffers,
    /// SSAS/SSDT) on fixed-width string handling instead of per-cell LOB
    /// spooling. Default: 4000.
    /// </summary>
    public int DefaultStringSize
    {
        get => GetIntOrDefault("DefaultStringSize", TypeSettings.DefaultStringColumnSize);
        set => this["DefaultStringSize"] = value;
    }

    /// <summary>
    /// Gets or sets whether GetSchemaTable probes the actual maximum length of
    /// each String column (one aggregate scan per schema read) and reports that
    /// as the column size, instead of the flat DefaultStringSize. On by default:
    /// it keeps SSIS/SSAS row buffers tight without manual tuning. The probe is
    /// a full scan — cheap for import-sized tables, expensive for very large
    /// ones; set false (and tune DefaultStringSize) for those.
    /// </summary>
    public bool ProbeStringLengths
    {
        get => GetBooleanOrDefault("ProbeStringLengths", true);
        set => this["ProbeStringLengths"] = value;
    }

    /// <summary>
    /// Gets or sets for how long (seconds) a ProbeStringLengths result is reused
    /// for the same connection string + command text before re-scanning.
    /// SSIS/SSDT re-trigger schema reads on every package validation; the cache
    /// collapses those repeated full scans. Staleness is low-risk: probed sizes
    /// are already rounded up to the next multiple of 64 for headroom. 0 disables
    /// the cache (probe on every schema read). Default: 300.
    /// </summary>
    public int ProbeStringLengthsCacheTtl
    {
        get => GetIntOrDefault("ProbeStringLengthsCacheTtl", 300);
        set => this["ProbeStringLengthsCacheTtl"] = value;
    }

    public string Protocol
    {
        get => GetStringOrDefault("Protocol", "http");
        set => this["Protocol"] = value;
    }

    public string Host
    {
        get => GetStringOrDefault("Host", "localhost");
        set => this["Host"] = value;
    }

    public string Path
    {
        get => GetStringOrDefault("Path", null);
        set => this["Path"] = value;
    }

    public bool Compression
    {
        get => GetBooleanOrDefault("Compression", true);
        set => this["Compression"] = value;
    }

    public bool UseSession
    {
        get => GetBooleanOrDefault("UseSession", false);
        set => this["UseSession"] = value;
    }

    public string SessionId
    {
        get => GetStringOrDefault("SessionId", null);
        set => this["SessionId"] = value;
    }

    public ushort Port
    {
        get => (ushort)GetIntOrDefault("Port", Protocol == "https" ? 8443 : 8123);
        set => this["Port"] = value;
    }

    public bool UseServerTimezone
    {
        get => GetBooleanOrDefault("UseServerTimezone", true);
        set => this["UseServerTimezone"] = value;
    }

    public bool UseCustomDecimals
    {
        get => GetBooleanOrDefault("UseCustomDecimals", true);
        set => this["UseCustomDecimals"] = value;
    }

    public TimeSpan Timeout
    {
        get
        {
            return TryGetValue("Timeout", out var value) && value is string @string && double.TryParse(@string, NumberStyles.Any, CultureInfo.InvariantCulture, out var timeout)
                ? TimeSpan.FromSeconds(timeout)
                : TimeSpan.FromMinutes(2);
        }
        set => this["Timeout"] = value.TotalSeconds;
    }

    private bool GetBooleanOrDefault(string name, bool @default)
    {
        if (TryGetValue(name, out var value))
            return "true".Equals(value as string, StringComparison.OrdinalIgnoreCase);
        else
            return @default;
    }

    private string GetStringOrDefault(string name, string @default)
    {
        if (TryGetValue(name, out var value))
            return (string)value;
        else
            return @default;
    }

    private int GetIntOrDefault(string name, int @default)
    {
        if (TryGetValue(name, out object o) && o is string s && int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int @int))
            return @int;
        else
            return @default;
    }
}
