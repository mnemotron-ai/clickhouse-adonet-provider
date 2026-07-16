using System;
using Mnemotron.Data.ClickHouse.Types.Grammar;

namespace Mnemotron.Data.ClickHouse.Types;

internal abstract class ParameterizedType : ClickHouseType
{
    public abstract string Name { get; }

    public abstract ParameterizedType Parse(SyntaxTreeNode typeName, Func<SyntaxTreeNode, ClickHouseType> parseClickHouseTypeFunc, TypeSettings settings);
}
