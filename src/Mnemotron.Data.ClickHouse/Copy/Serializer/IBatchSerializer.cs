using System.IO;

namespace Mnemotron.Data.ClickHouse.Copy.Serializer;

internal interface IBatchSerializer
{
    void Serialize(Batch batch, Stream stream);
}
