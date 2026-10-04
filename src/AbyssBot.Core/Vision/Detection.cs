using System.Text;
using OpenCvSharp;

namespace AbyssBot.Core.Vision;

public sealed record ImageEvidence(string File, double Score, double Threshold, Rect Rect, bool Matched, string? Note = null);

public sealed record OcrEvidence(bool Matched, IReadOnlyList<string> MatchedTexts, Rect Rect, string RawText, bool Upscaled, string How);

public sealed record ColorEvidence(int B, int G, int R, string Kind, bool Matched, Rect MeasuredRect)
{
    public string Describe() => $"색 {(Matched ? "일치" : "불일치")}({Kind}) BGR중앙값=({B},{G},{R}) G-max(R,B)={G - Math.Max(R, B)} B-max(R,G)={B - Math.Max(R, G)}";
}

/// <summary>화면 인식 결과. 인식 함수는 이 결과만 반환하고 입력은 하지 않는다.</summary>
public sealed class Detection
{
    public required string TargetId { get; init; }
    public bool Configured { get; init; } = true;
    public bool Found { get; init; }
    public int Score { get; init; }
    public int PassScore { get; init; }
    public Rect SearchRegion { get; init; }

    /// <summary>클릭할 버튼 영역(캡처 이미지 좌표). Found일 때만 의미가 있다.</summary>
    public Rect? ButtonRect { get; init; }

    /// <summary>OCR 결과 중 이번 대상에서 확인된 서로 다른 문구(메뉴 열림 판정용).</summary>
    public IReadOnlyList<string> DistinctTexts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ImageEvidence> Images { get; init; } = Array.Empty<ImageEvidence>();
    public OcrEvidence? Ocr { get; init; }
    public ColorEvidence? Color { get; init; }
    public bool Conflict { get; init; }

    /// <summary>선택형 버튼의 선택됨 여부(Selected 규칙이 있을 때만). 비율은 지정 색 픽셀 비율.</summary>
    public bool? Selected { get; init; }
    public double SelectedFraction { get; init; }
    public string? Note { get; init; }

    public ImageEvidence? BestImage => Images.Where(i => i.Note is null).OrderByDescending(i => i.Score).FirstOrDefault();

    public static Detection NotConfigured(string id) => new() { TargetId = id, Configured = false, Note = "설정되지 않은 대상" };

    public string Summary()
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(TargetId).Append("] ");
        if (!Configured) return sb.Append("미설정").ToString();
        sb.Append(Found ? "합격" : "불합격").Append($" 점수 {Score}/{PassScore}");
        if (Ocr is { } o)
        {
            sb.Append(" | OCR ");
            sb.Append(o.Matched ? $"일치 '{string.Join(",", o.MatchedTexts)}'({o.How}{(o.Upscaled ? ",2배" : "")})" : "불일치");
            sb.Append($" 읽은 글자='{Trim(o.RawText, 120)}'");
        }
        foreach (var i in Images)
            sb.Append($" | 사진 {i.File} {i.Score:0.000}/{i.Threshold:0.00}{(i.Matched ? "✓" : "")}{(i.Note is null ? "" : " " + i.Note)}");
        if (Color is { } c) sb.Append(" | ").Append(c.Describe());
        if (ButtonRect is { } r && Found) sb.Append($" | 위치 ({r.X},{r.Y},{r.Width}x{r.Height})");
        if (Selected is { } sel) sb.Append($" | 선택됨={(sel ? "예" : "아니오")}(색 비율 {SelectedFraction:P1})");
        if (Conflict) sb.Append(" | OCR·사진 위치 충돌");
        if (Note is not null) sb.Append(" | ").Append(Note);
        return sb.ToString();
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
