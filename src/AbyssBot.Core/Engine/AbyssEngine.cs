using AbyssBot.Core.Config;
using AbyssBot.Core.Logging;
using AbyssBot.Core.Vision;
using OpenCvSharp;

namespace AbyssBot.Core.Engine;

/// <summary>
/// 실행 엔진. 모든 실제 입력은 이 클래스에서만 순서대로 보낸다.
/// 각 단계는 성공/실패/사용자 정지를 반환하고, 성공일 때만 다음 단계로 간다.
/// </summary>
public sealed class AbyssEngine
{
    private readonly ScenarioConfig _s;
    private readonly IDetector _det;
    private readonly IGameWindow _win;
    private readonly IInputDevice _input;
    private readonly IClock _clock;
    private readonly IWaiter _waiter;
    private readonly BotLogger _log;
    private readonly IEvidenceStore _evidence;
    private readonly IEngineObserver? _observer;
    private readonly Random _rng;
    private readonly string _destKey;
    private readonly string _destTarget;

    private readonly Dictionary<string, DateTime> _lastAction = new(StringComparer.Ordinal);
    private readonly HashSet<string> _once = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Detection> _recent = new(StringComparer.Ordinal);
    private Mat? _lastFrame;
    private LastInput? _lastInput;
    private StepId _currentStep;
    private CancellationToken _ct;
    private int _reviveAttempts;

    public AbyssEngine(ScenarioConfig scenario, IDetector detector, IGameWindow window, IInputDevice input,
        IClock clock, IWaiter waiter, BotLogger log, IEvidenceStore evidence, IEngineObserver? observer = null,
        Random? rng = null)
    {
        _s = scenario; _det = detector; _win = window; _input = input; _clock = clock; _waiter = waiter;
        _log = log; _evidence = evidence; _observer = observer; _rng = rng ?? new Random();
        // 실행 중 목적지는 잠근다. 설정을 바꿔도 이 엔진 인스턴스에는 반영되지 않는다.
        _destKey = scenario.Options.Destination;
        _destTarget = scenario.Destinations.TryGetValue(_destKey, out var d)
            ? d.Target
            : throw new ConfigException($"목적지 '{_destKey}'가 scenario.json destinations에 없습니다.");
    }

    public RunStats Stats { get; } = new();
    public string LockedDestination => _destKey;
    public string DestinationTarget => _destTarget;
    public LastInput? LastInputRecord => _lastInput;

    private TimingSpec T => _s.Timing;
    private OptionsSpec O => _s.Options;

    /// <summary>시작 전 점검. 문제가 있으면 목록을 반환한다(비어 있으면 통과).</summary>
    public static List<string> Preflight(ScenarioConfig s, IDetector det, Func<string, IEnumerable<string>> missingImagesOf)
    {
        var problems = new List<string>();
        if (!s.Destinations.TryGetValue(s.Options.Destination, out var dest))
        {
            problems.Add($"목적지 '{s.Options.Destination}'가 설정에 없습니다.");
        }
        else
        {
            if (!det.IsConfigured(dest.Target)) problems.Add($"목적지 배너 대상 '{dest.Target}'이 설정되지 않았습니다.");
            foreach (var m in missingImagesOf(dest.Target))
                problems.Add($"목적지 배너 사진이 없습니다: images/{m} (목적지는 사진으로만 판정)");
        }
        foreach (var id in new[] { TargetIds.Chat, TargetIds.MenuOpen, TargetIds.AbyssMenu, TargetIds.Enter, TargetIds.ResultTouch, TargetIds.Replay })
            if (!det.IsConfigured(id)) problems.Add($"필수 대상 '{id}'이 설정되지 않았습니다.");

        void RequireFor(bool enabled, string feature, params string[] ids)
        {
            if (!enabled) return;
            foreach (var id in ids)
                if (!det.IsConfigured(id))
                    problems.Add($"{feature} 기능이 켜져 있지만 '{id}' 대상이 설정되지 않았습니다(영역·문구·사진 필요).");
        }
        RequireFor(s.Options.SkipDialogEnabled, "대화/장면 넘기기", TargetIds.Skip);
        RequireFor(s.Options.ReviveEnabled, "부활", TargetIds.ReviveButton, TargetIds.ReviveState, TargetIds.RevivePurchase);
        RequireFor(s.Options.MealEnabled, "음식 사용", TargetIds.MealButton);
        RequireFor(s.Options.ReconnectEnabled, "재접속", TargetIds.ReconnectNotice, TargetIds.ReconnectRetry);
        return problems;
    }

