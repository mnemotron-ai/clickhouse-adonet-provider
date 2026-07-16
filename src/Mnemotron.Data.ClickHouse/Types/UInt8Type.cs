using System;
using System.Globalization;
using Mnemotron.Data.ClickHouse.Formats;

namespace Mnemotron.Data.ClickHouse.Types;

internal class UInt8Type : IntegerType
{
    public override Type FrameworkType => typeof(byte);

    public override object Read(ExtendedBinaryReader reader) => reader.ReadByte();

    public override string ToString() => "UInt8";

    public override void Write(ExtendedBinaryWriter writer, object value) => writer.Write(Convert.ToByte(value, CultureInfo.InvariantCulture));
}
