using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using AbyssBot.Core.Engine;
using CvPoint = OpenCvSharp.Point;

namespace AbyssBot.App.Platform;

/// <summary>
/// Interception 드라이버 입력. x64 interception.dll(실행 파일 옆)과 드라이버 설치가 모두 필요하다.
/// DLL만 있고 드라이버가 없으면 컨텍스트 생성 또는 장치 조회가 실패하므로 '설치 완료'로 보지 않는다.
/// </summary>
public sealed class InterceptionInput : IInputDevice, IDisposable
{
    private const string Dll = "interception.dll";

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyStroke { public ushort Code; public ushort State; public uint Information; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseStroke { public ushort State; public ushort Flags; public short Rolling; public int X; public int Y; public uint Information; }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr interception_create_context();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void interception_destroy_context(IntPtr ctx);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int interception_send(IntPtr ctx, int device, ref KeyStroke s, uint n);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_send")] private static extern int interception_send_mouse(IntPtr ctx, int device, ref MouseStroke s, uint n);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint interception_get_hardware_id(IntPtr ctx, int device, IntPtr buf, uint size);

    private const ushort KeyDown = 0x00, KeyUp = 0x01;
    private const ushort MouseLeftDown = 0x001, MouseLeftUp = 0x002;
    private const ushort MoveAbsolute = 0x001, VirtualDesktop = 0x002;
    private const int CursorTolerancePx = 2;

    private readonly IntPtr _ctx;
    private readonly int _keyboard, _mouse;

    private InterceptionInput(IntPtr ctx, int keyboard, int mouse) { _ctx = ctx; _keyboard = keyboard; _mouse = mouse; }

    public sealed record Diagnosis(bool Ready, string Report, InterceptionInput? Input);

    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>x64, DLL, 드라이버, 장치, 관리자 권한을 점검하고 사용 가능하면 입력 장치를 만든다.</summary>
    public static Diagnosis Diagnose()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"64비트 프로세스: {(Environment.Is64BitProcess ? "예" : "아니오 — x64로 실행해야 함")}");
        sb.AppendLine($"관리자 권한: {(IsAdmin() ? "예" : "아니오 — 관리자 권한으로 실행 필요")}");
        var dllPath = Path.Combine(AppContext.BaseDirectory, Dll);
        sb.AppendLine($"interception.dll: {(File.Exists(dllPath) ? dllPath : "실행 파일 옆에 없음")}");
        if (!Environment.Is64BitProcess || !File.Exists(dllPath))
            return new Diagnosis(false, sb.ToString(), null);

