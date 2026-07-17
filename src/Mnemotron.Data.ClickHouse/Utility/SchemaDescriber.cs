// GetSchema support (FR-5/FR-6): the seven standard ADO.NET schema collections
// in the shapes SSDT's DSV/Import wizards expect (Microsoft "Common Schema
// Collections", DbMetaDataCollectionNames/DbMetaDataColumnNames).
// Tables/Views/Columns are served from system.tables / system.columns with
// parameterized restrictions ({name:String} binding) — no string concatenation
// of user-supplied values, ever.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using Mnemotron.Data.ClickHouse.ADO;
using Mnemotron.Data.ClickHouse.ADO.Readers;
using Mnemotron.Data.ClickHouse.Types;

namespace Mnemotron.Data.ClickHouse.Utility;

internal static class SchemaDescriber
{
    public static DataTable DescribeSchema(this ClickHouseDataReader reader)
    {
        var table = new DataTable();
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("ColumnOrdinal", typeof(int));
        table.Columns.Add("ColumnSize", typeof(int));
        table.Columns.Add("NumericPrecision", typeof(int));
        table.Columns.Add("NumericScale", typeof(int));
        table.Columns.Add("IsUnique", typeof(bool));
        table.Columns.Add("IsKey", typeof(bool));
        table.Columns.Add("DataType", typeof(Type));
        table.Columns.Add("AllowDBNull", typeof(bool));
        table.Columns.Add("ProviderType", typeof(string));
        table.Columns.Add("IsAliased", typeof(bool));
        table.Columns.Add("IsExpression", typeof(bool));
        table.Columns.Add("IsIdentity", typeof(bool));
        table.Columns.Add("IsAutoIncrement", typeof(bool));
        table.Columns.Add("IsRowVersion", typeof(bool));
        table.Columns.Add("IsHidden", typeof(bool));
        table.Columns.Add("IsLong", typeof(bool));
        table.Columns.Add("IsReadOnly", typeof(bool));

        for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            var chType = reader.GetClickHouseType(ordinal);

            // ProbeStringLengths: a measured max length overrides the flat
            // DefaultStringSize for this column (with headroom), keeping buffers
            // tight without truncating.
            var effectiveStringSize = reader.ProbedColumnSizes != null
                ? ProbedSize(reader.ProbedColumnSizes[ordinal], reader.TypeSettings.stringColumnSize)
                : reader.TypeSettings.stringColumnSize;
            var (columnSize, isLong) = GetStringSizing(chType, effectiveStringSize);
            var row = table.NewRow();
            row["ColumnName"] = reader.GetName(ordinal);
            row["ColumnOrdinal"] = ordinal;
            row["ColumnSize"] = columnSize;
            row["DataType"] = chType is NullableType nt ? nt.UnderlyingType.FrameworkType : chType.FrameworkType;
            row["ProviderType"] = chType;
            row["IsLong"] = isLong;
            row["AllowDBNull"] = chType is NullableType;
            row["IsReadOnly"] = true;
            row["IsRowVersion"] = false;
            row["IsUnique"] = false;
            row["IsKey"] = false;
            row["IsAutoIncrement"] = false;

            if (chType is DecimalType dt)
            {
                row["ColumnSize"] = dt.Size;
                row["NumericPrecision"] = dt.Precision;
                row["NumericScale"] = dt.Scale;
            }
            table.Rows.Add(row);
        }
        return table;
    }

    // ADO.NET consumers decide LOB-vs-inline string handling from BOTH IsLong
    // and ColumnSize, and IsLong wins: the SSIS ADO NET Source maps any column
    // with IsLong=true to DT_NTEXT (per-cell LOB spooling) regardless of the
    // reported size. So a bounded String must report IsLong=false AND a bounded
    // ColumnSize. FixedString(N) is always bounded; an unbounded String uses
    // the DefaultStringSize connection setting (0 or >4000 deliberately keeps
    // LOB semantics for genuinely huge columns). Nullable/LowCardinality
    // wrappers are unwrapped first.
    // Turn a probed max length into a reported width: keep headroom (round up
    // to the next multiple of 64) so a slightly longer future value does not
    // truncate. -1 (non-string / not probed) and 0 (all-NULL) fall back to the
    // flat DefaultStringSize. Values above 4000 stay unbounded (LOB) downstream.
    private static int ProbedSize(int probedMax, int defaultStringSize)
    {
        if (probedMax <= 0)
            return defaultStringSize;
        if (probedMax > 4000)
            return probedMax;
        return ((probedMax + 63) / 64) * 64;
    }

    private static (int ColumnSize, bool IsLong) GetStringSizing(ClickHouseType chType, int defaultStringSize)
    {
        while (true)
        {
            switch (chType)
            {
                case NullableType nt:
                    chType = nt.UnderlyingType;
                    continue;
                case LowCardinalityType lc:
                    chType = lc.UnderlyingType;
                    continue;
                case FixedStringType fs:
                    return (fs.Length, false);
                default:
                    if (chType.FrameworkType != typeof(string))
                        return (-1, false);
                    return defaultStringSize > 0 && defaultStringSize <= 4000
                        ? (defaultStringSize, false)
                        : (-1, true);
            }
        }
    }

    public static DataTable DescribeSchema(this ClickHouseConnection connection, string collectionName, string[] restrictions)
    {
        // ADO.NET convention: no collection name = MetaDataCollections; names are case-insensitive.
        collectionName ??= DbMetaDataCollectionNames.MetaDataCollections;

        if (Is(collectionName, DbMetaDataCollectionNames.MetaDataCollections))
            return DescribeMetaDataCollections();
        if (Is(collectionName, DbMetaDataCollectionNames.DataSourceInformation))
            return DescribeDataSourceInformation(connection);
        if (Is(collectionName, DbMetaDataCollectionNames.DataTypes))
            return DescribeDataTypes(connection);
        if (Is(collectionName, DbMetaDataCollectionNames.Restrictions))
            return DescribeRestrictions();
        if (Is(collectionName, "Tables"))
            return DescribeTables(connection, restrictions);
        if (Is(collectionName, "Views"))
            return DescribeViews(connection, restrictions);
        if (Is(collectionName, "Columns"))
            return DescribeColumns(connection, restrictions);

        throw new ArgumentException($"The requested collection ({collectionName}) is not defined.", nameof(collectionName));
    }

    private static bool Is(string requested, string collection) => string.Equals(requested, collection, StringComparison.OrdinalIgnoreCase);

    // --- MetaDataCollections ---

    private static DataTable DescribeMetaDataCollections()
    {
        var table = new DataTable(DbMetaDataCollectionNames.MetaDataCollections);
        table.Columns.Add(DbMetaDataColumnNames.CollectionName, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.NumberOfRestrictions, typeof(int));
        table.Columns.Add(DbMetaDataColumnNames.NumberOfIdentifierParts, typeof(int));

        table.Rows.Add(DbMetaDataCollectionNames.MetaDataCollections, 0, 0);
        table.Rows.Add(DbMetaDataCollectionNames.DataSourceInformation, 0, 0);
        table.Rows.Add(DbMetaDataCollectionNames.DataTypes, 0, 0);
        table.Rows.Add(DbMetaDataCollectionNames.Restrictions, 0, 0);
        table.Rows.Add("Tables", 2, 2);
        table.Rows.Add("Views", 2, 2);
        table.Rows.Add("Columns", 3, 3);
        return table;
    }

    // --- DataSourceInformation (FR-6) ---
    // ClickHouse identifier quoting: "ident" and `ident` are accepted, [ident] is NOT.
    // String literals use single quotes only. Parameters bind as {name:Type}
    // (ClickHouse HTTP native protocol form, see ClickHouseDbParameter.QueryForm).

    private static DataTable DescribeDataSourceInformation(ClickHouseConnection connection)
    {
        connection.EnsureOpenAsync().ConfigureAwait(false).GetAwaiter().GetResult(); // ServerVersion requires an open connection

        var table = new DataTable(DbMetaDataCollectionNames.DataSourceInformation);
        table.Columns.Add(DbMetaDataColumnNames.CompositeIdentifierSeparatorPattern, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.DataSourceProductName, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.DataSourceProductVersion, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.DataSourceProductVersionNormalized, typeof(string));
        // Enum-valued columns (GroupByBehavior/IdentifierCase/SupportedJoinOperators)
        // are typed as int: DataTable coerces enum values to their underlying type
        // on assignment anyway, and consumers (SSDT) read them by casting from int.
        table.Columns.Add(DbMetaDataColumnNames.GroupByBehavior, typeof(int));
        table.Columns.Add(DbMetaDataColumnNames.IdentifierCase, typeof(int));
        table.Columns.Add(DbMetaDataColumnNames.IdentifierPattern, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.OrderByColumnsInSelect, typeof(bool));
        table.Columns.Add(DbMetaDataColumnNames.ParameterMarkerFormat, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.ParameterMarkerPattern, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.ParameterNameMaxLength, typeof(int));
        table.Columns.Add(DbMetaDataColumnNames.ParameterNamePattern, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.QuotedIdentifierCase, typeof(int));
        table.Columns.Add(DbMetaDataColumnNames.QuotedIdentifierPattern, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.StatementSeparatorPattern, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.StringLiteralPattern, typeof(string));
        table.Columns.Add(DbMetaDataColumnNames.SupportedJoinOperators, typeof(int));

        var version = connection.ServerVersion;
        var row = table.NewRow();
        row[DbMetaDataColumnNames.CompositeIdentifierSeparatorPattern] = @"\.";
        row[DbMetaDataColumnNames.DataSourceProductName] = "ClickHouse";
        row[DbMetaDataColumnNames.DataSourceProductVersion] = version;
        row[DbMetaDataColumnNames.DataSourceProductVersionNormalized] = version;
        // Every non-aggregated SELECT column must appear in GROUP BY; extra grouping keys are allowed.
        row[DbMetaDataColumnNames.GroupByBehavior] = (int)GroupByBehavior.MustContainAll;
        row[DbMetaDataColumnNames.IdentifierCase] = (int)IdentifierCase.Sensitive;
        row[DbMetaDataColumnNames.IdentifierPattern] = "[A-Za-z_][A-Za-z0-9_]*";
        row[DbMetaDataColumnNames.OrderByColumnsInSelect] = false;
        // The real marker is {name:Type}; a name-only format string cannot carry the
        // mandatory type part, so the format renders the braces and the consumer is
        // expected to append ":Type" (see ParameterMarkerPattern for the full form).
        row[DbMetaDataColumnNames.ParameterMarkerFormat] = "{{{0}}}";
        row[DbMetaDataColumnNames.ParameterMarkerPattern] = @"\{[A-Za-z_][A-Za-z0-9_]*:[^}]+\}";
        row[DbMetaDataColumnNames.ParameterNameMaxLength] = 128; // ClickHouse publishes no hard limit; conservative value
        row[DbMetaDataColumnNames.ParameterNamePattern] = "^[A-Za-z_][A-Za-z0-9_]*$";
        row[DbMetaDataColumnNames.QuotedIdentifierCase] = (int)IdentifierCase.Sensitive;
        row[DbMetaDataColumnNames.QuotedIdentifierPattern] = "\"(([^\"]|\"\")*)\""; // "ident", NOT [ident]
        row[DbMetaDataColumnNames.StatementSeparatorPattern] = ";";
        row[DbMetaDataColumnNames.StringLiteralPattern] = "'(([^']|'')*)'"; // single quotes only
        row[DbMetaDataColumnNames.SupportedJoinOperators] =
            (int)(SupportedJoinOperators.Inner | SupportedJoinOperators.LeftOuter |
                  SupportedJoinOperators.RightOuter | SupportedJoinOperators.FullOuter);
        table.Rows.Add(row);
        return table;
    }

    // --- DataTypes ---

    private sealed class DataTypeEntry
    {
        public string TypeName;
        public string ConcreteExample;   // parseable form used to resolve the CLR type; null = CLR type depends on type argument
        public string CreateFormat;
        public string CreateParameters;
        public long ColumnSize = -1;     // fixed byte width; -1 = not applicable / unbounded
        public bool IsBestMatch;
        public bool IsFixedLength = true;
        public bool IsLong;
        public bool IsNullable;          // ClickHouse base types are non-nullable unless wrapped in Nullable(T)
        public bool IsUnsigned;
        public short MinimumScale = -1;
        public short MaximumScale = -1;
        public string LiteralPrefix;
        public string LiteralSuffix;
    }

    // FR-7 type matrix as surfaced by TypeConverter. Wrapper/composite types
    // (Nullable, LowCardinality, Array, Tuple, Map) have no fixed CLR type —
    // it depends on the type argument — so their DataType cell is null.
    private static readonly DataTypeEntry[] DataTypeEntries =
    [
        new() { TypeName = "Bool", ConcreteExample = "Bool", ColumnSize = 1, IsBestMatch = true },
        new() { TypeName = "Int8", ConcreteExample = "Int8", ColumnSize = 1, IsBestMatch = true },
        new() { TypeName = "Int16", ConcreteExample = "Int16", ColumnSize = 2, IsBestMatch = true },
        new() { TypeName = "Int32", ConcreteExample = "Int32", ColumnSize = 4, IsBestMatch = true },
        new() { TypeName = "Int64", ConcreteExample = "Int64", ColumnSize = 8, IsBestMatch = true },
        new() { TypeName = "Int128", ConcreteExample = "Int128", ColumnSize = 16, IsBestMatch = true },
        new() { TypeName = "Int256", ConcreteExample = "Int256", ColumnSize = 32 },
        new() { TypeName = "UInt8", ConcreteExample = "UInt8", ColumnSize = 1, IsUnsigned = true, IsBestMatch = true },
        new() { TypeName = "UInt16", ConcreteExample = "UInt16", ColumnSize = 2, IsUnsigned = true, IsBestMatch = true },
        new() { TypeName = "UInt32", ConcreteExample = "UInt32", ColumnSize = 4, IsUnsigned = true, IsBestMatch = true },
        new() { TypeName = "UInt64", ConcreteExample = "UInt64", ColumnSize = 8, IsUnsigned = true, IsBestMatch = true },
        new() { TypeName = "UInt128", ConcreteExample = "UInt128", ColumnSize = 16, IsUnsigned = true },
        new() { TypeName = "UInt256", ConcreteExample = "UInt256", ColumnSize = 32, IsUnsigned = true },
        new() { TypeName = "Float32", ConcreteExample = "Float32", ColumnSize = 4, IsBestMatch = true },
        new() { TypeName = "Float64", ConcreteExample = "Float64", ColumnSize = 8, IsBestMatch = true },
        new() { TypeName = "Decimal", ConcreteExample = "Decimal(38, 10)", CreateFormat = "Decimal({0}, {1})", CreateParameters = "precision,scale", MinimumScale = 0, MaximumScale = 76 },
        new() { TypeName = "Decimal32", ConcreteExample = "Decimal32(4)", CreateFormat = "Decimal32({0})", CreateParameters = "scale", ColumnSize = 4, MinimumScale = 0, MaximumScale = 9 },
        new() { TypeName = "Decimal64", ConcreteExample = "Decimal64(6)", CreateFormat = "Decimal64({0})", CreateParameters = "scale", ColumnSize = 8, MinimumScale = 0, MaximumScale = 18 },
        new() { TypeName = "Decimal128", ConcreteExample = "Decimal128(10)", CreateFormat = "Decimal128({0})", CreateParameters = "scale", ColumnSize = 16, MinimumScale = 0, MaximumScale = 38, IsBestMatch = true },
        new() { TypeName = "Decimal256", ConcreteExample = "Decimal256(10)", CreateFormat = "Decimal256({0})", CreateParameters = "scale", ColumnSize = 32, MinimumScale = 0, MaximumScale = 76 },
        new() { TypeName = "String", ConcreteExample = "String", IsFixedLength = false, IsLong = true, IsBestMatch = true, LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "FixedString", ConcreteExample = "FixedString(16)", CreateFormat = "FixedString({0})", CreateParameters = "length", LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "Date", ConcreteExample = "Date", ColumnSize = 2, LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "Date32", ConcreteExample = "Date32", ColumnSize = 4, LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "DateTime", ConcreteExample = "DateTime", ColumnSize = 4, IsBestMatch = true, LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "DateTime64", ConcreteExample = "DateTime64(3)", CreateFormat = "DateTime64({0})", CreateParameters = "precision", ColumnSize = 8, MinimumScale = 0, MaximumScale = 9, LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "UUID", ConcreteExample = "UUID", ColumnSize = 16, IsBestMatch = true, LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "IPv4", ConcreteExample = "IPv4", ColumnSize = 4, IsBestMatch = true, LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "IPv6", ConcreteExample = "IPv6", ColumnSize = 16, LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "Enum8", ConcreteExample = "Enum8('a' = 1)", CreateFormat = "Enum8({0})", CreateParameters = "definition", ColumnSize = 1, LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "Enum16", ConcreteExample = "Enum16('a' = 1)", CreateFormat = "Enum16({0})", CreateParameters = "definition", ColumnSize = 2, LiteralPrefix = "'", LiteralSuffix = "'" },
        new() { TypeName = "LowCardinality", CreateFormat = "LowCardinality({0})", CreateParameters = "type", IsFixedLength = false },
        new() { TypeName = "Nullable", CreateFormat = "Nullable({0})", CreateParameters = "type", IsFixedLength = false, IsNullable = true },
        new() { TypeName = "Array", CreateFormat = "Array({0})", CreateParameters = "type", IsFixedLength = false },
        new() { TypeName = "Tuple", CreateFormat = "Tuple({0})", CreateParameters = "types", IsFixedLength = false },
        new() { TypeName = "Map", CreateFormat = "Map({0}, {1})", CreateParameters = "key type,value type", IsFixedLength = false },
    ];

    private static DataTable DescribeDataTypes(ClickHouseConnection connection)
    {
        var table = new DataTable(DbMetaDataCollectionNames.DataTypes);
        table.Columns.Add("TypeName", typeof(string));
        table.Columns.Add("ProviderDbType", typeof(int)); // provider has no numeric type codes — always null (types are addressed by name)
        table.Columns.Add("ColumnSize", typeof(long));
        table.Columns.Add("CreateFormat", typeof(string));
        table.Columns.Add("CreateParameters", typeof(string));
        table.Columns.Add("DataType", typeof(string));
        table.Columns.Add("IsAutoIncrementable", typeof(bool));
        table.Columns.Add("IsBestMatch", typeof(bool));
        table.Columns.Add("IsCaseSensitive", typeof(bool));
        table.Columns.Add("IsFixedLength", typeof(bool));
        table.Columns.Add("IsFixedPrecisionScale", typeof(bool));
        table.Columns.Add("IsLong", typeof(bool));
        table.Columns.Add("IsNullable", typeof(bool));
        table.Columns.Add("IsSearchable", typeof(bool));
        table.Columns.Add("IsSearchableWithLike", typeof(bool));
        table.Columns.Add("IsUnsigned", typeof(bool));
        table.Columns.Add("MaximumScale", typeof(short));
        table.Columns.Add("MinimumScale", typeof(short));
        table.Columns.Add("IsConcurrencyType", typeof(bool));
        table.Columns.Add("IsLiteralSupported", typeof(bool));
        table.Columns.Add("LiteralPrefix", typeof(string));
        table.Columns.Add("LiteralSuffix", typeof(string));

        var settings = connection.State == ConnectionState.Open ? connection.TypeSettings : TypeSettings.Default;
        foreach (var e in DataTypeEntries)
        {
            var row = table.NewRow();
            row["TypeName"] = e.TypeName;
            if (e.ColumnSize >= 0)
                row["ColumnSize"] = e.ColumnSize;
            row["CreateFormat"] = (object)e.CreateFormat ?? e.TypeName;
            row["CreateParameters"] = (object)e.CreateParameters ?? DBNull.Value;
            row["DataType"] = e.ConcreteExample != null
                ? TypeConverter.ParseClickHouseType(e.ConcreteExample, settings).FrameworkType.FullName
                : (object)DBNull.Value;
            row["IsAutoIncrementable"] = false; // ClickHouse has no auto-increment columns
            row["IsBestMatch"] = e.IsBestMatch;
            row["IsCaseSensitive"] = e.TypeName is "String" or "FixedString";
            row["IsFixedLength"] = e.IsFixedLength;
            row["IsFixedPrecisionScale"] = e.TypeName.StartsWith("Decimal", StringComparison.Ordinal);
            row["IsLong"] = e.IsLong;
            row["IsNullable"] = e.IsNullable;
            row["IsSearchable"] = true;
            row["IsSearchableWithLike"] = e.TypeName is "String" or "FixedString" or "LowCardinality";
            row["IsUnsigned"] = e.IsUnsigned;
            if (e.MaximumScale >= 0)
                row["MaximumScale"] = e.MaximumScale;
            if (e.MinimumScale >= 0)
                row["MinimumScale"] = e.MinimumScale;
            row["IsConcurrencyType"] = false;
            row["IsLiteralSupported"] = e.LiteralPrefix != null;
            row["LiteralPrefix"] = (object)e.LiteralPrefix ?? DBNull.Value;
            row["LiteralSuffix"] = (object)e.LiteralSuffix ?? DBNull.Value;
            table.Rows.Add(row);
        }
        return table;
    }

    // --- Restrictions ---

    private static DataTable DescribeRestrictions()
    {
        var table = new DataTable(DbMetaDataCollectionNames.Restrictions);
        table.Columns.Add("CollectionName", typeof(string));
        table.Columns.Add("RestrictionName", typeof(string));
        table.Columns.Add("RestrictionDefault", typeof(string));
        table.Columns.Add("RestrictionNumber", typeof(int));

        table.Rows.Add("Tables", "Database", "TABLE_CATALOG", 1);
        table.Rows.Add("Tables", "Table", "TABLE_NAME", 2);
        table.Rows.Add("Views", "Database", "TABLE_CATALOG", 1);
        table.Rows.Add("Views", "Table", "TABLE_NAME", 2);
        table.Rows.Add("Columns", "Database", "TABLE_CATALOG", 1);
        table.Rows.Add("Columns", "Table", "TABLE_NAME", 2);
        table.Rows.Add("Columns", "Column", "COLUMN_NAME", 3);
        return table;
    }

    // --- Tables / Views / Columns (live system.* queries) ---

    // ClickHouse has no catalog/schema split: the database name serves as both
    // TABLE_CATALOG and TABLE_SCHEMA. System databases are NOT filtered out —
    // restrictions decide what the caller sees.

    private static string Restriction(string[] restrictions, int index) =>
        restrictions != null && restrictions.Length > index && !string.IsNullOrEmpty(restrictions[index]) ? restrictions[index] : null;

    private static void CheckRestrictionCount(string[] restrictions, int max, string collection)
    {
        if (restrictions != null && restrictions.Length > max)
            throw new ArgumentException($"More restrictions were provided than the requested schema ('{collection}') supports.");
    }

    private static ClickHouseCommand BuildSystemQuery(ClickHouseConnection connection, string select, string from,
        IReadOnlyList<(string Column, string Value)> filters, string extraCondition, string orderBy)
    {
        var command = connection.CreateCommand();
        var query = new StringBuilder(select).Append(" FROM ").Append(from);
        var conditions = new List<string>();
        if (extraCondition != null)
            conditions.Add(extraCondition);
        foreach (var (column, value) in filters)
        {
            if (value == null)
                continue;
            conditions.Add($"{column} = {{{column}:String}}"); // parameterized: user values never concatenated
            command.AddParameter(column, "String", value);
        }
        if (conditions.Count > 0)
            query.Append(" WHERE ").Append(string.Join(" AND ", conditions));
        query.Append(" ORDER BY ").Append(orderBy); // deterministic order for consumers and conformance dumps
        command.CommandText = query.ToString();
        return command;
    }

    private static bool IsViewEngine(string engine) => engine.IndexOf("View", StringComparison.Ordinal) >= 0;

    private static DataTable DescribeTables(ClickHouseConnection connection, string[] restrictions)
    {
        CheckRestrictionCount(restrictions, 2, "Tables");
        var table = new DataTable("Tables");
        table.Columns.Add("TABLE_CATALOG", typeof(string));
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("TABLE_TYPE", typeof(string));

        using var command = BuildSystemQuery(connection, "SELECT database, name, engine", "system.tables",
            [("database", Restriction(restrictions, 0)), ("name", Restriction(restrictions, 1))], null, "database, name");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var database = reader.GetString(0);
            var name = reader.GetString(1);
            var engine = reader.GetString(2);
            table.Rows.Add(database, database, name, IsViewEngine(engine) ? "VIEW" : "BASE TABLE");
        }
        return table;
    }

    private static DataTable DescribeViews(ClickHouseConnection connection, string[] restrictions)
    {
        CheckRestrictionCount(restrictions, 2, "Views");
        var table = new DataTable("Views");
        table.Columns.Add("TABLE_CATALOG", typeof(string));
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("CHECK_OPTION", typeof(string));
        table.Columns.Add("IS_UPDATABLE", typeof(string));

        using var command = BuildSystemQuery(connection, "SELECT database, name", "system.tables",
            [("database", Restriction(restrictions, 0)), ("name", Restriction(restrictions, 1))],
            "engine LIKE '%View%'", "database, name");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var database = reader.GetString(0);
            table.Rows.Add(database, database, reader.GetString(1), "NONE", "NO");
        }
        return table;
    }

    private static DataTable DescribeColumns(ClickHouseConnection connection, string[] restrictions)
    {
        CheckRestrictionCount(restrictions, 3, "Columns");
        var table = new DataTable("Columns");
        table.Columns.Add("TABLE_CATALOG", typeof(string));
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("COLUMN_NAME", typeof(string));
        table.Columns.Add("ORDINAL_POSITION", typeof(int));
        table.Columns.Add("COLUMN_DEFAULT", typeof(string));
        table.Columns.Add("IS_NULLABLE", typeof(string));
        table.Columns.Add("DATA_TYPE", typeof(string));
        table.Columns.Add("CHARACTER_MAXIMUM_LENGTH", typeof(long));
        table.Columns.Add("NUMERIC_PRECISION", typeof(int));
        table.Columns.Add("NUMERIC_SCALE", typeof(int));
        table.Columns.Add("DATETIME_PRECISION", typeof(int));

        using var command = BuildSystemQuery(connection,
            "SELECT database, table, name, type, position, default_expression, character_octet_length, numeric_precision, numeric_scale, datetime_precision",
            "system.columns",
            [("database", Restriction(restrictions, 0)), ("table", Restriction(restrictions, 1)), ("name", Restriction(restrictions, 2))],
            null, "database, table, position");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var database = reader.GetString(0);
            var type = reader.GetString(3);
            var defaultExpression = reader.GetString(5);

            var row = table.NewRow();
            row["TABLE_CATALOG"] = database;
            row["TABLE_SCHEMA"] = database;
            row["TABLE_NAME"] = reader.GetString(1);
            row["COLUMN_NAME"] = reader.GetString(2);
            row["ORDINAL_POSITION"] = Convert.ToInt32(reader.GetValue(4));
            row["COLUMN_DEFAULT"] = defaultExpression.Length > 0 ? defaultExpression : (object)DBNull.Value;
            row["IS_NULLABLE"] = IsNullableTypeName(type) ? "YES" : "NO";
            row["DATA_TYPE"] = type;
            row["CHARACTER_MAXIMUM_LENGTH"] = NullableCell<long>(reader, 6);
            row["NUMERIC_PRECISION"] = NullableCell<int>(reader, 7);
            row["NUMERIC_SCALE"] = NullableCell<int>(reader, 8);
            row["DATETIME_PRECISION"] = NullableCell<int>(reader, 9);
            table.Rows.Add(row);
        }
        return table;
    }

    // Nullability of the stored value: Nullable(T), possibly under LowCardinality.
    private static bool IsNullableTypeName(string type)
    {
        const string lowCardinality = "LowCardinality(";
        var t = type;
        if (t.StartsWith(lowCardinality, StringComparison.Ordinal) && t.EndsWith(")", StringComparison.Ordinal))
            t = t.Substring(lowCardinality.Length, t.Length - lowCardinality.Length - 1);
        return t.StartsWith("Nullable(", StringComparison.Ordinal);
    }

    private static object NullableCell<T>(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value is null or DBNull ? DBNull.Value : Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
