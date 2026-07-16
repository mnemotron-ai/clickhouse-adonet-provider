using System.Data.Common;
using Mnemotron.Data.ClickHouse.ADO;
using Mnemotron.Data.ClickHouse.ADO.Adapters;
using Mnemotron.Data.ClickHouse.ADO.Parameters;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests.ADO;

public class ProviderFactoryTests
{
    [Test]
    public void ShouldProduceCorrectTypes()
    {
        DbProviderFactory factory = new ClickHouseConnectionFactory();
        ClassicAssert.IsInstanceOf<ClickHouseConnection>(factory.CreateConnection());
        ClassicAssert.IsInstanceOf<ClickHouseCommand>(factory.CreateCommand());
        ClassicAssert.IsInstanceOf<ClickHouseDataAdapter>(factory.CreateDataAdapter());
        ClassicAssert.IsInstanceOf<ClickHouseConnectionStringBuilder>(factory.CreateConnectionStringBuilder());
        ClassicAssert.IsInstanceOf<ClickHouseDbParameter>(factory.CreateParameter());
#if NET7_0_OR_GREATER
        ClassicAssert.IsInstanceOf<ClickHouseDataSource>(factory.CreateDataSource("Host=ignored"));
#endif

        // TODO
        // ClassicAssert.IsInstanceOf<ClickHouseConnectionStringBuilder>(factory.CreateCommandBuilder());
    }
}
