using System;
using NodaTime;

namespace Mnemotron.Data.ClickHouse.Types;

public static class DateTimeConversions
{
    public static readonly DateTime DateTimeEpochStart = DateTimeOffset.FromUnixTimeSeconds(0).UtcDateTime;

#if NET6_0_OR_GREATER
    public static readonly DateOnly DateOnlyEpochStart = new(1970, 1, 1);
#endif

    public static int ToUnixTimeDays(this DateTimeOffset dto)
    {
        return (int)(dto.Date - DateTimeEpochStart.Date).TotalDays;
    }

    public static DateTime FromUnixTimeDays(int days) => DateTimeEpochStart.AddDays(days);
}

internal abstract class AbstractDateTimeType : ParameterizedType
{
    public DateTimeOffset CoerceToDateTimeOffset(object value)
    {
        return value switch
        {
#if NET6_0_OR_GREATER
            DateOnly date => new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero),
#endif
            DateTimeOffset v => v,
            DateTime dt => TimeZoneOrUtc.AtLeniently(LocalDateTime.FromDateTime(dt)).ToDateTimeOffset(),
            OffsetDateTime o => o.ToDateTimeOffset(),
            ZonedDateTime z => z.ToDateTimeOffset(),
            Instant i => ToDateTimeOffset(i),
            _ => throw new NotSupportedException()
        };
    }

    public override Type FrameworkType => typeof(DateTime);

    private DateTimeZone timeZone;
    private bool timeZoneIsUtc = true;

    public DateTimeZone TimeZone
    {
        get => timeZone;
        set
        {
            timeZone = value;
            // Servers commonly run in UTC (the Parse fallback resolves settings.timezone),
            // so the per-row NodaTime InZone conversion is skippable in the common case.
            timeZoneIsUtc = value == null || value == DateTimeZone.Utc || value.Id == "UTC";
        }
    }

    public DateTimeZone TimeZoneOrUtc => TimeZone ?? DateTimeZone.Utc;

    public override string ToString() => TimeZone == null ? $"{Name}" : $"{Name}({TimeZone.Id})";

    private DateTimeOffset ToDateTimeOffset(Instant instant) => instant.InZone(TimeZoneOrUtc).ToDateTimeOffset();

    public DateTime ToDateTime(Instant instant)
    {
        // UTC fast path: epoch + ticks, Kind=Utc — identical to ToDateTimeUtc()
        // for a zero-offset zone, without building the NodaTime calendar graph per row.
        if (timeZoneIsUtc)
            return DateTimeConversions.DateTimeEpochStart.AddTicks(instant.ToUnixTimeTicks());

        var zonedDateTime = instant.InZone(TimeZoneOrUtc);
        if (zonedDateTime.Offset.Ticks == 0)
            return zonedDateTime.ToDateTimeUtc();
        else
            return zonedDateTime.ToDateTimeUnspecified();
    }
}
