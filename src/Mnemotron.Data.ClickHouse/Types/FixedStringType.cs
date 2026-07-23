using System;
using System.Globalization;
using System.Text;
using Mnemotron.Data.ClickHouse.Formats;
using Mnemotron.Data.ClickHouse.Types.Grammar;

namespace Mnemotron.Data.ClickHouse.Types;

internal class FixedStringType : ParameterizedType
{
    public int Length { get; set; }

    public override Type FrameworkType => typeof(string);

    public override string Name => "FixedString";

    public override ParameterizedType Parse(SyntaxTreeNode node, Func<SyntaxTreeNode, ClickHouseType> parseClickHouseTypeFunc, TypeSettings settings)
    {
        return new FixedStringType
        {
            Length = int.Parse(node.SingleChild.Value, CultureInfo.InvariantCulture),
        };
    }

    public override string ToString() => $"FixedString({Length})";

    public override object Read(ExtendedBinaryReader reader) => reader.ReadFixedString(Length);

    public override void Write(ExtendedBinaryWriter writer, object value)
    {
        var @string = Convert.ToString(value, CultureInfo.InvariantCulture);
        var stringBytes = new byte[Length];
        Encoding.UTF8.GetBytes(@string, 0, @string.Length, stringBytes, 0);
        writer.Write(stringBytes);
    }
}
