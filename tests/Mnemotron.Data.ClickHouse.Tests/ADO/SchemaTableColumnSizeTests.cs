using System.Threading.Tasks;
using Mnemotron.Data.ClickHouse.ADO;
using Mnemotron.Data.ClickHouse.ADO.Readers;
using Mnemotron.Data.ClickHouse.Utility;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests.ADO;

// GetSchemaTable must report bounded sizes for string columns: sizeless
// strings are treated as LOBs by ADO.NET consumers (SSIS maps them to
// DT_NTEXT with per-cell spooling), which is disastrous for throughput.
public class SchemaTableColumnSizeTests : AbstractConnectionTestFixture
{
    [Test]
    [TestCase("'x'", 4000)]
    [TestCase("toFixedString('ab', 2)", 2)]
    [TestCase("CAST(NULL AS Nullable(String))", 4000)]
    [TestCase("toLowCardinality('v')", 4000)]
    [TestCase("toInt32(1)", -1)]
    public async Task ShouldReportBoundedColumnSize(string expression, int expectedSize)
    {
        using var reader = await connection.ExecuteReaderAsync($"SELECT {expression} AS value");
        var schema = reader.GetSchemaTable();
        Assert.That(schema.Rows[0]["ColumnSize"], Is.EqualTo(expectedSize));
    }

    [Test]
    public async Task ShouldHonorDefaultStringSizeSetting()
    {
        var builder = TestUtilities.GetConnectionStringBuilder();
        builder.DefaultStringSize = 123;
        using var cn = new ClickHouseConnection(builder.ConnectionString);
        await cn.OpenAsync();
        using var reader = await cn.ExecuteReaderAsync("SELECT 'x' AS value");
        var schema = reader.GetSchemaTable();
        Assert.That(schema.Rows[0]["ColumnSize"], Is.EqualTo(123));
    }
}
