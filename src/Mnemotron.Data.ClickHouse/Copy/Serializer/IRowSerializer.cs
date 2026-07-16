using Mnemotron.Data.ClickHouse.Formats;
using Mnemotron.Data.ClickHouse.Types;

namespace Mnemotron.Data.ClickHouse.Copy.Serializer;

internal interface IRowSerializer
{
    void Serialize(object[] row, ClickHouseType[] types, ExtendedBinaryWriter writer);
}
