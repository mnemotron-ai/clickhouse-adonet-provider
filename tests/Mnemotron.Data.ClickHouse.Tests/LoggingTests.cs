using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Mnemotron.Data.ClickHouse.Types;
using Mnemotron.Data.ClickHouse.Utility;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests;

/// <summary>
/// Covers issue #22: the provider must surface types/paths it cannot serve
/// faithfully as structured LogWarning entries through the connection's
/// Logger, rather than staying silent.
/// </summary>
public class LoggingTests
{
    /// <summary>
    /// Minimal capturing <see cref="ILogger"/>: records every log call instead of
    /// writing anywhere, so tests can assert on what was logged.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception Exception)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    [Test]
    public static void UnsupportedTypeShouldLogWarningThroughConnectionLogger()
    {
        using var connection = TestUtilities.GetTestClickHouseConnection();
        var logger = new CapturingLogger();
        connection.Logger = logger;

        // Same unsupported input as ErrorHandlingTests.UnknownTypeShouldThrowException:
        // ClickHouse reports this column as an "IntervalDay" type name the provider
        // does not register, so ParseClickHouseType still throws (behavior is
        // unchanged) but must warn on the way out.
        Assert.ThrowsAsync<ArgumentException>(async () => await connection.ExecuteScalarAsync("SELECT INTERVAL 4 DAY"));

        Assert.That(logger.Entries, Has.Some.Matches<(LogLevel Level, string Message, Exception Exception)>(
            e => e.Level == LogLevel.Warning && e.Message.Contains("Unsupported ClickHouse type")));
    }

    [Test]
    public static void ParseClickHouseType_UnknownTypeName_LogsWarningWithTypeName()
    {
        var logger = new CapturingLogger();
        var settings = new TypeSettings(true, TypeSettings.DefaultTimezone, TypeSettings.DefaultStringColumnSize, logger);

        Assert.Throws<ArgumentException>(() => TypeConverter.ParseClickHouseType("NotARealClickHouseType", settings));

        Assert.That(logger.Entries, Has.Some.Matches<(LogLevel Level, string Message, Exception Exception)>(
            e => e.Level == LogLevel.Warning && e.Message.Contains("NotARealClickHouseType")));
    }

    [Test]
    public static void ParseClickHouseType_UnknownTypeName_NeverThrowsFromLoggingWhenLoggerIsNull()
    {
        var settings = new TypeSettings(true, TypeSettings.DefaultTimezone, TypeSettings.DefaultStringColumnSize, logger: null);

        // The original ArgumentException must still surface; a null Logger must
        // never turn into a NullReferenceException from the logging path.
        Assert.Throws<ArgumentException>(() => TypeConverter.ParseClickHouseType("NotARealClickHouseType", settings));
    }

    [Test]
    public static void LogJsonDegradeIfNeeded_IsNoOpWhenLoggerIsNull()
    {
        // Must never throw, degrade or not - this only exercises the null-safety
        // contract, since this test process has a healthy System.Text.Json and so
        // never observes the actual degrade captured by TypeConverter's static ctor.
        Assert.DoesNotThrow(() => TypeConverter.LogJsonDegradeIfNeeded(null));
    }
}
