using System;
using System.Globalization;
using Mnemotron.Data.ClickHouse.Formats;

namespace Mnemotron.Data.ClickHouse.Types;

internal class StringType : ClickHouseType
{
    public override Type FrameworkType => typeof(string);

    public override object Read(ExtendedBinaryReader reader) => reader.ReadString();

    public override string ToString() => "String";

    public override void Write(ExtendedBinaryWriter writer, object value) => writer.Write(Convert.ToString(value, CultureInfo.InvariantCulture));
}
