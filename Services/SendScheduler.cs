namespace TimecardNotifier.Services;

/// <summary>
/// Pure, count-based scheduling math (no per-person objects), so it stays fast at tens of thousands
/// of reminders and can be reused by the capacity planner.
///
/// Time is measured in whole UTC minutes (unix seconds / 60). One shared limit (20/min) applies to every wave.
/// </summary>
public static class SendScheduler
{
    /// <param name="EarliestMinute">Earliest the wave may start (midnight local on the run date).</param>
    /// <param name="TargetMinute">Plan to be finished before this (deadline − safety margin).</param>
    /// <param name="DeadlineMinute">Sends in this minute or later are late (11:00 local).</param>
    public readonly record struct WaveInput(long EarliestMinute, long TargetMinute, long DeadlineMinute, int Count);

    public sealed record WaveResult(
        long? FirstMinute,
        long? LastMinute,
        int Late,
        int PastTarget,
        IReadOnlyList<(long Minute, int Sends)> Allocations);

    /// <summary>
    /// Picks each wave's start and runs the queue. Each wave starts as late as possible while every wave
    /// still finishes by its target (so reminders land as close to the working day as they can), but as
    /// early as needed. Starts are rounded down to 5 minutes.
    /// </summary>
    public static (long[] Starts, WaveResult[] Results) Plan(IReadOnlyList<WaveInput> waves, int ratePerMinute)
    {
        var latest = LatestStarts(waves, ratePerMinute);
        var starts = waves
            .Select((w, i) => Math.Max(w.EarliestMinute, latest[i] - ((latest[i] % 5) + 5) % 5))
            .ToArray();
        var results = RunForward(waves, starts, ratePerMinute);

        // Only possible if even starting at midnight isn't enough: start everything as early as allowed.
        if (results.Any(r => r.PastTarget > 0))
        {
            var earliest = waves.Select(w => w.EarliestMinute).ToArray();
            var fallback = RunForward(waves, earliest, ratePerMinute);
            if (fallback.Sum(r => r.Late) <= results.Sum(r => r.Late)) return (earliest, fallback);
        }
        return (starts, results);
    }

    /// <summary>
    /// Forward earliest-deadline-first: every minute, give the rate to whichever started waves have the
    /// earliest target. For one shared sender this finishes everyone on time whenever that is possible.
    /// </summary>
    public static WaveResult[] RunForward(IReadOnlyList<WaveInput> waves, long[] starts, int ratePerMinute)
    {
        var rate = Math.Max(1, ratePerMinute);
        var remaining = waves.Select(w => w.Count).ToArray();
        var allocs = waves.Select(_ => new List<(long, int)>()).ToArray();
        var late = new int[waves.Count];
        var pastTarget = new int[waves.Count];
        var byTarget = Enumerable.Range(0, waves.Count).OrderBy(i => waves[i].TargetMinute).ToArray();

        if (remaining.Sum() == 0)
            return waves.Select(_ => new WaveResult(null, null, 0, 0, Array.Empty<(long, int)>())).ToArray();

        long NextStart() => Enumerable.Range(0, waves.Count).Where(i => remaining[i] > 0).Min(i => starts[i]);

        var t = NextStart();
        while (remaining.Any(r => r > 0))
        {
            var capacity = rate;
            var anyStarted = false;
            foreach (var i in byTarget)
            {
                if (remaining[i] == 0 || starts[i] > t) continue;
                anyStarted = true;
                if (capacity == 0) break;
                var take = Math.Min(capacity, remaining[i]);
                remaining[i] -= take;
                capacity -= take;
                allocs[i].Add((t, take));
                if (t >= waves[i].DeadlineMinute) late[i] += take;
                if (t >= waves[i].TargetMinute) pastTarget[i] += take;
            }
            t = anyStarted ? t + 1 : NextStart(); // jump idle gaps
        }

        return waves.Select((_, i) => new WaveResult(
            allocs[i].Count > 0 ? allocs[i][0].Item1 : null,
            allocs[i].Count > 0 ? allocs[i][^1].Item1 : null,
            late[i],
            pastTarget[i],
            allocs[i])).ToArray();
    }

    /// <summary>
    /// Backward fill ("as late as possible"): latest target first, each wave takes the latest free minutes
    /// before its target. Returns, per wave, the latest minute it can start with everyone still on time.
    /// </summary>
    public static long[] LatestStarts(IReadOnlyList<WaveInput> waves, int ratePerMinute)
    {
        var rate = Math.Max(1, ratePerMinute);
        var used = new Dictionary<long, int>();
        var starts = waves.Select(w => w.TargetMinute).ToArray();

        foreach (var i in Enumerable.Range(0, waves.Count).OrderByDescending(i => waves[i].TargetMinute))
        {
            var remaining = waves[i].Count;
            var m = waves[i].TargetMinute - 1;
            while (remaining > 0)
            {
                var free = rate - used.GetValueOrDefault(m);
                if (free > 0)
                {
                    var take = Math.Min(free, remaining);
                    used[m] = used.GetValueOrDefault(m) + take;
                    remaining -= take;
                    starts[i] = m;
                }
                m--;
            }
        }
        return starts;
    }
}
