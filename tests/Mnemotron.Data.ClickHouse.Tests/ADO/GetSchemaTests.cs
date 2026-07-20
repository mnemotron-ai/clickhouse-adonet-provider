// WS-2: DbConnection.GetSchema — all seven FR-5 collections in the standard
// ADO.NET shape (SSDT DSV/Import). Tests run against a live server
// (CLICKHOUSE_CONNECTION); the conformance/fixture.sql fixture is applied
// in OneTimeSetUp (idempotent).
using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests.ADO;

public class GetSchemaTests : AbstractConnectionTestFixture
{
    private const string FixtureDatabase = "conformance_fixture";

    [OneTimeSetUp]
    public void ApplyFixture()
    {
        var path = FindFixture();
        // Top-level split on ';' — the fixture.sql contract (no ';' inside strings/comments).
        foreach (var statement in File.ReadAllText(path).Split(';').Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    private static string FindFixture()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "conformance", "fixture.sql");
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException("conformance/fixture.sql not found above " + AppContext.BaseDirectory);
    }

    private static string[] ColumnNames(DataTable table) =>
        table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();

    [Test]
    [TestCase("MetaDataCollections")]
    [TestCase("DataSourceInformation")]
    [TestCase("DataTypes")]
    [TestCase("Restrictions")]
    [TestCase("Tables")]
    [TestCase("Views")]
    [TestCase("Columns")]
    public void ShouldReturnNonEmptyCollection(string collectionName)
    {
        var table = connection.GetSchema(collectionName);
        Assert.That(table, Is.Not.Null);
        Assert.That(table.Rows, Is.Not.Empty, $"{collectionName} must not be empty");
    }

    [Test]
    public void MetaDataCollectionsShouldListAllSevenCollections()
    {
        var table = connection.GetSchema("MetaDataCollections");
        var names = table.Rows.Cast<DataRow>().Select(r => (string)r["CollectionName"]).ToArray();
        Assert.That(names, Is.EquivalentTo(new[]
        {
            "MetaDataCollections", "DataSourceInformation", "DataTypes", "Restrictions", "Tables", "Views", "Columns",
        }));
    }

    [Test]
    public void CollectionNameShouldBeCaseInsensitive()
    {
        Assert.That(connection.GetSchema("tables").Rows, Is.Not.Empty);
        Assert.That(connection.GetSchema("METADATACOLLECTIONS").Rows, Is.Not.Empty);
    }

    [Test]
    public void UnknownCollectionShouldThrow() =>
        Assert.Throws<ArgumentException>(() => connection.GetSchema("NoSuchCollection"));

    [Test]
    public void DataSourceInformationShouldDescribeClickHouseQuoting()
    {
        // FR-6: ClickHouse accepts "ident", does NOT accept [ident]; string literals are single-quoted.
        var table = connection.GetSchema("DataSourceInformation");
        Assert.That(table.Rows, Has.Count.EqualTo(1));
        var row = table.Rows[0];
        Assert.Multiple(() =>
        {
            Assert.That(row["DataSourceProductName"], Is.EqualTo("ClickHouse"));
            Assert.That((string)row["DataSourceProductVersion"], Is.Not.Empty);
            // The quoting character is a double quote, not a square bracket
            Assert.That((string)row["QuotedIdentifierPattern"], Does.StartWith("\"").And.EndWith("\""));
            Assert.That((string)row["QuotedIdentifierPattern"], Does.Not.StartWith("["));
            Assert.That(row["QuotedIdentifierCase"], Is.EqualTo((int)IdentifierCase.Sensitive));
            Assert.That(row["IdentifierCase"], Is.EqualTo((int)IdentifierCase.Sensitive));
            Assert.That((string)row["StringLiteralPattern"], Does.StartWith("'"));
            Assert.That((string)row["CompositeIdentifierSeparatorPattern"], Is.EqualTo(@"\."));
            // Parameters bind as {name:Type} (see ClickHouseDbParameter.QueryForm)
            Assert.That((string)row["ParameterMarkerPattern"], Does.StartWith(@"\{"));
        });
    }

    [Test]
    public void DataTypesShouldCoverProviderTypeMatrix()
    {
        var table = connection.GetSchema("DataTypes");
        var names = table.Rows.Cast<DataRow>().Select(r => (string)r["TypeName"]).ToArray();
        Assert.That(new[] { "Int32", "UInt64", "Float64", "Decimal", "String", "DateTime64", "Nullable", "LowCardinality", "UUID" }, Is.SubsetOf(names));
        var str = table.Rows.Cast<DataRow>().Single(r => (string)r["TypeName"] == "String");
        Assert.That(str["DataType"], Is.EqualTo("System.String"));
    }

    [Test]
    public void RestrictionsShouldDescribeTablesViewsColumns()
    {
        var table = connection.GetSchema("Restrictions");
        var byCollection = table.Rows.Cast<DataRow>().ToLookup(r => (string)r["CollectionName"]);
        Assert.Multiple(() =>
        {
            // SqlClient shapes: Tables (Catalog, Owner, Table, TableType),
            // Views (Catalog, Owner, Table), Columns (Catalog, Owner, Table, Column).
            Assert.That(byCollection["Tables"].Count(), Is.EqualTo(4));
            Assert.That(byCollection["Views"].Count(), Is.EqualTo(3));
            Assert.That(byCollection["Columns"].Count(), Is.EqualTo(4));
        });
    }

