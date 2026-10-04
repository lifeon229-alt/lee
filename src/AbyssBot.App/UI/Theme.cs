using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AbyssBot.App.UI;

/// <summary>어두운 테마 색과 글꼴.</summary>
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(18, 20, 26);
    public static readonly Color Card = Color.FromArgb(28, 31, 40);
    public static readonly Color CardBorder = Color.FromArgb(44, 48, 60);
    public static readonly Color Field = Color.FromArgb(38, 42, 54);
    public static readonly Color FieldHover = Color.FromArgb(50, 55, 70);
    public static readonly Color Text = Color.FromArgb(236, 238, 243);
    public static readonly Color Sub = Color.FromArgb(150, 156, 170);
    public static readonly Color Accent = Color.FromArgb(124, 92, 255);
    public static readonly Color Green = Color.FromArgb(34, 197, 94);
    public static readonly Color Red = Color.FromArgb(239, 68, 68);
    public static readonly Color Amber = Color.FromArgb(245, 158, 11);
    public static readonly Color Gray = Color.FromArgb(110, 116, 130);

    private const string Family = "Malgun Gothic";
    public static Font F(float size, FontStyle style = FontStyle.Regular) => new(Family, size, style, GraphicsUnit.Point);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 1) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

/// <summary>둥근 모서리 카드. 제목을 위에 그린다.</summary>
internal sealed class CardPanel : Panel
{
    public CardPanel(string title)
    {
        Title = title;
        DoubleBuffered = true;
        BackColor = Theme.Bg;
        Padding = new Padding(18, title.Length > 0 ? 48 : 16, 18, 16);
        Margin = new Padding(8);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 14);
        using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
        using (var p = new Pen(Theme.CardBorder)) g.DrawPath(p, path);
        if (Title.Length > 0)
        {
            using var f = Theme.F(11f, FontStyle.Bold);
            TextRenderer.DrawText(g, Title, f, new Point(18, 15), Theme.Text);
        }
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
}

/// <summary>둥근 버튼.</summary>
internal sealed class PillButton : Control
{
    private bool _hover, _down;

    public PillButton(string text, Color color)
    {
        Text = text;
        Color_ = color;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = Theme.F(10f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Size = new Size(120, 40);
        Margin = new Padding(4);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Color_ { get; set; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var c = !Enabled ? Theme.Field : _down ? Theme.Blend(Color_, Color.Black, 0.2f) : _hover ? Theme.Blend(Color_, Color.White, 0.12f) : Color_;
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Math.Min(12, Height / 2f));
        using (var b = new SolidBrush(c)) g.FillPath(b, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Gray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>켜고 끄는 스위치와 설명.</summary>
internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch(string text, string? note = null)
    {
        Text = text;
        Note = note;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Font = Theme.F(10f);
        ForeColor = Theme.Text;
        BackColor = Theme.Card;
        Height = 34;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 2, 0, 2);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Note { get; set; }

    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);
        var track = new RectangleF(0, (Height - 22) / 2f, 40, 22);
        var on = Checked && Enabled;
        using (var path = Theme.Round(track, 11))
        using (var b = new SolidBrush(on ? Theme.Accent : Theme.Field))
            g.FillPath(b, path);
        float knobX = Checked ? track.Right - 19 : track.X + 3;
        using (var b = new SolidBrush(Enabled ? Color.White : Theme.Gray))
            g.FillEllipse(b, knobX, track.Y + 3, 16, 16);
        var textColor = Enabled ? Theme.Text : Theme.Gray;
        var size = TextRenderer.MeasureText(g, Text, Font);
        TextRenderer.DrawText(g, Text, Font, new Point(52, (Height - size.Height) / 2), textColor);
        if (!string.IsNullOrEmpty(Note))
        {
            using var f = Theme.F(8.5f);
            var ns = TextRenderer.MeasureText(g, Note, f);
            TextRenderer.DrawText(g, Note, f, new Point(52 + size.Width + 6, (Height - ns.Height) / 2 + 1), Theme.Sub);
        }
    }
}

/// <summary>여러 항목 중 하나를 고르는 둥근 선택 막대.</summary>
internal sealed class Segmented : Control
{
    private readonly List<string> _items = new();
    private int _selected, _hover = -1;

    public Segmented(params string[] items)
    {
        _items.AddRange(items);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.F(9.5f, FontStyle.Bold);
        BackColor = Theme.Card;
        Height = 40;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 2, 0, 10);
    }

