using AbyssBot.Core.Config;
using OpenCvSharp;

namespace AbyssBot.Core.Vision;

/// <summary>배경이 바뀌는 흰 안내 문구를 위해 순백색 픽셀만 남긴다.</summary>
public static class WhiteText
{
    /// <summary>순백색 픽셀은 255, 나머지는 0인 단일 채널 그림.</summary>
    public static Mat Mask(Mat bgr, WhiteTextSpec spec)
    {
        Cv2.Split(bgr, out var ch);
        try
        {
            using var mn = new Mat();
            using var mx = new Mat();
            Cv2.Min(ch[0], ch[1], mn);
            Cv2.Min(mn, ch[2], mn);
            Cv2.Max(ch[0], ch[1], mx);
            Cv2.Max(mx, ch[2], mx);
            using var spread = new Mat();
            Cv2.Subtract(mx, mn, spread);
            using var bright = new Mat();
            Cv2.Threshold(mn, bright, spec.Min - 1, 255, ThresholdTypes.Binary);
            using var flat = new Mat();
            Cv2.Threshold(spread, flat, spec.Spread, 255, ThresholdTypes.BinaryInv);
            var mask = new Mat();
            Cv2.BitwiseAnd(bright, flat, mask);
            return mask;
        }
        finally
        {
            foreach (var c in ch) c.Dispose();
        }
    }

    /// <summary>OCR용: 흰 바탕에 검은 글자(BGR).</summary>
    public static Mat ForOcr(Mat bgr, WhiteTextSpec spec)
    {
        using var mask = Mask(bgr, spec);
        using var inv = new Mat();
        Cv2.BitwiseNot(mask, inv);
        var outp = new Mat();
        Cv2.CvtColor(inv, outp, ColorConversionCodes.GRAY2BGR);
        return outp;
    }
}
