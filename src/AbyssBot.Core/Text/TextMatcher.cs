using AbyssBot.Core.Ocr;
using OpenCvSharp;

namespace AbyssBot.Core.Text;

public sealed record TextMatch(string Target, string MatchedText, Rect Rect, string How);

/// <summary>OCR 결과와 목표 문구 비교. 모든 비교는 띄어쓰기를 제거한 뒤 수행한다.</summary>
public static class TextMatcher
{
    public static string Normalize(string s)
    {
        var chars = s.Where(c => !char.IsWhiteSpace(c)).ToArray();
        return new string(chars);
    }

    public static int EditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>목표 문구 하나를 찾는다. 찾지 못하면 null.</summary>
    public static TextMatch? Find(OcrResult ocr, string target, string mode)
    {
        var t = Normalize(target);
        if (t.Length == 0) return null;

        foreach (var line in ocr.Lines)
        {
            if (line.Words.Count == 0) continue;
            // 단어별 정규화 문자열과 줄 전체에서의 시작 위치
            var norm = line.Words.Select(w => Normalize(w.Text)).ToArray();
            var starts = new int[norm.Length];
            int pos = 0;
            for (int i = 0; i < norm.Length; i++) { starts[i] = pos; pos += norm[i].Length; }
            var lineNorm = string.Concat(norm);

            if (mode is "contains" or "fuzzy")
            {
                int idx = lineNorm.IndexOf(t, StringComparison.Ordinal);
                if (idx >= 0)
                    return new TextMatch(target, line.Text, UnionForRange(line, starts, norm, idx, t.Length), "포함");
            }

            // 후보: 단어 하나, 연속 단어 결합(줄 전체 포함)
            for (int i = 0; i < norm.Length; i++)
            {
                var sb = "";
                for (int j = i; j < norm.Length; j++)
                {
                    sb += norm[j];
                    if (sb.Length > t.Length + 1) break;
                    bool ok = mode switch
                    {
                        "exact" => sb == t,
                        "fuzzy" => t.Length >= 3 && Math.Abs(sb.Length - t.Length) <= 1 && EditDistance(sb, t) <= 1,
                        _ => false
                    };
                    if (ok)
                    {
                        var rect = Union(line.Words.Skip(i).Take(j - i + 1).Select(w => w.Rect));
                        var text = string.Join(" ", line.Words.Skip(i).Take(j - i + 1).Select(w => w.Text));
                        return new TextMatch(target, text, rect, mode == "exact" ? "정확" : $"유사(편집거리 {EditDistance(sb, t)})");
                    }
                }
            }
        }
        return null;
    }

    private static Rect UnionForRange(OcrLine line, int[] starts, string[] norm, int idx, int len)
    {
        var rects = new List<Rect>();
        for (int i = 0; i < norm.Length; i++)
        {
            int s = starts[i], e = starts[i] + norm[i].Length;
            if (e > idx && s < idx + len) rects.Add(line.Words[i].Rect);
        }
        return Union(rects);
    }

    public static Rect Union(IEnumerable<Rect> rects)
    {
        Rect? acc = null;
        foreach (var r in rects) acc = acc is null ? r : acc.Value | r;
        return acc ?? default;
    }
}
