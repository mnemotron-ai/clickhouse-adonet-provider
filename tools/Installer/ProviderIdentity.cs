namespace Mnemotron.Data.ClickHouse.Installer;

// Constants shared by every stage of install/uninstall. Kept byte-for-byte
// identical to the values hardcoded in deploy/register-provider.ps1 so the
// machine.config entry looks the same regardless of which tool wrote it.
internal static class ProviderIdentity
{
    public const string Invariant = "Mnemotron.Data.ClickHouse";

    public const string DisplayName = "Mnemotron ADO.NET Data Provider for ClickHouse";

    public const string Description =
        "ADO.NET data provider for ClickHouse over HTTP(S). Unofficial; not affiliated with or endorsed by ClickHouse, Inc.";

    public const string FactoryTypeName = "Mnemotron.Data.ClickHouse.ADO.ClickHouseConnectionFactory";

    public const string AssemblyFileName = "Mnemotron.Data.ClickHouse.dll";

    public const string CartridgeFileName = "clickhouse.xsl";
}
