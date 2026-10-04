using AbyssBot.Core.Config;
using AbyssBot.Core.Vision;
using OpenCvSharp;

namespace AbyssBot.Core.Engine;

public sealed record Classification(StepId? Step, string Reason, IReadOnlyList<Detection> Detections)
{
    public bool Known => Step is not null;
}

/// <summary>
/// 저장된 인식 규칙으로 현재 화면이 어느 단계인지 다시 분류한다.
/// 근거가 없거나 서로 충돌하면 알 수 없음(null)으로 두고 입력하지 않는다.
/// 채팅 입력창만으로는 마을/던전을 구분할 수 없으므로 단계로 연결하지 않는다.
/// </summary>
public sealed class ScreenClassifier(IDetector detector, string destinationTarget, bool reconnectEnabled)
{
    public Classification Classify(Mat frame)
    {
        var dets = new List<Detection>();
        Detection D(string id) { var d = detector.Detect(frame, id); dets.Add(d); return d; }

        if (reconnectEnabled && D(TargetIds.ReconnectNotice).Found)
            return new(null, "재접속 안내 표시 중", dets);

        bool touch = D(TargetIds.ResultTouch).Found;
        bool replay = D(TargetIds.Replay).Found;
        bool enter = D(TargetIds.Enter).Found;
        bool dest = D(destinationTarget).Found;
        bool menu = D(TargetIds.MenuOpen).Found;

        var seen = new List<string>();
        if (touch) seen.Add("결과 문구");
        if (replay) seen.Add("다시 하기");
        if (enter) seen.Add("입장하기");
        if (dest) seen.Add("목적지 배너");
        if (menu) seen.Add("열린 메뉴");

        // 입장하기와 목적지 배너는 같은 화면에 함께 있을 수 있다.
        int groups = (touch ? 1 : 0) + (replay ? 1 : 0) + (enter || dest ? 1 : 0) + (menu ? 1 : 0);
        if (groups > 1)
            return new(null, $"서로 충돌하는 화면 근거: {string.Join(", ", seen)}", dets);

        if (touch) return new(StepId.WaitResult, "결과 화면('터치해') → 결과 확인 단계", dets);
        if (replay) return new(StepId.Replay, "보상 화면('다시 하기') → 다시 하기 단계", dets);
        if (enter) return new(StepId.Enter, "입장하기 버튼 → 입장 단계", dets);
        if (dest) return new(StepId.SelectDestination, "어비스 목적지 목록 → 목적지 선택 단계", dets);
        if (menu) return new(StepId.SelectAbyss, "열린 메뉴 → 어비스 선택 단계", dets);

        var chat = D(TargetIds.Chat);
        return new(null, chat.Found
            ? "채팅 입력창만 확인됨(마을/던전 구분 불가) — 알 수 없는 화면"
            : "알 수 없는 화면", dets);
    }
}
