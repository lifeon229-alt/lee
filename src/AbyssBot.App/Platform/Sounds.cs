using System.Media;

namespace AbyssBot.App.Platform;

public static class Sounds
{
    public static void Countdown() => Task.Run(() => { try { Console.Beep(880, 120); } catch { SystemSounds.Beep.Play(); } });

    /// <summary>정상 완료: 짧은 알림 한 번.</summary>
    public static void Completed() => SystemSounds.Asterisk.Play();

    /// <summary>오류 정지: 약 3초 동안 반복되는 소리.</summary>
    public static void Error() => Task.Run(() =>
    {
        var until = DateTime.Now.AddSeconds(3);
        while (DateTime.Now < until)
        {
            try { Console.Beep(1200, 250); } catch { SystemSounds.Hand.Play(); Thread.Sleep(250); }
            Thread.Sleep(150);
        }
    });
}
