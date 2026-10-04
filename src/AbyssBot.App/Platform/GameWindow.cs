using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using AbyssBot.Core.Config;
using AbyssBot.Core.Engine;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;

namespace AbyssBot.App.Platform;

/// <summary>
/// 게임 창 찾기와 캡처. 캡처 원점(Origin)을 클릭 좌표 계산에도 그대로 써서 제목 표시줄 포함 여부 차이로 좌표가 밀리지 않게 한다.
/// 화면 복사 방식이므로 게임 창이 활성(맨 앞) 상태일 때만 분석한다.
/// </summary>
public sealed class GameWindow(WindowSpec spec, BaselineSpec baseline) : IGameWindow
{
    private IntPtr _hwnd;

    public IntPtr Handle => _hwnd;

    public static List<(IntPtr hwnd, string title, string process)> ListWindows()
    {
        var list = new List<(IntPtr, string, string)>();
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h)) return true;
            int len = Native.GetWindowTextLength(h);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            Native.GetWindowText(h, sb, sb.Capacity);
            Native.GetWindowThreadProcessId(h, out var pid);
            string proc;
            try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { proc = "?"; }
            list.Add((h, sb.ToString(), proc));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>창을 찾는다. 실패 시 이유를 반환.</summary>
    public string? Find()
    {
        if (string.IsNullOrWhiteSpace(spec.TitleContains))
            return "scenario.json의 window.titleContains가 비어 있습니다. 게임 창 제목을 설정하세요.";
        var matches = ListWindows().Where(w =>
            w.title.Contains(spec.TitleContains, StringComparison.OrdinalIgnoreCase) &&
            (spec.ProcessName is null || string.Equals(w.process, spec.ProcessName, StringComparison.OrdinalIgnoreCase))).ToList();
        if (matches.Count == 0) return $"제목에 '{spec.TitleContains}'이(가) 포함된 창을 찾지 못했습니다.";
        if (matches.Count > 1)
            return $"조건에 맞는 창이 {matches.Count}개입니다: {string.Join(", ", matches.Select(m => $"'{m.title}'({m.process})"))} — processName으로 좁혀 주세요.";
        _hwnd = matches[0].hwnd;
        return null;
    }

    public bool IsForeground()
    {
        if (_hwnd == IntPtr.Zero) return false;
        var fg = Native.GetForegroundWindow();
        return fg == _hwnd || Native.GetAncestor(fg, Native.GA_ROOT) == _hwnd;
    }

    public bool TryActivate() => _hwnd != IntPtr.Zero && Native.SetForegroundWindow(_hwnd);

    /// <summary>캡처 영역(화면 좌표).</summary>
    public Rectangle? CaptureBounds()
    {
        if (_hwnd == IntPtr.Zero || !Native.IsWindow(_hwnd)) return null;
        switch (spec.CaptureMode)
        {
            case "ExtendedFrame":
                if (Native.DwmGetWindowAttribute(_hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var e, Marshal.SizeOf<Native.RECT>()) != 0) return null;
                return Rectangle.FromLTRB(e.Left, e.Top, e.Right, e.Bottom);
            case "Client":
                if (!Native.GetClientRect(_hwnd, out var c)) return null;
                var p = new Native.POINT();
                Native.ClientToScreen(_hwnd, ref p);
                return new Rectangle(p.X, p.Y, c.Right - c.Left, c.Bottom - c.Top);
            default: // WindowRect: 테두리·제목 표시줄 포함
                if (!Native.GetWindowRect(_hwnd, out var w)) return null;
                return Rectangle.FromLTRB(w.Left, w.Top, w.Right, w.Bottom);
        }
    }

    public CvPoint? CurrentOrigin() => CaptureBounds() is { } b ? new CvPoint(b.X, b.Y) : null;

    public CaptureResult Capture() => CaptureInternal(requireForeground: true);

    /// <summary>캡처 저장 도구용(활성 상태 요구는 같다: 화면 복사이므로 가려진 창은 찍을 수 없음).</summary>
    public CaptureResult CaptureInternal(bool requireForeground)
    {
        if (_hwnd == IntPtr.Zero || !Native.IsWindow(_hwnd))
        {
            var why = Find();
            if (why is not null) return CaptureResult.Fatal(why);
        }
        if (Native.IsIconic(_hwnd)) return CaptureResult.Inactive("게임 창이 최소화됨");
        if (requireForeground && !IsForeground()) return CaptureResult.Inactive("다른 프로그램이 활성 상태");
        if (CaptureBounds() is not { } b || b.Width <= 0 || b.Height <= 0) return CaptureResult.Fatal("게임 창 영역을 읽지 못함");

        if (spec.RequireBaselineSize && (b.Width != baseline.Width || b.Height != baseline.Height))
            return CaptureResult.Fatal(
                $"캡처 크기 {b.Width}×{b.Height}가 기준 {baseline.Width}×{baseline.Height}와 다릅니다(캡처 방식 {spec.CaptureMode}). " +
                "게임 창 크기를 기준과 같게 맞추거나, 내 화면 기준으로 targets.json의 baseline과 영역을 다시 설정하세요. 단순 비율 변환은 하지 않습니다.");

        using var bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size, CopyPixelOperation.SourceCopy);
        var mat = ToMat(bmp);
        return CaptureResult.Ok(new Frame(mat, new CvPoint(b.X, b.Y), DateTime.Now));
    }

    private static Mat ToMat(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var bgra = Mat.FromPixelData(bmp.Height, bmp.Width, MatType.CV_8UC4, data.Scan0, data.Stride);
            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
