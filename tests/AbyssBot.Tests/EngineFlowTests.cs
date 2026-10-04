using AbyssBot.Core.Config;
using AbyssBot.Core.Engine;
using Xunit;

namespace AbyssBot.Tests;

public class EngineFlowTests
{
    [Fact]
    public void FirstRun_then_second_battle_entry_via_replay()
    {
        var rig = new Rig();
        rig.Game.BattleLength = 10;
        // 두 번째 판 전투 진입이 확인되면(다시 하기 버튼 소멸 확인 후 전투 상태) 사용자 정지
        rig.Time.CancelWhen = () => rig.Game.State == "battle" && rig.Lines.Any(l => l.Contains("다시 하기 버튼 소멸 확인"));
        var r = rig.Run();

        Assert.Equal(RunOutcome.UserStopped, r.Outcome);
        Assert.Equal(new[] { "ESC", "click:abyss_menu", "click:dest_husang", "SPACE", "click:result_touch", "click:replay" },
            rig.Game.Inputs);
        Assert.Contains(rig.Lines, l => l.Contains("클리어 1회째"));
    }

    [Fact]
    public void Loop_does_not_reopen_menu_on_later_runs()
    {
        var rig = new Rig();
        rig.Time.CancelWhen = () => rig.Game.Inputs.Count(i => i == "click:replay") >= 3 && rig.Game.State == "battle";
        rig.Run();
        Assert.Single(rig.Game.Inputs, i => i == "ESC");
        Assert.Single(rig.Game.Inputs, i => i == "click:abyss_menu");
        Assert.Single(rig.Game.Inputs, i => i == "click:dest_husang");
        Assert.Single(rig.Game.Inputs, i => i == "SPACE");
        Assert.Equal(3, rig.Game.Inputs.Count(i => i == "click:result_touch"));
    }

    [Fact]
    public void Esc_is_pressed_only_once_when_menu_does_not_open()
    {
        var rig = new Rig();
        rig.Game.IgnoreInputIn.Add("town");
        var r = rig.Run();
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Equal(StepId.OpenMenu, r.Step);
        Assert.Equal(new[] { "ESC" }, rig.Game.Inputs);
        Assert.Contains("error_OpenMenu", rig.Evidence.Labels);
    }

    [Fact]
    public void Menu_already_open_means_no_esc()
    {
        var rig = new Rig();
        rig.Game.State = "menu";
        rig.Time.CancelWhen = () => rig.Game.State == "destList";
        rig.Run();
        Assert.DoesNotContain("ESC", rig.Game.Inputs);
    }

    [Fact]
    public void Unknown_start_screen_sends_no_input_and_fails_on_timeout()
    {
        var rig = new Rig();
        rig.Game.State = "unknown";
        var r = rig.Run();
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Empty(rig.Game.Inputs);
    }

    [Fact]
    public void Replay_button_missing_stops_without_clicking_anything()
    {
        var rig = new Rig();
        rig.Game.State = "rewardNoButton";
        var start = rig.Time.Now;
        var r = rig.Run(StepId.Replay);
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Equal(StepId.Replay, r.Step);
        Assert.Empty(rig.Game.Inputs);
        Assert.True(rig.Time.Now - start >= TimeSpan.FromSeconds(60));
        Assert.Contains("나타나지 않음", r.Reason);
    }

    [Fact]
    public void Replay_button_that_stays_is_retried_limited_times_then_fails()
    {
        var rig = new Rig();
        rig.Game.State = "reward";
        rig.Game.IgnoreInputIn.Add("reward");
        var r = rig.Run(StepId.Replay);
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Equal(1 + rig.Scenario.Timing.MaxRetries, rig.Game.Inputs.Count(i => i == "click:replay"));
        Assert.Contains("error_Replay", rig.Evidence.Labels);
    }

    [Fact]
    public void Single_frame_miss_is_not_treated_as_button_gone()
    {
        var rig = new Rig();
        rig.Game.State = "reward";
        rig.Game.IgnoreInputIn.Add("reward");
        // 클릭 직후 첫 확인 프레임에서만 한 번 인식 실패
        bool flickered = false;
        rig.Game.Flicker = (n, id) =>
        {
            if (id == TargetIds.Replay && !flickered && rig.Game.Inputs.Count == 1) { flickered = true; return true; }
            return false;
        };
        var r = rig.Run(StepId.Replay);
        Assert.True(flickered);
        Assert.Equal(RunOutcome.Failed, r.Outcome); // 버튼은 계속 남아 있으므로 성공 처리하면 안 됨
        Assert.DoesNotContain(rig.Lines, l => l.Contains("다시 하기 버튼 소멸 확인"));
    }

    [Fact]
    public void No_result_clicks_during_battle()
    {
        var rig = new Rig();
        rig.Game.State = "battle";
        rig.Game.BattleFramesLeft = 1000;
        var r = rig.Run(StepId.WaitResult);
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Empty(rig.Game.Inputs);
        Assert.Contains("전투 대기", r.Reason);
        // 같은 '전투 대기 중' 로그를 반복해 쌓지 않는다
        Assert.True(rig.Lines.Count(l => l.Contains("전투 대기 중")) == 1);
    }

    [Fact]
    public void Input_delivery_failure_is_not_success()
    {
        var rig = new Rig();
        rig.Input.FailNext = true;
        var r = rig.Run();
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Equal(StepId.OpenMenu, r.Step);
        Assert.Contains("전달 실패", r.Reason);
    }

    [Fact]
    public void Inactive_game_window_gets_no_input()
    {
        var rig = new Rig();
        rig.Game.Foreground = false;
        var r = rig.Run();
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Empty(rig.Game.Inputs);
    }

    [Fact]
    public void Space_retry_waits_enter_delay_each_time()
    {
        var rig = new Rig();
        rig.Game.State = "destSelected";
        rig.Game.IgnoreInputIn.Add("destSelected");
        var r = rig.Run(StepId.Enter);
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Equal(1 + rig.Scenario.Timing.MaxRetries, rig.Game.Inputs.Count(i => i == "SPACE"));
    }

    [Fact]
    public void Skip_button_is_clicked_one_action_per_capture()
    {
        var rig = new Rig();
        rig.Game.State = "battle";
        rig.Game.BattleFramesLeft = 6;
        rig.Game.Extra.Add(TargetIds.Skip);
        rig.Time.CancelWhen = () => rig.Game.State == "reward";
        rig.Run(StepId.WaitResult);
        Assert.Equal("click:skip", rig.Game.Inputs[0]);
        Assert.Equal("click:result_touch", rig.Game.Inputs[1]);
    }

    [Fact]
    public void Target_runs_completes_without_pressing_replay()
    {
        var rig = new Rig();
        rig.Scenario.Options.TargetRuns = 1;
        var r = rig.Run();
        Assert.Equal(RunOutcome.Completed, r.Outcome);
        Assert.DoesNotContain("click:replay", rig.Game.Inputs);
    }

    [Fact]
    public void Destination_is_locked_at_engine_creation()
    {
        var rig = new Rig();
        var engine = rig.Engine();
        rig.Scenario.Options.Destination = "kwanggi";
        Assert.Equal("dest_husang", engine.DestinationTarget);
    }

    [Fact]
    public void Preflight_blocks_enabled_feature_without_target()
    {
        var rig = new Rig();
        rig.Scenario.Options.ReviveEnabled = true;
        var problems = AbyssEngine.Preflight(rig.Scenario, rig.Det, _ => Array.Empty<string>());
        Assert.Contains(problems, p => p.Contains("revive_button"));
    }
}
