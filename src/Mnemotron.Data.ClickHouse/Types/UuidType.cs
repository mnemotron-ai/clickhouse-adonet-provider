using System;
using Mnemotron.Data.ClickHouse.Formats;

namespace Mnemotron.Data.ClickHouse.Types;

internal class UuidType : ClickHouseType
{
    public override Type FrameworkType => typeof(Guid);

    public override object Read(ExtendedBinaryReader reader)
    {
        // Byte manipulation because of ClickHouse's weird GUID/UUID implementation.
        // One 16-byte read instead of four positional ones (the four separate
        // reader.Read calls dominated the whole wide-row bench on the net48 leg),
        // then assemble the Guid fields straight from wire order:
        // wire w0..w15 → Guid(a: w4..w7 LE, b: w2..w3 LE, c: w0..w1 LE, d..k: w15..w8).
        var b = reader.ReadBytes(16);
        return new Guid(
            b[4] | b[5] << 8 | b[6] << 16 | b[7] << 24,
            (short)(b[2] | b[3] << 8),
            (short)(b[0] | b[1] << 8),
            b[15], b[14], b[13], b[12], b[11], b[10], b[9], b[8]);
    }

    public override string ToString() => "UUID";

    public override void Write(ExtendedBinaryWriter writer, object value)
    {
        var guid = ExtractGuid(value);
        var bytes = guid.ToByteArray();
        Array.Reverse(bytes, 8, 8);
        writer.Write(bytes, 6, 2);
        writer.Write(bytes, 4, 2);
        writer.Write(bytes, 0, 4);
        writer.Write(bytes, 8, 8);
    }

    private static Guid ExtractGuid(object data) => data is Guid g ? g : new Guid((string)data);
}