    /// <summary>
    /// start 단계부터 실행한다. 사용자 정지 또는 실패 때만 반환한다(정상 반복은 끝나지 않음).
    /// </summary>
    public RunResult Run(StepId start, CancellationToken ct, bool resetStats = true)
    {
        _ct = ct;
        if (resetStats) Stats.Reset(_clock.Now); else Stats.BreakSegment();
        _observer?.StatsChanged(Stats);
        _log.Info($"실행 시작: 목적지 '{_destKey}'({_destTarget}) 잠금, 시작 단계 '{StepNames.Korean(start)}'");

        try
        {
            foreach (var step in Sequence(start))
            {
                _currentStep = step;
                _once.Clear();
                _recent.Clear();
                _observer?.StepChanged(step);
                _log.Info($"단계 시작: {StepNames.Korean(step)}");

                StepResult r;
                try
                {
                    r = step switch
                    {
                        StepId.OpenMenu => OpenMenu(),
                        StepId.SelectAbyss => ClickAndAdvance(TargetIds.AbyssMenu, _destTarget, "어비스 메뉴", "목적지 목록"),
                        StepId.SelectDestination => ClickAndAdvance(_destTarget, TargetIds.Enter, "목적지 배너", "입장하기 버튼"),
                        StepId.Enter => EnterDungeon(),
                        StepId.WaitResult => WaitResult(),
                        StepId.Replay => Replay(),
                        _ => StepResult.Fail($"알 수 없는 단계 {step}"),
                    };
                }
                catch (OperationCanceledException)
                {
                    r = new StepResult(StepOutcome.UserStopped);
                }

                if (r.Outcome == StepOutcome.UserStopped)
                {
                    _log.Info($"사용자 정지 ({StepNames.Korean(step)} 단계)");
                    return new RunResult(RunOutcome.UserStopped, step, "사용자 정지", null);
                }
                if (r.Outcome == StepOutcome.Failure)
                {
                    var path = SaveFailure(step, r.Reason ?? "실패");
                    _log.Error($"오류 정지 [{StepNames.Korean(step)}] {r.Reason}" + (path is null ? "" : $" — 저장: {path}"));
                    return new RunResult(RunOutcome.Failed, step, r.Reason ?? "실패", path);
                }
                _log.Info($"단계 성공: {StepNames.Korean(step)}");
                if (step == StepId.WaitResult && O.TargetRuns > 0 && Stats.Completed >= O.TargetRuns)
                {
                    _log.Info($"목표 {O.TargetRuns}회 완료 — 다시 하기를 누르지 않고 정상 종료");
                    return new RunResult(RunOutcome.Completed, step, $"목표 {O.TargetRuns}회 완료", null);
                }
            }
        }
        finally
        {
            _lastFrame?.Dispose();
            _lastFrame = null;
        }
        return new RunResult(RunOutcome.Failed, null, "실행 순서가 끝남", null);
    }

    private IEnumerable<StepId> Sequence(StepId start)
    {
        int i = _s.FirstRun.IndexOf(start);
        if (i >= 0)
        {
            for (; i < _s.FirstRun.Count; i++) yield return _s.FirstRun[i];
        }
        else
        {
            int j = _s.Loop.IndexOf(start);
            if (j < 0) throw new ConfigException($"시작 단계 {start}가 firstRun/loop에 없습니다.");
            for (; j < _s.Loop.Count; j++) yield return _s.Loop[j];
        }
        while (true)
            foreach (var s in _s.Loop) yield return s;
    }

    // ───────────────────────── 단계 ─────────────────────────