        IntPtr ctx;
        try { ctx = interception_create_context(); }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            sb.AppendLine($"DLL 로드 실패: {e.Message} (x64 DLL인지 확인)");
            return new Diagnosis(false, sb.ToString(), null);
        }
        if (ctx == IntPtr.Zero)
        {
            sb.AppendLine("드라이버: 연결 실패 — install-interception.exe /install 후 재부팅이 필요합니다(DLL 복사만으로는 설치되지 않음).");
            return new Diagnosis(false, sb.ToString(), null);
        }

        int kb = 0, ms = 0;
        var buf = Marshal.AllocHGlobal(1024);
        try
        {
            for (int dev = 1; dev <= 20; dev++)
            {
                uint n = interception_get_hardware_id(ctx, dev, buf, 1024);
                if (n == 0) continue;
                var hw = Marshal.PtrToStringUni(buf, (int)Math.Min(n / 2, 511))?.Split('\0')[0] ?? "";
                bool isKb = dev <= 10;
                sb.AppendLine($"  장치 {dev} ({(isKb ? "키보드" : "마우스")}): {hw}");
                if (isKb && kb == 0) kb = dev;
                if (!isKb && ms == 0) ms = dev;
            }
        }
        finally { Marshal.FreeHGlobal(buf); }

        sb.AppendLine($"사용할 키보드 장치: {(kb == 0 ? "없음" : kb)}, 마우스 장치: {(ms == 0 ? "없음" : ms)}");
        if (kb == 0 || ms == 0)
        {
            sb.AppendLine("드라이버는 열렸지만 키보드/마우스 장치를 찾지 못했습니다. 드라이버 설치 후 재부팅했는지 확인하세요.");
            interception_destroy_context(ctx);
            return new Diagnosis(false, sb.ToString(), null);
        }
        return new Diagnosis(true, sb.ToString(), new InterceptionInput(ctx, kb, ms));
    }

    public InputResult PressKey(ushort scanCode, int holdMs)
    {
        var down = new KeyStroke { Code = scanCode, State = KeyDown };
        var up = new KeyStroke { Code = scanCode, State = KeyUp };
        int sentDown = 0, sentUp = 0;
        try
        {
            sentDown = interception_send(_ctx, _keyboard, ref down, 1);
            if (sentDown > 0) Thread.Sleep(holdMs);
        }
        finally
        {
            // 정지·예외가 나도 반드시 해제
            sentUp = interception_send(_ctx, _keyboard, ref up, 1);
        }
        if (sentDown <= 0) return InputResult.Failure("키 누름 전달 실패(interception_send=0)");
        if (sentUp <= 0) return InputResult.Failure("키 해제 전달 실패(interception_send=0)");
        return InputResult.Success($"{ScanCode.Name(scanCode)} 누름 {holdMs}ms");
    }

    public InputResult Click(CvPoint screen, int holdMs)
    {
        if (!MoveTo(screen)) return InputResult.Failure("마우스 이동 전달 실패");
        Thread.Sleep(30);
        if (!CursorNear(screen, out var actual))
        {
            // 이동 오차일 수 있으니 한 번만 다시 옮긴다.
            if (!MoveTo(screen)) return InputResult.Failure("마우스 이동 전달 실패");
            Thread.Sleep(30);
            if (!CursorNear(screen, out actual))
                return InputResult.Failure($"커서가 목표({screen.X},{screen.Y})가 아닌 ({actual.X},{actual.Y})에 있음 — 사용자 이동 가능성, 클릭하지 않음");
        }

        var down = new MouseStroke { State = MouseLeftDown };
        var up = new MouseStroke { State = MouseLeftUp };
        int sentDown = 0, sentUp = 0;
        try
        {
            // 클릭 직전 마지막 확인
            if (!CursorNear(screen, out actual))
                return InputResult.Failure($"클릭 직전 커서 위치 변경 ({actual.X},{actual.Y}) — 클릭하지 않음");
            sentDown = interception_send_mouse(_ctx, _mouse, ref down, 1);
            if (sentDown > 0) Thread.Sleep(holdMs);
        }
        finally
        {
            if (sentDown > 0) sentUp = interception_send_mouse(_ctx, _mouse, ref up, 1);
        }
        if (sentDown <= 0) return InputResult.Failure("마우스 누름 전달 실패(interception_send=0)");
        if (sentUp <= 0) return InputResult.Failure("마우스 해제 전달 실패(interception_send=0)");
        return InputResult.Success($"왼쪽 클릭 {holdMs}ms");
    }

    private bool MoveTo(CvPoint p)
    {
        int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        int vw = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN), vh = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
        var s = new MouseStroke
        {
            Flags = MoveAbsolute | VirtualDesktop,
            X = (int)Math.Round((p.X - vx) * 65535.0 / Math.Max(1, vw - 1)),
            Y = (int)Math.Round((p.Y - vy) * 65535.0 / Math.Max(1, vh - 1)),
        };
        return interception_send_mouse(_ctx, _mouse, ref s, 1) > 0;
    }

    private static bool CursorNear(CvPoint target, out CvPoint actual)
    {
        Native.GetCursorPos(out var p);
        actual = new CvPoint(p.X, p.Y);
        return Math.Abs(p.X - target.X) <= CursorTolerancePx && Math.Abs(p.Y - target.Y) <= CursorTolerancePx;
    }

    public void Dispose()
    {
        if (_ctx != IntPtr.Zero) interception_destroy_context(_ctx);
    }
}
