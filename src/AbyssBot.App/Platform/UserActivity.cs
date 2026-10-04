using AbyssBot.Core.Engine;

namespace AbyssBot.App.Platform;

/// <summary>마지막 키보드·마우스 입력 시각(GetLastInputInfo).</summary>
public sealed class UserActivity : IUserActivity
{
    public DateTime LastInput
    {
        get
        {
            var info = new Native.LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.LASTINPUTINFO>() };
            if (!Native.GetLastInputInfo(ref info)) return DateTime.Now;
            uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
            return DateTime.Now - TimeSpan.FromMilliseconds(idleMs);
        }
    }
}
