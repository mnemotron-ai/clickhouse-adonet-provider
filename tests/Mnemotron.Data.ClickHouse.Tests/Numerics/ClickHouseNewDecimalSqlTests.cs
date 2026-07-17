using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mnemotron.Data.ClickHouse.ADO;
using Mnemotron.Data.ClickHouse.Numerics;
using Mnemotron.Data.ClickHouse.Types;
using Mnemotron.Data.ClickHouse.Utility;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests.Numerics;

[Category("ClickHouseDecimal")]
public class ClickHouseNewDecimalSqlTests
{
    private readonly ClickHouseConnection connection;

    public ClickHouseNewDecimalSqlTests()
    {
        connection = TestUtilities.GetTestClickHouseConnection(customDecimals: true);
    }

    public static IEnumerable<string> DecimalTypes
    {
        get
        {
            yield return "Decimal32(3)";
            yield return "Decimal64(3)";
            yield return "Decimal128(3)";
            if (TestUtilities.SupportedFeatures.HasFlag(Feature.WideTypes))
            {
                yield return "Decimal256(3)";
            }
        }
    }

    public static IEnumerable<TestCaseData> DecimalTestCases
    {
        get
        {
            var values = Enumerable.Range(0, 50).Select(i => $"1{new string('0', i)}").Select(ClickHouseDecimal.Parse).ToList();

            return from typeName in DecimalTypes
                   from v in values
                   let type = (DecimalType)TypeConverter.ParseClickHouseType(typeName, TypeSettings.Default)
                   where v < type.MaxValue && v > type.MinValue
                   select new TestCaseData(v, $"SELECT CAST('{v}', '{type}')");
        }
    }

    [Test]
    [TestCaseSource(typeof(ClickHouseNewDecimalSqlTests), nameof(DecimalTypes))]
    public async Task SelectMaxValue(string typeName)
    {
        var type = (DecimalType)TypeConverter.ParseClickHouseType(typeName, TypeSettings.Default);
        using var reader = await connection.ExecuteReaderAsync($"SELECT CAST('{type.MaxValue}', '{type}')");
        reader.AssertHasFieldCount(1);
        var result = reader.GetEnsureSingleRow().Single();
        AssertDecimalResult(result, type.MaxValue, type);
    }

    [Test]
    [TestCaseSource(typeof(ClickHouseNewDecimalSqlTests), nameof(DecimalTypes))]
    public async Task SelectMinValue(string typeName)
    {
        var type = (DecimalType)TypeConverter.ParseClickHouseType(typeName, TypeSettings.Default);
        using var reader = await connection.ExecuteReaderAsync($"SELECT CAST('{type.MinValue}', '{type}')");
        reader.AssertHasFieldCount(1);
        var result = reader.GetEnsureSingleRow().Single();
        AssertDecimalResult(result, type.MinValue, type);
    }

    [Test]
    [TestCaseSource(typeof(ClickHouseNewDecimalSqlTests), nameof(DecimalTestCases))]
    public async Task Select(ClickHouseDecimal expected, string sql)
    {
        using var reader = await connection.ExecuteReaderAsync(sql);
        reader.AssertHasFieldCount(1);
        var result = reader.GetEnsureSingleRow().Single();
        var actual = result is ClickHouseDecimal chd ? chd : new ClickHouseDecimal((decimal)result);
        Assert.That(actual, Is.EqualTo(expected));
    }

    // Decimal(P<=28) now maps to System.Decimal (fits exactly, and lets ADO.NET
    // consumers treat it as numeric); ClickHouseDecimal is reserved for
    // oversized decimals (P>28) where System.Decimal would overflow.
    private static void AssertDecimalResult(object result, ClickHouseDecimal expected, DecimalType type)
    {
        if (type.Precision > 28)
        {
            ClassicAssert.IsInstanceOf<ClickHouseDecimal>(result);
            Assert.That(result, Is.EqualTo(expected));
        }
        else
        {
            ClassicAssert.IsInstanceOf<decimal>(result);
            Assert.That((decimal)result, Is.EqualTo((decimal)expected));
        }
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        connection?.Dispose();
    }
}
