using AbyssBot.Core.Config;
using AbyssBot.Core.Ocr;
using AbyssBot.Core.Text;
using OpenCvSharp;

namespace AbyssBot.Core.Vision;

public interface IDetector
{
    /// <summary>대상을 찾아 판정 결과와 위치만 반환한다. 입력은 하지 않는다.</summary>
    Detection Detect(Mat frame, string targetId);

    bool IsConfigured(string targetId);
}

/// <summary>targets.json 규칙에 따라 사진(TM_CCOEFF_NORMED)·한국어 OCR·버튼 색으로 판정한다.</summary>
public sealed class Detector : IDetector
{
    private readonly TargetsConfig _cfg;
    private readonly ImageLibrary _images;
    private readonly IOcrEngine? _ocr;

    public Detector(TargetsConfig cfg, ImageLibrary images, IOcrEngine? ocr)
    {
        _cfg = cfg; _images = images; _ocr = ocr;
    }

    public bool IsConfigured(string targetId) =>
        _cfg.Targets.TryGetValue(targetId, out var d) && d.IsConfigured;

    public Detection Detect(Mat frame, string targetId)
    {
        if (!_cfg.Targets.TryGetValue(targetId, out var def) || !def.IsConfigured)
            return Detection.NotConfigured(targetId);

        var region = def.Region!.Resolve(frame.Width, frame.Height);
        if (region.Width <= 0 || region.Height <= 0)
            return new Detection { TargetId = targetId, PassScore = def.PassScore, SearchRegion = region, Note = "탐색 영역이 비어 있음" };

        // 1) 사진
        var imageEvidence = new List<ImageEvidence>();
        foreach (var img in def.Images ?? new())
            imageEvidence.Add(MatchImage(frame, region, img));
        var bestImage = imageEvidence.Where(i => i.Matched).OrderByDescending(i => i.Score).FirstOrDefault();

        // 2) OCR
        OcrEvidence? ocrEv = null;
        IReadOnlyList<string> distinct = Array.Empty<string>();
        if (def.Ocr is { Texts.Count: > 0 } rule)
            (ocrEv, distinct) = RunOcr(frame, region, rule);

        // 3) 여러 문구 개수로 판정하는 대상(메뉴 열림)
        if (def.Ocr is { MinDistinct: > 0 } md)
        {
            bool found = distinct.Count >= md.MinDistinct;
            return new Detection
            {
                TargetId = targetId, Found = found, Score = found ? def.PassScore : 0, PassScore = def.PassScore,
                SearchRegion = region, Ocr = ocrEv, DistinctTexts = distinct, Images = imageEvidence,
                Note = $"서로 다른 메뉴 이름 {distinct.Count}개 확인(기준 {md.MinDistinct}개): {string.Join(",", distinct)}",
            };
        }

        Rect? imgRect = bestImage?.Rect;
        Rect? ocrRect = ocrEv is { Matched: true } ? ocrEv.Rect : null;

        // 4) 같은 버튼을 가리키는지 확인
        if (imgRect is { } ir && ocrRect is { } or && !SameButton(ir, or, def.SameButtonTolerance))
        {
            var a = Evaluate(frame, region, def, image: true, ocr: false, ir, ocrEv);
            var b = Evaluate(frame, region, def, image: false, ocr: true, or, ocrEv);
            if (a.pass && b.pass)
                return Build(def, targetId, region, false, Math.Max(a.score, b.score), null, imageEvidence, ocrEv, a.color, conflict: true,
                    note: "사진과 OCR이 서로 다른 버튼을 가리켜 입력하지 않음");
            var win = a.pass ? a : b.pass ? b : (a.score >= b.score ? a : b);
            return Build(def, targetId, region, win.pass, win.score, win.pass ? win.rect : null, imageEvidence, ocrEv, win.color, conflict: !win.pass,
                note: win.pass ? "사진·OCR 위치가 달라 한쪽 근거만 사용" : "사진·OCR 위치 불일치");
        }

        var (pass, score, rect, color) = Evaluate(frame, region, def, imgRect is not null, ocrRect is not null,
            imgRect ?? ocrRect ?? region, ocrEv);
        // 사진과 OCR이 함께 검출되면 사진 위치를 클릭 영역으로 쓴다.
        return Build(def, targetId, region, pass, score, pass ? (imgRect ?? ocrRect) : null, imageEvidence, ocrEv, color, false, null);
    }

    private Detection Build(TargetDef def, string id, Rect region, bool found, int score, Rect? rect,
        List<ImageEvidence> images, OcrEvidence? ocr, ColorEvidence? color, bool conflict, string? note) => new()
    {
        TargetId = id, Found = found, Score = score, PassScore = def.PassScore, SearchRegion = region,
        ButtonRect = rect, Images = images, Ocr = ocr, Color = color, Conflict = conflict, Note = note,
    };

    private (bool pass, int score, Rect rect, ColorEvidence? color) Evaluate(
        Mat frame, Rect region, TargetDef def, bool image, bool ocr, Rect rect, OcrEvidence? ocrEv)
    {
        ColorEvidence? color = null;
        if (def.Color is { } cr && (image || ocr))
        {
            // 사진이 있으면 사진(버튼 모양 포함) 영역, OCR만 있으면 글자 주변 배경까지 넓힌 영역
            var measure = image ? rect : ColorJudge.ExpandWithin(rect, cr.OcrExpandX, cr.OcrExpandY, region);
            color = ColorJudge.Measure(frame, measure, cr);
        }
        bool colorOk = color?.Matched == true;
        int score = (ocr ? _cfg.Scoring.Ocr : 0) + (image ? _cfg.Scoring.Image : 0) + (colorOk ? _cfg.Scoring.Color : 0);
        bool reqOk = def.Require is null || def.Require.All(r => r switch
        {
            "ocr" => ocr,
            "image" => image,
            "color" => colorOk,
            _ => false,
        });
        // 색상만으로는 절대 합격하지 않는다.
        bool pass = (image || ocr) && score >= def.PassScore && reqOk;
        return (pass, score, rect, color);
    }

