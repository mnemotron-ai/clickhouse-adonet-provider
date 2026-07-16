using System;
using Mnemotron.Data.ClickHouse.Formats;

namespace Mnemotron.Data.ClickHouse.Types;

internal class BooleanType : ClickHouseType
{
    public override Type FrameworkType => typeof(bool);

    public override object Read(ExtendedBinaryReader reader) => reader.ReadBoolean();

    public override string ToString() => "Bool";

    public override void Write(ExtendedBinaryWriter writer, object value) => writer.Write((bool)value);
}
