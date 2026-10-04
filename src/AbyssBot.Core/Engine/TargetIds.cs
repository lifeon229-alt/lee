namespace AbyssBot.Core.Engine;

/// <summary>엔진이 사용하는 targets.json 대상 이름.</summary>
public static class TargetIds
{
    public const string Chat = "chat_input";
    public const string MenuOpen = "menu_open";
    public const string AbyssMenu = "abyss_menu";
    public const string Enter = "enter";
    public const string ResultTouch = "result_touch";
    public const string Replay = "replay";
    public const string OtherDungeon = "other_dungeon";

    /// <summary>options.difficulty 값 → 대상 이름과 표시 이름.</summary>
    public static (string id, string label)? Difficulty(string key) => key switch
    {
        "intro" => ("difficulty_intro", "입문"),
        "hard" => ("difficulty_hard", "어려움"),
        "veryHard" => ("difficulty_veryhard", "매우 어려움"),
        "hell1" => ("difficulty_hell1", "지옥1"),
        _ => null,
    };

    /// <summary>options.partyMode 값 → 대상 이름과 표시 이름.</summary>
    public static (string id, string label)? Party(string key) => key switch
    {
        "solo" => ("party_solo", "혼자하기"),
        "together" => ("party_together", "함께하기"),
        _ => null,
    };
    public const string Skip = "skip";
    public const string ReviveButton = "revive_button";
    public const string ReviveState = "revive_state";
    public const string RevivePurchase = "revive_purchase";
    public const string MealButton = "meal_button";
    public const string ReconnectNotice = "reconnect_notice";
    public const string ReconnectRetry = "reconnect_retry";
}

public static class StepNames
{
    public static string Korean(Config.StepId s) => s switch
    {
        Config.StepId.OpenMenu => "메뉴 열기",
        Config.StepId.SelectAbyss => "어비스 선택",
        Config.StepId.SelectDestination => "목적지 선택",
        Config.StepId.Enter => "입장하기",
        Config.StepId.WaitResult => "전투 결과 대기",
        Config.StepId.Replay => "다시 하기",
        Config.StepId.OtherDungeon => "다른 던전 가기",
        Config.StepId.SelectOptions => "난이도·방식 선택",
        _ => s.ToString(),
    };
}