    private static bool SameButton(Rect img, Rect ocr, int tol)
    {
        var ic = new Point(img.X + img.Width / 2, img.Y + img.Height / 2);
        var oc = new Point(ocr.X + ocr.Width / 2, ocr.Y + ocr.Height / 2);
        var ie = new Rect(img.X - tol, img.Y - tol, img.Width + 2 * tol, img.Height + 2 * tol);
        var oe = new Rect(ocr.X - tol, ocr.Y - tol, ocr.Width + 2 * tol, ocr.Height + 2 * tol);
        return ie.Contains(oc) || oe.Contains(ic);
    }

    /// <summary>
    /// 사진 비교. 탐색 영역을 사진 크기 절반만큼 넓혀 찾되, 사진 중심이 탐색 영역 안에 있는 후보만 인정한다.
    /// </summary>
    internal ImageEvidence MatchImage(Mat frame, Rect region, ImageRef img)
    {
        double thr = img.Threshold ?? _cfg.DefaultImageThreshold;
        var tpl = _images.Get(img.File);
        if (tpl is null) return new ImageEvidence(img.File, 0, thr, default, false, "파일 없음");

        int tw = tpl.Width, th = tpl.Height;
        var expanded = new Rect(region.X - tw / 2, region.Y - th / 2, region.Width + tw, region.Height + th)
                       & new Rect(0, 0, frame.Width, frame.Height);
        if (expanded.Width < tw || expanded.Height < th)
            return new ImageEvidence(img.File, 0, thr, default, false, "사진이 탐색 영역보다 큼");

        using var search = new Mat(frame, expanded);
        using var result = new Mat();
        Cv2.MatchTemplate(search, tpl, result, TemplateMatchModes.CCoeffNormed);

        // 결과 위치 (x,y)의 사진 중심 = expanded.X + x + tw/2. 중심이 region 안인 위치만 남긴다.
        int x0 = Math.Max(0, region.X - expanded.X - tw / 2);
        int x1 = Math.Min(result.Cols, region.Right - expanded.X - tw / 2);
        int y0 = Math.Max(0, region.Y - expanded.Y - th / 2);
        int y1 = Math.Min(result.Rows, region.Bottom - expanded.Y - th / 2);
        if (x1 <= x0 || y1 <= y0) return new ImageEvidence(img.File, 0, thr, default, false, "유효 후보 없음");

        using var valid = new Mat(result, new Rect(x0, y0, x1 - x0, y1 - y0));
        Cv2.PatchNaNs(valid, 0);
        valid.MinMaxLoc(out double _, out double max, out Point _, out Point loc);
        if (double.IsNaN(max) || double.IsInfinity(max)) max = 0;
        var rect = new Rect(expanded.X + x0 + loc.X, expanded.Y + y0 + loc.Y, tw, th);
        return new ImageEvidence(img.File, max, thr, rect, max >= thr);
    }

    private (OcrEvidence ev, IReadOnlyList<string> distinct) RunOcr(Mat frame, Rect region, OcrRule rule)
    {
        if (_ocr is null)
            return (new OcrEvidence(false, Array.Empty<string>(), default, "(OCR 엔진 없음)", false, ""), Array.Empty<string>());

        using var crop = new Mat(frame, region);
        var raw = _ocr.Recognize(crop).Scale(1, region.Location);
        var (matches, distinct) = Collect(raw, rule);
        bool satisfied = rule.MinDistinct > 0 ? distinct.Count >= rule.MinDistinct : matches.Count > 0;
        string rawText = raw.AllText;
        bool upscaled = false;

        if (!satisfied && rule.Upscale)
        {
            using var big = new Mat();
            Cv2.Resize(crop, big, new Size(crop.Width * 2, crop.Height * 2), 0, 0, InterpolationFlags.Cubic);
            // 확대 화면 좌표를 원래 크기로 환산
            var raw2 = _ocr.Recognize(big).Scale(0.5, region.Location);
            var (m2, d2) = Collect(raw2, rule);
            rawText += " ‖2배: " + raw2.AllText;
            // 같은 이름을 원본과 확대에서 각각 읽어도 한 번만 센다.
            var merged = distinct.Union(d2, StringComparer.Ordinal).ToList();
            if (matches.Count == 0 && m2.Count > 0) { matches = m2; upscaled = true; }
            else if (rule.MinDistinct > 0 && merged.Count > distinct.Count) upscaled = true;
            distinct = merged;
        }

        var matched = rule.MinDistinct > 0 ? distinct.Count >= rule.MinDistinct : matches.Count > 0;
        var rect = matches.Count > 0 ? matches[0].Rect : default;
        var how = matches.Count > 0 ? matches[0].How : "";
        return (new OcrEvidence(matched, distinct, rect, rawText, upscaled, how), distinct);
    }

    private static (List<TextMatch> matches, List<string> distinct) Collect(OcrResult r, OcrRule rule)
    {
        var matches = new List<TextMatch>();
        foreach (var t in rule.Texts)
            if (TextMatcher.Find(r, t, rule.Mode) is { } m) matches.Add(m);
        var distinct = matches.Select(m => m.Target).Distinct(StringComparer.Ordinal).ToList();
        return (matches, distinct);
    }
}
