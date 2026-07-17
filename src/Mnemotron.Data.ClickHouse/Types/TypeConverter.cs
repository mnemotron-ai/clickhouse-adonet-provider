using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.Logging;
using Mnemotron.Data.ClickHouse.Formats;
using Mnemotron.Data.ClickHouse.Numerics;
using Mnemotron.Data.ClickHouse.Types.Grammar;
using NodaTime;

[assembly: InternalsVisibleTo("Mnemotron.Data.ClickHouse.Tests, PublicKey=0024000004800000940000000602000000240000525341310004000001000100a343ada5efc2d68b36cb57b2cef4e344ff6fe0b19030959e7a78b148bd5c2553736fa14c2b863594ae939485b8b8a1bf6ce740017ef215955cc6cf6ffee7bd3b4cf6f400272ffcd74e88803edc62a4df5ee5e5086e200a7a6d263005422969d6dea53b19a5b73c376f80071f24ad9c9605336d0b9945c4b64f9f85bed22159b8")] // assembly-level tag to expose below classes to tests

namespace Mnemotron.Data.ClickHouse.Types;

internal static class TypeConverter
{
    private static readonly Dictionary<string, ClickHouseType> SimpleTypes = [];
    private static readonly Dictionary<string, ParameterizedType> ParameterizedTypes = [];
    private static readonly Dictionary<Type, ClickHouseType> ReverseMapping = [];

    // Captured by the static constructor's JSON-registration catch below; surfaced
    // (once, process-wide) via LogJsonDegradeIfNeeded the first time a connection
    // with a Logger asks for TypeSettings. Null when JSON registration succeeded.
    private static readonly Exception JsonTypesRegistrationError;
    private static int jsonDegradeLogged; // 0 = not yet logged, 1 = logged

    private static readonly Dictionary<string, string> Aliases = new()
    {
        { "BIGINT", "Int64" },
        { "BIGINT SIGNED", "Int64" },
        { "BIGINT UNSIGNED", "UInt64" },
        { "BINARY", "FixedString" },
        { "BINARY LARGE OBJECT", "String" },
        { "BINARY VARYING", "String" },
        { "BIT", "UInt64" },
        { "BLOB", "String" },
        { "BYTE", "Int8" },
        { "BYTEA", "String" },
        { "CHAR", "String" },
        { "CHAR LARGE OBJECT", "String" },
        { "CHAR VARYING", "String" },
        { "CHARACTER", "String" },
        { "CHARACTER LARGE OBJECT", "String" },
        { "CHARACTER VARYING", "String" },
        { "CLOB", "String" },
        { "DEC", "Decimal" },
        { "DOUBLE", "Float64" },
        { "DOUBLE PRECISION", "Float64" },
        { "ENUM", "Enum" },
        { "FIXED", "Decimal" },
        { "FLOAT", "Float32" },
        { "GEOMETRY", "String" },
        { "INET4", "IPv4" },
        { "INET6", "IPv6" },
        { "INT", "Int32" },
        { "INT SIGNED", "Int32" },
        { "INT UNSIGNED", "UInt32" },
        { "INT1", "Int8" },
        { "INT1 SIGNED", "Int8" },
        { "INT1 UNSIGNED", "UInt8" },
        { "INTEGER", "Int32" },
        { "INTEGER SIGNED", "Int32" },
        { "INTEGER UNSIGNED", "UInt32" },
        { "LONGBLOB", "String" },
        { "LONGTEXT", "String" },
        { "MEDIUMBLOB", "String" },
        { "MEDIUMINT", "Int32" },
        { "MEDIUMINT SIGNED", "Int32" },
        { "MEDIUMINT UNSIGNED", "UInt32" },
        { "MEDIUMTEXT", "String" },
        { "NATIONAL CHAR", "String" },
        { "NATIONAL CHAR VARYING", "String" },
        { "NATIONAL CHARACTER", "String" },
        { "NATIONAL CHARACTER LARGE OBJECT", "String" },
        { "NATIONAL CHARACTER VARYING", "String" },
        { "NCHAR", "String" },
        { "NCHAR LARGE OBJECT", "String" },
        { "NCHAR VARYING", "String" },
        { "NUMERIC", "Decimal" },
        { "NVARCHAR", "String" },
        { "REAL", "Float32" },
        { "SET", "UInt64" },
        { "SINGLE", "Float32" },
        { "SMALLINT", "Int16" },
        { "SMALLINT SIGNED", "Int16" },
        { "SMALLINT UNSIGNED", "UInt16" },
        { "TEXT", "String" },
        { "TIME", "Int64" },
        { "TIMESTAMP", "DateTime" },
        { "TINYBLOB", "String" },
        { "TINYINT", "Int8" },
        { "TINYINT SIGNED", "Int8" },
        { "TINYINT UNSIGNED", "UInt8" },
        { "TINYTEXT", "String" },
        { "VARBINARY", "String" },
        { "VARCHAR", "String" },
        { "VARCHAR2", "String" },
        { "YEAR", "UInt16" },
        { "BOOL", "Bool" },
        { "BOOLEAN", "Bool" },
        { "OBJECT('JSON')", "Json" },
        { "JSON", "Json" },
    };

