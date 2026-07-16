using System;
using System.Globalization;
using Mnemotron.Data.ClickHouse.Formats;

namespace Mnemotron.Data.ClickHouse.Types;

internal class Int64Type : IntegerType
{
    public override Type FrameworkType => typeof(long);

    public override object Read(ExtendedBinaryReader reader) => reader.ReadInt64();

    public override string ToString() => "Int64";

    public override void Write(ExtendedBinaryWriter writer, object value) => writer.Write(Convert.ToInt64(value, CultureInfo.InvariantCulture));
}
