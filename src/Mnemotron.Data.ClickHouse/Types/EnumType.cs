using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mnemotron.Data.ClickHouse.Formats;
using Mnemotron.Data.ClickHouse.Types.Grammar;

namespace Mnemotron.Data.ClickHouse.Types;

internal class EnumType : ParameterizedType
{
    private Dictionary<string, int> values = new Dictionary<string, int>();
    private Dictionary<int, string> names = new Dictionary<int, string>(); // reverse index: Lookup(int) runs once per row

    public override string Name => "Enum";

    public override Type FrameworkType => typeof(string);

    public override ParameterizedType Parse(SyntaxTreeNode node, Func<SyntaxTreeNode, ClickHouseType> parseClickHouseTypeFunc, TypeSettings settings)
    {
        var parameters = node.ChildNodes
            .Select(cn => cn.Value)
            .Select(p => p.Split('='))
            .ToDictionary(kvp => kvp[0].Trim().Trim('\''), kvp => Convert.ToInt32(kvp[1].Trim(), CultureInfo.InvariantCulture));
        var reverse = parameters.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

        switch (node.Value)
        {
            case "Enum":
            case "Enum8":
                return new Enum8Type { values = parameters, names = reverse };
            case "Enum16":
                return new Enum16Type { values = parameters, names = reverse };
            default: throw new ArgumentOutOfRangeException($"Unsupported Enum type: {node.Value}");
        }
    }

    public int Lookup(string key) => values[key];

    public string Lookup(int value) => names.TryGetValue(value, out var name) ? name : throw new KeyNotFoundException($"Enum has no value {value}");

    public override string ToString() => $"{Name}({string.Join(",", values.Select(kvp => kvp.Key + "=" + kvp.Value))}";

    public override object Read(ExtendedBinaryReader reader) => throw new NotImplementedException();

    public override void Write(ExtendedBinaryWriter writer, object value) => throw new NotImplementedException();
}
