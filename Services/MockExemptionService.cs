using TimecardNotifier.Models;

namespace TimecardNotifier.Services;

/// <summary>In-memory exemption list with one example. Replace with your API / table.</summary>
public sealed class MockExemptionService : IExemptionService
{
    private readonly Dictionary<string, CostCenterExemption> _items = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CC-2190"] = new("CC-2190", "Salaried Engineering", "Example: salaried staff, no hourly timecard", new DateOnly(2026, 9, 14)),
    };

    public Task<IReadOnlyList<CostCenterExemption>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CostCenterExemption>>(_items.Values.OrderBy(e => e.Code).ToList());

    public Task AddAsync(CostCenterExemption exemption, CancellationToken ct = default)
    {
        var code = CostCenterExemption.Normalize(exemption.Code);
        _items[code] = exemption with { Code = code };
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string code, CancellationToken ct = default)
    {
        _items.Remove(CostCenterExemption.Normalize(code));
        return Task.CompletedTask;
    }

    public CostCenterExemption? Find(string costCenterCode) =>
        _items.GetValueOrDefault(CostCenterExemption.Normalize(costCenterCode));
}
