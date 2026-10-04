using AbyssBot.Core.Config;
using AbyssBot.Core.Engine;
using AbyssBot.Core.Logging;
using Xunit;

namespace AbyssBot.Tests;

public class ResumeTests
{
    private sealed class Activity(FakeTime t) : IUserActivity
    {
        public DateTime LastInput { get; set; } = t.Now;
    }

    [Fact]
    public void Classifier_maps_screens_and_rejects_unknown_or_conflicts()
    {
        var rig = new Rig();
        var c = new ScreenClassifier(rig.Det, "dest_husang", false);
        OpenCvSharp.Mat Cap(string state)
        {
            rig.Game.State = state; rig.Game.Extra = new HashSet<string>();
            return rig.Win.Capture().Frame!.Image;
        }
        Classification Of(string state, params string[] extra)
        {
            rig.Game.State = state; rig.Game.Extra = new HashSet<string>(extra);
            rig.Game.BattleFramesLeft = 100;
            var cap = rig.Win.Capture();
            return c.Classify(cap.Frame!.Image);
        }
        Assert.Equal(StepId.WaitResult, Of("result").Step);
        Assert.Equal(StepId.OtherDungeon, Of("reward").Step);
        Assert.Null(Of("rewardOnlyReplay").Step);   // 선택한 방식의 버튼이 없으면 판별 안 함
        Assert.Equal(StepId.Replay, new ScreenClassifier(rig.Det, "dest_husang", false, RepeatMode.Replay).Classify(Cap("reward")).Step);
        Assert.Equal(StepId.Enter, Of("destSelected").Step);
        Assert.Equal(StepId.SelectDestination, Of("destList").Step);
        Assert.Equal(StepId.SelectAbyss, Of("menu").Step);
        Assert.Null(Of("battle").Step);          // 채팅창만으로 전투 중이라 단정하지 않음
        Assert.Null(Of("town").Step);
        Assert.Null(Of("unknown").Step);
        Assert.Null(Of("result", TargetIds.OtherDungeon).Step); // 충돌
    }

    private static (AutoResumeController ctl, FakeTime time, Activity act, Rig rig) Make(string state)
    {
        var rig = new Rig();
        rig.Game.State = state;
        var act = new Activity(rig.Time);
        var cls = new ScreenClassifier(rig.Det, "dest_husang", false);
        var ctl = new AutoResumeController(rig.Scenario.Options, rig.Time, rig.Time, act, rig.Win,
            () => { var cap = rig.Win.Capture(); return cap.Frame is null ? null : cls.Classify(cap.Frame.Image); },
            rig.Log);
        return (ctl, rig.Time, act, rig);
    }

    [Fact]
    public void Resumes_only_after_both_idle_windows_and_stable_checks()
    {
        var (ctl, time, act, rig) = Make("result");
        var errorAt = time.Now;
        var step = ctl.WaitForResume(errorAt, CancellationToken.None);
        Assert.Equal(StepId.WaitResult, step);
        Assert.True(time.Now - errorAt >= TimeSpan.FromMinutes(10));
        Assert.Empty(rig.Game.Inputs);
    }

    [Fact]
    public void User_input_during_countdown_cancels()
    {
        var (ctl, time, act, rig) = Make("reward");
        bool poked = false;
        time.OnWait = () =>
        {
            if (!poked && rig.Lines.Any(l => l.Contains("자동 재개 예정"))) { poked = true; act.LastInput = time.Now; }
        };
        var step = ctl.WaitForResume(time.Now, CancellationToken.None);
        Assert.True(poked);
        Assert.Contains(rig.Lines, l => l.Contains("자동 재개 취소"));
        Assert.Equal(StepId.OtherDungeon, step); // 취소 후 다시 10분 무입력이 지나야 재개
        Assert.True(time.Now - act.LastInput >= TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Unknown_screen_never_resumes()
    {
        var (ctl, time, act, rig) = Make("unknown");
        var cts = new CancellationTokenSource();
        time.CancelWhen = () => time.Now - new DateTime(2026, 1, 1, 12, 0, 0) > TimeSpan.FromMinutes(30);
        Assert.Throws<OperationCanceledException>(() => ctl.WaitForResume(time.Now, time.Cts.Token));
        Assert.Empty(rig.Game.Inputs);
    }

    [Fact]
    public void Session_user_stop_does_not_auto_resume()
    {
        var rig = new Rig();
        rig.Scenario.Options.AutoResumeEnabled = true;
        rig.Time.CancelWhen = () => rig.Game.State == "battle";
        var session = new BotSession(rig.Scenario, rig.Engine(), () => throw new InvalidOperationException("재개하면 안 됨"), rig.Time, rig.Log);
        var r = session.Run(rig.Time.Cts.Token);
        Assert.Equal(RunOutcome.UserStopped, r.Outcome);
    }
}
