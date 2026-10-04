using OpenCvSharp;

namespace AbyssBot.Core.Ocr;

public sealed record OcrWord(string Text, Rect Rect);

public sealed record OcrLine(IReadOnlyList<OcrWord> Words)
{
    public string Text => string.Join(" ", Words.Select(w => w.Text));
}

public sealed record OcrResult(IReadOnlyList<OcrLine> Lines)
{
    public static readonly OcrResult Empty = new(Array.Empty<OcrLine>());
    public string AllText => string.Join(" / ", Lines.Select(l => l.Text));

    public OcrResult Scale(double factor, Point offset) => new(Lines.Select(l => new OcrLine(
        l.Words.Select(w => new OcrWord(w.Text, new Rect(
            (int)Math.Round(w.Rect.X * factor) + offset.X,
            (int)Math.Round(w.Rect.Y * factor) + offset.Y,
            Math.Max(1, (int)Math.Round(w.Rect.Width * factor)),
            Math.Max(1, (int)Math.Round(w.Rect.Height * factor))))).ToList())).ToList());
}

/// <summary>한국어 OCR 엔진. 입력은 BGR 이미지, 결과 좌표는 입력 이미지 기준.</summary>
public interface IOcrEngine
{
    OcrResult Recognize(Mat bgr);
}
