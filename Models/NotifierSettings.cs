namespace TimecardNotifier.Models;

/// <summary>Front-end configuration for the reminder run. Persist via the back end when it exists.</summary>
public sealed class NotifierSettings
{
    /// <summary>Microsoft's limit. Fixed: it can't be raised, and every wave shares it.</summary>
    public const int RateLimitPerMinute = 20;

    public const string DefaultMessage =
        "It looks like you may have unrecorded work hours for the current week. Please enter your work hours as soon as possible.";

    /// <summary>Every reminder must be sent before this local time.</summary>
    public TimeOnly Deadline { get; set; } = new(11, 0);

    /// <summary>
    /// Plan to finish this many minutes before the deadline, leaving room for retries and slow sends.
    /// Waves start as late as possible while still finishing by (deadline − margin), and as early as needed.
    /// </summary>
    public int SafetyMarginMinutes { get; set; } = 15;

    public TimeOnly PlanningDeadline => Deadline.AddMinutes(-SafetyMarginMinutes);

    public DeliveryChannel Channel { get; set; } = DeliveryChannel.Both;

    public bool SkipWeekends { get; set; } = true;
    public bool SkipHolidays { get; set; } = true;

    public string CardTitle { get; set; } = "Timecard reminder";
    public string CardMessage { get; set; } = DefaultMessage;
    public string ButtonText { get; set; } = "Open my timecard";

    /// <summary>{date} is replaced with the employee's earliest missing workday; {employeeId} with the employee id.</summary>
    public string TimecardUrlTemplate { get; set; } = "https://timecard.contoso.com/my/timesheet?date={date}";

    public string BuildTimecardUrl(DateOnly workday, string employeeId) =>
        TimecardUrlTemplate
            .Replace("{date}", workday.ToString("yyyy-MM-dd"))
            .Replace("{employeeId}", Uri.EscapeDataString(employeeId));

    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}
