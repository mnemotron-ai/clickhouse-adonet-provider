using System;
using System.Text;
using Mnemotron.Data.ClickHouse.ADO;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests;

[TestFixture]
public class AbstractConnectionTestFixture : IDisposable
{
    protected readonly ClickHouseConnection connection;

    protected AbstractConnectionTestFixture()
    {
        connection = TestUtilities.GetTestClickHouseConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE DATABASE IF NOT EXISTS test;";
        command.ExecuteScalar();
    }

    protected static string SanitizeTableName(string input)
    {
        var builder = new StringBuilder();
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                builder.Append(c);
        }

        return builder.ToString();
    }

    [OneTimeTearDown]
    public void Dispose() => connection?.Dispose();
}