    /// <summary>메뉴 열기: 이미 열려 있으면 성공. 아니면 '말하기'가 확인될 때 ESC를 한 번만 누른다.</summary>
    private StepResult OpenMenu()
    {
        var deadline = _clock.Now.AddMilliseconds(T.MenuOpenTimeoutMs);
        bool escSent = false;
        while (true)
        {
            using (var cap = CaptureFrame(out var fatal))
            {
                if (fatal is not null) return StepResult.Fail(fatal);
                if (cap is not null)
                {
                    var aux = HandleReconnect(cap);
                    if (aux.Fail is not null) return StepResult.Fail(aux.Fail);
                    if (aux.Acted) continue;
                    if (aux.Blocked) goto next;

                    var menu = Detect(cap, TargetIds.MenuOpen);
                    if (menu.Found)
                    {
                        _log.Info($"메뉴 열림 확인: {menu.Note}");
                        return StepResult.Ok;
                    }
                    if (!escSent)
                    {
                        var chat = Detect(cap, TargetIds.Chat);
                        if (chat.Found)
                        {
                            _log.Info($"메인화면 근거 확인: {chat.Summary()}");
                            if (!SendKey(ScanCode.Esc, "메뉴 열기", out var why)) return StepResult.Fail(why);
                            escSent = true;
                            Wait(T.EscSettleMs);
                            continue;
                        }
                        LogOnce("menu-wait", $"메인화면('말하기')과 메뉴 모두 미확인 — 입력 없이 대기. {chat.Summary()} / {menu.Summary()}");
                    }
                    else
                    {
                        LogOnce("menu-after-esc", $"ESC 후 메뉴 아직 미확인(ESC 반복 안 함): {menu.Summary()}");
                    }
                }
            }
            next:
            if (_clock.Now > deadline)
                return StepResult.Fail(escSent
                    ? $"ESC 후 {T.MenuOpenTimeoutMs / 1000}초 동안 메뉴 열림이 확인되지 않음"
                    : $"{T.MenuOpenTimeoutMs / 1000}초 동안 메인화면('말하기')도 열린 메뉴도 확인되지 않음");
            Wait(T.PollIntervalMs);
        }
    }

    /// <summary>
    /// 클릭 후에도 남아 있을 수 있는 대상(메뉴 아이콘, 목적지 배너): 사진 소멸이 아니라 다음 화면 등장으로 성공을 확인한다.
    /// </summary>
    private StepResult ClickAndAdvance(string targetId, string nextId, string label, string nextLabel)
    {
        var deadline = _clock.Now.AddMilliseconds(T.GeneralButtonTimeoutMs);
        int clicks = 0;
        while (true)
        {
            using (var cap = CaptureFrame(out var fatal))
            {
                if (fatal is not null) return StepResult.Fail(fatal);
                if (cap is not null)
                {
                    var aux = HandleReconnect(cap);
                    if (aux.Fail is not null) return StepResult.Fail(aux.Fail);
                    if (aux.Acted) continue;
                    if (aux.Blocked) goto next;

                    var next = Detect(cap, nextId);
                    if (next.Found && clicks > 0)
                    {
                        _log.Info($"{nextLabel} 확인 → {label} 클릭 성공. {next.Summary()}");
                        return StepResult.Ok;
                    }
                    var d = Detect(cap, targetId);
                    if (d.Found)
                    {
                        if (clicks > T.MaxRetries)
                            return StepResult.Fail($"{label}을(를) {clicks}번 눌렀지만 {nextLabel}이(가) 나타나지 않음");
                        if (!SendClick(cap, d, label, out var why)) return StepResult.Fail(why);
                        clicks++;
                        if (WaitForTarget(nextId, T.TransitionWaitMs, out var nd))
                        {
                            _log.Info($"{nextLabel} 확인 → {label} 클릭 성공. {nd!.Summary()}");
                            return StepResult.Ok;
                        }
                        _log.Warn($"{label} 클릭 후 {T.TransitionWaitMs}ms 안에 {nextLabel} 미확인 ({clicks}/{T.MaxRetries + 1}회)");
                        continue;
                    }
                    LogOnce("adv-wait-" + targetId, $"{label} 찾는 중: {d.Summary()}");
                }
            }
            next:
            if (_clock.Now > deadline)
                return StepResult.Fail(clicks == 0
                    ? $"{T.GeneralButtonTimeoutMs / 1000}초 동안 {label}을(를) 찾지 못함"
                    : $"{label} 클릭 후 {nextLabel}이(가) 나타나지 않음");
            Wait(T.PollIntervalMs);
        }
    }

