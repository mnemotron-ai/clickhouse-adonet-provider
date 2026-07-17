using System.Threading.Tasks;
using Mnemotron.Data.ClickHouse.ADO;
using Mnemotron.Data.ClickHouse.ADO.Readers;
using Mnemotron.Data.ClickHouse.Utility;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests.ADO;

// GetSchemaTable must report bounded string columns as non-LOB. ADO.NET
// consumers decide LOB handling from BOTH IsLong and ColumnSize, and IsLong
// wins: the SSIS ADO NET Source maps IsLong=true to DT_NTEXT (per-cell LOB
// spooling) regardless of size, which is disastrous for throughput. So a
// bounded string must report IsLong=false AND a bounded ColumnSize together.
public class SchemaTableColumnSizeTests : AbstractConnectionTestFixture
{
    [Test]
    [TestCase("'x'", 4000, false)]
    [TestCase("toFixedString('ab', 2)", 2, false)]
    [TestCase("CAST(NULL AS Nullable(String))", 4000, false)]
    [TestCase("toLowCardinality('v')", 4000, false)]
    [TestCase("toInt32(1)", -1, false)]
    public async Task ShouldReportBoundedStringColumns(string expression, int expectedSize, bool expectedIsLong)
    {
        using var reader = await connection.ExecuteReaderAsync($"SELECT {expression} AS value");
        var schema = reader.GetSchemaTable();
        Assert.That(schema.Rows[0]["ColumnSize"], Is.EqualTo(expectedSize));
        Assert.That(schema.Rows[0]["IsLong"], Is.EqualTo(expectedIsLong));
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
        Assert.That(schema.Rows[0]["IsLong"], Is.EqualTo(false));
    }

    // DefaultStringSize=0 (or >4000) keeps LOB semantics for genuinely huge
    // columns: IsLong=true and an unbounded size.
    [Test]
    public async Task ShouldReportLobWhenDefaultStringSizeDisabled()
    {
        var builder = TestUtilities.GetConnectionStringBuilder();
        builder.DefaultStringSize = 0;
        using var cn = new ClickHouseConnection(builder.ConnectionString);
        await cn.OpenAsync();
        using var reader = await cn.ExecuteReaderAsync("SELECT 'x' AS value");
        var schema = reader.GetSchemaTable();
        Assert.That(schema.Rows[0]["IsLong"], Is.EqualTo(true));
        Assert.That(schema.Rows[0]["ColumnSize"], Is.EqualTo(-1));
    }

    // ProbeStringLengths: GetSchemaTable reports the actual max length (rounded
    // up to a multiple of 64 for headroom) instead of the flat DefaultStringSize.
    [Test]
    public async Task ShouldProbeActualStringLength()
    {
        var builder = TestUtilities.GetConnectionStringBuilder();
        builder.ProbeStringLengths = true;
        builder.DefaultStringSize = 4000;
        using var cn = new ClickHouseConnection(builder.ConnectionString);
        await cn.OpenAsync();
        using var cmd = cn.CreateCommand();
        // longest value is 100 chars -> rounded up to 128, IsLong=false
        cmd.CommandText = "SELECT repeat('a', number) AS value FROM numbers(101)";
        using var reader = cmd.ExecuteReader(System.Data.CommandBehavior.SchemaOnly);
        var schema = reader.GetSchemaTable();
        Assert.That(schema.Rows[0]["ColumnSize"], Is.EqualTo(128));
        Assert.That(schema.Rows[0]["IsLong"], Is.EqualTo(false));
    }

    // An all-NULL / empty String probe falls back to DefaultStringSize.
    [Test]
    public async Task ShouldFallBackWhenProbeIsEmpty()
    {
        var builder = TestUtilities.GetConnectionStringBuilder();
        builder.ProbeStringLengths = true;
        builder.DefaultStringSize = 512;
        using var cn = new ClickHouseConnection(builder.ConnectionString);
        await cn.OpenAsync();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT CAST(NULL AS Nullable(String)) AS value";
        using var reader = cmd.ExecuteReader(System.Data.CommandBehavior.SchemaOnly);
        var schema = reader.GetSchemaTable();
        Assert.That(schema.Rows[0]["ColumnSize"], Is.EqualTo(512));
    }
}
