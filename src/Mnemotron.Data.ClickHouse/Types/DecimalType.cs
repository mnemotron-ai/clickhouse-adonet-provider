using System;
using System.Globalization;
using System.Numerics;
using Mnemotron.Data.ClickHouse.Formats;
using Mnemotron.Data.ClickHouse.Numerics;
using Mnemotron.Data.ClickHouse.Types.Grammar;

namespace Mnemotron.Data.ClickHouse.Types;

internal class DecimalType : ParameterizedType
{
    private int scale;

    public virtual int Precision { get; init; }

    /// <summary>
    /// Gets or sets the decimal 'scale' (precision) in ClickHouse
    /// </summary>
    public int Scale
    {
        get => scale;
        set
        {
            scale = value;
            Exponent = BigInteger.Pow(10, value);
        }
    }

    /// <summary>
    /// Gets decimal exponent value based on Scale
    /// </summary>
    public BigInteger Exponent { get; private set; }

    public override string Name => "Decimal";

    /// <summary>
    /// Gets size of type in bytes
    /// </summary>
    public virtual int Size => GetSizeFromPrecision(Precision);

    // System.Decimal holds up to 28 digits; beyond that it overflows, so
    // Decimal(P>28) must use ClickHouseDecimal regardless of the flag. Fitting
    // decimals stay System.Decimal so ADO.NET consumers (SSIS/SSAS) recognize
    // them as numeric (DT_NUMERIC) rather than falling back to DT_NTEXT.
    // System.Decimal holds up to 28 digits exactly, so ClickHouseDecimal adds
    // nothing for Decimal(P<=28) — and using it there makes ADO.NET consumers
    // (SSIS/SSAS) fall back to DT_NTEXT instead of DT_NUMERIC. Reserve the
    // custom type for genuinely oversized decimals; UseBigDecimal (default on)
    // only takes effect where it is actually needed.
    private bool UseBigDecimalEffective => UseBigDecimal && Precision > 28;

    public override Type FrameworkType => UseBigDecimalEffective ? typeof(ClickHouseDecimal) : typeof(decimal);

    public ClickHouseDecimal MaxValue => new(BigInteger.Pow(10, Precision) - 1, Scale);

    public ClickHouseDecimal MinValue => new(1 - BigInteger.Pow(10, Precision), Scale);

    public override ParameterizedType Parse(SyntaxTreeNode node, Func<SyntaxTreeNode, ClickHouseType> parseClickHouseTypeFunc, TypeSettings settings)
    {
        var precision = int.Parse(node.ChildNodes[0].Value, CultureInfo.InvariantCulture);
        var scale = int.Parse(node.ChildNodes[1].Value, CultureInfo.InvariantCulture);

        var size = GetSizeFromPrecision(precision);

        return size switch
        {
            4 => new Decimal32Type { Precision = precision, Scale = scale, UseBigDecimal = settings.useBigDecimal },
            8 => new Decimal64Type { Precision = precision, Scale = scale, UseBigDecimal = settings.useBigDecimal },
            16 => new Decimal128Type { Precision = precision, Scale = scale, UseBigDecimal = settings.useBigDecimal },
            32 => new Decimal256Type { Precision = precision, Scale = scale, UseBigDecimal = settings.useBigDecimal },
            _ => new DecimalType { Precision = precision, Scale = scale, UseBigDecimal = settings.useBigDecimal },
        };
    }

    public override object Read(ExtendedBinaryReader reader)
    {
        if (UseBigDecimalEffective)
        {
            var mantissa = Size switch
            {
                4 => (BigInteger)reader.ReadInt32(),
                8 => (BigInteger)reader.ReadInt64(),
                _ => new BigInteger(reader.ReadBytes(Size)),
            };
            return new ClickHouseDecimal(mantissa, Scale);
        }
        else
        {
            var mantissa = Size switch
            {
                4 => reader.ReadInt32(),
                8 => reader.ReadInt64(),
                _ => (decimal)new BigInteger(reader.ReadBytes(Size)),
            };
            return mantissa / (decimal)Exponent;
        }
    }

    public override string ToString() => $"{Name}({Precision}, {Scale})";

    public override void Write(ExtendedBinaryWriter writer, object value)
    {
        try
        {
            ClickHouseDecimal @decimal = value is ClickHouseDecimal chd ? chd : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            var mantissa = ClickHouseDecimal.ScaleMantissa(@decimal, Scale);
            WriteBigInteger(writer, mantissa);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Value cannot be represented");
        }
    }

    protected virtual bool UseBigDecimal { get; init; }

    private static int GetSizeFromPrecision(int precision) => precision switch
    {
        int p when p >= 1 && p <= 9 => 4,
        int p when p >= 10 && p <= 18 => 8,
        int p when p >= 19 && p <= 38 => 16,
        int p when p >= 39 && p <= 76 => 32,
        _ => throw new ArgumentOutOfRangeException(nameof(precision)),
    };

    private void WriteBigInteger(ExtendedBinaryWriter writer, BigInteger value)
    {
        byte[] bigIntBytes = value.ToByteArray();
        byte[] decimalBytes = new byte[Size];

        if (bigIntBytes.Length > Size)
            throw new OverflowException($"Trying to write {bigIntBytes.Length} bytes, at most {Size} expected");

        bigIntBytes.CopyTo(decimalBytes, 0);

        // If a negative BigInteger is not long enough to fill the whole buffer,
        // the remainder needs to be filled with 0xFF
        if (value.Sign < 0)
        {
            for (int i = bigIntBytes.Length; i < Size; i++)
                decimalBytes[i] = 0xFF;
        }
        writer.Write(decimalBytes);
    }
}
