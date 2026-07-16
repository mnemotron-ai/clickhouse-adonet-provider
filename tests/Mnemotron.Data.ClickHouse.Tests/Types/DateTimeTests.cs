using System.Threading.Tasks;
using Mnemotron.Data.ClickHouse.ADO.Readers;
using Mnemotron.Data.ClickHouse.Utility;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests.Types;

public class DateTimeTests : AbstractConnectionTestFixture
{
    [Test]
    public async Task ShouldGetDateTimeOffsetFromNullable()
    {
        using var reader = (ClickHouseDataReader)await connection.ExecuteReaderAsync("SELECT toNullable(toDateTime('2033-01-01 12:34:56', 'Europe/Moscow'))");
        ClassicAssert.IsTrue(reader.Read());
        var datetime = reader.GetDateTimeOffset(0);
        ClassicAssert.IsFalse(reader.Read());
        ClassicAssert.NotNull(datetime);
    }
}
