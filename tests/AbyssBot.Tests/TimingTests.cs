using AbyssBot.Core.Config;
using AbyssBot.Core.Engine;
using Xunit;
using Xunit.Abstractions;

namespace AbyssBot.Tests;

/// <summary>
/// 화면 연출 시간을 흉내 낸 가상 게임으로 반복 구간을 돌려, 다시 누름이 없는지와 목적지 → 입장 소요 시간을 확인한다.
/// (가상 시간은 대기만 계산하므로 실제 화면 인식 시간은 빠져 있다)
/// </summary>
public class TimingTests(ITestOutputHelper output)
{
    private static Rig RealisticRig(Action<TimingSpec>? timing = null)
    {
        var rig = new Rig();
        rig.Game.Now = () => rig.Time.Now;
        rig.Game.BattleLength = 10;
        // 보상 화면: 0.3초 뒤 버튼이 보이지만 0.6초까지는 클릭을 받지 않음(나타나는 연출)
        rig.Game.AppearMs["reward"] = 300;
        rig.Game.InputLockMs["reward"] = 600;
        // 목적지 목록: 0.3초 뒤 표시, 0.4초까지 입력 무시
        rig.Game.AppearMs["destList"] = 300;
        rig.Game.InputLockMs["destList"] = 400;
        // 입장 화면: 배너 클릭 후 0.9초 로딩, 1.0초까지 입력 무시
        rig.Game.AppearMs["destSelected"] = 900;
        rig.Game.InputLockMs["destSelected"] = 1000;
        timing?.Invoke(rig.Scenario.Timing);
        return rig;
    }

    private static (Rig rig, List<double> bannerToSpace) RunLoops(Action<TimingSpec>? timing = null)
    {
        var rig = RealisticRig(timing);
        rig.Time.CancelWhen = () => rig.Game.Inputs.Count(i => i == "SPACE") >= 4 && rig.Game.State == "battle";
        var r = rig.Run();
        Assert.Equal(RunOutcome.UserStopped, r.Outcome);
        // 반복 구간(2회차부터): 배너 클릭 → SPACE 간격
        var gaps = new List<double>();
        var t = rig.Game.InputTimes;
        for (int i = 0; i < t.Count; i++)
        {
            if (t[i].input != "click:dest_husang" || i == 0 || t[i - 1].input != "click:other_dungeon") continue;
            var space = t.Skip(i + 1).First(x => x.input == "SPACE");
            gaps.Add((space.at - t[i].at).TotalMilliseconds);
        }
        return (rig, gaps);
    }

    [Fact]
    public void Other_dungeon_is_clicked_once_per_round_despite_fade_in()
    {
        var (rig, _) = RunLoops();
        // 3번의 반복에서 다른 던전 가기·배너·SPACE 모두 한 번씩만(다시 누름 없음)
        Assert.Equal(3, rig.Game.Inputs.Count(i => i == "click:other_dungeon"));
        Assert.Equal(4, rig.Game.Inputs.Count(i => i == "click:dest_husang"));
        Assert.Equal(4, rig.Game.Inputs.Count(i => i == "SPACE"));
        Assert.DoesNotContain(rig.Lines, l => l.Contains("미확인") || l.Contains("남아 있음"));
    }

    [Fact]
    public void Old_timing_without_appear_delay_reclicks_other_dungeon()
    {
        // 기존 설정(나타나자마자 클릭)에서는 연출 중 클릭이 무시돼 다시 누르는 현상이 재현됨
        var (rig, _) = RunLoops(t => { t.ButtonAppearDelayMs = 0; t.EnterDelayMs = 1000; t.TransitionPollMs = new IntRange(400, 650); });
        Assert.True(rig.Game.Inputs.Count(i => i == "click:other_dungeon") > 3);
    }

    [Fact]
    public void Banner_to_space_is_about_half_of_previous_wait()
    {
        var (_, oldGaps) = RunLoops(t => { t.EnterDelayMs = 1000; t.TransitionPollMs = new IntRange(400, 650); });
        var (_, newGaps) = RunLoops();
        double oldAvg = oldGaps.Average(), newAvg = newGaps.Average();
        output.WriteLine($"배너 → SPACE 평균: 이전 {oldAvg:0}ms, 변경 {newAvg:0}ms (게임 로딩 0.9초 포함)");
        // 게임 자체 로딩(0.9초)을 뺀 프로그램 대기 시간이 절반 이하
        Assert.True(newAvg - 900 <= (oldAvg - 900) * 0.5, $"이전 {oldAvg}, 변경 {newAvg}");
    }
}
