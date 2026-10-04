using AbyssBot.Core.Config;
using OpenCvSharp;

namespace AbyssBot.Core.Vision;

public static class ColorJudge
{
    /// <summary>사각형 안의 B,G,R 채널별 중앙값으로 초록/파랑 우세를 판정한다.</summary>
    public static ColorEvidence Measure(Mat bgr, Rect rect, ColorRule rule)
    {
        rect &= new Rect(0, 0, bgr.Width, bgr.Height);
        if (rect.Width <= 0 || rect.Height <= 0) return new ColorEvidence(0, 0, 0, "측정 불가", false, rect);
        using var roi = new Mat(bgr, rect);
        Cv2.Split(roi, out var ch);
        try
        {
            int b = Median(ch[0]), g = Median(ch[1]), r = Median(ch[2]);
            bool green = g - Math.Max(r, b) >= rule.MinDominance;
            bool blue = b - Math.Max(r, g) >= rule.MinDominance;
            string kind = green ? "초록" : blue ? "파랑" : "기타";
            bool ok = (green && rule.Allowed.Contains("green")) || (blue && rule.Allowed.Contains("blue"));
            return new ColorEvidence(b, g, r, kind, ok, rect);
        }
        finally
        {
            foreach (var c in ch) c.Dispose();
        }
    }

    private static int Median(Mat single)
    {
        var hist = new int[256];
        int total = 0;
        for (int y = 0; y < single.Rows; y++)
        for (int x = 0; x < single.Cols; x++) { hist[single.At<byte>(y, x)]++; total++; }
        int half = (total + 1) / 2, acc = 0;
        for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc >= half) return v; }
        return 255;
    }

    /// <summary>사각형 안에서 지정 색으로 분류되는 픽셀 비율(0~1).</summary>
    public static double Fraction(Mat bgr, Rect rect, string color, int dominance, int satMin = 120, int valMin = 120)
    {
        rect &= new Rect(0, 0, bgr.Width, bgr.Height);
        if (rect.Width <= 0 || rect.Height <= 0) return 0;
        using var roi = new Mat(bgr, rect);
        if (color == "saturated")
        {
            // 색과 무관하게 '선명한 색' 픽셀 비율(선택된 버튼의 테두리·배경은 보라/주황/빨강/청록 등 다양함)
            using var hsv = new Mat();
            Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);
            using var inRange = new Mat();
            Cv2.InRange(hsv, new Scalar(0, satMin, valMin), new Scalar(180, 255, 255), inRange);
            return (double)Cv2.CountNonZero(inRange) / (rect.Width * rect.Height);
        }
        var idx = roi.GetGenericIndexer<Vec3b>();
        int hit = 0;
        for (int y = 0; y < roi.Rows; y++)
        for (int x = 0; x < roi.Cols; x++)
        {
            var p = idx[y, x];
            int b = p.Item0, g = p.Item1, r = p.Item2;
            bool ok = color switch
            {
                "red" => r - Math.Max(g, b) >= dominance,
                "purple" => b - g >= dominance && r - g >= dominance / 2,
                "green" => g - Math.Max(r, b) >= dominance,
                "blue" => b - Math.Max(r, g) >= dominance,
                _ => false,
            };
            if (ok) hit++;
        }
        return (double)hit / (rect.Width * rect.Height);
    }

    /// <summary>OCR 글자 영역을 가로·세로 배율로 넓히고 탐색 영역 안으로 제한한다.</summary>
    public static Rect ExpandWithin(Rect r, double fx, double fy, Rect bounds)
    {
        double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0;
        double w = r.Width * fx, h = r.Height * fy;
        var e = new Rect((int)Math.Round(cx - w / 2), (int)Math.Round(cy - h / 2), (int)Math.Round(w), (int)Math.Round(h));
        return e & bounds;
    }
}
