using NodaTime;

namespace Mnemotron.Data.ClickHouse;

internal record struct TypeSettings(bool useBigDecimal, string timezone, int stringColumnSize)
{
    // Reported by GetSchemaTable for unbounded String columns. Bounded sizes
    // keep ADO.NET consumers (SSIS buffers, SSAS/SSDT) on fixed-width string
    // handling instead of per-cell LOB spooling; values above 4000 fall back
    // to LOB semantics in SSIS anyway.
    public const int DefaultStringColumnSize = 4000;

    public static string DefaultTimezone = DateTimeZoneProviders.Tzdb.GetSystemDefault().Id;

    public static TypeSettings Default => new TypeSettings(true, DefaultTimezone, DefaultStringColumnSize);
}
