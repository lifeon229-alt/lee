using AbyssBot.Core.Config;
using AbyssBot.Core.Ocr;
using AbyssBot.Core.Verify;
using AbyssBot.Core.Vision;
using OpenCvSharp;

namespace AbyssBot.App.UI;

/// <summary>
/// 저장된 화면으로 인식 위치를 검증한다(입력 없음). 정상 화면과 비슷한 다른 화면의 점수를 비교해 기준을 정할 때 쓴다.
/// </summary>
public sealed class VerifyForm : Form
{
    private readonly LoadedConfig _cfg;
    private readonly IOcrEngine _ocr;
    private readonly ListBox _files = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PictureBox _pic = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly TextBox _report = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f) };
    private readonly Dictionary<string, (string report, byte[] png, List<Detection> dets)> _results = new();

    public VerifyForm(LoadedConfig cfg, IOcrEngine ocr)
    {
        _cfg = cfg; _ocr = ocr;
        Text = "화면 검증 (입력 없음)";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.F(9.5f);
        _files.BackColor = Theme.Card; _files.ForeColor = Theme.Text; _files.BorderStyle = BorderStyle.None;
        _report.BackColor = Theme.Card; _report.ForeColor = Theme.Text; _report.BorderStyle = BorderStyle.None;
        Width = 1200; Height = 860;

        var open = new PillButton("화면 사진 열기…", Theme.Accent) { Width = 150, Height = 36 };
        open.Click += async (_, _) => await OpenAsync();
        var table = new PillButton("사진 점수표 저장", Theme.Field) { Width = 150, Height = 36 };
        table.Click += (_, _) => SaveTable();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), BackColor = Theme.Bg };
        top.Controls.AddRange(new Control[] { open, table, new Label
        {
            AutoSize = true, Padding = new Padding(8, 10, 0, 0), ForeColor = Theme.Sub,
            Text = "노랑=탐색 영역, 초록=합격 영역, 하늘=사진 일치, 자홍=OCR 일치, 빨강=클릭 대상 버튼",
        } });

        var left = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200 };
        left.Panel1.Controls.Add(_files);
        left.Panel2.Controls.Add(_report);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 640 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_pic);
        Controls.Add(split);
        Controls.Add(top);

        _files.SelectedIndexChanged += (_, _) => ShowSelected();
    }

    private async Task OpenAsync()
    {
        using var dlg = new OpenFileDialog { Multiselect = true, Filter = "PNG/JPG|*.png;*.jpg;*.jpeg;*.bmp", Title = "검증할 게임 화면 사진" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        UseWaitCursor = true;
        try
        {
            foreach (var path in dlg.FileNames)
            {
                var name = Path.GetFileName(path);
                var r = await Task.Run(() => VerifyOne(path, name));
                _results[name] = r;
                if (!_files.Items.Contains(name)) _files.Items.Add(name);
            }
            if (_files.Items.Count > 0) _files.SelectedIndex = _files.Items.Count - 1;
        }
        finally { UseWaitCursor = false; }
    }

    private (string, byte[], List<Detection>) VerifyOne(string path, string name)
    {
        using var images = new ImageLibrary(_cfg.ImagesDirectory);
        var det = new Detector(_cfg.Targets, images, _ocr);
        using var frame = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Color);
        if (frame.Empty()) return ($"■ {name}: 이미지를 읽지 못함", Array.Empty<byte>(), new());
        var v = new ScreenVerifier(_cfg, det).Verify(frame, name);
        using var ann = v.Annotated;
        Cv2.ImEncode(".png", ann, out var png);
        var dets = _cfg.Targets.Targets.Keys.Select(id => det.Detect(frame, id)).ToList();
        return (v.Report, png, dets);
    }

    private void ShowSelected()
    {
        if (_files.SelectedItem is not string name || !_results.TryGetValue(name, out var r)) return;
        _report.Text = r.report.Replace("\n", Environment.NewLine);
        _pic.Image?.Dispose();
        _pic.Image = r.png.Length == 0 ? null : Image.FromStream(new MemoryStream(r.png));
    }

    private void SaveTable()
    {
        if (_results.Count == 0) return;
        using var dlg = new SaveFileDialog { Filter = "TSV|*.tsv", FileName = $"scores_{DateTime.Now:yyyyMMdd_HHmmss}.tsv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var rows = _results.SelectMany(kv => kv.Value.dets.Select(d => (kv.Key, d)));
        File.WriteAllText(dlg.FileName, ScreenVerifier.ScoreTable(rows), System.Text.Encoding.UTF8);
    }
}
