using TimecardNotifier.Models;

namespace TimecardNotifier.Services;

/// <summary>
/// In-memory holiday calendar with sample 2026 holidays. Replace with your company's observed-holiday API.
/// A holiday with a TimeZoneId applies only to cost centers in that zone (e.g. a provincial holiday).
/// </summary>
public sealed class MockHolidayService : IHolidayService
{
    private readonly List<Holiday> _holidays = new()
    {
        // United States
        new(new DateOnly(2026, 11, 26), "Thanksgiving Day", "US"),
        new(new DateOnly(2026, 11, 27), "Day after Thanksgiving", "US"),
        new(new DateOnly(2026, 12, 24), "Christmas Eve", "US"),
        new(new DateOnly(2026, 12, 25), "Christmas Day", "US"),
        new(new DateOnly(2027, 1, 1), "New Year's Day", "US"),

        // Canada
        new(new DateOnly(2026, 10, 12), "Thanksgiving", "CA"),
        new(new DateOnly(2026, 11, 11), "Remembrance Day", "CA", "America/Edmonton"),
        new(new DateOnly(2026, 12, 25), "Christmas Day", "CA"),
        new(new DateOnly(2026, 12, 28), "Boxing Day (observed)", "CA"),
        new(new DateOnly(2027, 1, 1), "New Year's Day", "CA"),

        // Mexico
        new(new DateOnly(2026, 11, 16), "Revolution Day (observed)", "MX"),
        new(new DateOnly(2026, 12, 25), "Christmas Day", "MX"),
        new(new DateOnly(2027, 1, 1), "New Year's Day", "MX"),

        // United Kingdom
        new(new DateOnly(2026, 12, 25), "Christmas Day", "GB"),
        new(new DateOnly(2026, 12, 28), "Boxing Day (substitute)", "GB"),
        new(new DateOnly(2027, 1, 1), "New Year's Day", "GB"),
    };

    public Task<IReadOnlyList<Holiday>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Holiday>>(_holidays.OrderBy(h => h.Date).ThenBy(h => h.CountryCode).ToList());

    public Task AddAsync(Holiday holiday, CancellationToken ct = default)
    {
        _holidays.Add(holiday);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        _holidays.RemoveAll(h => h.Id == id);
        return Task.CompletedTask;
    }

    public Holiday? FindFor(string countryCode, string timeZoneId, DateOnly date) =>
        _holidays.FirstOrDefault(h =>
            h.Date == date &&
            h.CountryCode == countryCode &&
            (h.TimeZoneId is null || h.TimeZoneId == timeZoneId));
}
