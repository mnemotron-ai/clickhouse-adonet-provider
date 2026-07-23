using System;
using Mnemotron.Data.ClickHouse.Formats;

namespace Mnemotron.Data.ClickHouse.Types;

internal class UuidType : ClickHouseType
{
    public override Type FrameworkType => typeof(Guid);

    public override object Read(ExtendedBinaryReader reader)
    {
        // Byte manipulation because of ClickHouse's weird GUID/UUID implementation.
        // Two register-width reads, zero arrays (the four positional reader.Read
        // calls this replaces dominated the whole wide-row bench on the net48 leg).
        // Wire w0..w15 → Guid(a: w4..w7 LE, b: w2..w3 LE, c: w0..w1 LE, d..k: w15..w8);
        // little-endian ReadUInt64 makes a = high half of the first quad, and
        // d..k = the second quad's bytes from most to least significant.
        var q1 = reader.ReadUInt64();
        var q2 = reader.ReadUInt64();
        return new Guid(
            unchecked((int)(q1 >> 32)),
            unchecked((short)(q1 >> 16)),
            unchecked((short)q1),
            (byte)(q2 >> 56), (byte)(q2 >> 48), (byte)(q2 >> 40), (byte)(q2 >> 32),
            (byte)(q2 >> 24), (byte)(q2 >> 16), (byte)(q2 >> 8), (byte)q2);
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