    public static IEnumerable<string> RegisteredTypes => SimpleTypes.Keys
        .Concat(ParameterizedTypes.Values.Select(t => t.Name))
        .OrderBy(x => x)
        .ToArray();

    internal static readonly string[] Separator = [" "];

    static TypeConverter()
    {
        RegisterPlainType<BooleanType>();

        // Integral types
        RegisterPlainType<Int8Type>();
        RegisterPlainType<Int16Type>();
        RegisterPlainType<Int32Type>();
        RegisterPlainType<Int64Type>();
        RegisterPlainType<Int128Type>();
        RegisterPlainType<Int256Type>();

        RegisterPlainType<UInt8Type>();
        RegisterPlainType<UInt16Type>();
        RegisterPlainType<UInt32Type>();
        RegisterPlainType<UInt64Type>();
        RegisterPlainType<UInt128Type>();
        RegisterPlainType<UInt256Type>();

        // Floating point types
        RegisterPlainType<Float32Type>();
        RegisterPlainType<Float64Type>();

        // Special types
        RegisterPlainType<DynamicType>();
        RegisterPlainType<UuidType>();
        RegisterPlainType<IPv4Type>();
        RegisterPlainType<IPv6Type>();

        // String types
        RegisterPlainType<StringType>();
        RegisterParameterizedType<FixedStringType>();

        // DateTime types
        RegisterPlainType<DateType>();
        RegisterPlainType<Date32Type>();
        RegisterParameterizedType<DateTimeType>();
        RegisterParameterizedType<DateTime32Type>();
        RegisterParameterizedType<DateTime64Type>();

        // Special 'nothing' type
        RegisterPlainType<NothingType>();

        // complex types like Tuple/Array/Nested etc.
        RegisterParameterizedType<ArrayType>();
        RegisterParameterizedType<NullableType>();
        RegisterParameterizedType<TupleType>();
        RegisterParameterizedType<NestedType>();
        RegisterParameterizedType<LowCardinalityType>();

        RegisterParameterizedType<DecimalType>();
        RegisterParameterizedType<Decimal32Type>();
        RegisterParameterizedType<Decimal64Type>();
        RegisterParameterizedType<Decimal128Type>();
        RegisterParameterizedType<Decimal256Type>();

        RegisterParameterizedType<EnumType>();
        RegisterParameterizedType<Enum8Type>();
        RegisterParameterizedType<Enum16Type>();
        RegisterParameterizedType<SimpleAggregateFunctionType>();
        RegisterParameterizedType<MapType>();
        RegisterParameterizedType<VariantType>();

        // Geo types
        RegisterPlainType<PointType>();
        RegisterPlainType<RingType>();
        RegisterPlainType<PolygonType>();
        RegisterPlainType<MultiPolygonType>();

        RegisterParameterizedType<AggregateFunctionType>();

        // Mapping fixups
        ReverseMapping.Add(typeof(ClickHouseDecimal), new Decimal128Type());
        ReverseMapping.Add(typeof(decimal), new Decimal128Type());
#if NET6_0_OR_GREATER
        ReverseMapping.Add(typeof(DateOnly), new DateType());
#endif
        ReverseMapping[typeof(DateTime)] = new DateTimeType();
        ReverseMapping[typeof(DateTimeOffset)] = new DateTimeType();

        ReverseMapping[typeof(DBNull)] = new NullableType() { UnderlyingType = new NothingType() };

        // JSON support lives in a separate non-inlined method: some hosts
        // (VS/SSIS design-time processes) redirect System.Text.Json to an
        // app-local ancient version without System.Text.Json.Nodes, and a
        // direct typeof(JsonObject) here would poison the whole type
        // initializer. Without JSON support the provider still serves every
        // other ClickHouse type; a Json column then fails its own parse only.
        try
        {
            RegisterJsonTypes();
        }
        catch (Exception ex)
        {
            // Broken ambient System.Text.Json — degrade: no Json/Object('JSON') support.
            JsonTypesRegistrationError = ex;
        }
    }

