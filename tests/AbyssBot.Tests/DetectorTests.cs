using AbyssBot.Core.Config;
using AbyssBot.Core.Ocr;
using AbyssBot.Core.Text;
using AbyssBot.Core.Vision;
using OpenCvSharp;
using Xunit;

namespace AbyssBot.Tests;

/// <summary>탐색 영역 기준 OCR 흉내: 절대좌표 단어 목록을 잘라낸 영역 좌표로 바꿔 돌려준다.</summary>
public sealed class FakeOcr(Rect region) : IOcrEngine
{
    public List<(string text, Rect rect)> Words = new();
    public List<(string text, Rect rect)>? UpscaledWords;
    public int Calls;

    public OcrResult Recognize(Mat bgr)
    {
        Calls++;
        double scale = (double)bgr.Width / region.Width;
        var src = scale > 1.5 && UpscaledWords is not null ? UpscaledWords : Words;
        var words = src.Where(w => region.Contains(w.rect.Location))
            .Select(w => new OcrWord(w.text, new Rect(
                (int)((w.rect.X - region.X) * scale), (int)((w.rect.Y - region.Y) * scale),
                (int)(w.rect.Width * scale), (int)(w.rect.Height * scale)))).ToList();
        return new OcrResult(words.Count == 0 ? Array.Empty<OcrLine>() : new[] { new OcrLine(words) });
    }
}