    /// <summary>입장하기 확인 → 1초 대기 → SPACE. 버튼이 연속 프레임에서 사라져야 성공.</summary>
    private StepResult EnterDungeon()
    {
        var deadline = _clock.Now.AddMilliseconds(T.GeneralButtonTimeoutMs);
        int presses = 0;
        while (true)
        {
            Detection? found = null;
            using (var cap = CaptureFrame(out var fatal))
            {
                if (fatal is not null) return StepResult.Fail(fatal);
                if (cap is not null)
                {
                    var aux = HandleReconnect(cap);
                    if (aux.Fail is not null) return StepResult.Fail(aux.Fail);
                    if (aux.Acted) continue;
                    if (aux.Blocked) goto next;
                    var d = Detect(cap, TargetIds.Enter);
                    if (d.Found) found = d;
                    else LogOnce("enter-wait", $"입장하기 찾는 중: {d.Summary()}");
                }
            }

            next:
            if (found is not null)
            {
                if (presses > T.MaxRetries)
                    return StepResult.Fail($"SPACE를 {presses}번 보냈지만 입장하기 버튼이 그대로 남아 있음");
                _log.Info($"입장하기 확인: {found.Summary()}");
                // 중요한 화면 전환: 이 구간에는 보조 감시가 입력을 끼워 넣지 않는다.
                Wait(T.EnterDelayMs);
                using (var cap2 = CaptureFrame(out var fatal2))
                {
                    if (fatal2 is not null) return StepResult.Fail(fatal2);
                    if (cap2 is null) continue;
                    var again = Detect(cap2, TargetIds.Enter);
                    if (!again.Found)
                    {
                        _log.Warn($"SPACE 직전 입장하기가 확인되지 않아 입력하지 않음: {again.Summary()}");
                        continue;
                    }
                }
                if (!SendKey(ScanCode.Space, "입장하기", out var why)) return StepResult.Fail(why);
                presses++;
                var gone = ConfirmGone(TargetIds.Enter, null);
                if (gone.Fail is not null) return StepResult.Fail(gone.Fail);
                if (gone.Gone)
                {
                    _log.Info("입장하기 버튼 소멸 확인 → 전투 진입");
                    Stats.MarkBattleStart(_clock.Now);
                    return StepResult.Ok;
                }
                _log.Warn($"SPACE 후에도 입장하기가 남아 있음 ({presses}/{T.MaxRetries + 1}회)");
                continue;
            }

            if (_clock.Now > deadline)
                return StepResult.Fail($"{T.GeneralButtonTimeoutMs / 1000}초 동안 입장하기 버튼을 확인하지 못함");
            Wait(T.PollIntervalMs);
        }
    }

    /// <summary>전투 대기. 결과 문구가 확인될 때까지 결과창용 입력은 하지 않는다.</summary>
    private StepResult WaitResult()
    {
        var deadline = _clock.Now.AddMilliseconds(T.BattleTimeoutMs);
        _reviveAttempts = 0;
        _log.Info($"전투 대기 중 (최대 {T.BattleTimeoutMs / 60000.0:0.#}분)");
        while (true)
        {
            Detection? touch = null;
            using (var cap = CaptureFrame(out var fatal))
            {
                if (fatal is not null) return StepResult.Fail(fatal);
                if (cap is not null)
                {
                    var aux = HandleBattleAux(cap);
                    if (aux.Fail is not null) return StepResult.Fail(aux.Fail);
                    if (aux.Acted) continue; // 입력했으면 새 캡처로 처음부터 다시 판단
                    if (aux.Blocked) goto next;

                    var t = Detect(cap, TargetIds.ResultTouch);
                    if (t.Found) touch = t;
                    else if (t.Conflict) LogOnce("touch-conflict", t.Summary());
                }
            }

            next:
            if (touch is not null)
                return ConfirmResult(touch);

            if (_clock.Now > deadline)
                return StepResult.Fail($"전투 대기 {T.BattleTimeoutMs / 60000.0:0.#}분 초과: 결과 화면('터치해')을 확인하지 못함");
            Wait(T.BattlePollIntervalMs);
        }
    }

