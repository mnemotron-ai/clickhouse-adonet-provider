using System.Data.Common;
using System.Reflection;
using Mnemotron.Data.ClickHouse.ADO;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests.ADO;

public class ProviderFactoryRegistrationTests
{
    // On .NET Framework, DbProviderFactories.GetFactory resolves the type from
    // machine.config and reads a public static FIELD named "Instance" via
    // reflection. A property (or a field returning a fresh object) breaks
    // GetFactory for every machine.config consumer, including SSAS/SSDT.
    [Test]
    public void InstanceMustBePublicStaticField()
    {
        var field = typeof(ClickHouseConnectionFactory)
            .GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        Assert.That(field, Is.Not.Null);
        Assert.That(field.IsInitOnly, Is.True);
        Assert.That(field.GetValue(null), Is.SameAs(ClickHouseConnectionFactory.Instance));
    }

    [Test]
    public void FactoryCreatesFullSurface()
    {
        DbProviderFactory factory = ClickHouseConnectionFactory.Instance;
        Assert.That(factory.CreateConnection(), Is.Not.Null);
        Assert.That(factory.CreateCommand(), Is.Not.Null);
        Assert.That(factory.CreateParameter(), Is.Not.Null);
        Assert.That(factory.CreateDataAdapter(), Is.Not.Null);
        Assert.That(factory.CreateConnectionStringBuilder(), Is.Not.Null);
    }
}
