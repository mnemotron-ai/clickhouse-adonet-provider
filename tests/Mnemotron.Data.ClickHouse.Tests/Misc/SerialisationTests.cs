using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mnemotron.Data.ClickHouse.Formats;
using Mnemotron.Data.ClickHouse.Types;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests.Misc;

[TestFixture]
public class SerialisationTests
{
    public static IEnumerable<TestCaseData> TestCases => TestUtilities.GetDataTypeSamples()
        .Select(sample => new TestCaseData(sample.ExampleValue, sample.ClickHouseType)
        { TestName = $"ShouldRoundtripSerialisation({sample.ExampleExpression}, {sample.ClickHouseType})" });

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public void ShouldRoundtripSerialisation(object original, string clickHouseType)
    {
        var type = TypeConverter.ParseClickHouseType(clickHouseType, TypeSettings.Default);

        using var stream = new MemoryStream();
        using var writer = new ExtendedBinaryWriter(stream);
        using var reader = new ExtendedBinaryReader(stream);
        type.Write(writer, original);
        stream.Seek(0, SeekOrigin.Begin);
        var read = type.Read(reader);
        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(original).UsingPropertiesComparer(), "Different value read from stream");
            Assert.That(stream.Position, Is.EqualTo(stream.Length), "Read underflow");
        });
    }

    [Test]
    public void BinaryReaderShouldThrowOnOverflow()
    {
        using var stream = new MemoryStream();
        using var writer = new ExtendedBinaryWriter(stream);
        using var reader = new ExtendedBinaryReader(stream);

        writer.Write((short)1);
        stream.Seek(0, SeekOrigin.Begin);
        Assert.Throws<EndOfStreamException>(() => reader.ReadInt64());
    }
}