public class DetectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "abyss_test_" + Guid.NewGuid().ToString("N"));
    private static readonly Rect EnterRegion = new(280, 940, 280, 80);
    private static readonly Scalar Green = new(60, 190, 70);   // BGR
    private static readonly Scalar Gray = new(90, 90, 90);

    public DetectorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Mat Frame()
    {
        var m = new Mat(1039, 816, MatType.CV_8UC3, new Scalar(40, 35, 30));
        // 배경 무늬(템플릿 매칭이 평탄 영역에서 NaN을 내지 않도록)
        var rng = new RNG(7);
        for (int i = 0; i < 400; i++)
            m.Circle(new Point((int)rng.Uniform(0, 816), (int)rng.Uniform(0, 1039)), (int)rng.Uniform(2, 10),
                new Scalar(rng.Uniform(0, 255), rng.Uniform(0, 255), rng.Uniform(0, 255)), -1);
        return m;
    }

    private static void DrawButton(Mat m, Rect r, Scalar color)
    {
        m.Rectangle(r, color, -1);
        m.Rectangle(r, Scalar.White, 2);
        m.PutText("ENTER", new Point(r.X + 12, r.Y + r.Height / 2 + 6), HersheyFonts.HersheySimplex, 0.6, Scalar.White, 2);
    }

    private void SaveTemplate(Mat frame, Rect r, string name)
    {
        using var crop = new Mat(frame, r);
        Cv2.ImWrite(Path.Combine(_dir, name), crop);
    }

    private TargetsConfig Cfg(params (string id, TargetDef def)[] targets)
    {
        var c = new TargetsConfig();
        foreach (var (id, def) in targets) c.Targets[id] = def;
        return c;
    }

    private static TargetDef EnterDef() => new()
    {
        Region = RegionSpec.FromLtrb(280, 940, 560, 1020),
        Ocr = new OcrRule { Texts = { "입장하기" }, Mode = "fuzzy" },
        Images = new() { new ImageRef { File = "a_enter.png" } },
        Color = new ColorRule { Allowed = { "green", "blue" } },
        PassScore = 3,
    };

    [Fact]
    public void Enter_ocr_image_color_all_match_and_click_rect_is_inside_button()
    {
        using var f = Frame();
        var btn = new Rect(350, 955, 130, 45);
        DrawButton(f, btn, Green);
        SaveTemplate(f, btn, "a_enter.png");
        var ocr = new FakeOcr(EnterRegion) { Words = { ("입장하기", new Rect(370, 965, 80, 22)) } };
        using var lib = new ImageLibrary(_dir);
        var d = new Detector(Cfg(("enter", EnterDef())), lib, ocr).Detect(f, "enter");

        Assert.True(d.Found, d.Summary());
        Assert.Equal(5, d.Score);
        Assert.True(d.BestImage!.Score > 0.99);
        Assert.Equal(btn, d.ButtonRect);
        Assert.Equal("초록", d.Color!.Kind);
    }

    [Fact]
    public void Enter_ocr_plus_color_passes_but_ocr_alone_on_gray_fails()
    {
        using var lib = new ImageLibrary(_dir); // 사진 없음
        var ocr = new FakeOcr(EnterRegion) { Words = { ("입장 하기", new Rect(370, 965, 80, 22)) } };

        using var g = Frame();
        DrawButton(g, new Rect(340, 950, 150, 50), Green);
        var dg = new Detector(Cfg(("enter", EnterDef())), lib, ocr).Detect(g, "enter");
        Assert.True(dg.Found, dg.Summary());
        Assert.Equal(3, dg.Score);

        using var gr = Frame();
        DrawButton(gr, new Rect(340, 950, 150, 50), Gray);
        var dgr = new Detector(Cfg(("enter", EnterDef())), lib, ocr).Detect(gr, "enter");
        Assert.False(dgr.Found, dgr.Summary());
        Assert.Equal(2, dgr.Score);
    }

    [Fact]
    public void Blue_button_is_accepted_for_enter()
    {
        using var lib = new ImageLibrary(_dir);
        var ocr = new FakeOcr(EnterRegion) { Words = { ("입장하기", new Rect(370, 965, 80, 22)) } };
        using var f = Frame();
        DrawButton(f, new Rect(340, 950, 150, 50), new Scalar(200, 110, 40));
        var d = new Detector(Cfg(("enter", EnterDef())), lib, ocr).Detect(f, "enter");
        Assert.True(d.Found, d.Summary());
        Assert.Equal("파랑", d.Color!.Kind);
    }

    [Fact]
    public void Color_alone_never_passes()
    {
        using var lib = new ImageLibrary(_dir);
        var ocr = new FakeOcr(EnterRegion);
        using var f = Frame();
        DrawButton(f, new Rect(340, 950, 150, 50), Green);
        var d = new Detector(Cfg(("enter", EnterDef())), lib, ocr).Detect(f, "enter");
        Assert.False(d.Found);
    }

    [Fact]
    public void Replay_requires_both_ocr_and_green()
    {
        var region = new Rect(340, 900, 160, 139);
        var def = new TargetDef
        {
            Region = RegionSpec.FromLtrb(340, 900, 500, 1039),
            Ocr = new OcrRule { Texts = { "다시하기" }, Mode = "fuzzy" },
            Color = new ColorRule { Allowed = { "green" } },
            Require = new() { "ocr", "color" },
            PassScore = 3,
        };
        using var lib = new ImageLibrary(_dir);
        var ocr = new FakeOcr(region) { Words = { ("다시", new Rect(385, 965, 30, 20)), ("하기", new Rect(418, 965, 30, 20)) } };

        using var green = Frame();
        DrawButton(green, new Rect(360, 950, 110, 50), Green);
        var ok = new Detector(Cfg(("replay", def)), lib, ocr).Detect(green, "replay");
        Assert.True(ok.Found, ok.Summary());
        Assert.True(ok.ButtonRect!.Value.Width > 0);

        using var blue = Frame();
        DrawButton(blue, new Rect(360, 950, 110, 50), new Scalar(200, 110, 40));
        var no = new Detector(Cfg(("replay", def)), lib, ocr).Detect(blue, "replay");
        Assert.False(no.Found, no.Summary());
    }

    [Fact]
    public void Image_center_must_be_inside_region()
    {
        using var f = Frame();
        var tplRect = new Rect(600, 600, 60, 40);
        DrawButton(f, tplRect, Green);
        SaveTemplate(f, tplRect, "b.png");
        using var lib = new ImageLibrary(_dir);
        TargetDef Def(int l, int t, int r, int b) => new()
        {
            Region = RegionSpec.FromLtrb(l, t, r, b),
            Images = new() { new ImageRef { File = "b.png", Threshold = 0.9 } },
            PassScore = 2,
        };
        // 사진 중심(630,620)이 영역 안: 사진이 영역 가장자리에 걸쳐도 인정
        var inside = new Detector(Cfg(("x", Def(625, 500, 800, 800))), lib, null).Detect(f, "x");
        Assert.True(inside.Found, inside.Summary());
        // 사진 중심이 영역 밖
        var outside = new Detector(Cfg(("x", Def(635, 500, 800, 800))), lib, null).Detect(f, "x");
        Assert.False(outside.Found, outside.Summary());
    }

    [Fact]
    public void Multiple_images_use_single_best_score_not_sum()
    {
        using var f = Frame();
        var r = new Rect(300, 960, 120, 30);
        DrawButton(f, r, Gray);
        SaveTemplate(f, r, "touch.png");
        SaveTemplate(f, r, "touch2.png");
        using var lib = new ImageLibrary(_dir);
        var def = new TargetDef
        {
            Region = RegionSpec.FromLtrb(240, 930, 600, 1010),
            Images = new() { new ImageRef { File = "touch.png" }, new ImageRef { File = "touch2.png" } },
            PassScore = 2,
        };
        var d = new Detector(Cfg(("t", def)), lib, null).Detect(f, "t");
        Assert.True(d.Found);
        Assert.Equal(2, d.Score);
    }

    [Fact]
    public void Ocr_and_image_on_different_buttons_are_not_combined()
    {
        using var f = Frame();
        var imgBtn = new Rect(290, 950, 90, 45);
        DrawButton(f, imgBtn, Green);
        SaveTemplate(f, imgBtn, "a_enter.png");
        using var lib = new ImageLibrary(_dir);
        // OCR은 멀리 떨어진 다른 버튼을 가리킴 (그 위치도 초록)
        DrawButton(f, new Rect(450, 950, 100, 45), Green);
        var ocr = new FakeOcr(EnterRegion) { Words = { ("입장하기", new Rect(462, 962, 70, 20)) } };
        var d = new Detector(Cfg(("enter", EnterDef())), lib, ocr).Detect(f, "enter");
        Assert.False(d.Found, d.Summary());
        Assert.True(d.Conflict);
    }

    [Fact]
    public void Menu_open_counts_distinct_names_once_across_original_and_upscaled()
    {
        var region = new Rect(408, 120, 408, 580);
        var def = new TargetDef
        {
            Region = new RegionSpec("W/2", "120", "W", "700"),
            Ocr = new OcrRule { Texts = { "캐릭터", "가방", "퀘스트", "미션" }, Mode = "exact", MinDistinct = 3 },
        };
        using var f = Frame();
        using var lib = new ImageLibrary(_dir);
        var ocr = new FakeOcr(region)
        {
            Words = { ("캐릭터", new Rect(450, 200, 50, 20)), ("가방", new Rect(550, 200, 40, 20)) },
            UpscaledWords = new() { ("캐릭터", new Rect(450, 200, 50, 20)), ("가방", new Rect(550, 200, 40, 20)) },
        };
        var two = new Detector(Cfg(("menu", def)), lib, ocr).Detect(f, "menu");
        Assert.False(two.Found, two.Summary());
        Assert.Equal(2, two.DistinctTexts.Count);

        ocr.UpscaledWords = new() { ("가방", new Rect(550, 200, 40, 20)), ("퀘스트", new Rect(450, 300, 50, 20)) };
        var three = new Detector(Cfg(("menu", def)), lib, ocr).Detect(f, "menu");
        Assert.True(three.Found, three.Summary());
        Assert.Equal(3, three.DistinctTexts.Count);
    }

    [Fact]
    public void Upscale_only_when_original_fails_and_coordinates_are_mapped_back()
    {
        using var f = Frame();
        using var lib = new ImageLibrary(_dir);
        var region = new Rect(240, 930, 360, 80);
        var def = new TargetDef
        {
            Region = RegionSpec.FromLtrb(240, 930, 600, 1010),
            Ocr = new OcrRule { Texts = { "터치해" } },
            PassScore = 2,
        };
        var ocr = new FakeOcr(region)
        {
            Words = { ("확인", new Rect(300, 960, 30, 20)) },
            UpscaledWords = new() { ("화면을", new Rect(300, 960, 40, 20)), ("터치해", new Rect(345, 960, 40, 20)), ("주세요", new Rect(390, 960, 40, 20)) },
        };
        var d = new Detector(Cfg(("touch", def)), lib, ocr).Detect(f, "touch");
        Assert.True(d.Found, d.Summary());
        Assert.True(d.Ocr!.Upscaled);
        Assert.Equal(2, ocr.Calls);
        Assert.Equal(new Rect(345, 960, 40, 20), d.ButtonRect);

        ocr.Calls = 0;
        ocr.Words = new() { ("화면을터치해주세요", new Rect(300, 960, 150, 20)) };
        var d2 = new Detector(Cfg(("touch", def)), lib, ocr).Detect(f, "touch");
        Assert.True(d2.Found);
        Assert.Equal(1, ocr.Calls);
    }
}

