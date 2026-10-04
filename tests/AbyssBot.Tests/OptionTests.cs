using AbyssBot.Core.Config;
using AbyssBot.Core.Engine;
using AbyssBot.Core.Ocr;
using AbyssBot.Core.Vision;
using OpenCvSharp;
using Xunit;

namespace AbyssBot.Tests;

public class OptionFlowTests
{
    [Fact]
    public void Party_then_difficulty_are_selected_only_on_first_entry()
    {
        var rig = new Rig();
        rig.Scenario.Options.Difficulty = "hard";
        rig.Scenario.Options.PartyMode = "solo";
        rig.Game.BattleLength = 10;
        rig.Time.CancelWhen = () => rig.Game.Inputs.Count(i => i == "SPACE") >= 2 && rig.Game.State == "battle";
        rig.Run();
        Assert.Equal(new[]
        {
            "ESC", "click:abyss_menu", "click:dest_husang", "click:party_solo", "click:difficulty_hard", "SPACE",
            "click:result_touch", "click:other_dungeon", "click:dest_husang", "SPACE",
        }, rig.Game.Inputs);
    }

    [Fact]
    public void Already_selected_options_are_not_clicked()
    {
        var rig = new Rig();
        rig.Scenario.Options.Difficulty = "veryHard";   // 게임에서 이미 선택된 상태
        rig.Scenario.Options.PartyMode = "together";
        rig.Time.CancelWhen = () => rig.Game.State == "battle";
        rig.Run();
        Assert.DoesNotContain(rig.Game.Inputs, i => i.StartsWith("click:difficulty") || i.StartsWith("click:party"));
        Assert.Contains("SPACE", rig.Game.Inputs);
    }

    [Fact]
    public void Unchanged_selection_is_retried_limited_times_and_never_enters()
    {
        var rig = new Rig();
        rig.Scenario.Options.Difficulty = "intro";
        rig.Game.OptionClicksIgnored = true;
        var r = rig.Run();
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Equal(StepId.SelectOptions, r.Step);
        Assert.Equal(1 + rig.Scenario.Timing.MaxRetries, rig.Game.Inputs.Count(i => i == "click:difficulty_intro"));
        Assert.DoesNotContain("SPACE", rig.Game.Inputs);
    }

    [Fact]
    public void Momentary_selected_flash_is_not_taken_as_selected()
    {
        // 실제 오류 재현: 함께하기를 눌렀을 때 한 프레임만 선택된 것처럼 보이고 실제로는 혼자하기 유지
        var rig = new Rig();
        rig.Scenario.Options.PartyMode = "together";
        rig.Scenario.Options.Difficulty = "veryHard";
        rig.Game.SelectedParty = "party_solo";
        rig.Game.OptionClicksIgnored = true;
        rig.Game.FlashCapturesOnClick = 1;
        var r = rig.Run();
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Equal(StepId.SelectOptions, r.Step);
        Assert.DoesNotContain("SPACE", rig.Game.Inputs);
        Assert.DoesNotContain("click:difficulty_veryhard", rig.Game.Inputs); // 방식이 확인되기 전에는 난이도로 넘어가지 않음
    }

    [Fact]
    public void Waits_for_entry_screen_redraw_after_party_click()
    {
        var rig = new Rig();
        rig.Scenario.Options.PartyMode = "solo";
        rig.Scenario.Options.Difficulty = "hard";
        rig.Game.RedrawCapturesOnPartyClick = 3;
        rig.Time.CancelWhen = () => rig.Game.State == "battle";
        rig.Run();
        Assert.Equal(new[] { "ESC", "click:abyss_menu", "click:dest_husang", "click:party_solo", "click:difficulty_hard", "SPACE" }, rig.Game.Inputs);
    }