    private StepResult ConfirmResult(Detection first)
    {
        _log.Info($"결과 화면 확인: {first.Summary()}");
        var d = first;
        int clicks = 0;
        var deadline = _clock.Now.AddMilliseconds(T.GeneralButtonTimeoutMs);
        while (true)
        {
            if (_clock.Now > deadline)
                return StepResult.Fail("결과 화면 처리 시간 초과");
            if (clicks > T.MaxRetries)
                return StepResult.Fail($"결과 화면을 {clicks}번 눌렀지만 '화면을 터치해 주세요'가 남아 있음");
            using (var cap = CaptureFrame(out var fatal))
            {
                if (fatal is not null) return StepResult.Fail(fatal);
                if (cap is null) { Wait(T.PollIntervalMs); continue; }
                // 클릭 위치는 항상 방금 캡처한 화면의 검출 위치를 쓴다.
                d = Detect(cap, TargetIds.ResultTouch);
                if (!d.Found)
                {
                    if (clicks == 0) return StepResult.Fail($"결과 문구가 클릭 직전에 사라짐: {d.Summary()}");
                    // 이미 넘어갔을 수 있다 → 아래 확인 단계에서 판단
                }
                else
                {
                    if (!SendClick(cap, d, "결과 화면 터치", out var why)) return StepResult.Fail(why);
                    clicks++;
                }
            }
            var gone = ConfirmGone(TargetIds.ResultTouch, TargetIds.Replay);
            if (gone.Fail is not null) return StepResult.Fail(gone.Fail);
            if (gone.Gone)
            {
                Stats.MarkCleared(_clock.Now);
                _observer?.StatsChanged(Stats);
                _log.Info($"클리어 {Stats.Completed}회째 확인 ({gone.How})");
                return StepResult.Ok;
            }
            _log.Warn($"결과 문구가 아직 남아 있음 ({clicks}/{T.MaxRetries + 1}회)");
        }
    }

    /// <summary>
    /// 보상 화면의 '다시 하기'(OCR + 초록 배경). 나타나지 않으면 다른 버튼이나 빈자리를 누르지 않고 시간 초과로 정지한다.
    /// </summary>
    private StepResult Replay()
    {
        var deadline = _clock.Now.AddMilliseconds(T.ReplayTimeoutMs);
        int clicks = 0;
        while (true)
        {
            Detection? d = null;
            using (var cap = CaptureFrame(out var fatal))
            {
                if (fatal is not null) return StepResult.Fail(fatal);
                if (cap is not null)
                {
                    var aux = HandleReconnect(cap);
                    if (aux.Fail is not null) return StepResult.Fail(aux.Fail);
                    if (aux.Acted) continue;
                    if (aux.Blocked) goto next;
                    var r = Detect(cap, TargetIds.Replay);
                    if (r.Found)
                    {
                        if (clicks > T.MaxRetries)
                            return StepResult.Fail($"'다시 하기'를 {clicks}번 눌렀지만 버튼이 그대로 남아 있음");
                        if (clicks == 0) _log.Info($"다시 하기 확인: {r.Summary()}");
                        if (!SendClick(cap, r, "다시 하기", out var why)) return StepResult.Fail(why);
                        clicks++;
                        d = r;
                    }
                    else LogOnce("replay-wait", $"다시 하기 찾는 중: {r.Summary()}");
                }
            }

            next:
            if (d is not null)
            {
                var gone = ConfirmGone(TargetIds.Replay, null);
                if (gone.Fail is not null) return StepResult.Fail(gone.Fail);
                if (gone.Gone)
                {
                    _log.Info("다시 하기 버튼 소멸 확인 → 다음 전투 대기");
                    return StepResult.Ok;
                }
                _log.Warn($"다시 하기 버튼이 아직 남아 있음 ({clicks}/{T.MaxRetries + 1}회)");
                continue;
            }

            if (_clock.Now > deadline)
                return StepResult.Fail(clicks == 0
                    ? $"{T.ReplayTimeoutMs / 1000}초 동안 '다시 하기' 버튼이 나타나지 않음(게임 버그 가능) — 다른 버튼/빈자리는 누르지 않고 정지"
                    : "'다시 하기' 클릭 후 결과를 확인하지 못함");
            Wait(T.PollIntervalMs);
        }
    }

    // ───────────────────────── 보조 감시 ─────────────────────────

    /// <summary>Acted: 입력함(새 캡처로 다시 판단). Blocked: 입력은 안 했지만 이 화면에서 다른 판단을 하면 안 됨.</summary>
    private readonly record struct AuxResult(bool Acted, string? Fail, bool Blocked = false)
    {
        public static readonly AuxResult None = new(false, null);
        public static readonly AuxResult Done = new(true, null);
        public static readonly AuxResult Hold = new(false, null, true);
        public static AuxResult Failed(string why) => new(false, why);
        public bool Stop => Acted || Blocked || Fail is not null;
    }

