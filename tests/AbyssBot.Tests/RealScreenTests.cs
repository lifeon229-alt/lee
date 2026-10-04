using AbyssBot.Core.Config;
using AbyssBot.Core.Vision;
using OpenCvSharp;
using Xunit;

namespace AbyssBot.Tests;

/// <summary>사용자 실제 캡처(어비스 목적지 목록 부분)와 배포 설정·사진으로 배너 판정을 확인한다.</summary>
public class RealScreenTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    /// <summary>목록 부분 캡처를 1920×1080 화면의 원래 위치(1170,80)에 붙인다.</summary>
    private static Mat ListScreen()
    {
        var part = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "abyss_list_1170_80.png"));
        var frame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
        part.CopyTo(new Mat(frame, new Rect(1170, 80, part.Width, part.Height)));
        return frame;
    }

    [Theory]
    [InlineData("dest_husang", 128)]
    [InlineData("dest_kwanggi", 274)]
    [InlineData("dest_moolgil", 420)]
    public void Each_destination_banner_is_found_at_its_own_row(string target, int expectedY)
    {
        var cfg = ConfigLoader.Load(Path.Combine(Root, "config"));
        using var images = new ImageLibrary(cfg.ImagesDirectory);
        var det = new Detector(cfg.Targets, images, null);
        using var frame = ListScreen();

        var d = det.Detect(frame, target);
        Assert.True(d.Found, d.Summary());
        Assert.InRange(d.ButtonRect!.Value.Y, expectedY - 3, expectedY + 3);
        Assert.True(d.BestImage!.Score > 0.98, d.Summary());
    }

    [Fact]
    public void Banner_templates_do_not_match_other_rows_or_an_empty_screen()
    {
        var cfg = ConfigLoader.Load(Path.Combine(Root, "config"));
        using var images = new ImageLibrary(cfg.ImagesDirectory);
        var det = new Detector(cfg.Targets, images, null);
        using var frame = ListScreen();

        // 허상 배너 행을 가리면 허상 사진은 다른 배너에 맞으면 안 된다
        using var masked = frame.Clone();
        masked.Rectangle(new Rect(1183, 92, 690, 137), Scalar.All(0), -1);
        var d = det.Detect(masked, "dest_husang");
        Assert.False(d.Found, d.Summary());

        using var blank = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(30));
        Assert.False(det.Detect(blank, "dest_kwanggi").Found);
    }
}
