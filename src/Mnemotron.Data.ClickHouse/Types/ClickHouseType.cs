using System;
using Mnemotron.Data.ClickHouse.Formats;

namespace Mnemotron.Data.ClickHouse.Types;

internal abstract class ClickHouseType
{
    public abstract Type FrameworkType { get; }

    public abstract object Read(ExtendedBinaryReader reader);

    public abstract void Write(ExtendedBinaryWriter writer, object value);

    public abstract override string ToString();

    protected static object ClearDBNull(object value) => value is DBNull ? null : value;
}
