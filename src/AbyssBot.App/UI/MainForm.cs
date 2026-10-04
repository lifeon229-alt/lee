using System.Diagnostics;
using AbyssBot.App.Platform;
using AbyssBot.Core.Config;
using AbyssBot.Core.Engine;
using AbyssBot.Core.Logging;
using AbyssBot.Core.Vision;
using OpenCvSharp;

namespace AbyssBot.App.UI;

public sealed class MainForm : Form, IEngineObserver, ISessionNotifier, IResumeNotifier
{
    private const int HotkeyId = 0x0A10;
    private static readonly string BaseDir = AppContext.BaseDirectory;

    private readonly ComboBox _dest = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly NumericUpDown _targetRuns = new() { Minimum = 0, Maximum = 9999, Width = 70 };
    private readonly CheckBox _skip = new() { Text = "대화·장면 넘기기", AutoSize = true };
    private readonly CheckBox _revive = new() { Text = "여기서 부활", AutoSize = true };
    private readonly CheckBox _meal = new() { Text = "음식 사용", AutoSize = true };
    private readonly CheckBox _reconnect = new() { Text = "재접속", AutoSize = true };
    private readonly CheckBox _autoResume = new() { Text = "오류 후 자동 재개(10분 무입력)", AutoSize = true };
    private readonly Button _start = new() { Text = "시작", Width = 90, Height = 34 };
    private readonly Button _stop = new() { Text = "정지 (F10)", Width = 90, Height = 34, Enabled = false };
    private readonly Label _step = new() { AutoSize = true, Text = "-" };
    private readonly Label _count = new() { AutoSize = true, Text = "0" };
    private readonly Label _elapsed = new() { AutoSize = true, Text = "00:00:00" };
    private readonly Label _avg = new() { AutoSize = true, Text = "-" };
    private readonly Label _status = new() { AutoSize = true, Text = "대기", ForeColor = Color.DimGray };
    private readonly Button _toggleLog = new() { Text = "상세 로그 펼치기 ▼", AutoSize = true };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Visible = false, Font = new Font("Consolas", 9f) };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    private BotLogger _logger;
    private LoadedConfig? _cfg;
    private WinOcrEngine? _ocr;
    private InterceptionInput? _input;
    private CancellationTokenSource? _cts;
    private RunStats? _stats;
    private bool _running;

    public MainForm()
    {
        Text = "마비노기 모바일 어비스 반복";
        Width = 640; Height = 420;
        MinimumSize = new System.Drawing.Size(560, 360);
        StartPosition = FormStartPosition.CenterScreen;

        LoadConfigSafe(out var loadError);
        _logger = new BotLogger(Path.Combine(BaseDir, "logs"), _cfg?.Scenario.Logging ?? new LoggingSpec());
        _logger.Line += l => UI(() => AppendLog(l.ToString()));
        BuildLayout();
        FillFromConfig();

        if (loadError is not null) _logger.Error("설정 읽기 실패: " + loadError);
        else _logger.Info(_cfg!.Describe());

        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += (_, _) => StopByUser();
        _toggleLog.Click += (_, _) =>
        {
            _log.Visible = !_log.Visible;
            _toggleLog.Text = _log.Visible ? "상세 로그 접기 ▲" : "상세 로그 펼치기 ▼";
            Height = _log.Visible ? Math.Max(Height, 700) : 420;
        };
        _timer.Tick += (_, _) => RefreshStats();
        _timer.Start();
    }

    // ───────────── 화면 구성 ─────────────

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var row1 = Flow();
        row1.Controls.Add(Lbl("목적지"));
        row1.Controls.Add(_dest);
        row1.Controls.Add(Lbl("  목표 횟수(0=무제한)"));
        row1.Controls.Add(_targetRuns);
        root.Controls.Add(row1);

        var row2 = Flow();
        row2.Controls.AddRange(new Control[] { _skip, _revive, _meal, _reconnect, _autoResume });
        root.Controls.Add(row2);

        var row3 = Flow();
        row3.Controls.Add(_start);
        row3.Controls.Add(_stop);
        var verify = new Button { Text = "화면 검증…", AutoSize = true, Height = 34 };
        verify.Click += (_, _) => OpenVerify();
        var capture = new Button { Text = "현재 창 캡처 저장", AutoSize = true, Height = 34 };
        capture.Click += async (_, _) => await CaptureSnapshotAsync();
        var diag = new Button { Text = "입력 장치 점검", AutoSize = true, Height = 34 };
        diag.Click += (_, _) => ShowDiagnosis();
        var folder = new Button { Text = "설정 폴더", AutoSize = true, Height = 34 };
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo { FileName = BaseDir, UseShellExecute = true });
        row3.Controls.AddRange(new Control[] { verify, capture, diag, folder });
        root.Controls.Add(row3);

        var stats = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Dock = DockStyle.Top };
        stats.Controls.Add(Lbl("현재 단계")); stats.Controls.Add(_step);
        stats.Controls.Add(Lbl("완료 횟수")); stats.Controls.Add(_count);
        stats.Controls.Add(Lbl("경과 시간")); stats.Controls.Add(_elapsed);
        stats.Controls.Add(Lbl("평균 한 판")); stats.Controls.Add(_avg);
        stats.Controls.Add(Lbl("상태")); stats.Controls.Add(_status);
        stats.SetColumnSpan(_status, 3);
        _step.Font = new Font(Font, FontStyle.Bold);
        root.Controls.Add(stats);

        var logPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        logPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logPanel.Controls.Add(_toggleLog);
        logPanel.Controls.Add(_log);
        root.Controls.Add(logPanel);

        Controls.Add(root);
    }

    private static FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
    private static Label Lbl(string t) => new() { Text = t, AutoSize = true, Padding = new Padding(0, 6, 4, 0) };

    private void FillFromConfig()
    {
        _dest.Items.Clear();
        if (_cfg is null) return;
        var o = _cfg.Scenario.Options;
        foreach (var (key, d) in _cfg.Scenario.Destinations)
            _dest.Items.Add(new DestItem(key, d.DisplayName));
        _dest.SelectedItem = _dest.Items.Cast<DestItem>().FirstOrDefault(i => i.Key == o.Destination);
        _targetRuns.Value = Math.Clamp(o.TargetRuns, 0, 9999);
        _skip.Checked = o.SkipDialogEnabled;
        _revive.Checked = o.ReviveEnabled;
        _meal.Checked = o.MealEnabled;
        _reconnect.Checked = o.ReconnectEnabled;
        _autoResume.Checked = o.AutoResumeEnabled;
    }

    private sealed record DestItem(string Key, string Name)
    {
        public override string ToString() => Name;
    }

    private void SetRunningUi(bool running)
    {
        _running = running;
        _start.Enabled = !running;
        _stop.Enabled = running;
        // 실행 중 목적지와 기능 설정은 잠근다(바꾸려면 정지 후 다시 시작 → 최초 입장부터).
        foreach (var c in new Control[] { _dest, _targetRuns, _skip, _revive, _meal, _reconnect, _autoResume }) c.Enabled = !running;
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
        // 실행 파일 옆 설정을 매번 새로 읽는다.
        if (!LoadConfigSafe(out var err)) { Fail("설정 오류", err!); return; }
        var cfg = _cfg!;
        var o = cfg.Scenario.Options;
        if (_dest.SelectedItem is not DestItem dest) { Fail("시작 불가", "목적지를 선택하세요."); return; }
        o.Destination = dest.Key;
        o.TargetRuns = (int)_targetRuns.Value;
        o.SkipDialogEnabled = _skip.Checked;
        o.ReviveEnabled = _revive.Checked;
        o.MealEnabled = _meal.Checked;
        o.ReconnectEnabled = _reconnect.Checked;
        o.AutoResumeEnabled = _autoResume.Checked;
        try { ConfigLoader.SaveScenario(cfg); } catch (IOException e) { _logger.Warn("scenario.json 저장 실패: " + e.Message); }
        _logger.Info(cfg.Describe());

        string? ocrProblem = null;
        _ocr ??= WinOcrEngine.TryCreate(out ocrProblem);
        if (_ocr is null) { Fail("한국어 OCR 없음", ocrProblem!); return; }

        if (_input is null)
        {
            var dg = InterceptionInput.Diagnose();
            _logger.Info("입력 장치 점검\n" + dg.Report);
            if (!dg.Ready) { Fail("입력 장치 준비 안 됨", dg.Report); return; }
            _input = dg.Input;
        }

        var images = new ImageLibrary(cfg.ImagesDirectory);
        var detector = new Detector(cfg.Targets, images, _ocr);
        var problems = AbyssEngine.Preflight(cfg.Scenario, detector, t => MissingImages(cfg, images, t));
        if (problems.Count > 0) { Fail("시작 전 점검 실패", string.Join("\n", problems)); return; }

        var window = new GameWindow(cfg.Scenario.Window, cfg.Targets.Baseline);
        if (window.Find() is { } why) { Fail("게임 창", why + "\n\n현재 창 목록:\n" + WindowList()); return; }
        if (window.CaptureBounds() is { } b && cfg.Scenario.Window.RequireBaselineSize &&
            (b.Width != cfg.Targets.Baseline.Width || b.Height != cfg.Targets.Baseline.Height))
        {
            Fail("게임 창 크기", $"캡처 크기 {b.Width}×{b.Height}가 기준 {cfg.Targets.Baseline.Width}×{cfg.Targets.Baseline.Height}와 다릅니다.\n" +
                             "창 크기를 맞추거나 내 화면 기준으로 targets.json을 다시 설정하세요.");
            return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetRunningUi(true);
        try
        {
            // 시작 알림 + 카운트다운. F10/정지로 즉시 취소.
            for (int s = cfg.Scenario.Timing.StartCountdownSeconds; s > 0; s--)
            {
                SetStatus($"{s}초 후 시작 — 게임 창을 활성화하세요 (F10 정지)", Color.DarkOrange);
                Sounds.Countdown();
                await Task.Delay(1000, ct);
            }
            window.TryActivate();
            SetStatus("실행 중", Color.ForestGreen);

            var clock = new SystemClock();
            var waiter = new RealWaiter();
            var evidence = new EvidenceStore(Path.Combine(BaseDir, "errors"), cfg.Scenario.Logging);
            var engine = new AbyssEngine(cfg.Scenario, detector, window, _input!, clock, waiter, _logger, evidence, this);
            _stats = engine.Stats;
            var classifier = new ScreenClassifier(detector, engine.DestinationTarget, o.ReconnectEnabled);
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
            SetStatus("사용자 정지", Color.DimGray);
            _logger.Info("카운트다운 중 사용자 정지");
        }
        catch (Exception e)
        {
            _logger.Error("예상하지 못한 오류: " + e);
            SetStatus("오류 정지: " + e.Message, Color.Firebrick);
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
        SetStatus(title, Color.Firebrick);
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static string WindowList() =>
        string.Join("\n", GameWindow.ListWindows().Take(40).Select(w => $"  '{w.title}' ({w.process})"));

    // ───────────── 도구 ─────────────

    private void OpenVerify()
    {
        if (!LoadConfigSafe(out var err)) { Fail("설정 오류", err!); return; }
        string? problem = null;
        _ocr ??= WinOcrEngine.TryCreate(out problem);
        if (_ocr is null) { Fail("한국어 OCR 없음", problem!); return; }
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
        if (window.Find() is { } why) { Fail("게임 창", why + "\n\n현재 창 목록:\n" + WindowList()); return; }
        for (int s = 3; s > 0; s--)
        {
            SetStatus($"{s}초 후 게임 창을 캡처합니다 — 게임 창을 활성화하세요", Color.DarkOrange);
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
        var b = _cfg.Targets.Baseline;
        var same = f.Width == b.Width && f.Height == b.Height;
        _logger.Info($"캡처 저장: {path} ({f.Width}×{f.Height}, 캡처 방식 {spec.CaptureMode}, 기준 {(same ? "일치" : "불일치")})");
        SetStatus("캡처 저장 완료", Color.DimGray);
        MessageBox.Show(this, $"{path}\n크기 {f.Width}×{f.Height} (기준 {b.Width}×{b.Height} {(same ? "일치" : "불일치")})", "캡처 저장");
    }

    private void ShowDiagnosis()
    {
        var report = _input is not null ? "입력 장치가 이미 준비되어 있습니다.\n" : InterceptionInput.Diagnose().Report;
        _logger.Info("입력 장치 점검\n" + report);
        MessageBox.Show(this, report, "입력 장치 점검");
    }

    // ───────────── 엔진/세션 알림 (작업 스레드 → UI) ─────────────

    public void StepChanged(StepId step) => UI(() => _step.Text = StepNames.Korean(step));
    public void StatsChanged(RunStats stats) => UI(RefreshStats);

    public void Completed(RunStats stats) => UI(() =>
    {
        SetStatus($"정상 완료: {stats.Completed}회", Color.ForestGreen);
        Sounds.Completed();
    });

    public void ErrorStopped(RunResult result) => UI(() =>
    {
        SetStatus($"오류 정지 [{(result.Step is { } s ? StepNames.Korean(s) : "-")}] {result.Reason}", Color.Firebrick);
        Sounds.Error();
    });

    public void UserStopped() => UI(() => SetStatus("사용자 정지", Color.DimGray));

    public void AutoResumeWaiting(string message) => UI(() => SetStatus(message, Color.DarkOrange));

    public void Countdown(int secondsLeft, Classification c) => UI(() =>
    {
        SetStatus($"자동 재개 {secondsLeft}초 전 ({c.Reason}) — 입력하면 취소", Color.DarkOrange);
        Sounds.Countdown();
    });

    public void Cancelled(string why) => UI(() => SetStatus($"자동 재개 취소: {why}", Color.DimGray));

    private void RefreshStats()
    {
        if (_stats is null) return;
        _count.Text = _stats.Completed.ToString();
        if (_running) _elapsed.Text = (DateTime.Now - _stats.StartedAt).ToString(@"hh\:mm\:ss");
        _avg.Text = _stats.Average is { } a ? a.ToString(@"mm\:ss") : "-";
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

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
        if (!Platform.Native.RegisterHotKey(Handle, HotkeyId, 0, Platform.Native.VK_F10))
            _logger.Warn("F10 전역 단축키 등록 실패 — 정지 버튼을 사용하세요.");
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Platform.Native.UnregisterHotKey(Handle, HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Platform.Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
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
