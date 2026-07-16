using System;
using Mnemotron.Data.ClickHouse.Formats;

namespace Mnemotron.Data.ClickHouse.Types;

internal class NothingType : ClickHouseType
{
    public override Type FrameworkType => typeof(DBNull);

    public override object Read(ExtendedBinaryReader reader) => DBNull.Value;

    public override string ToString() => "Nothing";

    public override void Write(ExtendedBinaryWriter writer, object value) { }
}