    /// <summary>전투 중 우선순위: 재접속 → 대화·장면 넘기기 → 부활 → 음식. 한 캡처로 입력은 하나만.</summary>
    private AuxResult HandleBattleAux(Frame cap)
    {
        var r = HandleReconnect(cap);
        if (r.Stop) return r;

        if (O.SkipDialogEnabled)
        {
            var skip = Detect(cap, TargetIds.Skip);
            if (skip.Found && IntervalOk("skip", T.SkipMinIntervalMs))
            {
                if (!SendClick(cap, skip, "넘기기", out var why)) return AuxResult.Failed(why);
                Wait(T.SkipSettleMs);
                return AuxResult.Done;
            }
        }

        if (O.ReviveEnabled)
        {
            var res = HandleRevive(cap);
            if (res.Acted || res.Fail is not null) return res;
        }

        if (O.MealEnabled)
        {
            var meal = Detect(cap, TargetIds.MealButton);
            if (meal.Found && IntervalOk("meal", T.MealMinIntervalMs))
            {
                if (!SendClick(cap, meal, "음식 사용", out var why)) return AuxResult.Failed(why);
                Wait(T.PollIntervalMs);
                return AuxResult.Done;
            }
        }
        return AuxResult.None;
    }

    /// <summary>
    /// 재접속 안내 문구와 '다시 시도하기' 버튼이 모두 확인될 때만 검출 위치를 누른다. 고정 좌표는 쓰지 않는다.
    /// </summary>
    private AuxResult HandleReconnect(Frame cap)
    {
        if (!O.ReconnectEnabled) return AuxResult.None;
        var notice = Detect(cap, TargetIds.ReconnectNotice);
        if (!notice.Found) return AuxResult.None;

        var btn = Detect(cap, TargetIds.ReconnectRetry);
        if (!btn.Found)
        {
            LogOnce("reconnect-nobtn", $"재접속 안내는 보이지만 '다시 시도하기' 버튼 미확인 — 입력 안 함. {notice.Summary()} / {btn.Summary()}");
            return AuxResult.Hold;
        }
        if (!IntervalOk("reconnect", T.ReconnectMinIntervalMs)) return AuxResult.Hold;

        _log.Warn($"재접속 안내 감지: {notice.Summary()} / {btn.Summary()}");
        _evidence.Save(cap.Image, "reconnect_before", Report("재접속 안내 감지(클릭 전)", notice, btn));
        if (!SendClick(cap, btn, "다시 시도하기", out var why)) return AuxResult.Failed(why);
        Wait(T.ReconnectCheckDelayMs);
        using (var after = CaptureFrame(out var fatal))
        {
            if (fatal is not null) return AuxResult.Failed(fatal);
            if (after is not null)
            {
                var n2 = Detect(after, TargetIds.ReconnectNotice);
                _evidence.Save(after.Image, "reconnect_after", Report("재접속 클릭 후 화면", n2));
                _log.Info(n2.Found ? $"재접속 안내가 아직 표시됨: {n2.Summary()}" : "재접속 안내 사라짐 → 현재 화면 다시 판별");
            }
        }
        return AuxResult.Done;
    }

