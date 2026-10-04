using System.Text.Json;
using AbyssBot.Core.Config;
using OpenCvSharp;

namespace AbyssBot.Core.Logging;

public interface IEvidenceStore
{
    /// <summary>화면 사진과 보고서(JSON)를 저장하고 사진 경로를 반환한다.</summary>
    string? Save(Mat? frame, string label, object report);
}

/// <summary>오류·특이 화면 사진 저장. 개수와 총용량을 넘으면 오래된 것부터 지운다.</summary>
public sealed class EvidenceStore(string directory, LoggingSpec spec, Func<DateTime>? now = null) : IEvidenceStore
{
    private readonly Func<DateTime> _now = now ?? (() => DateTime.Now);

    public string Directory_ => directory;

    public string? Save(Mat? frame, string label, object report)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var safe = string.Concat(label.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
            var stem = Path.Combine(directory, $"{_now():yyyyMMdd_HHmmss_fff}_{safe}");
            string? png = null;
            if (frame is not null && !frame.Empty())
            {
                png = stem + ".png";
                Cv2.ImEncode(".png", frame, out var bytes);
                File.WriteAllBytes(png, bytes);
            }
            File.WriteAllText(stem + ".json", JsonSerializer.Serialize(report, ConfigLoader.JsonOptions));
            Prune();
            return png ?? stem + ".json";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Prune()
    {
        var files = new DirectoryInfo(directory).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        long maxBytes = Math.Max(1, spec.MaxEvidenceMb) * 1024L * 1024L;
        long total = 0;
        for (int i = 0; i < files.Count; i++)
        {
            total += files[i].Length;
            if (i >= spec.MaxEvidenceFiles * 2 || total > maxBytes) files[i].Delete();
        }
    }
}
