using AbyssBot.App.Platform;
using AbyssBot.Core.Config;

namespace AbyssBot.App.UI;

/// <summary>준비 상태: 관리자 권한, 한국어 OCR, Interception, 게임 창, 목적지 사진을 점검하고 설치 버튼을 제공한다.</summary>
public sealed class SetupForm : Form
{
    private readonly Func<LoadedConfig?> _reload;
    private readonly Action<string> _log;
    private readonly TableLayoutPanel _rows = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, BackColor = Theme.Card };
    private readonly Label _busy = new() { AutoSize = true, ForeColor = Theme.Amber, BackColor = Theme.Card, Font = Theme.F(9.5f) };
    private readonly ComboBox _windows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420, FlatStyle = FlatStyle.Flat, BackColor = Theme.Field, ForeColor = Theme.Text, Font = Theme.F(9.5f) };
    private bool _working;

    public bool AllReady { get; private set; }

    public SetupForm(Func<LoadedConfig?> reload, Action<string> log)
    {
        _reload = reload; _log = log;
        Text = "준비 상태";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.F(9.5f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(760, 600);

        var card = new CardPanel("사용 전 준비") { Dock = DockStyle.Fill };
        var intro = new Label
        {
            Text = "아래 항목이 모두 초록색이면 바로 사용할 수 있습니다. 빨간 항목은 오른쪽 버튼으로 해결하세요.",
            AutoSize = false, Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Sub, BackColor = Theme.Card,
        };
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

        var winRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Card, Padding = new Padding(0, 14, 0, 0) };
        var refresh = new PillButton("목록 새로고침", Theme.Field) { Width = 120, Height = 32 };
        refresh.Click += (_, _) => FillWindows();
        var pick = new PillButton("이 창 사용", Theme.Accent) { Width = 100, Height = 32 };
        pick.Click += (_, _) => PickWindow();
        winRow.Controls.Add(new Label { Text = "게임 창 선택", AutoSize = true, ForeColor = Theme.Text, Font = Theme.F(10f, FontStyle.Bold), Padding = new Padding(0, 7, 8, 0) });
        winRow.Controls.AddRange(new Control[] { _windows, pick, refresh });

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Card };
        var close = new PillButton("닫기", Theme.Field) { Width = 100 };
        close.Click += (_, _) => Close();
        CancelButton = new EscCloser(this);
        var recheck = new PillButton("다시 점검", Theme.Accent) { Width = 110 };
        recheck.Click += (_, _) => Refresh_();
        bottom.Controls.AddRange(new Control[] { close, recheck, _busy });

        card.Controls.Add(winRow);
        card.Controls.Add(_rows);
        card.Controls.Add(intro);
        card.Controls.Add(bottom);
        Controls.Add(card);
        Padding = new Padding(8);

        Shown += (_, _) => { FillWindows(); Refresh_(); };
    }

    private void Refresh_()
    {
        var cfg = _reload();
        _rows.SuspendLayout();
        _rows.Controls.Clear();
        _rows.RowStyles.Clear();
        var checks = new List<(string title, SetupService.Check c, string? action, Func<Task>? run)>
        {
            ("관리자 권한", SetupService.CheckAdmin(), null, null),
            ("한국어 OCR", SetupService.CheckOcr(), "설치", InstallOcr),
            ("입력 드라이버 (Interception)", SetupService.CheckInterception(), "설치", InstallInterception),
            ("게임 창", SetupService.CheckGameWindow(cfg), null, null),
            ("목적지 배너 사진", SetupService.CheckImages(cfg), null, null),
        };
        foreach (var (title, c, action, run) in checks)
        {
            var dot = new Label { Text = "●", ForeColor = c.Ok ? Theme.Green : Theme.Red, Font = Theme.F(14f), AutoSize = true, BackColor = Theme.Card, Padding = new Padding(0, 6, 0, 0) };
            var text = new Label
            {
                AutoSize = true, MaximumSize = new Size(520, 0), BackColor = Theme.Card, Padding = new Padding(0, 6, 0, 10),
                Text = $"{title}\n{c.Detail}", ForeColor = Theme.Text,
            };
            Control right = new Label { BackColor = Theme.Card, AutoSize = true };
            if (!c.Ok && action is not null && run is not null)
            {
                var b = new PillButton(action, Theme.Accent) { Width = 110, Height = 34, Enabled = !_working };
                b.Click += async (_, _) => await RunGuarded(run);
                right = b;
            }
            _rows.Controls.Add(dot);
            _rows.Controls.Add(text);
            _rows.Controls.Add(right);
        }
        _rows.ResumeLayout();
        AllReady = checks.All(x => x.c.Ok);
    }

    private async Task RunGuarded(Func<Task> run)
    {
        if (_working) return;
        _working = true;
        Refresh_();
        try { await run(); }
        finally { _working = false; _busy.Text = ""; Refresh_(); }
    }

    private async Task InstallOcr()
    {
        _busy.Text = "한국어 OCR 설치 중… (수 분 걸릴 수 있습니다)";
        var (ok, log) = await SetupService.InstallOcrAsync();
        _log("한국어 OCR 설치: " + log);
        var after = SetupService.CheckOcr();
        MessageBox.Show(this,
            ok && after.Ok ? "한국어 OCR 설치 완료." :
            ok ? "설치는 끝났지만 아직 인식되지 않습니다. 프로그램을 다시 시작하거나 재부팅한 뒤 확인하세요." :
            "설치하지 못했습니다. 인터넷 연결과 Windows 업데이트 상태를 확인하세요.\n\n" + Tail(log),
            "한국어 OCR");
    }

    private async Task InstallInterception()
    {
        var confirm = MessageBox.Show(this,
            "Interception 입력 드라이버를 공식 GitHub(oblitum/Interception v1.0.1)에서 받아 설치합니다.\n" +
            "설치 후 재부팅해야 사용할 수 있습니다.\n\n" +
            "주의: 입력 드라이버와 자동화 프로그램은 게임 보안 프로그램에 감지되거나 이용약관에 어긋날 수 있습니다.\n\n계속할까요?",
            "입력 드라이버 설치", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;
        var progress = new Progress<string>(s => _busy.Text = s);
        var (ok, log) = await SetupService.InstallInterceptionAsync(progress);
        _log("Interception 설치: " + log);
        if (!ok)
        {
            MessageBox.Show(this, Tail(log), "입력 드라이버 설치 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (MessageBox.Show(this, "드라이버 설치가 끝났습니다. 재부팅해야 적용됩니다.\n지금 재부팅할까요? (10초 뒤)",
                "재부팅 필요", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            SetupService.Reboot();
    }

    private sealed record WinItem(string Title, string Process)
    {
        public override string ToString() => $"{Title}   ({Process})";
    }

    private void FillWindows()
    {
        var selfName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        var items = GameWindow.ListWindows()
            .Where(w => w.process != selfName)
            .Select(w => new WinItem(w.title, w.process))
            .DistinctBy(w => w.Title + "|" + w.Process)
            .ToList();
        _windows.Items.Clear();
        foreach (var i in items) _windows.Items.Add(i);
        var cfg = _reload();
        var current = cfg?.Scenario.Window.TitleContains;
        _windows.SelectedItem =
            items.FirstOrDefault(i => !string.IsNullOrEmpty(current) && i.Title.Contains(current)) ??
            items.FirstOrDefault(i => i.Title.Contains("마비노기")) ??
            items.FirstOrDefault();
    }

    private void PickWindow()
    {
        if (_windows.SelectedItem is not WinItem w) return;
        var cfg = _reload();
        if (cfg is null) { MessageBox.Show(this, "설정 파일을 읽지 못했습니다.", "게임 창"); return; }
        SetupService.SaveGameWindow(cfg, w.Title, w.Process);
        _log($"게임 창 선택 저장: '{w.Title}' ({w.Process})");
        Refresh_();
    }

    /// <summary>ESC로 닫기.</summary>
    private sealed class EscCloser(Form f) : IButtonControl
    {
        public DialogResult DialogResult { get; set; } = DialogResult.Cancel;
        public void NotifyDefault(bool value) { }
        public void PerformClick() => f.Close();
    }

    private static string Tail(string s) => s.Length <= 1200 ? s : "…" + s[^1200..];
}
