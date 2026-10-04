using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace AbyssBot.Core.Config;

public sealed record LoadedConfig(
    ScenarioConfig Scenario,
    TargetsConfig Targets,
    string BaseDirectory,
    string ScenarioPath,
    string TargetsPath,
    string ImagesDirectory)
{
    /// <summary>사용자 선택(게임 창, 목적지, 기능 켜기)을 저장하는 파일. 업데이트 때 scenario.json을 덮어써도 유지된다.</summary>
    public string UserPath => Path.Combine(BaseDirectory, "user.json");

    public string Describe()
    {
        static string Stamp(string p) => File.Exists(p) ? File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm:ss") : "없음";
        return $"설정 폴더: {BaseDirectory}\n" +
               $"  scenario.json 수정 시각 {Stamp(ScenarioPath)}\n" +
               $"  targets.json  수정 시각 {Stamp(TargetsPath)}\n" +
               $"  user.json     수정 시각 {Stamp(Path.Combine(BaseDirectory, "user.json"))}\n" +
               $"  images 폴더: {ImagesDirectory}";
    }
}

public static class ConfigLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// 실행 중 읽는 설정은 항상 이 폴더(기본: 실행 파일 옆)의 scenario.json, targets.json, images 폴더다.
    /// </summary>
    public static LoadedConfig Load(string baseDirectory)
    {
        var scenarioPath = Path.Combine(baseDirectory, "scenario.json");
        var targetsPath = Path.Combine(baseDirectory, "targets.json");
        var imagesDir = Path.Combine(baseDirectory, "images");
        if (!File.Exists(scenarioPath)) throw new ConfigException($"scenario.json이 없습니다: {scenarioPath}");
        if (!File.Exists(targetsPath)) throw new ConfigException($"targets.json이 없습니다: {targetsPath}");

        var scenario = Deserialize<ScenarioConfig>(scenarioPath);
        var userPath = Path.Combine(baseDirectory, "user.json");
        if (File.Exists(userPath))
        {
            var user = Deserialize<UserSettings>(userPath);
            if (user.Window is not null) scenario.Window = user.Window;
            if (user.Options is not null) scenario.Options = user.Options;
        }
        var targets = Deserialize<TargetsConfig>(targetsPath);
        targets.Targets = new Dictionary<string, TargetDef>(targets.Targets, StringComparer.Ordinal);
        scenario.Destinations = new Dictionary<string, DestinationSpec>(scenario.Destinations, StringComparer.Ordinal);
        Validate(scenario, targets);
        return new LoadedConfig(scenario, targets, baseDirectory, scenarioPath, targetsPath, imagesDir);
    }

    /// <summary>사용자 선택만 user.json에 저장한다(scenario.json은 프로그램 기본값으로 두고 건드리지 않음).</summary>
    public static void SaveUser(LoadedConfig cfg)
    {
        var json = JsonSerializer.Serialize(new UserSettings { Window = cfg.Scenario.Window, Options = cfg.Scenario.Options }, JsonOptions);
        File.WriteAllText(cfg.UserPath, json);
    }

    private static T Deserialize<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                   ?? throw new ConfigException($"{Path.GetFileName(path)}이 비어 있습니다.");
        }
        catch (JsonException e)
        {
            throw new ConfigException($"{Path.GetFileName(path)} 형식 오류: {e.Message}");
        }
    }

    public static void Validate(ScenarioConfig s, TargetsConfig t)
    {
        var errors = new List<string>();
        foreach (var d in s.Destinations)
        {
            if (!t.Targets.ContainsKey(d.Value.Target))
                errors.Add($"목적지 '{d.Key}'의 대상 '{d.Value.Target}'이 targets.json에 없습니다.");
            if (!t.Targets.ContainsKey(d.Value.TitleTarget))
                errors.Add($"목적지 '{d.Key}'의 제목 대상 '{d.Value.TitleTarget}'이 targets.json에 없습니다.");
        }
        if (s.FirstRun.Count == 0) errors.Add("firstRun이 비어 있습니다.");
        if (!s.Loops.ContainsKey(s.Options.RepeatMode))
            errors.Add($"loops에 선택한 반복 방식 '{s.Options.RepeatMode}'의 순서가 없습니다.");
        foreach (var (mode, loop) in s.Loops)
        {
            if (loop.Count == 0) errors.Add($"loops.{mode}가 비어 있습니다.");
            if (loop.Any(x => x is StepId.OpenMenu or StepId.SelectAbyss))
                errors.Add($"loops.{mode}에는 메뉴 열기/어비스 선택 단계를 넣을 수 없습니다(최초 한 번만 수행).");
            for (int i = 0; i < loop.Count; i++)
            {
                if (loop[i] == StepId.SelectDestination && (i == 0 || loop[i - 1] != StepId.OtherDungeon))
                    errors.Add($"loops.{mode}: 반복 중 목적지 선택은 '다른 던전 가기' 바로 다음에만 올 수 있습니다.");
                if (loop[i] == StepId.Enter && (i == 0 || loop[i - 1] is not (StepId.OtherDungeon or StepId.SelectDestination)))
                    errors.Add($"loops.{mode}: 반복 중 입장하기는 '다른 던전 가기' 또는 목적지 선택 바로 다음에만 올 수 있습니다.");
            }
        }
        if (s.Timing.MaxRetries < 0 || s.Timing.MaxRetries > 10) errors.Add("maxRetries는 0~10이어야 합니다.");
        if (s.Timing.DisappearConfirmFrames < 1) errors.Add("disappearConfirmFrames는 1 이상이어야 합니다.");
        if (s.Timing.ReviveMinIntervalMs < 4000) errors.Add("reviveMinIntervalMs는 4000 이상이어야 합니다.");
        if (s.Timing.MealMinIntervalMs < 4000) errors.Add("mealMinIntervalMs는 4000 이상이어야 합니다.");
        if (s.Timing.ReconnectMinIntervalMs < 3000) errors.Add("reconnectMinIntervalMs는 3000 이상이어야 합니다.");
        foreach (var (name, def) in t.Targets)
        {
            if (def.Ocr is { } o && o.Mode is not ("contains" or "fuzzy" or "exact"))
                errors.Add($"대상 '{name}': ocr.mode는 contains, fuzzy, exact 중 하나여야 합니다.");
            if (def.Color is { } c && c.Allowed.Any(a => a is not ("green" or "blue")))
                errors.Add($"대상 '{name}': color.allowed는 green, blue만 허용합니다.");
            if (def.Require is { } req && req.Any(r => r is not ("ocr" or "image" or "color")))
                errors.Add($"대상 '{name}': require는 ocr, image, color만 허용합니다.");
        }
        if (errors.Count > 0) throw new ConfigException(string.Join("\n", errors));
    }
}

/// <summary>user.json: 사용자가 고른 게임 창과 실행 옵션.</summary>
public sealed class UserSettings
{
    public WindowSpec? Window { get; set; }
    public OptionsSpec? Options { get; set; }
}

public sealed class ConfigException(string message) : Exception(message);
