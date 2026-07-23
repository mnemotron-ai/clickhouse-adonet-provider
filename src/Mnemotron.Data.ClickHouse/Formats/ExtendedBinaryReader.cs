using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Mnemotron.Data.ClickHouse.Formats;

/// <summary>
/// Buffered little-endian binary reader for the RowBinary hot path. Standalone
/// (deliberately NOT a BinaryReader): one internal buffer filled straight from
/// the response stream, primitives decoded in place. Replaces the previous
/// BufferedStream + PeekableStreamWrapper + BinaryReader stack, whose per-value
/// overhead (two stream calls per primitive via the peek wrapper, 128-byte
/// chunked ReadString) dominated the net48 leg of the throughput bench.
/// Method names/signatures mirror the BinaryReader surface the Types/ readers
/// already call, so call sites compile unchanged.
/// </summary>
internal class ExtendedBinaryReader : IDisposable
{
    private const int DefaultBufferSize = 512 * 1024;
    private const int MinBufferSize = 16; // must hold the largest in-place primitive (8) with headroom

    private readonly Stream stream;
    private readonly byte[] buffer;
    // ArrayPool can hand back a bigger array than requested; honor the requested
    // size so small test buffers genuinely exercise the refill boundaries.
    private readonly int capacity;
    private int pos;
    private int len;
    private bool eofSeen; // latch: once the stream reports end, never touch it again
                          // (consumers probe PeekChar past EOF, possibly after disposal —
                          // the old PeekableStreamWrapper cached its -1 the same way)
    private bool disposed;

    public ExtendedBinaryReader(Stream stream)
        : this(stream, DefaultBufferSize)
    {
    }

    public ExtendedBinaryReader(Stream stream, int bufferSize)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        capacity = Math.Max(bufferSize, MinBufferSize);
        buffer = ArrayPool<byte>.Shared.Rent(capacity);
    }

    public byte ReadByte()
    {
        Ensure(1);
        return buffer[pos++];
    }

    public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

    public bool ReadBoolean() => ReadByte() != 0;

    public short ReadInt16()
    {
        Ensure(2);
        var v = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(pos, 2));
        pos += 2;
        return v;
    }

    public ushort ReadUInt16()
    {
        Ensure(2);
        var v = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(pos, 2));
        pos += 2;
        return v;
    }

    public int ReadInt32()
    {
        Ensure(4);
        var v = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(pos, 4));
        pos += 4;
        return v;
    }

    public uint ReadUInt32()
    {
        Ensure(4);
        var v = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(pos, 4));
        pos += 4;
        return v;
    }

    public long ReadInt64()
    {
        Ensure(8);
        var v = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(pos, 8));
        pos += 8;
        return v;
    }

    public ulong ReadUInt64()
    {
        Ensure(8);
        var v = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(pos, 8));
        pos += 8;
        return v;
    }

    public float ReadSingle()
    {
        // BitConverter is host-endian; every supported .NET platform is little-endian,
        // and net48's BinaryPrimitives has no float overloads.
        Ensure(4);
        var v = BitConverter.ToSingle(buffer, pos);
        pos += 4;
        return v;
    }

    public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

    // LEB128, same encoding and 5-byte limit as BinaryReader.Read7BitEncodedInt
    // (it is also the RowBinary length/count prefix).
    public int Read7BitEncodedInt()
    {
        var result = 0;
        var shift = 0;
        byte b;
        do
        {
            if (shift == 35)
                throw new FormatException("Too many bytes in a 7-bit encoded int");
            b = ReadByte();
            result |= (b & 0x7F) << shift;
            shift += 7;
        }
        while ((b & 0x80) != 0);
        return result;
    }

    public string ReadString()
    {
        var length = Read7BitEncodedInt();
        if (length < 0)
            throw new IOException($"Invalid string length: {length}");
        return ReadFixedString(length);
    }

    /// <summary>
    /// UTF-8 decode of exactly <paramref name="length"/> raw bytes (no length
    /// prefix) — the FixedString wire format. Decodes straight from the internal
    /// buffer: one Ensure, one GetString, no intermediate copies.
    /// </summary>
    public string ReadFixedString(int length)
    {
        if (length == 0)
            return string.Empty;
        if (length <= capacity)
        {
            Ensure(length);
            var s = Encoding.UTF8.GetString(buffer, pos, length);
            pos += length;
            return s;
        }
        var tmp = new byte[length];
        Read(tmp, 0, length);
        return Encoding.UTF8.GetString(tmp);
    }

    /// <summary>
    /// Guaranteed read of exactly <paramref name="count"/> bytes
    /// (throws <see cref="EndOfStreamException"/> otherwise), like the override
    /// on the previous BinaryReader-based implementation.
    /// </summary>
    public int Read(byte[] dest, int index, int count)
    {
        var fromBuffer = Math.Min(count, len - pos);
        if (fromBuffer <= 64)
        {
            // Manual loop: Buffer.BlockCopy's fixed (icall) cost dominates tiny
            // copies on the .NET Framework leg — measured on UUID/Int128 reads
            for (var i = 0; i < fromBuffer; i++)
                dest[index + i] = buffer[pos + i];
        }
        else
        {
            Buffer.BlockCopy(buffer, pos, dest, index, fromBuffer);
        }
        pos += fromBuffer;
        index += fromBuffer;
        var remaining = count - fromBuffer;
        while (remaining > 0)
        {
            // Large residues read straight into the destination, bypassing the buffer
            var read = eofSeen ? 0 : stream.Read(dest, index, remaining);
            if (read == 0)
            {
                eofSeen = true;
                throw new EndOfStreamException($"Expected to read {count} bytes, got {count - remaining}");
            }
            index += read;
            remaining -= read;
        }
        return count;
    }

    /// <inheritdoc cref="Read(byte[], int, int)"/>
    public byte[] ReadBytes(int count)
    {
        var result = new byte[count];
        Read(result, 0, count);
        return result;
    }

    /// <summary>Returns the next byte without consuming it, or -1 at end of stream.</summary>
    public int PeekChar() => TryEnsure(1) ? buffer[pos] : -1;

    private void Ensure(int count)
    {
        if (!TryEnsure(count))
            throw new EndOfStreamException($"Expected to read {count} bytes");
    }

    private bool TryEnsure(int count)
    {
        var available = len - pos;
        if (available >= count)
            return true;
        if (pos > 0)
        {
            Buffer.BlockCopy(buffer, pos, buffer, 0, available);
            pos = 0;
            len = available;
        }
        while (len < count)
        {
            if (eofSeen)
                return false;
            var read = stream.Read(buffer, len, capacity - len);
            if (read == 0)
            {
                eofSeen = true;
                return false;
            }
            len += read;
        }
        return true;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        stream.Dispose();
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
