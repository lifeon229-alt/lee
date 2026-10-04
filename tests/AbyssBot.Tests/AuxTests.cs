using AbyssBot.Core.Config;
using AbyssBot.Core.Engine;
using Xunit;

namespace AbyssBot.Tests;

public class AuxTests
{
    private static Rig BattleRig()
    {
        var rig = new Rig();
        rig.Det.Unconfigured.Clear();
        rig.Game.State = "battle";
        rig.Game.BattleFramesLeft = 1000;
        rig.Scenario.Timing.BattleTimeoutMs = 60000;
        return rig;
    }

    [Fact]
    public void Revive_requires_incapacitated_evidence()
    {
        var rig = BattleRig();
        rig.Scenario.Options.ReviveEnabled = true;
        rig.Game.Extra.Add(TargetIds.ReviveButton); // 행동불능 근거 없음
        rig.Run(StepId.WaitResult);
        Assert.Empty(rig.Game.Inputs);
    }

    [Fact]
    public void Revive_purchase_prompt_stops_without_buying()
    {
        var rig = BattleRig();
        rig.Scenario.Options.ReviveEnabled = true;
        rig.Game.Extra.UnionWith(new[] { TargetIds.ReviveButton, TargetIds.ReviveState });
        rig.Time.OnWait = () =>
        {
            if (rig.Game.Inputs.Contains("click:revive_button"))
            {
                rig.Game.Extra.Clear();
                rig.Game.Extra.Add(TargetIds.RevivePurchase);
            }
        };
        var r = rig.Run(StepId.WaitResult);
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Contains("구매", r.Reason);
        Assert.Equal(new[] { "click:revive_button" }, rig.Game.Inputs);
        Assert.Contains("revive_before", rig.Evidence.Labels);
        Assert.Contains("revive_purchase", rig.Evidence.Labels);
    }

    [Fact]
    public void Revive_repeat_clicks_are_at_least_4s_apart_and_limited()
    {
        var rig = BattleRig();
        rig.Scenario.Options.ReviveEnabled = true;
        rig.Game.Extra.UnionWith(new[] { TargetIds.ReviveButton, TargetIds.ReviveState });
        var times = new List<DateTime>();
        rig.Game.IgnoreInputIn.Add("battle");
        int before = 0;
        rig.Time.OnWait = () =>
        {
            int n = rig.Game.Inputs.Count;
            if (n > before) { times.Add(rig.Time.Now); before = n; }
        };
        var r = rig.Run(StepId.WaitResult);
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Equal(rig.Scenario.Timing.ReviveMaxAttempts, rig.Game.Inputs.Count);
        for (int i = 1; i < times.Count; i++)
            Assert.True((times[i] - times[i - 1]).TotalMilliseconds >= 4000);
    }

    [Fact]
    public void Reconnect_notice_without_button_sends_nothing_and_blocks_result_click()
    {
        var rig = BattleRig();
        rig.Scenario.Options.ReconnectEnabled = true;
        rig.Game.BattleFramesLeft = 2;
        rig.Game.Extra.Add(TargetIds.ReconnectNotice);
        rig.Run(StepId.WaitResult);
        Assert.Empty(rig.Game.Inputs);
    }

    [Fact]
    public void Reconnect_clicks_detected_button_and_saves_before_after()
    {
        var rig = BattleRig();
        rig.Scenario.Options.ReconnectEnabled = true;
        rig.Game.Extra.UnionWith(new[] { TargetIds.ReconnectNotice, TargetIds.ReconnectRetry });
        rig.Time.OnWait = () =>
        {
            if (rig.Game.Inputs.Contains("click:reconnect_retry")) rig.Game.Extra.Clear();
        };
        rig.Time.CancelWhen = () => rig.Game.Inputs.Count > 0 && rig.Evidence.Labels.Contains("reconnect_after");
        rig.Run(StepId.WaitResult);
        Assert.Equal(new[] { "click:reconnect_retry" }, rig.Game.Inputs);
        Assert.Contains("reconnect_before", rig.Evidence.Labels);
        Assert.Contains("reconnect_after", rig.Evidence.Labels);
    }

    [Fact]
    public void Meal_clicks_are_at_least_4s_apart()
    {
        var rig = BattleRig();
        rig.Scenario.Options.MealEnabled = true;
        rig.Game.Extra.Add(TargetIds.MealButton);
        var times = new List<DateTime>();
        int before = 0;
        rig.Time.OnWait = () =>
        {
            if (rig.Game.Inputs.Count > before) { times.Add(rig.Time.Now); before = rig.Game.Inputs.Count; }
        };
        rig.Time.CancelWhen = () => rig.Game.Inputs.Count >= 4;
        rig.Run(StepId.WaitResult);
        Assert.True(times.Count >= 3);
        for (int i = 1; i < times.Count; i++)
            Assert.True((times[i] - times[i - 1]).TotalMilliseconds >= 4000 - 1000); // 기록 시점이 대기 후라 1회 대기 오차 허용
    }
}