    public event EventHandler? SelectedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set { if (value >= 0 && value < _items.Count && value != _selected) { _selected = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); } }
    }

    private RectangleF Cell(int i)
    {
        float w = (Width - 8f) / _items.Count;
        return new RectangleF(4 + i * w, 4, w, Height - 8);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _items.Count; i++) if (Cell(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e) { var h = HitTest(e.Location); if (h != _hover) { _hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { if (Enabled) { var h = HitTest(e.Location); if (h >= 0) SelectedIndex = h; } base.OnMouseClick(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10))
        using (var b = new SolidBrush(Theme.Field))
            g.FillPath(b, path);
        for (int i = 0; i < _items.Count; i++)
        {
            var r = Cell(i);
            if (i == _selected || i == _hover)
            {
                var c = i == _selected ? (Enabled ? Theme.Accent : Theme.Gray) : Theme.FieldHover;
                using var path = Theme.Round(r, 8);
                using var b = new SolidBrush(c);
                g.FillPath(b, path);
            }
            var fg = i == _selected ? Color.White : Enabled ? Theme.Sub : Theme.Gray;
            TextRenderer.DrawText(g, _items[i], Font, Rectangle.Round(r), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>작은 제목과 큰 값을 보여 주는 통계 칸.</summary>
internal sealed class StatTile : Control
{
    private string _value = "-";

    public StatTile(string caption)
    {
        Caption = caption;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Height = 78;
        Margin = new Padding(4);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value { get => _value; set { if (_value != value) { _value = value; Invalidate(); } } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10))
        using (var b = new SolidBrush(Theme.Field))
            g.FillPath(b, path);
        using var cf = Theme.F(8.5f);
        using var vf = Theme.F(16f, FontStyle.Bold);
        TextRenderer.DrawText(g, Caption, cf, new Point(14, 10), Theme.Sub);
        TextRenderer.DrawText(g, Value, vf, new Rectangle(12, 30, Width - 20, Height - 34), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>실행 단계 진행 표시(점과 이름). 현재 단계를 강조한다.</summary>
internal sealed class StepTracker : Control
{
    private string[] _steps = Array.Empty<string>();
    private int _current = -1;

    public StepTracker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Height = 64;
        Font = Theme.F(8.5f);
    }

    public void SetSteps(string[] steps) { _steps = steps; _current = -1; Invalidate(); }
    public void SetCurrent(int index) { _current = index; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_steps.Length == 0) return;
        Theme.Smooth(g);
        float pad = 30, y = 18;
        float step = _steps.Length == 1 ? 0 : (Width - 2 * pad) / (_steps.Length - 1);
        using (var line = new Pen(Theme.Field, 3))
            g.DrawLine(line, pad, y, Width - pad, y);
        if (_current > 0)
            using (var line = new Pen(Theme.Accent, 3))
                g.DrawLine(line, pad, y, pad + step * _current, y);
        for (int i = 0; i < _steps.Length; i++)
        {
            float x = pad + step * i;
            bool done = i < _current, cur = i == _current;
            var c = cur ? Theme.Accent : done ? Theme.Blend(Theme.Accent, Theme.Card, 0.35f) : Theme.Field;
            float r = cur ? 8 : 6;
            if (cur)
                using (var halo = new SolidBrush(Color.FromArgb(70, Theme.Accent)))
                    g.FillEllipse(halo, x - 13, y - 13, 26, 26);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
            var text = _steps[i];
            var size = TextRenderer.MeasureText(g, text, Font);
            TextRenderer.DrawText(g, text, Font, new Point((int)(x - size.Width / 2f), (int)y + 16),
                cur ? Theme.Text : done ? Theme.Sub : Theme.Gray);
        }
    }
}

/// <summary>상태를 색 점과 글자로 보여 주는 표시.</summary>
internal sealed class StatusPill : Control
{
    private Color _dot = Theme.Gray;

    public StatusPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        Font = Theme.F(9.5f, FontStyle.Bold);
        Height = 34;
        Text = "대기";
    }

    public void Set(string text, Color dot)
    {
        Text = text;
        _dot = dot;
        using var g = CreateGraphics();
        Width = Math.Min(560, TextRenderer.MeasureText(g, text, Font).Width + 44);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
        using (var b = new SolidBrush(Theme.Field))
            g.FillPath(b, path);
        using (var b = new SolidBrush(_dot)) g.FillEllipse(b, 14, Height / 2f - 5, 10, 10);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(32, 0, Width - 40, Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}