    /// <summary>
    /// '여기서 부활'은 행동불능 화면 근거가 함께 있을 때만 누른다. 클릭 후 구매 안내가 보이면 구매하지 않고 정지한다.
    /// </summary>
    private AuxResult HandleRevive(Frame cap)
    {
        var btn = Detect(cap, TargetIds.ReviveButton);
        if (!btn.Found) return AuxResult.None;
        var state = Detect(cap, TargetIds.ReviveState);
        if (!state.Found)
        {
            LogOnce("revive-nostate", $"부활 버튼은 보이지만 행동불능 화면 근거 없음 — 입력 안 함. {btn.Summary()} / {state.Summary()}");
            return AuxResult.None;
        }
        if (!IntervalOk("revive", T.ReviveMinIntervalMs)) return AuxResult.None;

        if (_reviveAttempts >= T.ReviveMaxAttempts)
            return AuxResult.Failed($"부활을 {_reviveAttempts}번 시도했지만 행동불능 화면이 계속됨");

        _log.Warn($"행동불능 감지: {state.Summary()} / {btn.Summary()}");
        _evidence.Save(cap.Image, "revive_before", Report("부활 클릭 전", btn, state));
        if (!SendClick(cap, btn, "여기서 부활", out var why)) return AuxResult.Failed(why);
        _reviveAttempts++;
        Wait(T.ReviveCheckDelayMs);

        using var after = CaptureFrame(out var fatal);
        if (fatal is not null) return AuxResult.Failed(fatal);
        if (after is null) return AuxResult.Done;
        var purchase = Detect(after, TargetIds.RevivePurchase);
        var btn2 = Detect(after, TargetIds.ReviveButton);
        var state2 = Detect(after, TargetIds.ReviveState);
        if (purchase.Found)
        {
            _evidence.Save(after.Image, "revive_purchase", Report("부활 후 구매 안내", purchase, btn2, state2));
            return AuxResult.Failed($"부활 재료 부족/구매 안내 감지 — 구매하지 않고 정지. {purchase.Summary()}");
        }
        if (!btn2.Found && !state2.Found)
        {
            _log.Info("부활 확인(부활 버튼과 행동불능 화면이 사라짐)");
            _reviveAttempts = 0;
        }
        else
        {
            _evidence.Save(after.Image, "revive_unexpected", Report("부활 클릭 후에도 행동불능 화면", btn2, state2));
            _log.Warn($"부활 클릭 후에도 행동불능 화면 근거가 남음 ({_reviveAttempts}/{T.ReviveMaxAttempts}): {btn2.Summary()} / {state2.Summary()}");
        }
        return AuxResult.Done;
    }

    // ───────────────────────── 공통 도구 ─────────────────────────

    private Frame? CaptureFrame(out string? fatal)
    {
        _ct.ThrowIfCancellationRequested();
        var r = _win.Capture();
        fatal = null;
        switch (r.Status)
        {
            case CaptureStatus.Ok:
                _once.Remove("inactive");
                _lastFrame?.Dispose();
                _lastFrame = r.Frame!.Image.Clone();
                return r.Frame;
            case CaptureStatus.Inactive:
                LogOnce("inactive", $"게임 창 비활성 — 분석·입력하지 않고 대기 ({r.Problem})");
                return null;
            default:
                fatal = $"게임 창 문제: {r.Problem}";
                return null;
        }
    }

    private Detection Detect(Frame f, string id)
    {
        var d = _det.Detect(f.Image, id);
        _recent[id] = d;
        return d;
    }

    /// <summary>
    /// 입력 후 대상 소멸 확인. 한 프레임의 인식 실패로 확정하지 않고 DisappearConfirmFrames 연속으로 없어야 한다.
    /// nextId가 보이면 다음 화면의 명확한 근거로 즉시 성공.
    /// </summary>
    private (bool Gone, string How, string? Fail) ConfirmGone(string targetId, string? nextId)
    {
        int missing = 0, attempts = 0;
        int maxAttempts = T.DisappearConfirmFrames + 4; // 비활성 프레임 등으로 인한 무한 대기 방지
        while (attempts++ < maxAttempts)
        {
            Wait(T.PostInputCheckMs);
            using var cap = CaptureFrame(out var fatal);
            if (fatal is not null) return (false, "", fatal);
            if (cap is null) continue;
            if (nextId is not null)
            {
                var next = Detect(cap, nextId);
                if (next.Found) return (true, $"다음 화면 근거: {next.Summary()}", null);
            }
            var d = Detect(cap, targetId);
            if (d.Found) return (false, "", null);
            if (++missing >= T.DisappearConfirmFrames)
                return (true, $"{missing}프레임 연속 미검출", null);
        }
        return (false, "", null);
    }

    private bool WaitForTarget(string id, int timeoutMs, out Detection? det)
    {
        var until = _clock.Now.AddMilliseconds(timeoutMs);
        det = null;
        do
        {
            Wait(T.PostInputCheckMs);
            using var cap = CaptureFrame(out var fatal);
            if (fatal is not null || cap is null) continue;
            var d = Detect(cap, id);
            if (d.Found) { det = d; return true; }
        } while (_clock.Now < until);
        return false;
    }

    private bool SendKey(ushort scan, string purpose, out string why)
    {
        why = "";
        if (!_win.IsForeground())
        {
            why = $"{ScanCode.Name(scan)} 입력 직전 게임 창이 활성 상태가 아님 — 입력 중단";
            Record("키", ScanCode.Name(scan) + " " + purpose, null, false, why);
            return false;
        }
        var r = _input.PressKey(scan, T.KeyHoldMs.Next(_rng));
        Record("키", ScanCode.Name(scan) + " " + purpose, null, r.Ok, r.Message);
        if (!r.Ok) why = $"{ScanCode.Name(scan)} 입력 전달 실패: {r.Message}";
        return r.Ok;
    }

