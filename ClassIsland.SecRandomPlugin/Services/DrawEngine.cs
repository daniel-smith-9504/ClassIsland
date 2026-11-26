using System.Security.Cryptography;
using ClassIsland.SecRandomPlugin.Models;

namespace ClassIsland.SecRandomPlugin.Services;

public enum DrawStatus
{
    Success,
    NoCandidates,
    NoCandidatesInScope,
    RepeatLimitExhausted,
    NoEligibleCandidates
}

public sealed class DrawOutcome
{
    public DrawStatus Status { get; init; }
    public IReadOnlyList<Student> Selected { get; init; } = [];
    public bool IsSuccess => Status == DrawStatus.Success;
}

/// <summary>
/// 参考 SecRandom 的公平抽取算法实现：
/// 权重是候选人与「平均抽取次数」档位的距离，领先过多的学生权重降为 0，
/// 并可选地在抽取后的一段时间内让学生进入冷却（权重为 0）。
/// 抽样使用加密随机数，按权重不放回抽样。
/// </summary>
public static class DrawEngine
{
    /// <summary>
    /// 允许某位候选人领先最少抽取者几个档位。达到该领先量后权重为 0。
    /// </summary>
    private const int MaxLeadTiers = 2;

    public static DrawOutcome Draw(
        IReadOnlyList<Student> students,
        IReadOnlyDictionary<Guid, int> drawCounts,
        IReadOnlyDictionary<Guid, DateTime> lastDrawTimes,
        int requestedCount,
        SecRandomConfig config,
        DateTime now)
    {
        var count = Math.Max(1, requestedCount);
        var candidates = students.Where(IsCandidate).Where(student => MatchesScope(student, config)).ToList();
        if (candidates.Count == 0)
        {
            var scopeLimited = config.ScopeType != SecRandomConstants.ScopeTypeAll &&
                               !string.IsNullOrWhiteSpace(config.ScopeValue);
            return new DrawOutcome
            {
                Status = scopeLimited ? DrawStatus.NoCandidatesInScope : DrawStatus.NoCandidates
            };
        }

        var threshold = ResolveRepeatThreshold(config);
        var eligible = threshold <= 0
            ? candidates
            : candidates.Where(student => drawCounts.GetValueOrDefault(student.RecordId) < threshold).ToList();

        if (eligible.Count < count)
        {
            return new DrawOutcome { Status = DrawStatus.RepeatLimitExhausted };
        }

        var weights = BuildWeights(eligible, drawCounts, lastDrawTimes, count, config, now);
        if (weights.Count(weight => weight > 0) < count)
        {
            return new DrawOutcome { Status = DrawStatus.NoEligibleCandidates };
        }

        return new DrawOutcome
        {
            Status = DrawStatus.Success,
            Selected = WeightedSample(eligible, weights, count)
        };
    }

    public static bool IsCandidate(Student student) =>
        student.IsEnabled &&
        (!string.IsNullOrWhiteSpace(student.Name) || !string.IsNullOrWhiteSpace(student.Id));

    /// <summary>
    /// 判断学生是否落在当前抽取范围内。范围为空时视为全部学生。
    /// </summary>
    public static bool MatchesScope(Student student, SecRandomConfig config)
    {
        if (config.ScopeType == SecRandomConstants.ScopeTypeAll || string.IsNullOrWhiteSpace(config.ScopeValue))
        {
            return true;
        }

        var value = config.ScopeValue.Trim();
        return config.ScopeType switch
        {
            SecRandomConstants.ScopeTypeGroup => student.Group.Trim() == value,
            SecRandomConstants.ScopeTypeGender => student.Gender.Trim() == value,
            SecRandomConstants.ScopeTypeTag => student.Tag.Trim() == value,
            _ => true
        };
    }

    /// <summary>
    /// 将重复控制转换为历史次数阈值：0 表示不限制，1 表示本轮内不重复，其它值为「半重复」上限。
    /// </summary>
    public static int ResolveRepeatThreshold(SecRandomConfig config) => config.RepeatMode switch
    {
        SecRandomConstants.RepeatModeRepeat => 0,
        SecRandomConstants.RepeatModeNoRepeat => 1,
        SecRandomConstants.RepeatModeHalfRepeat => Math.Max(1, config.HalfRepeat),
        _ => 1
    };

    private static List<double> BuildWeights(
        List<Student> candidates,
        IReadOnlyDictionary<Guid, int> drawCounts,
        IReadOnlyDictionary<Guid, DateTime> lastDrawTimes,
        int count,
        SecRandomConfig config,
        DateTime now)
    {
        if (config.DrawType != SecRandomConstants.DrawTypeFair)
        {
            return Enumerable.Repeat(1.0, candidates.Count).ToList();
        }

        var counts = candidates
            .Select(student => Math.Max(0, drawCounts.GetValueOrDefault(student.RecordId)))
            .ToArray();
        var minCount = counts.Min();
        // 还有人一次都没抽到时，先把领先上限压到 1，保证「先全员抽一遍」，
        // 之后再放开到两档，让真正的随机性体现出来。
        var leadCap = minCount == 0 ? 1 : MaxLeadTiers;
        var ceiling = minCount + leadCap;

        if (counts.Count(value => value < ceiling) < count)
        {
            var sorted = counts.OrderBy(value => value).ToArray();
            ceiling = Math.Max(ceiling, sorted[Math.Clamp(count, 1, sorted.Length) - 1] + 1);
        }

        return candidates.Select((student, index) =>
        {
            var duration = ResolveShieldDuration(student, config);
            if (duration is { } effectiveDuration &&
                lastDrawTimes.TryGetValue(student.RecordId, out var lastTime) &&
                now - lastTime < effectiveDuration)
            {
                return 0.0;
            }

            return Math.Max(ceiling - counts[index], 0.0);
        }).ToList();
    }

    /// <summary>
    /// 优先使用学生单独的冷却时长；未设置时回退到全局冷却。
    /// </summary>
    private static TimeSpan? ResolveShieldDuration(Student student, SecRandomConfig config)
    {
        if (student.CooldownMinutes > 0)
        {
            return TimeSpan.FromMinutes(student.CooldownMinutes);
        }

        return config.ShieldEnabled && config.ShieldMinutes > 0
            ? TimeSpan.FromMinutes(config.ShieldMinutes)
            : null;
    }

    private static List<Student> WeightedSample(List<Student> candidates, List<double> weights, int count)
    {
        var pool = candidates.Select((student, index) => (Student: student, Weight: weights[index])).ToList();
        var totalWeight = pool.Sum(item => item.Weight);
        var result = new List<Student>(count);

        for (var i = 0; i < count && pool.Count > 0; i++)
        {
            var point = NextDouble() * totalWeight;
            var picked = pool.Count - 1;
            for (var j = 0; j < pool.Count; j++)
            {
                point -= pool[j].Weight;
                if (point < 0)
                {
                    picked = j;
                    break;
                }
            }

            result.Add(pool[picked].Student);
            totalWeight -= pool[picked].Weight;
            pool.RemoveAt(picked);
        }

        return result;
    }

    private static double NextDouble()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt64(bytes);
        return (value >> 11) * (1.0 / (1UL << 53));
    }
}
