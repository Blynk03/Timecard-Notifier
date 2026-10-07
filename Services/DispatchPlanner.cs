using TimecardNotifier.Models;

namespace TimecardNotifier.Services;

/// <summary>
/// Builds the day's send plan:
///  1. Exempt cost centers are never reminded.
///  2. No reminders on weekends, or on a recipient's holiday (country + optional time zone).
///  3. Week-to-date rule: the work week runs Sunday–Saturday. A recipient gets ONE reminder if ANY
///     weekday earlier this week (not a weekend, not their holiday) has no hours. Example: on Wednesday,
///     missing Monday but not Tuesday still gets a reminder.
///     Friday catch-up: on a recipient's first workday of the week (Monday, or Tuesday if Monday is their
///     holiday) the previous week's Friday is checked too, unless that Friday was their holiday.
///  4. Countries/zones that share a UTC offset on the run date form one send wave (one 11:00 deadline).
///  5. Each wave starts as late as possible while every wave still finishes by its target
///     (11:00 local minus the safety margin), and as early as needed. The fixed 20/min limit is shared.
///  6. Anyone whose minute is at or after 11:00 local is flagged as late risk.
/// </summary>
public sealed class DispatchPlanner
{
    public DispatchPlan Build(
        DateOnly runDate,
        IReadOnlyList<Recipient> recipients,
        IHolidayService holidays,
        IExemptionService exemptions,
        NotifierSettings settings)
    {
        const int rate = NotifierSettings.RateLimitPerMinute;
        var isWeekend = runDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var priorWeekdays = TimeZoneCatalog.PriorWeekdays(runDate);

        var waves = recipients
            // Same offset at the deadline => same wall clock => same deadline instant.
            .GroupBy(r => TimeZoneCatalog.OffsetAt(runDate, settings.Deadline, r.TimeZoneId))
            .Select(g =>
            {
                var anyZone = g.First().TimeZoneId;
                var wave = new SendWave
                {
                    UtcOffset = g.Key,
                    DeadlineUtc = TimeZoneCatalog.LocalToUtc(runDate, settings.Deadline, anyZone),
                    TargetUtc = TimeZoneCatalog.LocalToUtc(runDate, settings.PlanningDeadline, anyZone),
                    IsWeekend = isWeekend,
                };
                foreach (var r in g) wave.Items.Add(Classify(r, runDate, isWeekend, priorWeekdays, holidays, exemptions, settings));
                return wave;
            })
            .OrderBy(w => w.DeadlineUtc)
            .ToList();

        // ── Schedule on counts ──────────────────────────────────────────────
        waves.ForEach(w => w.Recount());
        var inputs = waves
            .Select(w => new SendScheduler.WaveInput(
                ToMinute(TimeZoneCatalog.LocalToUtc(runDate, TimeOnly.MinValue, w.ReferenceZoneId)), // earliest: local midnight
                ToMinute(w.TargetUtc),
                ToMinute(w.DeadlineUtc),
                w.ToSend))
            .ToList();

        var (_, results) = SendScheduler.Plan(inputs, rate);

        var perMinute = new Dictionary<long, int>();
        for (var i = 0; i < waves.Count; i++)
        {
            AssignTimes(waves[i], results[i], rate, perMinute);
            waves[i].Recount();
        }

        return new DispatchPlan { RunDate = runDate, Waves = waves, SendsPerMinute = perMinute };
    }

    private static DispatchItem Classify(
        Recipient r, DateOnly runDate, bool isWeekend, IReadOnlyList<DateOnly> priorWeekdays,
        IHolidayService holidays, IExemptionService exemptions, NotifierSettings settings)
    {
        var item = new DispatchItem { Recipient = r };

        if (exemptions.Find(r.CostCenterCode) is { } ex)
        {
            item.Status = DispatchStatus.Exempt;
            item.Note = string.IsNullOrWhiteSpace(ex.Reason) ? $"{ex.Code} is exempt" : $"{ex.Code} exempt: {ex.Reason}";
            return item;
        }
        if (settings.SkipWeekends && isWeekend)
        {
            item.Status = DispatchStatus.SkippedWeekend;
            item.Note = $"{runDate.DayOfWeek}: no reminders on weekends";
            return item;
        }
        if (settings.SkipHolidays && holidays.FindFor(r.CountryCode, r.TimeZoneId, runDate) is { } today)
        {
            item.Status = DispatchStatus.SkippedHoliday;
            item.Holiday = today;
            item.Note = $"{today.Name}: no reminders today";
            return item;
        }

        // Which days are checked: this week's earlier weekdays that weren't this recipient's holiday...
        bool IsWorkday(DateOnly d) => !settings.SkipHolidays || holidays.FindFor(r.CountryCode, r.TimeZoneId, d) is null;
        var checkedDays = priorWeekdays.Where(IsWorkday).ToList();

        // ...plus last Friday on their first workday of the week (Friday is never followed by a workday in its own week).
        if (checkedDays.Count == 0)
        {
            var friday = TimeZoneCatalog.PreviousFriday(runDate);
            if (IsWorkday(friday)) checkedDays.Add(friday);
        }

        var missing = r.MissingDays.ToHashSet();
        item.MissingWorkdays = checkedDays.Where(missing.Contains).ToList();

        if (item.MissingWorkdays.Count == 0)
        {
            item.Status = DispatchStatus.NoMissingWorkdays;
            item.Note = checkedDays.Count == 0
                ? "No workdays to check"
                : r.MissingDays.Count > 0 ? "Only missing days that don't count today" : "All checked days entered";
        }
        else
        {
            var weekStart = TimeZoneCatalog.WeekStart(runDate);
            item.Note = "Missing " + string.Join(", ", item.MissingWorkdays.Select(d => d < weekStart ? $"last {d:ddd}" : d.ToString("ddd")));
        }
        return item;
    }

    /// <summary>Hands out the wave's per-minute allocations to people, interleaving the wave's zones.</summary>
    private static void AssignTimes(SendWave wave, SendScheduler.WaveResult result, int rate, Dictionary<long, int> perMinute)
    {
        var queue = wave.Items
            .Where(i => !i.IsSkipped)
            .GroupBy(i => i.Recipient.TimeZoneId)
            .SelectMany(g => g.OrderBy(i => i.Recipient.DisplayName).Select((item, idx) => (item, idx)))
            .OrderBy(x => x.idx)
            .Select(x => x.item)
            .GetEnumerator();

        var spacing = 60.0 / rate;
        var deadlineMinute = ToMinute(wave.DeadlineUtc);

        foreach (var (minute, sends) in result.Allocations)
        {
            for (var k = 0; k < sends && queue.MoveNext(); k++)
            {
                var slot = perMinute.GetValueOrDefault(minute);
                perMinute[minute] = slot + 1;
                var item = queue.Current;
                item.PlannedUtc = FromMinute(minute).AddSeconds(slot * spacing);
                item.IsLateRisk = minute >= deadlineMinute;
            }
        }
    }

    private static long ToMinute(DateTimeOffset t) => t.ToUnixTimeSeconds() / 60;
    private static DateTimeOffset FromMinute(long m) => DateTimeOffset.FromUnixTimeSeconds(m * 60);
}
