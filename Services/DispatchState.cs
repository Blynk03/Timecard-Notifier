using TimecardNotifier.Models;

namespace TimecardNotifier.Services;

/// <summary>
/// App-wide state for the selected run date: the recipients, the computed plan and the
/// (simulated) dispatch progress. Pages subscribe to <see cref="Changed"/>.
/// </summary>
public sealed class DispatchState
{
    private readonly IRecipientSource _source;
    private readonly IHolidayService _holidays;
    private readonly IExemptionService _exemptions;
    private readonly INotificationSender _sender;
    private readonly DispatchPlanner _planner;
    private readonly NotifierSettings _settings;

    private IReadOnlyList<Recipient> _recipients = Array.Empty<Recipient>();
    private CancellationTokenSource? _runCts;

    public DispatchState(IRecipientSource source, IHolidayService holidays, IExemptionService exemptions,
        INotificationSender sender, DispatchPlanner planner, NotifierSettings settings)
    {
        _source = source;
        _holidays = holidays;
        _exemptions = exemptions;
        _sender = sender;
        _planner = planner;
        _settings = settings;
        _settings.Changed += Replan;
    }

    public DateOnly RunDate { get; private set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>Sunday that starts the run date's work week.</summary>
    public DateOnly WeekStart => TimeZoneCatalog.WeekStart(RunDate);

    /// <summary>Weekdays earlier this week that are checked for missing hours (before per-recipient holidays).</summary>
    public IReadOnlyList<DateOnly> PriorWeekdays => TimeZoneCatalog.PriorWeekdays(RunDate);

    /// <summary>True on Monday: the only day checked is last week's Friday.</summary>
    public bool IsFridayCatchUp => PriorWeekdays.Count == 0 && RunDate.DayOfWeek == DayOfWeek.Monday;

    /// <summary>A representative missing day for previews (first checked day).</summary>
    public DateOnly SampleWorkday => PriorWeekdays.Count > 0 ? PriorWeekdays[0] : TimeZoneCatalog.PreviousFriday(RunDate);

    /// <summary>e.g. "Mon Oct 5 – Tue Oct 6", "Friday, Oct 2" on Mondays, or null on weekends.</summary>
    public string? CheckedRangeLabel => PriorWeekdays.Count switch
    {
        0 when IsFridayCatchUp => TimeZoneCatalog.PreviousFriday(RunDate).ToString("dddd, MMM d"),
        0 => null,
        1 => PriorWeekdays[0].ToString("dddd, MMM d"),
        _ => $"{PriorWeekdays[0]:ddd, MMM d} – {PriorWeekdays[^1]:ddd, MMM d}",
    };
    public DispatchPlan? Plan { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsRunning { get; private set; }
    public DateTimeOffset? SimulatedNowUtc { get; private set; }
    public DateTimeOffset? LastRefreshedUtc { get; private set; }
    public List<DispatchItem> RecentActivity { get; } = new();

    /// <summary>Cost centers seen in the latest query result (code, name, headcount), for the exemptions picker.</summary>
    public IEnumerable<(string Code, string Name, int Count)> KnownCostCenters => _recipients
        .GroupBy(r => (r.CostCenterCode, r.CostCenterName))
        .Select(g => (g.Key.CostCenterCode, g.Key.CostCenterName, g.Count()))
        .OrderBy(c => c.CostCenterCode);

    public event Action? Changed;

    public async Task EnsureLoadedAsync()
    {
        if (Plan is null && !IsLoading) await LoadAsync(RunDate);
    }

    public async Task LoadAsync(DateOnly runDate)
    {
        Cancel();
        RunDate = runDate;
        IsLoading = true;
        Notify();
        try
        {
            _recipients = await _source.GetRecipientsAsync(runDate);
            LastRefreshedUtc = DateTimeOffset.UtcNow;
            Plan = null;
            RecentActivity.Clear();
            SimulatedNowUtc = null;
            Plan = _planner.Build(RunDate, _recipients, _holidays, _exemptions, _settings);
        }
        finally
        {
            IsLoading = false;
            Notify();
        }
    }

    /// <summary>Rebuild the plan from the cached recipients (after a settings or holiday change).</summary>
    public void Replan()
    {
        if (Plan is null && _recipients.Count == 0) return; // nothing loaded yet; LoadAsync will plan
        Cancel();
        RecentActivity.Clear();
        SimulatedNowUtc = null;
        Plan = _planner.Build(RunDate, _recipients, _holidays, _exemptions, _settings);
        Notify();
    }

    /// <summary>
    /// Plays the plan back in fast-forward: each send happens at its planned slot on a simulated clock.
    /// In production the back end does the sending; this lets you watch the throttle and ordering.
    /// </summary>
    public async Task SimulateAsync()
    {
        if (Plan is null || IsRunning) return;

        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;
        IsRunning = true;
        Notify();

        try
        {
            // One tick per planned minute (up to the rate limit per tick), so ~13k reminders replay in ~20 s.
            var minutes = Plan.Items
                .Where(i => i.Status == DispatchStatus.Queued && i.PlannedUtc is not null)
                .GroupBy(i => i.PlannedUtc!.Value.ToUnixTimeSeconds() / 60)
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var batch in minutes)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var item in batch)
                {
                    var result = await _sender.SendAsync(item, _settings, LinkDate(item), ct);
                    item.Status = result.Success ? DispatchStatus.Sent : DispatchStatus.Failed;
                    item.SentUtc = item.PlannedUtc;
                    item.Note = result.Error ?? item.Note;
                }

                Plan?.Recount();
                SimulatedNowUtc = batch.Max(i => i.PlannedUtc);
                RecentActivity.InsertRange(0, batch.OrderByDescending(i => i.PlannedUtc).Take(8));
                if (RecentActivity.Count > 8) RecentActivity.RemoveRange(8, RecentActivity.Count - 8);
                Notify();

                await Task.Delay(25, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user or by a replan.
        }
        finally
        {
            IsRunning = false;
            Plan?.Recount();
            Notify();
        }
    }

    /// <summary>Retry one failed recipient (UI action; the back end would re-queue it).</summary>
    public async Task RetryAsync(DispatchItem item)
    {
        item.Status = DispatchStatus.Sending;
        Plan?.Recount();
        Notify();
        await Task.Delay(300);
        var result = await _sender.SendAsync(item, _settings, LinkDate(item));
        item.Status = result.Success ? DispatchStatus.Sent : DispatchStatus.Failed;
        item.Note = result.Error;
        Plan?.Recount();
        Notify();
    }

    public void Cancel()
    {
        if (_runCts is { IsCancellationRequested: false }) _runCts.Cancel();
    }

    /// <summary>The card's timecard link opens the employee's earliest missing workday.</summary>
    private DateOnly LinkDate(DispatchItem item) => item.MissingWorkdays.Count > 0 ? item.MissingWorkdays[0] : SampleWorkday;

    private void Notify() => Changed?.Invoke();
}
