using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using AbyssBot.Core.Config;

namespace AbyssBot.App.Platform;

/// <summary>
/// 필요한 구성 요소 점검과 설치. 사용자가 '설치' 버튼을 눌렀을 때만 실행한다.
/// - 한국어 OCR: Windows 기능(Language.OCR~~~ko-KR) 설치
/// - Interception: 공식 GitHub 배포 파일을 받아 SHA256을 확인한 뒤 x64 DLL 복사와 드라이버 설치(재부팅 필요)
/// </summary>
public static class SetupService
{
    public const string OcrCapability = "Language.OCR~~~ko-KR~0.0.1.0";
    public const string InterceptionUrl = "https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip";
    /// <summary>v1.0.1 Interception.zip의 SHA256. 다르면 설치하지 않는다.</summary>
    public const string InterceptionSha256 = "ad038963d6413055765128b0b931f6e765147c9916dba79e65d872b261f9af10";

    public static string BaseDir => AppContext.BaseDirectory;
    public static string DllPath => Path.Combine(BaseDir, "interception.dll");

    public sealed record Check(bool Ok, string Detail);

    public static Check CheckAdmin() => InterceptionInput.IsAdmin()
        ? new(true, "관리자 권한으로 실행 중")
        : new(false, "관리자 권한이 아닙니다. 프로그램을 닫고 '관리자 권한으로 실행'하세요.");

    public static Check CheckOcr()
    {
        var e = WinOcrEngine.TryCreate(out var problem);
        return e is not null ? new(true, "Windows 한국어 OCR 사용 가능") : new(false, problem ?? "한국어 OCR 없음");
    }

    public static Check CheckInterception()
    {
        if (!File.Exists(DllPath)) return new(false, "interception.dll이 없습니다. '설치'를 누르면 받아서 설치합니다.");
        var d = InterceptionInput.Diagnose();
        d.Input?.Dispose();
        return d.Ready
            ? new(true, "드라이버와 키보드·마우스 장치 확인됨")
            : new(false, "DLL은 있지만 드라이버가 준비되지 않았습니다. '설치' 후 재부팅이 필요합니다.");
    }

    public static Check CheckGameWindow(LoadedConfig? cfg)
    {
        var title = cfg?.Scenario.Window.TitleContains;
        if (string.IsNullOrWhiteSpace(title)) return new(false, "게임 창을 아직 고르지 않았습니다.");
        var w = new GameWindow(cfg!.Scenario.Window, cfg.Targets.Baseline);
        var why = w.Find();
        if (why is not null) return new(false, $"'{title}' — 지금 찾을 수 없음(게임을 켜고 다시 확인)");
        var b = w.CaptureBounds();
        var size = b is { } r ? $"{r.Width}×{r.Height}" : "?";
        var bl = cfg.Targets.Baseline;
        bool same = b is { } rr && rr.Width == bl.Width && rr.Height == bl.Height;
        return same
            ? new(true, $"'{title}' 확인 ({size})")
            : new(false, $"'{title}' 크기 {size} — 기준 {bl.Width}×{bl.Height}와 다름(테두리 없는 전체 화면 1920×1080 필요)");
    }

    public static Check CheckImages(LoadedConfig? cfg)
    {
        if (cfg is null) return new(false, "설정을 읽지 못했습니다.");
        var missing = cfg.Scenario.Destinations.Values
            .SelectMany(d => cfg.Targets.Targets.TryGetValue(d.Target, out var t) ? t.Images ?? new() : new())
            .Select(i => i.File)
            .Where(f => !File.Exists(Path.Combine(cfg.ImagesDirectory, f)))
            .ToList();
        return missing.Count == 0 ? new(true, "목적지 배너 사진 3장 준비됨") : new(false, "없는 사진: " + string.Join(", ", missing));
    }

    /// <summary>한국어 OCR 기능 설치(PowerShell Add-WindowsCapability). 수 분 걸릴 수 있다.</summary>
    public static async Task<(bool ok, string log)> InstallOcrAsync()
    {
        var (code, output) = await RunAsync("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"$r = Add-WindowsCapability -Online -Name '{OcrCapability}'; $r | Format-List | Out-String; (Get-WindowsCapability -Online -Name '{OcrCapability}').State\"");
        bool ok = code == 0 && output.Contains("Installed", StringComparison.OrdinalIgnoreCase);
        return (ok, output.Trim());
    }

    /// <summary>Interception 내려받기 → 해시 확인 → x64 DLL 복사 → 드라이버 설치. 성공 시 재부팅이 필요하다.</summary>
    public static async Task<(bool ok, string log)> InstallInterceptionAsync(IProgress<string> progress)
    {
        var temp = Path.Combine(Path.GetTempPath(), "AbyssBot_Interception_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            progress.Report("Interception 내려받는 중…");
            var zipPath = Path.Combine(temp, "Interception.zip");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
            {
                var bytes = await http.GetByteArrayAsync(InterceptionUrl);
                await File.WriteAllBytesAsync(zipPath, bytes);
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (hash != InterceptionSha256)
                    return (false, $"내려받은 파일의 해시가 예상과 다릅니다(변조 가능성). 설치하지 않았습니다.\n받은 값: {hash}");
            }

            progress.Report("압축 푸는 중…");
            var dir = Path.Combine(temp, "x");
            ZipFile.ExtractToDirectory(zipPath, dir);
            var root = Path.Combine(dir, "Interception");
            var dll = Path.Combine(root, "library", "x64", "interception.dll");
            var installer = Path.Combine(root, "command line installer", "install-interception.exe");
            if (!File.Exists(dll) || !File.Exists(installer)) return (false, "압축 안에서 DLL 또는 설치 파일을 찾지 못했습니다.");

            File.Copy(dll, DllPath, overwrite: true);
            File.Copy(Path.Combine(root, "licenses", "non-commercial-usage", "LGPL 3.0.txt"), Path.Combine(BaseDir, "Interception-LGPL-3.0.txt"), true);

            progress.Report("드라이버 설치 중…");
            var (code, output) = await RunAsync(installer, "/install");
            var log = $"설치 프로그램 종료 코드 {code}\n{output.Trim()}";
            return (code == 0, log);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidDataException)
        {
            return (false, "설치 실패: " + e.Message);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { /* 임시 폴더 정리 실패는 무시 */ }
        }
    }

    public static void Reboot() => Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 10 /c \"Interception 드라이버 설치를 마치기 위해 재부팅합니다.\"")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    });

    /// <summary>게임 창 선택을 user.json(실행 파일 옆)에 저장한다.</summary>
    public static void SaveGameWindow(LoadedConfig cfg, string title, string process)
    {
        cfg.Scenario.Window.TitleContains = title;
        cfg.Scenario.Window.ProcessName = process;
        ConfigLoader.SaveUser(cfg);
    }

    private static async Task<(int code, string output)> RunAsync(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new IOException($"{file} 실행 실패");
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await outTask + await errTask);
    }
}
