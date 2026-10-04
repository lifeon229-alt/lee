using AbyssBot.Core.Config;
using AbyssBot.Core.Logging;

namespace AbyssBot.Core.Engine;

public interface IUserActivity
{
    /// <summary>마지막 사용자 키보드·마우스 입력 시각.</summary>
    DateTime LastInput { get; }
}

public interface IResumeNotifier
{
    void Countdown(int secondsLeft, Classification c);
    void Cancelled(string why);
}

/// <summary>
/// 오류 정지 후 자동 재개(선택 기능). 입력 없이 화면만 다시 판별한다.
/// 조건: 오류 후 N분, 마지막 사용자 입력 후 N분, 게임 활성, 같은 단계 연속 K회 확인.
/// 그 뒤 알림과 카운트다운, 그 사이 사용자 입력이 있으면 취소, 재개 직전 다시 확인.
/// </summary>
public sealed class AutoResumeController(
    OptionsSpec options,
    IClock clock,
    IWaiter waiter,
    IUserActivity activity,
    IGameWindow window,
    Func<Classification?> classify,
    BotLogger log,
    IResumeNotifier? notifier = null)
{
    /// <summary>재개할 단계를 반환. 사용자 정지(취소 토큰) 시 OperationCanceledException.</summary>
    public StepId WaitForResume(DateTime errorAt, CancellationToken ct)
    {
        var errorWait = TimeSpan.FromMinutes(options.AutoResumeAfterErrorMinutes);
        var idleWait = TimeSpan.FromMinutes(options.AutoResumeIdleMinutes);
        string? lastReason = null;

        while (true)
        {
            // 1) 시간 조건
            while (true)
            {
                var now = clock.Now;
                if (now - errorAt >= errorWait && now - activity.LastInput >= idleWait) break;
                waiter.Wait(1000, ct);
            }

            // 2) 같은 단계 연속 확인(입력 없음)
            StepId? prev = null;
            int streak = 0;
            while (streak < options.AutoResumeStableChecks)
            {
                waiter.Wait(options.AutoResumeCheckIntervalMs, ct);
                if (clock.Now - activity.LastInput < idleWait) { streak = 0; prev = null; goto again; }
                if (!window.IsForeground()) { streak = 0; prev = null; Note(ref lastReason, "게임 창 비활성"); continue; }
                var c = classify();
                if (c is null || !c.Known) { streak = 0; prev = null; Note(ref lastReason, c?.Reason ?? "캡처 실패"); continue; }
                streak = prev == c.Step ? streak + 1 : 1;
                prev = c.Step;
            }

            // 3) 알림 + 카운트다운. 사용자 입력이 있으면 취소.
            var startInput = activity.LastInput;
            var shown = classify();
            if (shown is not { Known: true } || shown.Step != prev) goto again;
            log.Warn($"자동 재개 예정: {shown.Reason} — {options.AutoResumeCountdownSeconds}초 뒤 재개(입력하면 취소)");
            bool cancelled = false;
            for (int s = options.AutoResumeCountdownSeconds; s > 0; s--)
            {
                notifier?.Countdown(s, shown);
                for (int k = 0; k < 10; k++)
                {
                    waiter.Wait(100, ct);
                    if (activity.LastInput != startInput) { cancelled = true; break; }
                }
                if (cancelled) break;
            }
            if (cancelled)
            {
                log.Info("자동 재개 취소: 카운트다운 중 사용자 입력");
                notifier?.Cancelled("사용자 입력");
                goto again;
            }

            // 4) 재개 직전 재확인
            var final = window.IsForeground() ? classify() : null;
            if (final is { Known: true } && final.Step == prev)
            {
                log.Info($"자동 재개: {final.Reason}");
                return final.Step!.Value;
            }
            log.Info($"자동 재개 취소: 재개 직전 화면이 바뀜 ({final?.Reason ?? "게임 비활성"})");
            notifier?.Cancelled("화면 변경");

            again:
            continue;
        }
    }

    private void Note(ref string? last, string reason)
    {
        if (reason == last) return;
        last = reason;
        log.Info($"자동 재개 대기: {reason}");
    }
}
