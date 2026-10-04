namespace AbyssBot.Core.Config;

/// <summary>scenario.json: 실행 순서, 시간, 선택 기능.</summary>
public sealed class ScenarioConfig
{
    public WindowSpec Window { get; set; } = new();

    /// <summary>최초 한 번만 수행하는 입장 순서. 끝나면 선택한 반복 방식의 순서(Loops)를 반복한다.</summary>
    public List<StepId> FirstRun { get; set; } = new()
    {
        StepId.OpenMenu, StepId.SelectAbyss, StepId.SelectDestination, StepId.Enter,
    };

    /// <summary>반복 방식별 순서.</summary>
    public Dictionary<RepeatMode, List<StepId>> Loops { get; set; } = new()
    {
        [RepeatMode.OtherDungeon] = new() { StepId.WaitResult, StepId.OtherDungeon, StepId.Enter },
        [RepeatMode.Replay] = new() { StepId.WaitResult, StepId.Replay },
    };

    /// <summary>목적지 키 → 배너 대상 이름.</summary>
    public Dictionary<string, DestinationSpec> Destinations { get; set; } = new(StringComparer.Ordinal);

    public TimingSpec Timing { get; set; } = new();
    public OptionsSpec Options { get; set; } = new();
    public LoggingSpec Logging { get; set; } = new();
}

public enum StepId
{
    OpenMenu,
    SelectAbyss,
    SelectDestination,
    Enter,
    WaitResult,
    Replay,
    OtherDungeon,
}

/// <summary>보상 화면에서 다음 판으로 가는 방식.</summary>
public enum RepeatMode
{
    /// <summary>'다른 던전 가기' → 목적지 화면(직전 목적지 선택 상태) → 입장하기.</summary>
    OtherDungeon,
    /// <summary>'다시 하기' → 바로 다음 전투.</summary>
    Replay,
}

public sealed class WindowSpec
{
    /// <summary>게임 창 제목에 포함된 문자열. 실제 창 제목을 확인한 뒤 설정한다.</summary>
    public string TitleContains { get; set; } = "";

    /// <summary>선택: 프로세스 이름(확장자 제외). 지정하면 제목과 함께 일치해야 한다.</summary>
    public string? ProcessName { get; set; }

    /// <summary>WindowRect(테두리·제목 표시줄 포함, GetWindowRect), ExtendedFrame(보이는 테두리), Client(내부 영역).</summary>
    public string CaptureMode { get; set; } = "WindowRect";

    /// <summary>true면 캡처 크기가 targets.json baseline과 다를 때 시작/진행하지 않는다.</summary>
    public bool RequireBaselineSize { get; set; } = true;
}

public sealed class DestinationSpec
{
    public string DisplayName { get; set; } = "";
    public string Target { get; set; } = "";
}

public sealed class TimingSpec
{
    public int MenuOpenTimeoutMs { get; set; } = 15000;
    public int GeneralButtonTimeoutMs { get; set; } = 20000;
    public int ReplayTimeoutMs { get; set; } = 60000;
    public int BattleTimeoutMs { get; set; } = 600000;

    public IntRange PollIntervalMs { get; set; } = new(380, 520);
    public IntRange BattlePollIntervalMs { get; set; } = new(620, 800);
    public IntRange PostInputCheckMs { get; set; } = new(400, 650);

    /// <summary>최초 입력 외 재시도 최대 횟수.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>버튼 소멸을 확정하기 위해 연속으로 '없음'이 확인돼야 하는 프레임 수.</summary>
    public int DisappearConfirmFrames { get; set; } = 2;

    public int EscSettleMs { get; set; } = 700;
    public int EnterDelayMs { get; set; } = 1000;

    /// <summary>메뉴 아이콘/목적지 배너 클릭 후 다음 화면을 기다리는 시간.</summary>
    public int TransitionWaitMs { get; set; } = 3000;

    public IntRange SkipSettleMs { get; set; } = new(250, 350);
    public int SkipMinIntervalMs { get; set; } = 1000;
    public int ReviveMinIntervalMs { get; set; } = 4000;
    public int ReviveCheckDelayMs { get; set; } = 1500;
    public int ReviveMaxAttempts { get; set; } = 3;
    public int MealMinIntervalMs { get; set; } = 4000;
    public int ReconnectMinIntervalMs { get; set; } = 3000;
    public int ReconnectCheckDelayMs { get; set; } = 3000;

    public IntRange KeyHoldMs { get; set; } = new(40, 110);
    public IntRange MouseHoldMs { get; set; } = new(30, 110);

    public int StartCountdownSeconds { get; set; } = 3;
}

public sealed class OptionsSpec
{
    /// <summary>Destinations의 키.</summary>
    public string Destination { get; set; } = "husang";

    /// <summary>반복 방식. 실행 중에는 바뀌지 않는다.</summary>
    public RepeatMode RepeatMode { get; set; } = RepeatMode.OtherDungeon;

    /// <summary>0이면 무제한. 1 이상이면 이 횟수만큼 클리어한 뒤 정상 종료(완료 알림).</summary>
    public int TargetRuns { get; set; }

    public bool SkipDialogEnabled { get; set; } = true;
    public bool ReviveEnabled { get; set; }
    public bool MealEnabled { get; set; }
    public bool ReconnectEnabled { get; set; }

    public bool AutoResumeEnabled { get; set; }
    public int AutoResumeAfterErrorMinutes { get; set; } = 10;
    public int AutoResumeIdleMinutes { get; set; } = 10;
    public int AutoResumeStableChecks { get; set; } = 3;
    public int AutoResumeCheckIntervalMs { get; set; } = 250;
    public int AutoResumeCountdownSeconds { get; set; } = 3;
    /// <summary>한 번의 실행(시작~사용자 정지)에서 자동 재개를 허용하는 최대 횟수.</summary>
    public int AutoResumeMaxCount { get; set; } = 3;
}

public sealed class LoggingSpec
{
    public int MaxLogFileKb { get; set; } = 2048;
    public int MaxLogFiles { get; set; } = 5;
    public int MaxEvidenceFiles { get; set; } = 200;
    public int MaxEvidenceMb { get; set; } = 300;
}
