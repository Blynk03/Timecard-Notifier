namespace TimecardNotifier.Models;

/// <summary>
/// One employee returned by the timecard query, with the days this week (Sunday up to yesterday)
/// that have no hours recorded. The planner decides which of those days count.
/// </summary>
public sealed record Recipient(
    string EmployeeId,
    string DisplayName,
    string Email,
    string CostCenterCode,
    string CostCenterName,
    string CountryCode,
    string TimeZoneId,
    bool HasTeamsAccount = true)
{
    /// <summary>Days earlier this week with no hours entered (may include weekends/holidays, which are ignored).</summary>
    public IReadOnlyList<DateOnly> MissingDays { get; init; } = Array.Empty<DateOnly>();

    public string CostCenter => $"{CostCenterCode} {CostCenterName}";
}

/// <summary>A company holiday. TimeZoneId == null means it applies to the whole country.</summary>
public sealed record Holiday(DateOnly Date, string Name, string CountryCode, string? TimeZoneId = null)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

/// <summary>A cost center whose employees are never reminded, whether or not they've entered hours.</summary>
public sealed record CostCenterExemption(string Code, string? Name, string? Reason, DateOnly AddedOn)
{
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}

public enum DeliveryChannel
{
    ChatMessage,
    ActivityFeed,
    Both,
}

public enum DispatchStatus
{
    Queued,
    Sending,
    Sent,
    Failed,
    Exempt,
    SkippedWeekend,
    SkippedHoliday,

    /// <summary>Only missing days this week are weekends/holidays (or nothing is missing): no reminder.</summary>
    NoMissingWorkdays,
}

/// <summary>A recipient plus everything the UI tracks about today's reminder for them.</summary>
public sealed class DispatchItem
{
    public required Recipient Recipient { get; init; }
    public DispatchStatus Status { get; set; } = DispatchStatus.Queued;

    /// <summary>The workdays earlier this week that are missing hours and count toward a reminder.</summary>
    public IReadOnlyList<DateOnly> MissingWorkdays { get; set; } = Array.Empty<DateOnly>();

    public DateTimeOffset? PlannedUtc { get; set; }
    public DateTimeOffset? SentUtc { get; set; }
    public bool IsLateRisk { get; set; }
    public Holiday? Holiday { get; set; }
    public string? Note { get; set; }

    /// <summary>True for anyone who will not be sent a reminder today.</summary>
    public bool IsSkipped => Status is DispatchStatus.Exempt or DispatchStatus.SkippedWeekend
        or DispatchStatus.SkippedHoliday or DispatchStatus.NoMissingWorkdays;
}

/// <summary>
/// All recipients whose cost-center time zones share the same UTC offset on the run date
/// (e.g. New York + Toronto). They share one 11:00 deadline, so they're sent as one queue.
/// </summary>
public sealed class SendWave
{
    public required TimeSpan UtcOffset { get; init; }
    public required DateTimeOffset DeadlineUtc { get; init; }

    /// <summary>Deadline minus the safety margin: what the plan aims for.</summary>
    public required DateTimeOffset TargetUtc { get; init; }

    public bool IsWeekend { get; init; }
    public List<DispatchItem> Items { get; } = new();

    public string OffsetLabel => TimeZoneCatalog.FormatOffset(UtcOffset);

    /// <summary>Zones in this wave with how many reminders each will get, largest first.</summary>
    public IReadOnlyList<(ZoneDef Zone, int Count)> Zones { get; private set; } = Array.Empty<(ZoneDef, int)>();

    /// <summary>Any zone id in the wave, for converting times to wave-local wall clock (all share the offset).</summary>
    public string ReferenceZoneId => Items[0].Recipient.TimeZoneId;

    // Counts are cached (thousands of items per wave); call Recount() after statuses change.
    public DateTimeOffset? FirstSendUtc { get; private set; }
    public DateTimeOffset? LastSendUtc { get; private set; }
    public int MinutesUsed { get; private set; }
    public int Total { get; private set; }
    public int ToSend { get; private set; }
    public int Exempt { get; private set; }
    public int Skipped { get; private set; }
    public int UpToDate { get; private set; }
    public int Sent { get; private set; }
    public int Failed { get; private set; }
    public int LateRisk { get; private set; }
    public bool AnySending { get; private set; }

    public int NotNotified => Exempt + Skipped + UpToDate;

    public void Recount()
    {
        Total = Items.Count;
        ToSend = Exempt = Skipped = UpToDate = Sent = Failed = LateRisk = 0;
        AnySending = false;
        DateTimeOffset? first = null, last = null;
        var minutes = new HashSet<long>();
        foreach (var i in Items)
        {
            if (!i.IsSkipped) ToSend++;
            if (i.Status == DispatchStatus.Exempt) Exempt++;
            if (i.Status is DispatchStatus.SkippedHoliday or DispatchStatus.SkippedWeekend) Skipped++;
            if (i.Status == DispatchStatus.NoMissingWorkdays) UpToDate++;
            if (i.Status == DispatchStatus.Sent) Sent++;
            if (i.Status == DispatchStatus.Failed) Failed++;
            if (i.Status == DispatchStatus.Sending) AnySending = true;
            if (i.IsLateRisk) LateRisk++;
            if (i.PlannedUtc is { } p)
            {
                if (first is null || p < first) first = p;
                if (last is null || p > last) last = p;
                minutes.Add(p.ToUnixTimeSeconds() / 60);
            }
        }
        FirstSendUtc = first;
        LastSendUtc = last;
        MinutesUsed = minutes.Count;
        if (Zones.Count == 0) // skip statuses are fixed once planned, so this only needs computing once
        {
            Zones = Items
                .GroupBy(i => i.Recipient.TimeZoneId)
                .Select(g => (TimeZoneCatalog.Get(g.Key), g.Count(i => !i.IsSkipped)))
                .OrderByDescending(z => z.Item2)
                .ToList();
        }
    }

    public string Status =>
        IsWeekend ? "Weekend"
        : ToSend == 0 && Skipped > 0 ? "Holiday"
        : ToSend == 0 ? "Nothing to send"
        : Sent + Failed == ToSend ? "Complete"
        : AnySending || Sent > 0 ? "Sending"
        : "Scheduled";
}

public sealed class DispatchPlan
{
    public required DateOnly RunDate { get; init; }
    public required List<SendWave> Waves { get; init; }

    /// <summary>Sends booked per UTC minute across all waves. Never exceeds the rate limit.</summary>
    public required IReadOnlyDictionary<long, int> SendsPerMinute { get; init; }

    public int PeakPerMinute => SendsPerMinute.Count == 0 ? 0 : SendsPerMinute.Values.Max();

    public IEnumerable<DispatchItem> Items => Waves.SelectMany(w => w.Items);

    public void Recount() => Waves.ForEach(w => w.Recount());
    public int Total => Waves.Sum(w => w.Total);
    public int ToSend => Waves.Sum(w => w.ToSend);
    public int Exempt => Waves.Sum(w => w.Exempt);
    public int Skipped => Waves.Sum(w => w.Skipped);
    public int UpToDate => Waves.Sum(w => w.UpToDate);
    public int Sent => Waves.Sum(w => w.Sent);
    public int Failed => Waves.Sum(w => w.Failed);
    public int LateRisk => Waves.Sum(w => w.LateRisk);
}

public sealed record SendResult(bool Success, string? Error = null);
