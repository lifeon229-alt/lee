using System.Diagnostics;
using AbyssBot.App.Platform;
using AbyssBot.Core.Config;
using AbyssBot.Core.Engine;
using AbyssBot.Core.Logging;
using AbyssBot.Core.Vision;
using Cv2 = OpenCvSharp.Cv2;

namespace AbyssBot.App.UI;

public sealed class MainForm : Form, IEngineObserver, ISessionNotifier, IResumeNotifier
{
    private const int HotkeyId = 0x0A10;
    private static readonly string BaseDir = AppContext.BaseDirectory;

    // 설정
    private Segmented _dest = new();
    private readonly List<string> _destKeys = new();
    private readonly Segmented _mode = new("다른 던전 가기", "다시 하기");
    private readonly NumericUpDown _targetRuns = new()
    {
        Minimum = 0, Maximum = 9999, Width = 90, BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Field, ForeColor = Theme.Text, Font = Theme.F(11f, FontStyle.Bold), TextAlign = HorizontalAlignment.Center,
    };
    private readonly ToggleSwitch _skip = new("대화·장면 넘기기");
    private readonly ToggleSwitch _revive = new("여기서 부활");
    private readonly ToggleSwitch _meal = new("음식 사용");
    private readonly ToggleSwitch _reconnect = new("재접속");
    private readonly ToggleSwitch _autoResume = new("오류 후 자동 재개", "10분 무입력 시 화면 재판별");

    // 진행
    private readonly StatusPill _status = new();
    private readonly StepTracker _tracker = new() { Dock = DockStyle.Top };
    private readonly StatTile _stepTile = new("현재 단계");
    private readonly StatTile _countTile = new("완료 횟수");
    private readonly StatTile _elapsedTile = new("경과 시간");
    private readonly StatTile _avgTile = new("평균 한 판");
    private readonly PillButton _start = new("▶  시작", Theme.Green) { Size = new Size(170, 50), Font = Theme.F(12f, FontStyle.Bold) };
    private readonly PillButton _stop = new("■  정지 (F10)", Theme.Red) { Size = new Size(170, 50), Font = Theme.F(12f, FontStyle.Bold), Enabled = false };
    private readonly PillButton _toggleLog = new("상세 로그 보기 ▼", Theme.Field) { Size = new Size(150, 34), Font = Theme.F(9f, FontStyle.Bold) };

