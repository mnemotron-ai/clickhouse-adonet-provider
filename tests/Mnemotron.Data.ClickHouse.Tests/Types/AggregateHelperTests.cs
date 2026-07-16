using System.Threading.Tasks;
using Mnemotron.Data.ClickHouse.Utility;
using NUnit.Framework;
using Mnemotron.Data.ClickHouse.Types;

namespace Mnemotron.Data.ClickHouse.Tests.Types;
public class AggregateHelperTests : AbstractConnectionTestFixture
{
    [Test]
    public async Task ShouldThrowCorrectExceptionWhenSelectingAggregateFunction()
    {
        var targetTable = "test.aggregate_test";

        await connection.ExecuteStatementAsync($"TRUNCATE TABLE IF EXISTS {targetTable}");
        await connection.ExecuteStatementAsync($"CREATE TABLE IF NOT EXISTS {targetTable} (value AggregateFunction(uniq, UInt8)) ENGINE Memory");
        await connection.ExecuteStatementAsync($"INSERT INTO {targetTable} SELECT uniqState(1)");

        using var reader = await connection.ExecuteReaderAsync($"SELECT * from {targetTable}");
        Assert.Throws<AggregateFunctionType.AggregateFunctionException>(() => reader.GetEnsureSingleRow());
    }
}
