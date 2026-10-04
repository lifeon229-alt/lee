using System.Runtime.CompilerServices;
using AbyssBot.Core.Config;
using AbyssBot.Core.Engine;
using AbyssBot.Core.Logging;
using AbyssBot.Core.Vision;
using OpenCvSharp;

namespace AbyssBot.Tests;

/// <summary>입력에 반응하는 가상 게임. 화면은 '보이는 대상 이름' 집합으로 표현한다.</summary>
public sealed class FakeGame
{
    public string State = "town";
    public int BattleFramesLeft;
    public int BattleLength = 3;
    public bool Foreground = true;
    public HashSet<string> Extra = new();
    /// <summary>특정 상태에서 입력을 무시(버튼이 남는 버그 재현).</summary>
    public HashSet<string> IgnoreInputIn = new();
    /// <summary>캡처 번호별로 한 번 인식 실패(깜빡임)를 흉내.</summary>
    public Func<int, string, bool>? Flicker;
    public int Captures;
    public string SelectedDifficulty = "difficulty_veryhard";
    public string SelectedParty = "party_together";
    /// <summary>입장 화면에 보이는 난이도 버튼(지옥2가 표시된 경우 등은 hell1을 빼서 흉내).</summary>
    public HashSet<string> DifficultyButtons = new() { "difficulty_intro", "difficulty_hard", "difficulty_veryhard", "difficulty_hell1" };
    public bool OptionClicksIgnored;
    /// <summary>탭 클릭 직후 잠깐 선택된 것처럼 보이는 캡처 수(실제 선택은 안 바뀜).</summary>
    public int FlashCapturesOnClick;
    public string? FlashTarget;
    public int FlashLeft;
    /// <summary>탭 클릭 뒤 입장 화면이 다시 그려지는 동안(제목·입장하기 안 보임) 캡처 수.</summary>
    public int RedrawCapturesOnPartyClick;
    public int RedrawLeft;
    /// <summary>다른 던전 가기 후 나타나는 화면(기본: 직전 목적지 선택 + 입장하기).</summary>
    public string AfterOtherDungeon = "destList";
    public readonly List<string> Inputs = new();

    public static readonly Dictionary<string, Rect> Buttons = new()
    {
        [TargetIds.AbyssMenu] = new Rect(500, 300, 60, 60),
        ["dest_husang"] = new Rect(150, 500, 200, 80),
        ["dest_kwanggi"] = new Rect(150, 600, 200, 80),
        [TargetIds.Enter] = new Rect(350, 960, 120, 40),
        [TargetIds.ResultTouch] = new Rect(300, 950, 200, 30),
        [TargetIds.Replay] = new Rect(370, 950, 100, 50),
        [TargetIds.OtherDungeon] = new Rect(560, 950, 160, 50),
        [TargetIds.Skip] = new Rect(700, 80, 90, 40),
        [TargetIds.ReviveButton] = new Rect(350, 600, 120, 50),
        [TargetIds.ReconnectRetry] = new Rect(350, 650, 120, 50),
        [TargetIds.MealButton] = new Rect(50, 700, 50, 50),
        [TargetIds.Chat] = new Rect(100, 1000, 300, 30),
        ["difficulty_intro"] = new Rect(60, 175, 50, 25),
        ["difficulty_hard"] = new Rect(140, 175, 70, 25),
        ["difficulty_veryhard"] = new Rect(240, 175, 110, 25),
        ["difficulty_hell1"] = new Rect(390, 175, 40, 25),
        ["party_solo"] = new Rect(790, 45, 80, 25),
        ["party_together"] = new Rect(1050, 45, 90, 25),
    };