    private readonly CardPanel _logCard = new("상세 로그") { Dock = DockStyle.Fill, Visible = false };
    private readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Sub, Font = new Font("Consolas", 9f),
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly TableLayoutPanel _root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg, Padding = new Padding(10) };

    private BotLogger _logger;
    private LoadedConfig? _cfg;
    private WinOcrEngine? _ocr;
    private InterceptionInput? _input;
    private CancellationTokenSource? _cts;
    private RunStats? _stats;
    private bool _running;

    public MainForm()
    {
        Text = "어비스 반복 — 마비노기 모바일";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.F(9.5f);
        ClientSize = new Size(1060, 640);
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        LoadConfigSafe(out var loadError);
        _logger = new BotLogger(Path.Combine(BaseDir, "logs"), _cfg?.Scenario.Logging ?? new LoggingSpec());
        _logger.Line += l => UI(() => AppendLog(l.ToString()));
        BuildLayout();
        FillFromConfig();

        if (loadError is not null) _logger.Error("설정 읽기 실패: " + loadError);
        else _logger.Info(_cfg!.Describe());

        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += (_, _) => StopByUser();
        _toggleLog.Click += (_, _) => ToggleLog();
        _mode.SelectedChanged += (_, _) => UpdateTracker();
        _timer.Tick += (_, _) => RefreshStats();
        _timer.Start();
        SetStatus("대기 중", Theme.Gray);

        Shown += (_, _) => FirstRunCheck();
    }

    // ───────────── 화면 구성 ─────────────

    private void BuildLayout()
    {
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 540));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 머리글
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var title = new Label { Text = "어비스 반복", Font = Theme.F(18f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new System.Drawing.Point(10, 6) };
        var sub = new Label { Text = "마비노기 모바일  ·  입장, 결과 확인, 다음 판까지 자동 진행", Font = Theme.F(9f), ForeColor = Theme.Sub, AutoSize = true, Location = new System.Drawing.Point(13, 42) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, Width = 560, BackColor = Theme.Bg, Padding = new Padding(0, 14, 0, 0) };
        tools.Controls.Add(Tool("폴더 열기", () => Process.Start(new ProcessStartInfo { FileName = BaseDir, UseShellExecute = true })));
        tools.Controls.Add(Tool("화면 검증", OpenVerify));
        tools.Controls.Add(Tool("화면 캡처 저장", () => _ = CaptureSnapshotAsync()));
        tools.Controls.Add(Tool("준비 상태", () => OpenSetup()));
        header.Controls.Add(tools);
        header.Controls.Add(title);
        header.Controls.Add(sub);
        _root.Controls.Add(header);

        // 본문: 왼쪽 설정, 오른쪽 진행
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Bg, Margin = new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        body.Controls.Add(BuildSettingsCard());
        body.Controls.Add(BuildProgressCard());
        _root.Controls.Add(body);

        _logCard.Controls.Add(_log);
        _root.Controls.Add(_logCard);
        Controls.Add(_root);
    }

    private static PillButton Tool(string text, Action onClick)
    {
        var b = new PillButton(text, Theme.Card) { Height = 36, Font = Theme.F(9f, FontStyle.Bold), ForeColor = Theme.Text };
        using (var g = b.CreateGraphics()) b.Width = TextRenderer.MeasureText(g, text, b.Font).Width + 34;
        b.Click += (_, _) => onClick();
        return b;
    }

    private Control BuildSettingsCard()
    {
        var card = new CardPanel("실행 설정") { Dock = DockStyle.Fill };
        var col = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Card };
        col.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        col.Controls.Add(Section("목적지"));
        col.Controls.Add(_dest);
        col.Controls.Add(Section("다음 판 진행 방식"));
        col.Controls.Add(_mode);

        var runs = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 0, 0, 8), WrapContents = false };
        runs.Controls.Add(_targetRuns);
        runs.Controls.Add(new Label { Text = "회 후 정지   (0 = 무제한)", AutoSize = true, ForeColor = Theme.Sub, BackColor = Theme.Card, Padding = new Padding(6, 6, 0, 0) });
        col.Controls.Add(Section("목표 횟수"));
        col.Controls.Add(runs);

        col.Controls.Add(Section("보조 기능"));
        foreach (var t in new[] { _skip, _revive, _meal, _reconnect, _autoResume })
        {
            t.Dock = DockStyle.Top;
            col.Controls.Add(t);
        }
        foreach (Control c in col.Controls) if (c is Segmented) c.Dock = DockStyle.Top;
        card.Controls.Add(col);
        return card;
    }

    private Control BuildProgressCard()
    {
        var card = new CardPanel("진행 상황") { Dock = DockStyle.Fill };
        var col = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Card };
        col.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _status.BackColor = Theme.Card;
        _status.Margin = new Padding(0, 0, 0, 12);
        col.Controls.Add(_status);
        col.Controls.Add(_tracker);

        var tiles = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, Height = 176, BackColor = Theme.Card, Margin = new Padding(0, 10, 0, 6) };
        tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var t in new[] { _stepTile, _countTile, _elapsedTile, _avgTile }) { t.Dock = DockStyle.Fill; tiles.Controls.Add(t); }
        col.Controls.Add(tiles);

        var buttons = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 8, 0, 0), WrapContents = false };
        buttons.Controls.Add(_start);
        buttons.Controls.Add(_stop);
        col.Controls.Add(buttons);
        col.Controls.Add(new Label
        {
            Text = "시작을 누르면 3초 카운트다운 — 그 사이 게임 창을 클릭해 맨 앞에 두세요. F10은 언제든 즉시 정지합니다.",
            AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Theme.Sub, BackColor = Theme.Card, Margin = new Padding(4, 8, 0, 8),
        });
        col.Controls.Add(_toggleLog);
        card.Controls.Add(col);
        return card;
    }

    private static Label Section(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.Sub, BackColor = Theme.Card, Font = Theme.F(9f, FontStyle.Bold),
        Margin = new Padding(0, 6, 0, 4),
    };

    private void ToggleLog()
    {
        _logCard.Visible = !_logCard.Visible;
        _toggleLog.Text = _logCard.Visible ? "상세 로그 접기 ▲" : "상세 로그 보기 ▼";
        if (_logCard.Visible && ClientSize.Height < 900) ClientSize = new Size(ClientSize.Width, 900);
        if (!_logCard.Visible) ClientSize = new Size(ClientSize.Width, 640);
    }

    private void FillFromConfig()
    {
        if (_cfg is null) return;
        var o = _cfg.Scenario.Options;
        _destKeys.Clear();
        _destKeys.AddRange(_cfg.Scenario.Destinations.Keys);
        var parent = _dest.Parent;
        var idx = parent?.Controls.GetChildIndex(_dest) ?? -1;
        var fresh = new Segmented(_cfg.Scenario.Destinations.Values.Select(d => d.DisplayName).ToArray()) { Dock = DockStyle.Top };
        if (parent is not null) { parent.Controls.Remove(_dest); parent.Controls.Add(fresh); parent.Controls.SetChildIndex(fresh, idx); }
        _dest.Dispose();
        _dest = fresh;
        _dest.SelectedIndex = Math.Max(0, _destKeys.IndexOf(o.Destination));
        _mode.SelectedIndex = o.RepeatMode == RepeatMode.OtherDungeon ? 0 : 1;
        _targetRuns.Value = Math.Clamp(o.TargetRuns, 0, 9999);

        // 화면 자료가 없는 보조 기능은 켤 수 없게 표시한다.
        void Feature(ToggleSwitch t, bool value, params string[] targets)
        {
            bool ready = targets.All(id => _cfg.Targets.Targets.TryGetValue(id, out var d) && d.IsConfigured);
            t.Enabled = ready;
            t.Checked = ready && value;
            t.Note = ready ? null : "화면 자료 필요";
            t.Invalidate();
        }
        Feature(_skip, o.SkipDialogEnabled, TargetIds.Skip);
        Feature(_revive, o.ReviveEnabled, TargetIds.ReviveButton, TargetIds.ReviveState, TargetIds.RevivePurchase);
        Feature(_meal, o.MealEnabled, TargetIds.MealButton);
        Feature(_reconnect, o.ReconnectEnabled, TargetIds.ReconnectNotice, TargetIds.ReconnectRetry);
        _autoResume.Checked = o.AutoResumeEnabled;
        UpdateTracker();
    }

    private static readonly StepId[] TrackerOrder =
        { StepId.OpenMenu, StepId.SelectAbyss, StepId.SelectDestination, StepId.Enter, StepId.WaitResult, StepId.OtherDungeon };

    private void UpdateTracker() =>
        _tracker.SetSteps(new[] { "메뉴", "어비스", "목적지", "입장", "전투·결과", _mode.SelectedIndex == 0 ? "다른 던전" : "다시 하기" });

    private void SetRunningUi(bool running)
    {
        _running = running;
        _start.Enabled = !running;
        _stop.Enabled = running;
        // 실행 중 목적지·방식·기능은 잠근다(바꾸려면 정지 후 다시 시작 → 최초 입장부터).
        _dest.Enabled = _mode.Enabled = _targetRuns.Enabled = _autoResume.Enabled = !running;
        if (running) foreach (var t in new[] { _skip, _revive, _meal, _reconnect }) t.Enabled = false;
        else FillFromConfig();
    }

    private void FirstRunCheck()
    {
        bool ready = SetupService.CheckAdmin().Ok && SetupService.CheckOcr().Ok &&
                     File.Exists(SetupService.DllPath) && SetupService.CheckGameWindow(_cfg).Ok;
        if (!ready) OpenSetup();
    }

    private void OpenSetup()
    {
        using var f = new SetupForm(() => { LoadConfigSafe(out _); return _cfg; }, m => _logger.Info(m));
        f.ShowDialog(this);
        LoadConfigSafe(out _);
        if (!_running) FillFromConfig();
    }

    // ───────────── 시작/정지 ─────────────

    private bool LoadConfigSafe(out string? error)
    {
        try { _cfg = ConfigLoader.Load(BaseDir); error = null; return true; }
        catch (Exception e) when (e is ConfigException or IOException or UnauthorizedAccessException)
        {
            error = e.Message; return false;
        }
    }

    private async Task StartAsync()
    {
        if (_running) return;
        // 화면의 선택값을 읽어 둔 뒤, 실행 파일 옆 설정을 새로 읽어 반영한다.
        int destIndex = _dest.SelectedIndex, modeIndex = _mode.SelectedIndex, targetRuns = (int)_targetRuns.Value;
        bool skip = _skip.Checked, revive = _revive.Checked, meal = _meal.Checked, reconnect = _reconnect.Checked, auto = _autoResume.Checked;
        if (!LoadConfigSafe(out var err)) { Fail("설정 오류", err!); return; }
        var cfg = _cfg!;
        var o = cfg.Scenario.Options;
        if (destIndex < 0 || destIndex >= _destKeys.Count) { Fail("시작 불가", "목적지를 선택하세요."); return; }
        o.Destination = _destKeys[destIndex];
        o.RepeatMode = modeIndex == 0 ? RepeatMode.OtherDungeon : RepeatMode.Replay;
        o.TargetRuns = targetRuns;
        o.SkipDialogEnabled = skip;
        o.ReviveEnabled = revive;
        o.MealEnabled = meal;
        o.ReconnectEnabled = reconnect;
        o.AutoResumeEnabled = auto;
        try { ConfigLoader.SaveUser(cfg); } catch (IOException e) { _logger.Warn("user.json 저장 실패: " + e.Message); }
        _logger.Info(cfg.Describe());

        string? ocrProblem = null;
        _ocr ??= WinOcrEngine.TryCreate(out ocrProblem);
        if (_ocr is null) { Fail("한국어 OCR 없음", ocrProblem + "\n\n'준비 상태'에서 설치할 수 있습니다."); return; }

        if (_input is null)
        {
            var dg = InterceptionInput.Diagnose();
            _logger.Info("입력 장치 점검\n" + dg.Report);
            if (!dg.Ready) { Fail("입력 드라이버 준비 안 됨", dg.Report + "\n'준비 상태'에서 설치할 수 있습니다."); return; }
            _input = dg.Input;
        }

        var images = new ImageLibrary(cfg.ImagesDirectory);
        var detector = new Detector(cfg.Targets, images, _ocr);
        var problems = AbyssEngine.Preflight(cfg.Scenario, detector, t => MissingImages(cfg, images, t));
        if (problems.Count > 0) { images.Dispose(); Fail("시작 전 점검 실패", string.Join("\n", problems)); return; }

        var window = new GameWindow(cfg.Scenario.Window, cfg.Targets.Baseline);
        if (window.Find() is { } why) { images.Dispose(); Fail("게임 창", why + "\n\n'준비 상태'에서 게임 창을 고르세요."); return; }
        if (window.CaptureBounds() is { } b && cfg.Scenario.Window.RequireBaselineSize &&
            (b.Width != cfg.Targets.Baseline.Width || b.Height != cfg.Targets.Baseline.Height))
        {
            images.Dispose();
            Fail("게임 화면 크기", $"게임 화면 {b.Width}×{b.Height}가 기준 {cfg.Targets.Baseline.Width}×{cfg.Targets.Baseline.Height}와 다릅니다.\n" +
                             "게임을 테두리 없는 전체 화면 1920×1080으로 설정하세요.");
            return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetRunningUi(true);
        _tracker.SetCurrent(-1);
        try
        {
            // 시작 알림 + 카운트다운. F10/정지로 즉시 취소.
            for (int s = cfg.Scenario.Timing.StartCountdownSeconds; s > 0; s--)
            {
                SetStatus($"{s}초 후 시작 — 게임 창을 클릭하세요", Theme.Amber);
                Sounds.Countdown();
                await Task.Delay(1000, ct);
            }
            window.TryActivate();
            SetStatus("실행 중", Theme.Green);

            var clock = new SystemClock();
            var waiter = new RealWaiter();
            var evidence = new EvidenceStore(Path.Combine(BaseDir, "errors"), cfg.Scenario.Logging);
            var engine = new AbyssEngine(cfg.Scenario, detector, window, _input!, clock, waiter, _logger, evidence, this);
            _stats = engine.Stats;
            var classifier = new ScreenClassifier(detector, engine.DestinationTarget, engine.DestinationTitleTarget, o.ReconnectEnabled, engine.RepeatMode);
            Func<AutoResumeController> resumeFactory = () => new AutoResumeController(o, clock, waiter, new UserActivity(), window,
                () =>
                {
                    var cap = window.Capture();
                    if (cap.Frame is null) return null;
                    using (cap.Frame) return classifier.Classify(cap.Frame.Image);
                }, _logger, this);
            var session = new BotSession(cfg.Scenario, engine, resumeFactory, clock, _logger, this);
            await Task.Run(() => session.Run(ct));
        }
        catch (OperationCanceledException)
        {
            SetStatus("정지됨", Theme.Gray);
            _logger.Info("카운트다운 중 사용자 정지");
        }
        catch (Exception e)
        {
            _logger.Error("예상하지 못한 오류: " + e);
            SetStatus("오류 정지: " + e.Message, Theme.Red);
            Sounds.Error();
        }
        finally
        {
            SetRunningUi(false);
            _cts?.Dispose();
            _cts = null;
            images.Dispose();
        }
    }

    private static IEnumerable<string> MissingImages(LoadedConfig cfg, ImageLibrary images, string target)
    {
        if (!cfg.Targets.Targets.TryGetValue(target, out var def) || def.Images is not { Count: > 0 } list) return Array.Empty<string>();
        var missing = list.Where(i => !images.Exists(i.File)).Select(i => i.File).ToList();
        return missing.Count == list.Count ? missing : Array.Empty<string>();
    }

    private void StopByUser()
    {
        if (_cts is null) return;
        _logger.Info("정지 요청(F10/정지 버튼) — 자동 재개 예약도 해제");
        _cts.Cancel();
    }

    private void Fail(string title, string message)
    {
        _logger.Error($"{title}: {message}");
        SetStatus(title, Theme.Red);
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ───────────── 도구 ─────────────

    private void OpenVerify()
    {
        if (!LoadConfigSafe(out var err)) { Fail("설정 오류", err!); return; }
        string? problem = null;
        _ocr ??= WinOcrEngine.TryCreate(out problem);
        if (_ocr is null) { Fail("한국어 OCR 없음", problem + "\n\n'준비 상태'에서 설치할 수 있습니다."); return; }
        new VerifyForm(_cfg!, _ocr).Show(this);
    }

    private async Task CaptureSnapshotAsync()
    {
        if (_running) return;
        if (!LoadConfigSafe(out var err)) { Fail("설정 오류", err!); return; }
        var spec = new WindowSpec
        {
            TitleContains = _cfg!.Scenario.Window.TitleContains,
            ProcessName = _cfg.Scenario.Window.ProcessName,
            CaptureMode = _cfg.Scenario.Window.CaptureMode,
            RequireBaselineSize = false,
        };
        var window = new GameWindow(spec, _cfg.Targets.Baseline);
        if (window.Find() is { } why) { Fail("게임 창", why + "\n\n'준비 상태'에서 게임 창을 고르세요."); return; }
        for (int s = 3; s > 0; s--)
        {
            SetStatus($"{s}초 후 게임 화면을 캡처합니다 — 게임 창을 클릭하세요", Theme.Amber);
            await Task.Delay(1000);
        }
        var r = window.CaptureInternal(requireForeground: true);
        if (r.Frame is null) { Fail("캡처 실패", r.Problem ?? "알 수 없음"); return; }
        using var f = r.Frame;
        var dir = Path.Combine(BaseDir, "captures");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"capture_{DateTime.Now:yyyyMMdd_HHmmss}_{f.Width}x{f.Height}.png");
        Cv2.ImEncode(".png", f.Image, out var bytes);
        File.WriteAllBytes(path, bytes);
        var bl = _cfg.Targets.Baseline;
        var same = f.Width == bl.Width && f.Height == bl.Height;
        _logger.Info($"캡처 저장: {path} ({f.Width}×{f.Height}, 기준 {(same ? "일치" : "불일치")})");
        SetStatus("캡처 저장 완료", Theme.Gray);
        MessageBox.Show(this, $"{path}\n크기 {f.Width}×{f.Height} (기준 {bl.Width}×{bl.Height} {(same ? "일치" : "불일치")})", "캡처 저장");
    }

    // ───────────── 엔진/세션 알림 (작업 스레드 → UI) ─────────────

    public void StepChanged(StepId step) => UI(() =>
    {
        _stepTile.Value = StepNames.Korean(step);
        int i = Array.IndexOf(TrackerOrder, step == StepId.Replay ? StepId.OtherDungeon : step);
        _tracker.SetCurrent(i);
    });

    public void StatsChanged(RunStats stats) => UI(RefreshStats);

    public void Completed(RunStats stats) => UI(() =>
    {
        SetStatus($"정상 완료 — {stats.Completed}회", Theme.Green);
        Sounds.Completed();
    });

    public void ErrorStopped(RunResult result) => UI(() =>
    {
        SetStatus($"오류 정지 · {(result.Step is { } s ? StepNames.Korean(s) : "-")} · {result.Reason}", Theme.Red);
        Sounds.Error();
    });

    public void UserStopped() => UI(() => SetStatus("정지됨", Theme.Gray));

    public void AutoResumeWaiting(string message) => UI(() => SetStatus("자동 재개 대기 중 (10분 무입력 후 화면 재판별)", Theme.Amber));

    public void Countdown(int secondsLeft, Classification c) => UI(() =>
    {
        SetStatus($"{secondsLeft}초 뒤 자동 재개 — 입력하면 취소", Theme.Amber);
        Sounds.Countdown();
    });

    public void Cancelled(string why) => UI(() => SetStatus($"자동 재개 취소: {why}", Theme.Gray));

    private void RefreshStats()
    {
        if (_stats is null) { _countTile.Value = "0"; _elapsedTile.Value = "00:00:00"; _avgTile.Value = "-"; _stepTile.Value = "-"; return; }
        _countTile.Value = $"{_stats.Completed}회";
        if (_running) _elapsedTile.Value = (DateTime.Now - _stats.StartedAt).ToString(@"hh\:mm\:ss");
        _avgTile.Value = _stats.Average is { } a ? a.ToString(@"m\:ss") : "-";
    }

    private void SetStatus(string text, Color color) => _status.Set(text, color);

    private void AppendLog(string line)
    {
        if (_log.Lines.Length > 1500)
            _log.Lines = _log.Lines.Skip(500).ToArray();
        _log.AppendText(line + Environment.NewLine);
    }

    private void UI(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(a); else a();
    }

    // ───────────── F10 전역 단축키 ─────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Native.RegisterHotKey(Handle, HotkeyId, 0, Native.VK_F10))
            _logger.Warn("F10 전역 단축키 등록 실패 — 정지 버튼을 사용하세요.");
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Native.UnregisterHotKey(Handle, HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            StopByUser();
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        _input?.Dispose();
        base.OnFormClosing(e);
    }
}
