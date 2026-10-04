using System.Text;
using AbyssBot.Core.Config;
using AbyssBot.Core.Engine;
using AbyssBot.Core.Vision;
using OpenCvSharp;

namespace AbyssBot.Core.Verify;

/// <summary>
/// 저장된 화면으로 인식 위치를 검증한다. 입력은 전혀 하지 않는다.
/// 모든 대상의 점수·OCR 문구·색 판정과 재분류 결과를 보고서로 만들고, 영역과 검출 위치를 그린 사진을 만든다.
/// </summary>
public sealed class ScreenVerifier(LoadedConfig cfg, IDetector detector)
{
    public sealed record Result(string Report, Mat Annotated);

    public Result Verify(Mat frame, string name)
    {
        var sb = new StringBuilder();
        var b = cfg.Targets.Baseline;
        sb.AppendLine($"■ {name}  크기 {frame.Width}×{frame.Height} (기준 {b.Width}×{b.Height}, 제목 표시줄 포함={b.IncludesTitleBar})");
        if (frame.Width != b.Width || frame.Height != b.Height)
            sb.AppendLine("  ⚠ 기준 크기와 다릅니다. 영역이 어긋날 수 있으니 같은 창 크기로 캡처하거나 영역을 다시 설정하세요.");

        var annotated = frame.Clone();
        foreach (var (id, def) in cfg.Targets.Targets.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var d = detector.Detect(frame, id);
            sb.AppendLine("  " + d.Summary());
            if (!d.Configured) continue;
            annotated.Rectangle(d.SearchRegion, d.Found ? Scalar.Lime : Scalar.Yellow, 1);
            annotated.PutText(id, new Point(d.SearchRegion.X + 2, d.SearchRegion.Y + 12), HersheyFonts.HersheySimplex, 0.4,
                d.Found ? Scalar.Lime : Scalar.Yellow, 1);
            foreach (var img in d.Images.Where(i => i.Note is null && i.Score > 0))
                annotated.Rectangle(img.Rect, img.Matched ? Scalar.Cyan : Scalar.Gray, 1);
            if (d.Ocr is { Matched: true } o && o.Rect.Width > 0) annotated.Rectangle(o.Rect, Scalar.Magenta, 1);
            if (d.Found && d.ButtonRect is { } r) annotated.Rectangle(r, Scalar.Red, 2);
        }

        var dest = cfg.Scenario.Destinations.TryGetValue(cfg.Scenario.Options.Destination, out var ds) ? ds.Target : "";
        var c = new ScreenClassifier(detector, dest, cfg.Scenario.Options.ReconnectEnabled).Classify(frame);
        sb.AppendLine($"  → 재분류: {(c.Step is { } s ? StepNames.Korean(s) : "판별 안 함")} — {c.Reason}");
        return new Result(sb.ToString(), annotated);
    }

    /// <summary>사진별 최고 점수 표(정상 화면 vs 비슷한 다른 화면 비교로 기준을 정할 때 사용).</summary>
    public static string ScoreTable(IEnumerable<(string name, Detection d)> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("화면\t대상\t사진\t점수\t기준");
        foreach (var (name, d) in rows)
        foreach (var i in d.Images)
            sb.AppendLine($"{name}\t{d.TargetId}\t{i.File}\t{i.Score:0.000}\t{i.Threshold:0.00}");
        return sb.ToString();
    }
}
