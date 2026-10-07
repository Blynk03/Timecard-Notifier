using TimecardNotifier.Models;

namespace TimecardNotifier.Services;

/// <summary>Deterministic sample data standing in for the "missing entries" query. Replace with an HTTP call.</summary>
public sealed class MockRecipientSource : IRecipientSource
{
    private static readonly string[] First =
    {
        "Ava", "Marcus", "Priya", "Diego", "Hannah", "Kwame", "Sofia", "Liam", "Mei", "Jordan",
        "Isabel", "Tariq", "Grace", "Mateo", "Chloe", "Noah", "Amara", "Ethan", "Lucía", "Owen",
        "Fatima", "Caleb", "Nina", "Ryan", "Elena", "Samuel", "Zoe", "Andre", "Leah", "Javier",
    };

    private static readonly string[] Last =
    {
        "Thompson", "Okafor", "Patel", "Ramírez", "Becker", "Mensah", "Rossi", "Murphy", "Chen", "Hayes",
        "Delgado", "Haddad", "Kim", "Morales", "Dubois", "Walker", "Nwosu", "Brooks", "Herrera", "Fraser",
        "Khan", "Sullivan", "Novak", "Price", "Vargas", "Grant", "Lindqvist", "Coleman", "Ortiz", "Reid",
    };

    // (zone, count, cost centers as "CODE Name").
    // Volumes follow the estimates provided (offsets in standard time):
    //   UTC−5 Eastern ≈ 3,500 · UTC−6 Central ≈ 3,000 · UTC−7 Mountain ≈ 900
    //   UTC−8 Pacific, UTC−9 Alaska, UTC−10 Hawaii: unknown, "between Mountain and Central" → 1,950 each as a placeholder.
    // Plus 150 people in an exempt cost center (CC-2190) so the to-send numbers above stay intact,
    // and ~4% extra per zone who only missed Sunday (listed, but not reminded).
    private static readonly (string Zone, int Count, string[] CostCenters)[] Distribution =
    {
        ("Europe/London",         60, new[] { "CC-7100 UK Field Engineering" }),
        ("America/New_York",    3200, new[] { "CC-1100 Northeast Field Services", "CC-1120 Mid-Atlantic Installations", "CC-1140 Southeast Construction" }),
        ("America/Toronto",      300, new[] { "CC-5100 Ontario Field Services" }),
        ("America/Chicago",     2700, new[] { "CC-2100 Central Field Ops", "CC-2140 Gulf Coast Construction", "CC-2160 Midwest Maintenance" }),
        ("America/Chicago",      150, new[] { "CC-2190 Salaried Engineering" }),
        ("America/Mexico_City",  300, new[] { "CC-6100 Mexico Field Ops" }),
        ("America/Denver",       800, new[] { "CC-3100 Mountain Field Ops" }),
        ("America/Edmonton",     100, new[] { "CC-5200 Alberta Operations" }),
        ("America/Los_Angeles", 1950, new[] { "CC-4100 West Coast Installations", "CC-4120 Pacific Northwest Service" }),
        ("America/Anchorage",   1950, new[] { "CC-8100 Alaska Field Ops" }),
        ("Pacific/Honolulu",    1950, new[] { "CC-8200 Hawaii Field Services" }),
    };

    private readonly IReadOnlyList<(Recipient Person, bool WeekendOnly)> _all = Build();

    /// <summary>
    /// Returns everyone with at least one day with no hours, from last week's Friday up to yesterday.
    /// Mock pattern: each regular person misses one or two weekdays earlier in the week (on Mondays, last Friday);
    /// every 5th person also missed last Friday. An extra ~4% per zone only missed Sunday, so they appear in the
    /// list but shouldn't get a reminder.
    /// </summary>
    public async Task<IReadOnlyList<Recipient>> GetRecipientsAsync(DateOnly runDate, CancellationToken ct = default)
    {
        await Task.Delay(350, ct); // feel like a network call

        var weekStart = TimeZoneCatalog.WeekStart(runDate);
        var weekdays = TimeZoneCatalog.PriorWeekdays(runDate);
        var sundayIsPast = weekStart < runDate;
        var friday = TimeZoneCatalog.PreviousFriday(runDate);

        return _all.Select((x, n) =>
        {
            var missing = new List<DateOnly>();
            if (x.WeekendOnly)
            {
                if (sundayIsPast) missing.Add(weekStart);
            }
            else if (weekdays.Count > 0)
            {
                missing.Add(weekdays[n % weekdays.Count]);
                if (n % 3 == 0 && weekdays.Count > 1) missing.Add(weekdays[(n + 1) % weekdays.Count]);
                if (n % 7 == 0 && sundayIsPast) missing.Add(weekStart); // weekend gaps are ignored by the rule
                if (n % 5 == 0) missing.Add(friday); // only counts on their first workday of the week
            }
            else if (runDate.DayOfWeek == DayOfWeek.Monday)
            {
                missing.Add(friday);
                if (n % 7 == 0) missing.Add(weekStart);
            }
            return x.Person with { MissingDays = missing.Distinct().Order().ToList() };
        }).ToList();
    }

    private static IReadOnlyList<(Recipient, bool)> Build()
    {
        var list = new List<(Recipient, bool)>();
        var n = 0;
        foreach (var (zone, count, centers) in Distribution)
        {
            var country = TimeZoneCatalog.Get(zone).CountryCode;
            var extra = count / 25; // weekend-only people
            for (var i = 0; i < count + extra; i++, n++)
            {
                var first = First[(n * 7) % First.Length];
                var last = Last[(n * 11 + 3) % Last.Length];
                var id = $"E{10400 + n * 13}";
                list.Add((new Recipient(
                    EmployeeId: id,
                    DisplayName: $"{first} {last}",
                    Email: $"{Ascii(first)}.{Ascii(last)}{(n >= 900 ? (n / 900).ToString() : "")}@contoso.com".ToLowerInvariant(),
                    CostCenterCode: centers[i % centers.Length][..7],
                    CostCenterName: centers[i % centers.Length][8..],
                    CountryCode: country,
                    TimeZoneId: zone,
                    HasTeamsAccount: n != 41), // one record shows the failure path
                    i >= count));
            }
        }
        return list;
    }

    private static string Ascii(string s) => s
        .Replace("í", "i").Replace("á", "a").Replace("é", "e").Replace("ó", "o").Replace("ú", "u");
}
