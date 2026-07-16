using System;
using Mnemotron.Data.ClickHouse.Formats;
using Mnemotron.Data.ClickHouse.Types.Grammar;

namespace Mnemotron.Data.ClickHouse.Types;

internal class Date32Type : DateType
{
    public override string Name { get; }

    public override string ToString() => "Date32";

    public override object Read(ExtendedBinaryReader reader) => DateTimeConversions.FromUnixTimeDays(reader.ReadInt32());

    public override ParameterizedType Parse(SyntaxTreeNode typeName, Func<SyntaxTreeNode, ClickHouseType> parseClickHouseTypeFunc, TypeSettings settings) => throw new NotImplementedException();

    public override void Write(ExtendedBinaryWriter writer, object value)
    {
        writer.Write(CoerceToDateTimeOffset(value).ToUnixTimeDays());
    }
}