    public HashSet<string> Visible()
    {
        var v = State switch
        {
            "town" => new HashSet<string> { TargetIds.Chat },
            "menu" => new HashSet<string> { TargetIds.MenuOpen, TargetIds.AbyssMenu, TargetIds.Chat },
            "destList" => new HashSet<string> { "dest_husang", "dest_kwanggi" },
            // 배너를 누르면 나오는 입장 화면: 목적지 제목 + 입장하기 (배너는 없음)
            "destSelected" => new HashSet<string> { "dest_title_husang", TargetIds.Enter },
            "otherEnterOnly" => new HashSet<string> { "dest_title_kwanggi", TargetIds.Enter },
            "battle" => new HashSet<string> { TargetIds.Chat },
            "result" => new HashSet<string> { TargetIds.ResultTouch },
            "reward" => new HashSet<string> { TargetIds.Replay, TargetIds.OtherDungeon },
            "rewardNoButton" => new HashSet<string>(),
            "rewardOnlyReplay" => new HashSet<string> { TargetIds.Replay },
            "unknown" => new HashSet<string>(),
            _ => new HashSet<string>(),
        };
        if (State is "destSelected" && RedrawLeft > 0)
        {
            v.Remove("dest_title_husang"); v.Remove(TargetIds.Enter);
            v.UnionWith(Extra);
            return v;
        }
        if (State is "destSelected")
        {
            v.UnionWith(DifficultyButtons);
            v.Add("party_solo"); v.Add("party_together");
        }
        v.UnionWith(Extra);
        return v;
    }

    public void OnCapture()
    {
        Captures++;
        if (FlashLeft > 0) FlashLeft--;
        if (RedrawLeft > 0) RedrawLeft--;
        if (State == "battle" && --BattleFramesLeft <= 0) State = "result";
    }

    public void OnKey(ushort scan)
    {
        Inputs.Add(ScanCode.Name(scan));
        if (IgnoreInputIn.Contains(State)) return;
        if (scan == ScanCode.Esc && State == "town") State = "menu";
        else if (scan == ScanCode.Space && State == "destSelected") StartBattle();
    }

    public void OnClick(Point p)
    {
        var hit = Buttons.FirstOrDefault(b => Visible().Contains(b.Key) && b.Value.Contains(p)).Key ?? "빈자리";
        Inputs.Add("click:" + hit);
        if (IgnoreInputIn.Contains(State)) return;
        switch (State, hit)
        {
            case ("menu", TargetIds.AbyssMenu): State = "destList"; break;
            case ("destList", "dest_husang"): State = "destSelected"; break;
            case ("result", TargetIds.ResultTouch): State = "reward"; break;
            case ("reward", TargetIds.Replay): StartBattle(); break;
            case ("reward", TargetIds.OtherDungeon): State = AfterOtherDungeon; break;
        }
        if (hit == TargetIds.Skip) Extra.Remove(TargetIds.Skip);
        if (State == "destSelected" && FlashCapturesOnClick > 0 && hit.StartsWith("party_"))
        {
            FlashTarget = hit; FlashLeft = FlashCapturesOnClick + 1;
        }
        if (State == "destSelected" && hit.StartsWith("party_") && RedrawCapturesOnPartyClick > 0) RedrawLeft = RedrawCapturesOnPartyClick + 1;
        if (State == "destSelected" && !OptionClicksIgnored)
        {
            if (hit.StartsWith("difficulty_")) SelectedDifficulty = hit;
            if (hit.StartsWith("party_")) SelectedParty = hit;
        }
    }

    private void StartBattle() { State = "battle"; BattleFramesLeft = BattleLength; }
}

public sealed class FakeDetector(FakeGame game) : IDetector
{
    public readonly ConditionalWeakTable<Mat, HashSet<string>> Screens = new();
    public HashSet<string> Unconfigured = new() { TargetIds.ReviveButton, TargetIds.ReviveState, TargetIds.RevivePurchase, TargetIds.MealButton, TargetIds.ReconnectNotice, TargetIds.ReconnectRetry };

    public bool IsConfigured(string id) => !Unconfigured.Contains(id);