    [Fact]
    public void Hell1_missing_stops_without_clicking_anything_on_entry_screen()
    {
        var rig = new Rig();
        rig.Scenario.Options.Difficulty = "hell1";
        rig.Game.DifficultyButtons.Remove("difficulty_hell1"); // 버튼에 지옥2가 표시된 경우
        var r = rig.Run();
        Assert.Equal(RunOutcome.Failed, r.Outcome);
        Assert.Equal(StepId.SelectOptions, r.Step);
        Assert.Equal(new[] { "ESC", "click:abyss_menu", "click:dest_husang" }, rig.Game.Inputs);
    }

    [Fact]
    public void No_option_chosen_keeps_game_state()
    {
        var rig = new Rig();
        rig.Time.CancelWhen = () => rig.Game.State == "battle";
        rig.Run();
        Assert.Equal(new[] { "ESC", "click:abyss_menu", "click:dest_husang", "SPACE" }, rig.Game.Inputs);
    }
}

public class OptionDetectorTests
{
    private static readonly Rect Region = new(40, 155, 520, 70);

    private static (Detector det, Mat frame) Make(string target, TargetDef def, FakeOcr ocr, Action<Mat>? draw = null)
    {
        var cfg = new TargetsConfig();
        cfg.Targets[target] = def;
        var frame = new Mat(1080, 1920, MatType.CV_8UC3, new Scalar(30, 28, 26));
        draw?.Invoke(frame);
        return (new Detector(cfg, new ImageLibrary(Path.GetTempPath()), ocr), frame);
    }

    private static TargetDef Diff(string text, List<string>? exclude = null, double? left = null) => new()
    {
        Region = RegionSpec.FromLtrb(40, 155, 560, 225),
        Ocr = new OcrRule { Texts = { text }, Mode = exclude is null && text != "입문" ? "contains" : "exact", ExcludeTexts = exclude },
        ClickLeftFraction = left,
        Selected = new SelectedSpec { Color = "red", MinDominance = 80, MinFraction = 0.04, ExpandX = 1.6, ExpandY = 2.2 },
        PassScore = 2,
    };

    private static FakeOcr RowOcr() => new(Region)
    {
        Words =
        {
            ("입문", new Rect(70, 178, 36, 22)), ("어려움", new Rect(155, 178, 52, 22)),
            ("매우", new Rect(255, 178, 36, 22)), ("어려움", new Rect(295, 178, 52, 22)), ("지옥1", new Rect(395, 178, 44, 22)),
        },
    };

    [Fact]
    public void Hard_excludes_the_word_inside_very_hard()
    {
        var (det, f) = Make("h", Diff("어려움", new() { "매우어려움" }), RowOcr());
        using (f)
        {
            var d = det.Detect(f, "h");
            Assert.True(d.Found, d.Summary());
            Assert.Equal(155, d.ButtonRect!.Value.X);
        }
    }

    [Fact]
    public void Hell1_click_area_is_limited_to_left_part_of_text()
    {
        var ocr = RowOcr();
        ocr.Words[4] = ("지옥1∨", new Rect(395, 178, 90, 22)); // 화살표까지 한 단어로 읽힌 경우
        var (det, f) = Make("x", Diff("지옥1", left: 0.5), ocr);
        using (f)
        {
            var d = det.Detect(f, "x");
            Assert.True(d.Found, d.Summary());
            Assert.Equal(new Rect(395, 178, 45, 22), d.ButtonRect);
        }
    }

    [Fact]
    public void Selected_is_judged_by_red_pixels_around_text()
    {
        void RedPill(Mat m) { m.Rectangle(new Rect(240, 168, 128, 42), new Scalar(40, 40, 230), 3); m.PutText("XXXX", new OpenCvSharp.Point(260, 198), HersheyFonts.HersheySimplex, 0.7, new Scalar(40, 40, 230), 2); }
        var (det, f) = Make("v", Diff("매우어려움"), RowOcr(), RedPill);
        using (f)
        {
            var d = det.Detect(f, "v");
            Assert.True(d.Found, d.Summary());
            Assert.True(d.Selected, d.Summary());
        }
        var (det2, f2) = Make("v", Diff("매우어려움"), RowOcr());
        using (f2) Assert.False(det2.Detect(f2, "v").Selected);
    }
}

