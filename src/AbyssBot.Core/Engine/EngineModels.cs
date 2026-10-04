using AbyssBot.Core.Config;

namespace AbyssBot.Core.Engine;

public enum StepOutcome { Success, Failure, UserStopped }

public sealed record StepResult(StepOutcome Outcome, string? Reason = null)
{
    public static readonly StepResult Ok = new(StepOutcome.Success);
    public static StepResult Fail(string reason) => new(StepOutcome.Failure, reason);
}

public enum RunOutcome { UserStopped, Failed, Completed }

public sealed record RunResult(RunOutcome Outcome, StepId? Step, string Reason, string? EvidencePath);

public sealed record LastInput(DateTime Time, string Kind, string Target, OpenCvSharp.Point? Screen, bool Ok, string Message)
{
    public override string ToString() =>
        $"{Time:HH:mm:ss.fff} {Kind} {Target}{(Screen is { } p ? $" @({p.X},{p.Y})" : "")} → {(Ok ? "성공" : "실패")} {Message}";
}

/// <summary>UI 표시용 통계.</summary>
public sealed class RunStats
{
    private readonly List<TimeSpan> _durations = new();
    private DateTime? _runStart;

    public DateTime StartedAt { get; private set; }
    public int Completed { get; private set; }
    public TimeSpan? Average => _durations.Count == 0 ? null : TimeSpan.FromTicks((long)_durations.Average(d => d.Ticks));

    public void Reset(DateTime now) { StartedAt = now; Completed = 0; _durations.Clear(); _runStart = null; }

    /// <summary>전투 진입이 확인된 시각(첫 판).</summary>
    public void MarkBattleStart(DateTime now) => _runStart ??= now;

    /// <summary>결과 화면 처리 성공 = 한 판 완료.</summary>
    public void MarkCleared(DateTime now)
    {
        Completed++;
        if (_runStart is { } s) _durations.Add(now - s);
        _runStart = now; // 다음 판은 이번 결과부터 다음 결과까지
    }

    /// <summary>오류 정지 후 재개처럼 구간이 끊긴 경우 다음 판 시간을 새로 잰다.</summary>
    public void BreakSegment() => _runStart = null;
}

public interface IEngineObserver
{
    void StepChanged(StepId step);
    void StatsChanged(RunStats stats);
}
