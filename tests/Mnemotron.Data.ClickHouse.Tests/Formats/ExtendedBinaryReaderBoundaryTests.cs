using System;
using System.IO;
using System.Linq;
using System.Text;
using Mnemotron.Data.ClickHouse.Formats;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests.Formats;

// The buffered reader's refill/compaction paths are invisible to the conformance
// corpus (its rows never straddle a 512K boundary): these tests force every
// boundary case with deliberately tiny buffers.
[TestFixture]
public class ExtendedBinaryReaderBoundaryTests
{
    private const int TinyBuffer = 16; // the reader's minimum

    private static ExtendedBinaryReader Make(byte[] data, int bufferSize = TinyBuffer) =>
        new(new MemoryStream(data), bufferSize);

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
            write(bw);
        return ms.ToArray();
    }

    [Test]
    public void EmptyStream_PeeksEof()
    {
        using var reader = Make([]);
        Assert.That(reader.PeekChar(), Is.EqualTo(-1));
    }

    [Test]
    public void ValuesExactlyFillingBuffer_ReadBackWithEof()
    {
        using var reader = Make(Bytes(w => { w.Write(1234567890123456789L); w.Write(-987654321098765432L); }));
        Assert.That(reader.ReadInt64(), Is.EqualTo(1234567890123456789L));
        Assert.That(reader.ReadInt64(), Is.EqualTo(-987654321098765432L));
        Assert.That(reader.PeekChar(), Is.EqualTo(-1));
    }

    [Test]
    public void ValueStraddlingRefillBoundary_IsCompactedAndRead()
    {
        // byte + 2×int64 = 17 bytes: the second int64 straddles the 16-byte buffer
        using var reader = Make(Bytes(w => { w.Write((byte)7); w.Write(long.MaxValue); w.Write(long.MinValue); }));
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.ReadInt64(), Is.EqualTo(long.MaxValue));
        Assert.That(reader.ReadInt64(), Is.EqualTo(long.MinValue));
        Assert.That(reader.PeekChar(), Is.EqualTo(-1));
    }

    [Test]
    public void AllPrimitiveKinds_AcrossManyRefills()
    {
        var data = Bytes(w =>
        {
            for (var i = 0; i < 100; i++)
            {
                w.Write((byte)i);
                w.Write((sbyte)-i);
                w.Write((short)(i * 100));
                w.Write((ushort)(i * 200));
                w.Write(i * 100_000);
                w.Write((uint)i * 200_000);
                w.Write(i * 1_000_000_000L);
                w.Write((ulong)i * 2_000_000_000);
                w.Write(i * 1.5f);
                w.Write(i * 2.5);
                w.Write(i % 2 == 0);
            }
        });
        using var reader = Make(data);
        for (var i = 0; i < 100; i++)
        {
            Assert.That(reader.ReadByte(), Is.EqualTo((byte)i));
            Assert.That(reader.ReadSByte(), Is.EqualTo((sbyte)-i));
            Assert.That(reader.ReadInt16(), Is.EqualTo((short)(i * 100)));
            Assert.That(reader.ReadUInt16(), Is.EqualTo((ushort)(i * 200)));
            Assert.That(reader.ReadInt32(), Is.EqualTo(i * 100_000));
            Assert.That(reader.ReadUInt32(), Is.EqualTo((uint)i * 200_000));
            Assert.That(reader.ReadInt64(), Is.EqualTo(i * 1_000_000_000L));
            Assert.That(reader.ReadUInt64(), Is.EqualTo((ulong)i * 2_000_000_000));
            Assert.That(reader.ReadSingle(), Is.EqualTo(i * 1.5f));
            Assert.That(reader.ReadDouble(), Is.EqualTo(i * 2.5));
            Assert.That(reader.ReadBoolean(), Is.EqualTo(i % 2 == 0));
        }
        Assert.That(reader.PeekChar(), Is.EqualTo(-1));
    }

    [Test]
    public void String_LengthPrefixJustBeforeBoundary_PayloadStraddles()
    {
        // 10 padding bytes, then a 10-char string: the length prefix sits at
        // offset 10, the payload crosses the 16-byte boundary
        var data = Bytes(w => { w.Write(new byte[10]); w.Write("abcdefghij"); });
        using var reader = Make(data);
        reader.Read(new byte[10], 0, 10);
        Assert.That(reader.ReadString(), Is.EqualTo("abcdefghij"));
        Assert.That(reader.PeekChar(), Is.EqualTo(-1));
    }

    [Test]
    public void String_LargerThanBuffer_TakesTempPath()
    {
        var s = string.Concat(Enumerable.Repeat("абвгд-12345", 40)); // multi-byte UTF-8, 440 chars >> 16 bytes
        using var reader = Make(Bytes(w => { w.Write(s); w.Write((byte)42); }));
        Assert.That(reader.ReadString(), Is.EqualTo(s));
        Assert.That(reader.ReadByte(), Is.EqualTo(42));
        Assert.That(reader.PeekChar(), Is.EqualTo(-1));
    }

    [Test]
    public void MultiByteVarint_StraddlesBoundary()
    {
        // 15 padding bytes, then varint 300 = [0xAC, 0x02] across the boundary
        var data = new byte[15].Concat(new byte[] { 0xAC, 0x02 }).ToArray();
        using var reader = Make(data);
        reader.Read(new byte[15], 0, 15);
        Assert.That(reader.Read7BitEncodedInt(), Is.EqualTo(300));
    }

    [Test]
    public void ReadBytes_LargerThanBuffer_BypassesIt()
    {
        var payload = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        using var reader = Make(payload.Concat(new byte[] { 0xFF }).ToArray());
        Assert.That(reader.ReadBytes(100), Is.EqualTo(payload));
        Assert.That(reader.ReadByte(), Is.EqualTo(0xFF));
        Assert.That(reader.PeekChar(), Is.EqualTo(-1));
    }

    [Test]
    public void TruncatedValue_ThrowsEndOfStream()
    {
        using var reader = Make([1, 2, 3]);
        Assert.Throws<EndOfStreamException>(() => reader.ReadInt32());
    }

    [Test]
    public void TruncatedReadBytes_ThrowsEndOfStream()
    {
        using var reader = Make([1, 2, 3]);
        Assert.Throws<EndOfStreamException>(() => reader.ReadBytes(50));
    }

    [Test]
    public void FixedString_StraddlingAndOversized()
    {
        var small = Encoding.UTF8.GetBytes("0123456789abcde"); // 15 bytes
        var big = Encoding.UTF8.GetBytes(new string('ж', 40)); // 80 bytes, > capacity
        using var reader = Make(small.Concat(small).Concat(big).ToArray());
        Assert.That(reader.ReadFixedString(15), Is.EqualTo("0123456789abcde"));
        Assert.That(reader.ReadFixedString(15), Is.EqualTo("0123456789abcde")); // second value straddles
        Assert.That(reader.ReadFixedString(80), Is.EqualTo(new string('ж', 40)));
        Assert.That(reader.PeekChar(), Is.EqualTo(-1));
    }

    // The reader mimics BinaryReader's 7-bit encoding, so writer output and
    // dribbling one byte at a time from the stream must both round-trip.
    [Test]
    public void OneByteAtATimeStream_StillReadsWholeValues()
    {
        var data = Bytes(w => { w.Write(0x12345678); w.Write("hello"); });
        using var reader = new ExtendedBinaryReader(new DribbleStream(data), TinyBuffer);
        Assert.That(reader.ReadInt32(), Is.EqualTo(0x12345678));
        Assert.That(reader.ReadString(), Is.EqualTo("hello"));
        Assert.That(reader.PeekChar(), Is.EqualTo(-1));
    }

    // Returns at most one byte per Read call — models a slow socket.
    private sealed class DribbleStream(byte[] data) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, 1));
    }
}