public class TextAndRegionTests
{
    private static OcrResult Line(params string[] words) =>
        new(new[] { new OcrLine(words.Select((w, i) => new OcrWord(w, new Rect(i * 50, 0, 40, 20))).ToList()) });

    [Theory]
    [InlineData("다시 하기", "다시하기", "fuzzy", true)]
    [InlineData("다시하가", "다시하기", "fuzzy", true)]
    [InlineData("나가기", "다시하기", "fuzzy", false)]
    [InlineData("확인", "터치해", "contains", false)]
    [InlineData("화면을 터치해 주세요", "터치해", "contains", true)]
    [InlineData("미선", "미션", "fuzzy", false)]           // 3글자 미만은 유사 일치 없음
    [InlineData("캐릭터정보", "캐릭터", "exact", false)]
    [InlineData("환경 설정", "환경설정", "exact", true)]
    [InlineData("내 주변에 말하기", "말하기", "contains", true)]
    [InlineData("길드에 말하기", "말하기", "contains", true)]
    [InlineData("다시 시도", "다시시도하기", "contains", false)]
    public void Text_rules(string ocrText, string target, string mode, bool expected)
    {
        var r = Line(ocrText.Split(' '));
        Assert.Equal(expected, TextMatcher.Find(r, target, mode) is not null);
    }

