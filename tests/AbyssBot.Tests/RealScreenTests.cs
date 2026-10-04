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

public class RealResultScreenTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    private static Mat Paste(string fixture, int x, int y, Scalar? bg = null)
    {
        var part = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        var frame = new Mat(1080, 1920, MatType.CV_8UC3, bg ?? Scalar.All(0));
        part.CopyTo(new Mat(frame, new Rect(x, y, part.Width, part.Height)));
        return frame;
    }

    [Fact]
    public void Result_phrase_over_character_leg_is_found_by_white_text_image()
    {
        var cfg = ConfigLoader.Load(Path.Combine(Root, "config"));
        using var images = new ImageLibrary(cfg.ImagesDirectory);
        var det = new Detector(cfg.Targets, images, null);
        using var frame = Paste("result_touch_690_960.png", 690, 960);
        var d = det.Detect(frame, "result_touch");
        Assert.True(d.Found, d.Summary());
        Assert.True(d.BestImage!.Score > 0.95, d.Summary());
        // 클릭 위치는 문구 안
        Assert.InRange(d.ButtonRect!.Value.X, 830, 850);
    }

    [Fact]
    public void Result_phrase_image_does_not_match_destination_list_or_bright_area()
    {
        var cfg = ConfigLoader.Load(Path.Combine(Root, "config"));
        using var images = new ImageLibrary(cfg.ImagesDirectory);
        var det = new Detector(cfg.Targets, images, null);
        using var list = Paste("abyss_list_1170_80.png", 1170, 80);
        Assert.False(det.Detect(list, "result_touch").Found);
        using var white = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(255)); // 영역 전체가 흰색
        Assert.False(det.Detect(white, "result_touch").Found);
        // 목록 화면 부분을 결과 문구 영역 위치로 옮겨도 맞지 않음
        using var moved = Paste("abyss_list_1170_80.png", 690, 600);
        Assert.False(det.Detect(moved, "result_touch").Found);
    }
}
