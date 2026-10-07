namespace TimecardNotifier.Models;

/// <summary>Display metadata for a cost-center time zone. Ids are IANA (work in WASM and on .NET 6+ everywhere).</summary>
public sealed record ZoneDef(string Id, string City, string Region, string CountryCode);

public static class TimeZoneCatalog
{
    public static readonly IReadOnlyList<ZoneDef> All = new List<ZoneDef>
    {
        new("Europe/London",       "London",      "United Kingdom",   "GB"),
        new("America/New_York",    "New York",    "US Eastern",       "US"),
        new("America/Toronto",     "Toronto",     "Canada Eastern",   "CA"),
        new("America/Chicago",     "Chicago",     "US Central",       "US"),
        new("America/Mexico_City", "Mexico City", "Mexico Central",   "MX"),
        new("America/Edmonton",    "Edmonton",    "Canada Mountain",  "CA"),
        new("America/Denver",      "Denver",      "US Mountain",      "US"),
        new("America/Los_Angeles", "Los Angeles", "US Pacific",       "US"),
        new("America/Anchorage",   "Anchorage",   "US Alaska",        "US"),
        new("Pacific/Honolulu",    "Honolulu",    "US Hawaii",        "US"),
    };

    public static readonly IReadOnlyDictionary<string, string> Countries = new Dictionary<string, string>
    {
        ["US"] = "United States",
        ["CA"] = "Canada",
        ["MX"] = "Mexico",
        ["GB"] = "United Kingdom",
    };

    public static ZoneDef Get(string id) =>
        All.FirstOrDefault(z => z.Id == id) ?? new ZoneDef(id, id, id, "??");

    public static TimeZoneInfo Resolve(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>Converts a wall-clock time on a date in the given zone to UTC (DST-aware).</summary>
    public static DateTimeOffset LocalToUtc(DateOnly date, TimeOnly time, string zoneId)
    {
        var tz = Resolve(zoneId);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }

    public static DateTimeOffset ToLocal(DateTimeOffset utc, string zoneId) =>
        TimeZoneInfo.ConvertTime(utc, Resolve(zoneId));

    public static string OffsetLabel(DateOnly date, string zoneId)
    {
        var offset = Resolve(zoneId).GetUtcOffset(date.ToDateTime(new TimeOnly(12, 0)));
        return FormatOffset(offset);
    }

    public static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        var abs = offset.Duration();
        return abs.Minutes == 0 ? $"UTC{sign}{abs.Hours}" : $"UTC{sign}{abs.Hours}:{abs.Minutes:00}";
    }

    /// <summary>UTC offset of the zone at a given local wall-clock time on a date (DST-aware).</summary>
    public static TimeSpan OffsetAt(DateOnly date, TimeOnly time, string zoneId) =>
        Resolve(zoneId).GetUtcOffset(date.ToDateTime(time, DateTimeKind.Unspecified));

    public static string Flag(string countryCode) => countryCode switch
    {
        "US" => "🇺🇸",
        "CA" => "🇨🇦",
        "MX" => "🇲🇽",
        "GB" => "🇬🇧",
        _ => "🏳️",
    };

    /// <summary>Sunday that starts the work week (Sunday–Saturday) containing this date.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(int)date.DayOfWeek);

    /// <summary>Friday of the previous work week (checked on the first workday of a new week).</summary>
    public static DateOnly PreviousFriday(DateOnly date) => WeekStart(date).AddDays(-2);

    /// <summary>
    /// Weekdays earlier in this work week (Sunday up to yesterday, weekends removed). These are the days
    /// that can trigger a reminder, before holidays are removed per recipient. Empty on Sunday and Monday.
    /// </summary>
    public static IReadOnlyList<DateOnly> PriorWeekdays(DateOnly date)
    {
        var list = new List<DateOnly>();
        for (var d = WeekStart(date); d < date; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) list.Add(d);
        return list;
    }
}