    [Fact]
    public void Match_rect_covers_only_matching_words()
    {
        var r = Line("화면을", "터치해", "주세요");
        var m = TextMatcher.Find(r, "터치해", "contains")!;
        Assert.Equal(new Rect(50, 0, 40, 20), m.Rect);
    }

    [Fact]
    public void Region_expressions_resolve_against_capture_size()
    {
        Assert.Equal(new Rect(80, 929, 736, 110), new RegionSpec("80", "H-110", "W", "H").Resolve(816, 1039));
        Assert.Equal(new Rect(408, 120, 408, 580), new RegionSpec("W/2", "120", "W", "700").Resolve(816, 1039));
        Assert.Equal(new Rect(450, 280, 190, 140), RegionSpec.FromLtrb(450, 280, 640, 420).Resolve(816, 1039));
        Assert.Throws<FormatException>(() => new RegionSpec("X", "0", "1", "1"));
    }

    [Fact]
    public void Shipped_config_files_load_and_validate()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../config"));
        var cfg = ConfigLoader.Load(dir);
        Assert.Equal(816, cfg.Targets.Baseline.Width);
        Assert.Equal(3, cfg.Scenario.Destinations.Count);
        Assert.False(cfg.Targets.Targets["revive_state"].IsConfigured);
        Assert.True(cfg.Targets.Targets["enter"].IsConfigured);
        Assert.Equal(new Rect(340, 900, 160, 139), cfg.Targets.Targets["replay"].Region!.Resolve(816, 1039));
    }
}
