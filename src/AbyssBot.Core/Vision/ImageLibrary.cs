using OpenCvSharp;

namespace AbyssBot.Core.Vision;

/// <summary>images 폴더의 비교 사진을 읽어 둔다. 한글 경로 문제를 피하려고 바이트로 읽어 디코딩한다.</summary>
public sealed class ImageLibrary : IDisposable
{
    private readonly Dictionary<string, Mat> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);

    public ImageLibrary(string directory) => Directory_ = directory;

    public string Directory_ { get; }

    public Mat? Get(string file)
    {
        if (_cache.TryGetValue(file, out var m)) return m;
        if (_missing.Contains(file)) return null;
        var path = Path.Combine(Directory_, file);
        if (!File.Exists(path)) { _missing.Add(file); return null; }
        var mat = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Color);
        if (mat.Empty()) { mat.Dispose(); _missing.Add(file); return null; }
        _cache[file] = mat;
        return mat;
    }

    public bool Exists(string file) => Get(file) is not null;

    public void Dispose()
    {
        foreach (var m in _cache.Values) m.Dispose();
        _cache.Clear();
    }
}
