using System.Text;
using AbyssBot.Core.Config;

namespace AbyssBot.Core.Logging;

public enum LogLevel { Info, Warn, Error }

public sealed record LogLine(DateTime Time, LogLevel Level, string Message)
{
    public override string ToString() =>
        $"{Time:yyyy-MM-dd HH:mm:ss.fff} [{(Level == LogLevel.Info ? "정보" : Level == LogLevel.Warn ? "주의" : "오류")}] {Message}";
}

/// <summary>
/// 상태 변화와 주요 사건 위주 로그. 같은 메시지가 짧은 간격으로 반복되면 쌓지 않는다.
/// 파일 크기가 한도를 넘으면 회전하고 오래된 파일을 지운다.
/// </summary>
public sealed class BotLogger
{
    private readonly object _lock = new();
    private readonly string? _dir;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private string? _lastMessage;
    private DateTime _lastMessageAt;
    private readonly TimeSpan _dedupeWindow = TimeSpan.FromSeconds(60);
    private readonly Func<DateTime> _now;

    public BotLogger(string? directory, LoggingSpec spec, Func<DateTime>? now = null)
    {
        _dir = directory;
        _maxBytes = Math.Max(64, spec.MaxLogFileKb) * 1024L;
        _maxFiles = Math.Max(1, spec.MaxLogFiles);
        _now = now ?? (() => DateTime.Now);
        if (_dir is not null) Directory.CreateDirectory(_dir);
    }

    public event Action<LogLine>? Line;

    public string? CurrentFile => _dir is null ? null : Path.Combine(_dir, "abyss.log");

    public void Info(string m) => Write(LogLevel.Info, m);
    public void Warn(string m) => Write(LogLevel.Warn, m);
    public void Error(string m) => Write(LogLevel.Error, m);

    public void Write(LogLevel level, string message)
    {
        LogLine line;
        lock (_lock)
        {
            var now = _now();
            if (message == _lastMessage && now - _lastMessageAt < _dedupeWindow) return;
            _lastMessage = message; _lastMessageAt = now;
            line = new LogLine(now, level, message);
            AppendToFile(line);
        }
        Line?.Invoke(line);
    }

    private void AppendToFile(LogLine line)
    {
        if (CurrentFile is null) return;
        try
        {
            var fi = new FileInfo(CurrentFile);
            if (fi.Exists && fi.Length > _maxBytes) Rotate();
            File.AppendAllText(CurrentFile, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (IOException)
        {
            // 로그 실패로 자동화를 멈추지 않는다.
        }
    }

    private void Rotate()
    {
        for (int i = _maxFiles - 1; i >= 1; i--)
        {
            var src = Path.Combine(_dir!, i == 1 ? "abyss.log" : $"abyss.{i - 1}.log");
            var dst = Path.Combine(_dir!, $"abyss.{i}.log");
            if (File.Exists(dst)) File.Delete(dst);
            if (File.Exists(src)) File.Move(src, dst);
        }
        if (_maxFiles == 1 && File.Exists(CurrentFile)) File.Delete(CurrentFile!);
    }
}
