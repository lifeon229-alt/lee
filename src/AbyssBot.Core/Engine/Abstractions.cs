using OpenCvSharp;

namespace AbyssBot.Core.Engine;

/// <summary>게임 창 한 장의 캡처. Origin은 캡처 이미지 (0,0)의 화면 좌표이며 클릭 좌표 계산에 그대로 쓴다.</summary>
public sealed class Frame : IDisposable
{
    public Frame(Mat image, Point origin, DateTime capturedAt)
    {
        Image = image; Origin = origin; CapturedAt = capturedAt;
    }

    public Mat Image { get; }
    public Point Origin { get; }
    public DateTime CapturedAt { get; }
    public int Width => Image.Width;
    public int Height => Image.Height;

    public void Dispose() => Image.Dispose();
}

public enum CaptureStatus
{
    Ok,
    /// <summary>게임 창이 활성 상태가 아님(다른 프로그램이 앞에 있음). 분석·입력하지 않고 기다린다.</summary>
    Inactive,
    /// <summary>창을 찾을 수 없거나 크기가 기준과 다름. 진행할 수 없다.</summary>
    Fatal,
}

public sealed record CaptureResult(CaptureStatus Status, Frame? Frame, string? Problem)
{
    public static CaptureResult Ok(Frame f) => new(CaptureStatus.Ok, f, null);
    public static CaptureResult Inactive(string why) => new(CaptureStatus.Inactive, null, why);
    public static CaptureResult Fatal(string why) => new(CaptureStatus.Fatal, null, why);
}

public interface IGameWindow
{
    /// <summary>게임 창이 활성(포그라운드) 상태일 때만 캡처한다.</summary>
    CaptureResult Capture();

    bool IsForeground();

    /// <summary>현재 캡처 원점(화면 좌표). 창을 찾지 못하면 null.</summary>
    Point? CurrentOrigin();
}

public readonly record struct InputResult(bool Ok, string Message)
{
    public static InputResult Success(string msg = "전달됨") => new(true, msg);
    public static InputResult Failure(string msg) => new(false, msg);
}

public static class ScanCode
{
    public const ushort Esc = 0x01;
    public const ushort Space = 0x39;

    public static string Name(ushort code) => code switch { Esc => "ESC", Space => "SPACE", _ => $"0x{code:X2}" };
}

public interface IInputDevice
{
    /// <summary>누름 후 holdMs 뒤 해제. 예외가 나도 반드시 해제한다. 전달 실패면 Ok=false.</summary>
    InputResult PressKey(ushort scanCode, int holdMs);

    /// <summary>
    /// 화면 좌표로 커서를 옮기고, 실제 커서가 목표 근처인지 확인한 뒤 왼쪽 버튼을 누르고 holdMs 뒤 해제한다.
    /// 커서가 목표에서 벗어나 있으면(사용자 이동 등) 클릭하지 않고 실패를 반환한다.
    /// </summary>
    InputResult Click(Point screen, int holdMs);
}

public interface IClock
{
    DateTime Now { get; }
}

public interface IWaiter
{
    /// <summary>사용자 정지 시 즉시 OperationCanceledException.</summary>
    void Wait(int ms, CancellationToken ct);
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

public sealed class RealWaiter : IWaiter
{
    public void Wait(int ms, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ms <= 0) return;
        if (ct.WaitHandle.WaitOne(ms)) ct.ThrowIfCancellationRequested();
    }
}
