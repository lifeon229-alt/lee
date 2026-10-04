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
        _ => s.ToString(),
    };
}