    [Test]
    public void TablesShouldReturnFixtureTables()
    {
        var table = connection.GetSchema("Tables", [FixtureDatabase]);
        Assert.That(ColumnNames(table), Is.EqualTo(new[] { "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "TABLE_TYPE" }));
        var byName = table.Rows.Cast<DataRow>().ToDictionary(r => (string)r["TABLE_NAME"], r => (string)r["TABLE_TYPE"]);
        Assert.Multiple(() =>
        {
            Assert.That(byName["types_matrix"], Is.EqualTo("BASE TABLE"));
            Assert.That(byName["orders"], Is.EqualTo("BASE TABLE"));
            Assert.That(byName["orders_view"], Is.EqualTo("VIEW"));
            Assert.That(table.Rows.Cast<DataRow>().Select(r => (string)r["TABLE_CATALOG"]), Is.All.EqualTo(FixtureDatabase));
        });
    }

    [Test]
    public void TablesWithTableRestrictionShouldReturnSingleRow()
    {
        var table = connection.GetSchema("Tables", [FixtureDatabase, null, "orders"]);
        Assert.That(table.Rows, Has.Count.EqualTo(1));
        Assert.That(table.Rows[0]["TABLE_NAME"], Is.EqualTo("orders"));
    }

    [Test]
    public void ViewsShouldReturnFixtureView()
    {
        var table = connection.GetSchema("Views", [FixtureDatabase]);
        Assert.That(ColumnNames(table), Is.EqualTo(new[] { "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "CHECK_OPTION", "IS_UPDATABLE" }));
        Assert.That(table.Rows, Has.Count.EqualTo(1));
        var row = table.Rows[0];
        Assert.Multiple(() =>
        {
            Assert.That(row["TABLE_NAME"], Is.EqualTo("orders_view"));
            Assert.That(row["CHECK_OPTION"], Is.EqualTo("NONE"));
            Assert.That(row["IS_UPDATABLE"], Is.EqualTo("NO"));
        });
    }

    [Test]
    public void ColumnsShouldReturnStandardShapeWithHonestFacts()
    {
        var table = connection.GetSchema("Columns", [FixtureDatabase, null, "orders"]);
        Assert.That(ColumnNames(table), Is.EqualTo(new[]
        {
            "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "ORDINAL_POSITION", "COLUMN_DEFAULT",
            "IS_NULLABLE", "DATA_TYPE", "CHARACTER_MAXIMUM_LENGTH", "NUMERIC_PRECISION", "NUMERIC_SCALE", "DATETIME_PRECISION",
        }));
        var rows = table.Rows.Cast<DataRow>().ToDictionary(r => (string)r["COLUMN_NAME"]);
        Assert.That(rows.Keys, Is.EquivalentTo(new[] { "order_id", "customer", "amount", "created_at", "note" }));
        Assert.Multiple(() =>
        {
            Assert.That(rows["order_id"]["ORDINAL_POSITION"], Is.EqualTo(1));
            Assert.That(rows["order_id"]["IS_NULLABLE"], Is.EqualTo("NO"));
            Assert.That(rows["note"]["IS_NULLABLE"], Is.EqualTo("YES"));
            Assert.That(rows["note"]["DATA_TYPE"], Is.EqualTo("Nullable(String)"));
            Assert.That(rows["amount"]["NUMERIC_PRECISION"], Is.EqualTo(18));
            Assert.That(rows["amount"]["NUMERIC_SCALE"], Is.EqualTo(2));
            Assert.That(rows["created_at"]["DATETIME_PRECISION"], Is.EqualTo(3));
        });
    }

    [Test]
    public void ColumnsWithColumnRestrictionShouldReturnSingleColumn()
    {
        var table = connection.GetSchema("Columns", [FixtureDatabase, null, "types_matrix", "fs"]);
        Assert.That(table.Rows, Has.Count.EqualTo(1));
        var row = table.Rows[0];
        Assert.Multiple(() =>
        {
            Assert.That(row["DATA_TYPE"], Is.EqualTo("FixedString(16)"));
            Assert.That(row["CHARACTER_MAXIMUM_LENGTH"], Is.EqualTo(16));
        });
    }

    [Test]
    public void TableTypeRestrictionShouldFilter()
    {
        // SSDT's DSV wizard passes the 4-element SqlClient shape (Catalog, Owner, Table, TableType).
        var tables = connection.GetSchema("Tables", [FixtureDatabase, null, null, "TABLE"]);
        Assert.That(tables.Rows, Is.Not.Empty);
        Assert.That(tables.Rows.Cast<DataRow>().Select(r => (string)r["TABLE_TYPE"]), Is.All.EqualTo("BASE TABLE"));
        var views = connection.GetSchema("Tables", [FixtureDatabase, null, null, "VIEW"]);
        Assert.That(views.Rows.Cast<DataRow>().Select(r => (string)r["TABLE_NAME"]), Is.EqualTo(new[] { "orders_view" }));
    }

    [Test]
    public void OwnerRestrictionShouldActAsDatabaseFilter()
    {
        // ClickHouse has no catalog/schema split: Owner is a database filter too...
        var table = connection.GetSchema("Tables", [null, FixtureDatabase, "orders"]);
        Assert.That(table.Rows, Has.Count.EqualTo(1));
        // ...so two different non-null databases at once match nothing.
        Assert.That(connection.GetSchema("Tables", [FixtureDatabase, "somewhere_else"]).Rows, Is.Empty);
    }

    [Test]
    public void ExtraRestrictionsShouldBeIgnoredNotThrown()
    {
        // SSDT swallows GetSchema exceptions and renders an empty tree — never throw on extras.
        var table = connection.GetSchema("Tables", [FixtureDatabase, null, "orders", "BASE TABLE", "extra"]);
        Assert.That(table.Rows, Has.Count.EqualTo(1));
    }
}