    private bool SendClick(Frame cap, Detection d, string label, out string why)
    {
        why = "";
        if (d.ButtonRect is not { } rect || rect.Width <= 0 || rect.Height <= 0)
        {
            why = $"{label}: 클릭할 버튼 영역이 없음";
            return false;
        }
        if (!_win.IsForeground())
        {
            why = $"{label} 클릭 직전 게임 창이 활성 상태가 아님 — 입력 중단";
            Record("클릭", label, null, false, why);
            return false;
        }
        var origin = _win.CurrentOrigin();
        if (origin is not { } o || o != cap.Origin)
        {
            why = $"{label}: 캡처 후 게임 창 위치가 바뀌어 입력하지 않음";
            Record("클릭", label, null, false, why);
            return false;
        }
        var p = PointInside(rect);
        var screen = new Point(o.X + p.X, o.Y + p.Y);
        var r = _input.Click(screen, T.MouseHoldMs.Next(_rng));
        Record("클릭", label, screen, r.Ok, r.Message + $" (창 내부 {p.X},{p.Y} / 버튼 {rect.X},{rect.Y},{rect.Width}x{rect.Height})");
        if (!r.Ok) why = $"{label} 클릭 전달 실패: {r.Message}";
        return r.Ok;
    }

    /// <summary>버튼 중앙 절반 영역 안의 임의 위치. 버튼 밖으로 벗어나지 않는다.</summary>
    internal Point PointInside(Rect r)
    {
        int mx = r.Width / 4, my = r.Height / 4;
        int x0 = r.X + mx, x1 = r.Right - 1 - mx;
        int y0 = r.Y + my, y1 = r.Bottom - 1 - my;
        if (x1 < x0) x0 = x1 = r.X + r.Width / 2;
        if (y1 < y0) y0 = y1 = r.Y + r.Height / 2;
        return new Point(_rng.Next(x0, x1 + 1), _rng.Next(y0, y1 + 1));
    }

    private void Record(string kind, string target, Point? screen, bool ok, string msg)
    {
        _lastInput = new LastInput(_clock.Now, kind, target, screen, ok, msg);
        if (ok) _log.Info($"입력: {_lastInput}");
        else _log.Error($"입력: {_lastInput}");
    }

    private bool IntervalOk(string key, int minMs)
    {
        var now = _clock.Now;
        if (_lastAction.TryGetValue(key, out var last) && (now - last).TotalMilliseconds < minMs) return false;
        _lastAction[key] = now;
        return true;
    }

    private void Wait(int ms) => _waiter.Wait(ms, _ct);
    private void Wait(IntRange r) => _waiter.Wait(r.Next(_rng), _ct);

    private void LogOnce(string key, string message)
    {
        if (_once.Add(key)) _log.Info(message);
    }

    private object Report(string reason, params Detection[] dets) => new
    {
        time = _clock.Now,
        step = StepNames.Korean(_currentStep),
        destination = _destKey,
        reason,
        detections = dets.Select(DetectionReport).ToList(),
        lastInput = _lastInput?.ToString(),
    };

    private string? SaveFailure(StepId step, string reason)
    {
        var report = new
        {
            time = _clock.Now,
            step = StepNames.Korean(step),
            destination = _destKey,
            reason,
            lastInput = _lastInput?.ToString(),
            recentDetections = _recent.Values.Select(DetectionReport).ToList(),
            completed = Stats.Completed,
        };
        return _evidence.Save(_lastFrame, $"error_{step}", report);
    }

    private static object DetectionReport(Detection d) => new
    {
        target = d.TargetId,
        summary = d.Summary(),
        found = d.Found,
        score = d.Score,
        pass = d.PassScore,
        region = new[] { d.SearchRegion.Left, d.SearchRegion.Top, d.SearchRegion.Right, d.SearchRegion.Bottom },
        ocrText = d.Ocr?.RawText,
        images = d.Images.Select(i => new { i.File, score = Math.Round(i.Score, 4), i.Threshold, i.Matched, i.Note }),
        color = d.Color?.Describe(),
    };
}
