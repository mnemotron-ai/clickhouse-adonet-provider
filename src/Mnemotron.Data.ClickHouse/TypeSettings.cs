using Microsoft.Extensions.Logging;
using NodaTime;

namespace Mnemotron.Data.ClickHouse;

// `logger` is optional and null-safe: it is how TypeConverter's schema/type-resolution
// warnings (unknown type names, JSON degrade) reach a connection's ILogger without
// threading a connection reference through the (static) type parser. See
// ClickHouseConnection.TypeSettings for where it is populated.
internal record struct TypeSettings(bool useBigDecimal, string timezone, int stringColumnSize, ILogger logger = null)
{
    // Reported by GetSchemaTable for unbounded String columns. Bounded sizes
    // keep ADO.NET consumers (SSIS buffers, SSAS/SSDT) on fixed-width string
    // handling instead of per-cell LOB spooling; values above 4000 fall back
    // to LOB semantics in SSIS anyway.
    public const int DefaultStringColumnSize = 4000;

    public static string DefaultTimezone = DateTimeZoneProviders.Tzdb.GetSystemDefault().Id;

    public static TypeSettings Default => new TypeSettings(true, DefaultTimezone, DefaultStringColumnSize);
}
