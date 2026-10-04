using AbyssBot.Core.Config;
using AbyssBot.Core.Logging;

namespace AbyssBot.Core.Engine;

public interface ISessionNotifier
{
    void Completed(RunStats stats);
    void ErrorStopped(RunResult result);
    void UserStopped();
    void AutoResumeWaiting(string message);
}

/// <summary>
/// 한 번의 실행(시작 버튼 ~ 사용자 정지). 첫 실행은 메뉴 열기부터.
/// 오류 정지 후 자동 재개가 켜져 있으면 화면 재판별로만 재개하고, 횟수를 제한한다.
/// 사용자 정지(F10/정지 버튼)는 자동 재개 예약까지 해제한다.
/// </summary>
public sealed class BotSession(
    ScenarioConfig scenario,
    AbyssEngine engine,
    Func<AutoResumeController>? autoResumeFactory,
    IClock clock,
    BotLogger log,
    ISessionNotifier? notifier = null)
{
    public RunResult Run(CancellationToken ct)
    {
        var start = scenario.FirstRun[0];
        bool reset = true;
        int resumes = 0;
        while (true)
        {
            var r = engine.Run(start, ct, reset);
            reset = false;
            if (r.Outcome == RunOutcome.Completed)
            {
                notifier?.Completed(engine.Stats);
                return r;
            }
            if (r.Outcome == RunOutcome.UserStopped || ct.IsCancellationRequested)
            {
                notifier?.UserStopped();
                return r with { Outcome = RunOutcome.UserStopped };
            }

            notifier?.ErrorStopped(r);
            if (!scenario.Options.AutoResumeEnabled || autoResumeFactory is null) return r;
            if (resumes >= scenario.Options.AutoResumeMaxCount)
            {
                log.Warn($"자동 재개 한도({scenario.Options.AutoResumeMaxCount}회) 도달 — 더 이상 재개하지 않음");
                return r;
            }

            var msg = $"오류 정지 후 자동 재개 대기 ({resumes + 1}/{scenario.Options.AutoResumeMaxCount}): " +
                      $"오류 후 {scenario.Options.AutoResumeAfterErrorMinutes}분, 마지막 사용자 입력 후 {scenario.Options.AutoResumeIdleMinutes}분이 지나면 화면을 다시 판별";
            log.Info(msg);
            notifier?.AutoResumeWaiting(msg);
            try
            {
                start = autoResumeFactory().WaitForResume(clock.Now, ct);
            }
            catch (OperationCanceledException)
            {
                log.Info("사용자 정지: 자동 재개 예약 해제");
                notifier?.UserStopped();
                return r with { Outcome = RunOutcome.UserStopped };
            }
            resumes++;
        }
    }
}
