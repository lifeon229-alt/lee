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

    /// <summary>OCR 글자 영역을 가로·세로 배율로 넓히고 탐색 영역 안으로 제한한다.</summary>
    public static Rect ExpandWithin(Rect r, double fx, double fy, Rect bounds)
    {
        double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0;
        double w = r.Width * fx, h = r.Height * fy;
        var e = new Rect((int)Math.Round(cx - w / 2), (int)Math.Round(cy - h / 2), (int)Math.Round(w), (int)Math.Round(h));
        return e & bounds;
    }
}
