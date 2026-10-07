using TimecardNotifier.Models;

namespace TimecardNotifier.Services;

/// <summary>
/// Supplies employees and the days with no hours, from last week's Friday up to yesterday.
/// Back end: the Snowflake query, run early each morning so results are ready before the first wave.
/// The planner applies the rules: weekends and holidays don't count, exempt cost centers are removed,
/// and anyone with at least one missing workday gets a single reminder.
/// </summary>
public interface IRecipientSource
{
    Task<IReadOnlyList<Recipient>> GetRecipientsAsync(DateOnly runDate, CancellationToken ct = default);
}

/// <summary>Company holiday calendar, checked per recipient by country and (optionally) time zone.</summary>
public interface IHolidayService
{
    Task<IReadOnlyList<Holiday>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Holiday holiday, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns the holiday that applies to this country/time zone on this date, if any.</summary>
    Holiday? FindFor(string countryCode, string timeZoneId, DateOnly date);
}

/// <summary>
/// Cost centers whose employees are never reminded (e.g. salaried or contractor cost centers),
/// regardless of whether they've entered hours.
/// </summary>
public interface IExemptionService
{
    Task<IReadOnlyList<CostCenterExemption>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Adds or updates an exemption (matched on normalized code).</summary>
    Task AddAsync(CostCenterExemption exemption, CancellationToken ct = default);
    Task RemoveAsync(string code, CancellationToken ct = default);

    /// <summary>Returns the exemption for this cost center code, if any.</summary>
    CostCenterExemption? Find(string costCenterCode);
}

/// <summary>Sends one Teams reminder (chat message and/or activity feed notification with the adaptive card).</summary>
public interface INotificationSender
{
    /// <param name="linkDate">Date the card's timecard link opens (the employee's earliest missing workday).</param>
    Task<SendResult> SendAsync(DispatchItem item, NotifierSettings settings, DateOnly linkDate, CancellationToken ct = default);
}