/// <summary>탐색 영역 크기로 어느 대상의 OCR 호출인지 구분해 절대좌표 단어를 돌려주는 가짜 OCR.</summary>
public sealed class RegionOcr(params (Rect region, (string text, Rect rect)[] words)[] table) : IOcrEngine
{
    public OcrResult Recognize(Mat bgr)
    {
        foreach (var (region, words) in table)
        {
            double scale = (double)bgr.Width / region.Width;
            if (Math.Abs(bgr.Height - region.Height * scale) > 2 || (scale is not (1 or 2))) continue;
            var list = words.Select(w => new OcrWord(w.text, new Rect(
                (int)((w.rect.X - region.X) * scale), (int)((w.rect.Y - region.Y) * scale),
                (int)(w.rect.Width * scale), (int)(w.rect.Height * scale)))).ToList();
            return new OcrResult(new[] { new OcrLine(list) });
        }
        return OcrResult.Empty;
    }
}

public class RealEntryScreenTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    private static readonly RegionOcr Ocr = new(
        (new Rect(40, 155, 520, 70), new[]
        {
            ("입문", new Rect(72, 178, 36, 22)), ("어려움", new Rect(155, 178, 52, 22)),
            ("매우", new Rect(255, 178, 38, 22)), ("어려움", new Rect(297, 178, 50, 22)), ("지옥1", new Rect(394, 178, 44, 22)),
        }),
        (new Rect(640, 20, 680, 80), new[] { ("혼자하기", new Rect(788, 45, 84, 26)), ("함께하기", new Rect(1042, 45, 104, 26)) }));

    private static Mat Screen(int n)
    {
        var part = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"entry_top_{n}.png"));
        var frame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
        part.CopyTo(new Mat(frame, new Rect(0, 0, part.Width, part.Height)));
        return frame;
    }

    [Theory]
    [InlineData(6, "difficulty_intro", "party_together")]
    [InlineData(7, "difficulty_hard", "party_together")]
    [InlineData(8, "difficulty_veryhard", "party_together")]
    [InlineData(9, "difficulty_veryhard", "party_solo")]
    public void Only_the_selected_buttons_read_as_selected(int n, string diff, string party)
    {
        var cfg = ConfigLoader.Load(Path.Combine(Root, "config"));
        using var images = new ImageLibrary(cfg.ImagesDirectory);
        var det = new Detector(cfg.Targets, images, Ocr);
        using var f = Screen(n);
        foreach (var id in new[] { "difficulty_intro", "difficulty_hard", "difficulty_veryhard", "difficulty_hell1", "party_solo", "party_together" })
        {
            var d = det.Detect(f, id);
            Assert.True(d.Found, d.Summary());
            Assert.Equal(id == diff || id == party, d.Selected == true);
        }
    }

    [Fact]
    public void Solo_with_hell1_is_rejected_by_config_validation()
    {
        var cfg = ConfigLoader.Load(Path.Combine(Root, "config"));
        cfg.Scenario.Options.PartyMode = "solo";
        cfg.Scenario.Options.Difficulty = "hell1";
        var e = Assert.Throws<ConfigException>(() => ConfigLoader.Validate(cfg.Scenario, cfg.Targets));
        Assert.Contains("혼자하기", e.Message);
    }

    [Fact]
    public void Scene_skip_button_found_by_white_text_image()
    {
        var cfg = ConfigLoader.Load(Path.Combine(Root, "config"));
        using var images = new ImageLibrary(cfg.ImagesDirectory);
        var det = new Detector(cfg.Targets, images, null);
        var part = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "scene_skip_1680_15.png"));
        using var frame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
        part.CopyTo(new Mat(frame, new Rect(1680, 15, part.Width, part.Height)));
        var d = det.Detect(frame, "skip");
        Assert.True(d.Found, d.Summary());
        using var entry = Screen(6);
        Assert.False(det.Detect(entry, "skip").Found);
    }
}