    /// <summary>
    /// Emits a one-time, process-wide warning for the JSON-support degrade caught in
    /// the static constructor above. That degrade happens at type-initialization time,
    /// before any <see cref="ADO.ClickHouseConnection"/> (and its Logger) exists, so it
    /// cannot log itself; instead a connection calls this the first time it resolves
    /// <see cref="TypeSettings"/>. No-op (and never throws) when there was no degrade,
    /// it already logged, or <paramref name="logger"/> is null.
    /// </summary>
    internal static void LogJsonDegradeIfNeeded(ILogger logger)
    {
        if (JsonTypesRegistrationError is null || logger is null || jsonDegradeLogged != 0)
            return;

        if (Interlocked.CompareExchange(ref jsonDegradeLogged, 1, 0) != 0)
            return;

        try
        {
            logger.LogWarning(JsonTypesRegistrationError, "ClickHouse JSON type support is disabled for this process: the ambient System.Text.Json does not expose System.Text.Json.Nodes. Json/Object('JSON') columns will fail to parse; every other type is unaffected.");
        }
        catch
        {
            // Never let a broken logger implementation break the connection.
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterJsonTypes()
    {
        RegisterPlainType<JsonType>();
        RegisterParameterizedType<ObjectType>();
        ReverseMapping[typeof(JsonObject)] = new JsonType();
    }

    private static void RegisterPlainType<T>()
        where T : ClickHouseType, new()
    {
        var type = new T();
        var name = string.Intern(type.ToString()); // There is a limited number of types, interning them will help performance
        SimpleTypes.Add(name, type);
        if (!ReverseMapping.ContainsKey(type.FrameworkType))
        {
            ReverseMapping.Add(type.FrameworkType, type);
        }
    }

    private static void RegisterParameterizedType<T>()
        where T : ParameterizedType, new()
    {
        var t = new T();
        var name = string.Intern(t.Name); // There is a limited number of types, interning them will help performance
        ParameterizedTypes.Add(name, t);
    }

    public static ClickHouseType ParseClickHouseType(string type, TypeSettings settings)
    {
        var node = Parser.Parse(type);
        return ParseClickHouseType(node, settings);
    }

    internal static ClickHouseType ParseClickHouseType(SyntaxTreeNode node, TypeSettings settings)
    {
        var typeName = node.Value.Trim().Trim('\'');

        if (Aliases.TryGetValue(typeName.ToUpperInvariant(), out var alias))
            typeName = alias;

        if (typeName.Contains(' '))
        {
            var parts = typeName.Split(Separator, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                typeName = parts[1].Trim();
            }
            else
            {
                LogUnsupportedType(settings.logger, node.Value, "cannot split into a modifier and a base type name");
                throw new ArgumentException($"Cannot parse {node.Value} as type", nameof(node));
            }
        }

        if (node.ChildNodes.Count == 0 && SimpleTypes.TryGetValue(typeName, out var typeInfo))
        {
            return typeInfo;
        }

        if (ParameterizedTypes.TryGetValue(typeName, out var value))
        {
            return value.Parse(node, (n) => ParseClickHouseType(n, settings), settings);
        }

        LogUnsupportedType(settings.logger, node.Value, "not a registered simple or parameterized ClickHouse type");
        throw new ArgumentException("Unknown type: " + node.ToString());
    }

    // Schema/type-resolution time only (once per column, not per row): flags a
    // ClickHouse type name the provider cannot serve before the caller sees the
    // ArgumentException. Null-safe and never throws — a broken logger must not
    // break type resolution.
    private static void LogUnsupportedType(ILogger logger, string typeName, string reason)
    {
        if (logger is null)
            return;

        try
        {
            logger.LogWarning("Unsupported ClickHouse type {TypeName}: {Reason}", typeName, reason);
        }
        catch
        {
            // Never let a broken logger implementation break type resolution.
        }
    }

    /// <summary>
    /// Recursively build ClickHouse type from .NET complex type
    /// Supports nullable and arrays.
    /// </summary>
    /// <param name="type">framework type to map</param>
    /// <returns>Corresponding ClickHouse type</returns>
    public static ClickHouseType ToClickHouseType(Type type)
    {
        if (ReverseMapping.TryGetValue(type, out var value))
        {
            return value;
        }

        if (type.IsArray)
        {
            return new ArrayType() { UnderlyingType = ToClickHouseType(type.GetElementType()) };
        }

        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
        {
            return new NullableType() { UnderlyingType = ToClickHouseType(underlyingType) };
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName.StartsWith("System.Tuple", StringComparison.InvariantCulture))
        {
            return new TupleType { UnderlyingTypes = type.GetGenericArguments().Select(ToClickHouseType).ToArray() };
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName.StartsWith("System.Collections.Generic.Dictionary", StringComparison.InvariantCulture))
        {
            var types = type.GetGenericArguments().Select(ToClickHouseType).ToArray();
            return new MapType { UnderlyingTypes = Tuple.Create(types[0], types[1]) };
        }

        throw new ArgumentOutOfRangeException(nameof(type), "Unknown type: " + type.ToString());
    }

    // See https://github.com/ClickHouse/ClickHouse/blob/b618fe03bf96e64bea1a1bdec01adc1c00cd61fb/src/DataTypes/DataTypesBinaryEncoding.cpp#L48
    // https://clickhouse.com/docs/en/sql-reference/data-types/data-types-binary-encoding
    internal static ClickHouseType FromByteCode(ExtendedBinaryReader reader)
    {
        var value = reader.ReadByte();
        switch (value)
        {
            case 0x00: return new NothingType();
            case 0x01: return new UInt8Type();
            case 0x02: return new UInt16Type();
            case 0x03: return new UInt32Type();
            case 0x04: return new UInt64Type();
            case 0x05: return new UInt128Type();
            case 0x06: return new UInt256Type();
            case 0x07: return new Int8Type();
            case 0x08: return new Int16Type();
            case 0x09: return new Int32Type();
            case 0x0A: return new Int64Type();
            case 0x0B: return new Int128Type();
            case 0x0C: return new Int256Type();
            case 0x0D: return new Float32Type();
            case 0x0E: return new Float64Type();
            case 0x0F: return new DateType();
            case 0x10: return new Date32Type();
            case 0x11: return new DateTimeType();
            case 0x12: return new DateTimeType { TimeZone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(reader.ReadString()) };
            case 0x13: return new DateTime64Type() { Scale = reader.Read7BitEncodedInt() };
            case 0x14: return new DateTime64Type() { Scale = reader.Read7BitEncodedInt(), TimeZone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(reader.ReadString()) };
            case 0x15: return new StringType();
            case 0x16: return new FixedStringType() { Length = reader.Read7BitEncodedInt() };
            // case 0x17: return new Enum8Type(); // TODO values
            // case 0x18: return new Enum16Type(); // TODO values
            // case 0x19: return new Decimal32Type(); // TODO precision and scale
            // case 0x1A: return new Decimal64Type(); // TODO precision and scale
            // case 0x1B: return new Decimal128Type(); // TODO precision and scale
            // case 0x1C: return new Decimal256Type(); // TODO precision and scale
            case 0x1D: return new UuidType();
            case 0x1E: return new ArrayType() { UnderlyingType = FromByteCode(reader) };
            // case 0x1F: return new TupleType();
            // case 0x20: return new TupleType();
            // case 0x21: return new UInt64Type();
            // case 0x22: return new Int64Type();
            case 0x23: return new NullableType() { UnderlyingType = FromByteCode(reader) };
            // case 0x24: return new SimpleAggregateFunctionType(); // TODO function
            // case 0x25: return new AggregateFunctionType(); // TODO function
            case 0x26: return new LowCardinalityType() { UnderlyingType = FromByteCode(reader) };
            case 0x27: return new MapType() { UnderlyingTypes = Tuple.Create(FromByteCode(reader), FromByteCode(reader)) };
            case 0x28: return new IPv4Type();
            case 0x29: return new IPv6Type();
            // case 0x2A: return new VariantType(); // TODO nested types
            case 0x2B: return new DynamicType();
            // case 0x2C: return new RingType(); // TODO custom type
            case 0x2D: return new BooleanType();
            // case 0x2E: return new SimpleAggregateFunctionType(); // TODO function
            // case 0x2F: return new NestedType(); // TODO nested types
            case 0x30:
                var _serializationVersion = reader.ReadByte(); // <uint8_serialization_version>
                var _maxDynamicPaths = reader.Read7BitEncodedInt(); // <var_int_max_dynamic_paths>
                var _maxDynamicTypes = reader.ReadInt32(); // <uint8_max_dynamic_types>
                return new JsonType(); // TODO JSON settings
            default:
                break;
        }

        // ponytail: no LogWarning here on purpose. FromByteCode decodes a per-value
        // type tag for Dynamic/Json columns (called from DynamicType.Read /
        // JsonType.ReadJsonNode), so it runs once per cell, not once per column —
        // logging here would be exactly the per-row flood issue #22 asks us to
        // avoid. The ArgumentOutOfRangeException below still surfaces the failure
        // to the caller; see ParseClickHouseType above for the schema-time
        // (once-per-column) equivalent that does log.
        throw new ArgumentOutOfRangeException(nameof(value), $"Unknown type: {value}");
    }
}
