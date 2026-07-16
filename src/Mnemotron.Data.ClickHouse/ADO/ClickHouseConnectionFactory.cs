using System.Data.Common;
using Mnemotron.Data.ClickHouse.ADO.Adapters;
using Mnemotron.Data.ClickHouse.ADO.Parameters;

namespace Mnemotron.Data.ClickHouse.ADO;

public class ClickHouseConnectionFactory : DbProviderFactory
{
    // Must be a public static FIELD, not a property: on .NET Framework,
    // DbProviderFactories.GetFactory resolves the machine.config registration
    // by reflecting a static field named "Instance".
    public static readonly ClickHouseConnectionFactory Instance = new();

#if NETFRAMEWORK
    static ClickHouseConnectionFactory() => Utility.NetFxAssemblyResolver.Install();
#endif

    public override DbConnection CreateConnection() => new ClickHouseConnection();

    public override DbDataAdapter CreateDataAdapter() => new ClickHouseDataAdapter();

    public override DbConnectionStringBuilder CreateConnectionStringBuilder() => new ClickHouseConnectionStringBuilder();

    public override DbParameter CreateParameter() => new ClickHouseDbParameter();

    public override DbCommand CreateCommand() => new ClickHouseCommand();

#if NET7_0_OR_GREATER
    public override DbDataSource CreateDataSource(string connectionString) => new ClickHouseDataSource(connectionString);
#endif
}