    public Detection Detect(Mat frame, string id)
    {
        if (!IsConfigured(id)) return Detection.NotConfigured(id);
        var visible = Screens.TryGetValue(frame, out var v) ? v : new HashSet<string>();
        bool found = visible.Contains(id);
        if (found && game.Flicker?.Invoke(game.Captures, id) == true) found = false;
        bool? selected = id.StartsWith("difficulty_") ? game.SelectedDifficulty == id
            : id.StartsWith("party_") ? game.SelectedParty == id || (game.FlashLeft > 0 && game.FlashTarget == id) : null;
        return new Detection
        {
            TargetId = id, Found = found, Score = found ? 2 : 0, PassScore = 2,
            ButtonRect = found ? FakeGame.Buttons.GetValueOrDefault(id, new Rect(10, 10, 20, 20)) : null,
            Selected = found ? selected : null,
        };
    }
}

public sealed class FakeWindow(FakeGame game, FakeDetector det) : IGameWindow
{
    public CaptureResult Capture()
    {
        if (!game.Foreground) return CaptureResult.Inactive("다른 창 활성");
        game.OnCapture();
        var m = new Mat(4, 4, MatType.CV_8UC3, Scalar.All(0));
        det.Screens.Add(m, game.Visible());
        return CaptureResult.Ok(new Frame(m, new Point(0, 0), DateTime.Now));
    }

    public bool IsForeground() => game.Foreground;
    public Point? CurrentOrigin() => new Point(0, 0);
}

public sealed class FakeInput(FakeGame game) : IInputDevice
{
    public bool FailNext;
    public InputResult PressKey(ushort scanCode, int holdMs)
    {
        if (FailNext) { FailNext = false; return InputResult.Failure("드라이버 응답 없음"); }
        game.OnKey(scanCode); return InputResult.Success();
    }

    public InputResult Click(Point screen, int holdMs)
    {
        if (FailNext) { FailNext = false; return InputResult.Failure("드라이버 응답 없음"); }
        game.OnClick(screen); return InputResult.Success();
    }
}

/// <summary>대기하면 가상 시간이 흐른다. 정지 조건을 걸 수 있다.</summary>
public sealed class FakeTime : IClock, IWaiter
{
    public DateTime Now { get; private set; } = new(2026, 1, 1, 12, 0, 0);
    public Func<bool>? CancelWhen;
    public CancellationTokenSource Cts = new();
    public Action? OnWait;

    public void Wait(int ms, CancellationToken ct)
    {
        Now = Now.AddMilliseconds(ms);
        OnWait?.Invoke();
        if (CancelWhen?.Invoke() == true) Cts.Cancel();
        ct.ThrowIfCancellationRequested();
    }

    public void Advance(TimeSpan t) => Now += t;
}

public sealed class NullEvidence : IEvidenceStore
{
    public readonly List<string> Labels = new();
    public string? Save(Mat? frame, string label, object report) { Labels.Add(label); return label; }
}

public sealed class Rig
{
    public readonly FakeGame Game = new();
    public readonly FakeDetector Det;
    public readonly FakeWindow Win;
    public readonly FakeInput Input;
    public readonly FakeTime Time = new();
    public readonly NullEvidence Evidence = new();
    public readonly BotLogger Log;
    public readonly List<string> Lines = new();
    public readonly ScenarioConfig Scenario = DefaultScenario();

    public Rig()
    {
        Det = new FakeDetector(Game);
        Win = new FakeWindow(Game, Det);
        Input = new FakeInput(Game);
        Log = new BotLogger(null, new LoggingSpec(), () => Time.Now);
        Log.Line += l => Lines.Add(l.ToString());
    }

    public static ScenarioConfig DefaultScenario()
    {
        var s = new ScenarioConfig();
        s.Destinations["husang"] = new DestinationSpec { DisplayName = "허상의 정박지", Target = "dest_husang", TitleTarget = "dest_title_husang" };
        s.Destinations["kwanggi"] = new DestinationSpec { DisplayName = "광기의 동굴", Target = "dest_kwanggi", TitleTarget = "dest_title_kwanggi" };
        return s;
    }

    public AbyssEngine Engine() => new(Scenario, Det, Win, Input, Time, Time, Log, Evidence, rng: new Random(1));

    public RunResult Run(StepId start = StepId.OpenMenu)
    {
        Time.Cts = new CancellationTokenSource();
        return Engine().Run(start, Time.Cts.Token);
    }
}
