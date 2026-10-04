namespace AbyssBot.Core.Config;

/// <summary>targets.json: 인식 대상 정의.</summary>
public sealed class TargetsConfig
{
    /// <summary>영역 좌표의 기준이 된 캡처 크기와 캡처 방식.</summary>
    public BaselineSpec Baseline { get; set; } = new();

    /// <summary>사진에 개별 기준이 없을 때 쓰는 초기 일치 기준(TM_CCOEFF_NORMED).</summary>
    public double DefaultImageThreshold { get; set; } = 0.75;

    public ScoringSpec Scoring { get; set; } = new();

    public Dictionary<string, TargetDef> Targets { get; set; } = new(StringComparer.Ordinal);
}

public sealed class BaselineSpec
{
    public int Width { get; set; } = 816;
    public int Height { get; set; } = 1039;
    /// <summary>캡처에 테두리와 제목 표시줄이 포함되는지. 캡처와 클릭은 항상 같은 원점을 쓴다.</summary>
    public bool IncludesTitleBar { get; set; } = true;
}

public sealed class ScoringSpec
{
    public int Ocr { get; set; } = 2;
    public int Image { get; set; } = 2;
    public int Color { get; set; } = 1;
}

public sealed class TargetDef
{
    public string? Description { get; set; }

    /// <summary>null이면 아직 설정되지 않은 대상(사용 불가).</summary>
    public RegionSpec? Region { get; set; }

    public OcrRule? Ocr { get; set; }
    public List<ImageRef>? Images { get; set; }
    public ColorRule? Color { get; set; }

    /// <summary>합격 점수.</summary>
    public int PassScore { get; set; } = 2;

    /// <summary>반드시 성공해야 하는 근거: "ocr", "image", "color".</summary>
    public List<string>? Require { get; set; }

    /// <summary>
    /// OCR과 사진이 서로 다른 위치를 가리킬 때 같은 버튼으로 보는 허용 거리(px).
    /// 두 검출 중심이 상대 사각형을 이 값만큼 넓힌 범위 밖이면 충돌로 처리한다.
    /// </summary>
    public int SameButtonTolerance { get; set; } = 12;

    /// <summary>
    /// 0~1. 설정하면 클릭 위치를 버튼(검출 영역)의 왼쪽 이 비율 안으로만 제한한다.
    /// 예: '지옥1' 글자 오른쪽의 화살표(단계 선택 창)를 누르지 않도록.
    /// </summary>
    public double? ClickLeftFraction { get; set; }

    /// <summary>선택형 버튼의 '선택됨' 표시 판정(난이도, 혼자하기/함께하기).</summary>
    public SelectedSpec? Selected { get; set; }

    public bool IsConfigured =>
        Region is not null &&
        ((Ocr is { } o && o.Texts.Count > 0) || (Images is { Count: > 0 }));
}

public sealed class OcrRule
{
    public List<string> Texts { get; set; } = new();

    /// <summary>
    /// contains: 띄어쓰기 제거 후 포함.
    /// fuzzy: contains + (목표 3글자 이상이면 길이 차 1 이하·편집거리 1 이하 허용).
    /// exact: 단어/줄/연속 단어 결합이 목표와 정확히 같아야 함.
    /// </summary>
    public string Mode { get; set; } = "contains";

    /// <summary>원본에서 못 찾으면 2배 확대해 다시 읽을지.</summary>
    public bool Upscale { get; set; } = true;

    /// <summary>
    /// 0이면 Texts 중 하나만 맞으면 된다. 1 이상이면 서로 다른 Texts가 이 개수 이상 확인돼야 한다(메뉴 열림 판정).
    /// </summary>
    public int MinDistinct { get; set; }

    /// <summary>
    /// 설정하면 원본·2배 확대로 못 찾았을 때 한 번 더, 순백색 글자만 남긴 흑백 그림(흰 바탕 검은 글자, 2배)으로 읽는다.
    /// </summary>
    public WhiteTextSpec? WhiteText { get; set; }

    /// <summary>이 문구들과 겹치는 위치는 제외한다. 예: '어려움'을 찾을 때 '매우 어려움' 안의 '어려움' 제외.</summary>
    public List<string>? ExcludeTexts { get; set; }
}

/// <summary>
/// 선택됨 판정: 검출된 글자 영역을 넓힌 범위에서 지정 색 픽셀 비율이 MinFraction 이상이면 선택된 상태.
/// color: saturated(색과 무관하게 HSV 채도≥SatMin·밝기≥ValMin, 기본), red(R-max(G,B)≥dominance),
/// purple(B-G≥dominance 이고 R-G≥dominance/2), green, blue.
/// </summary>
public sealed class SelectedSpec
{
    public string Color { get; set; } = "saturated";
    public int SatMin { get; set; } = 120;
    public int ValMin { get; set; } = 120;
    public int MinDominance { get; set; } = 60;
    public double MinFraction { get; set; } = 0.05;
    public double ExpandX { get; set; } = 1.5;
    public double ExpandY { get; set; } = 2.0;
}

public sealed class ImageRef
{
    public string File { get; set; } = "";
    /// <summary>null이면 DefaultImageThreshold.</summary>
    public double? Threshold { get; set; }

    /// <summary>
    /// 설정하면 사진과 탐색 영역을 모두 '순백색 글자 픽셀만 남긴 흑백 그림'으로 바꾼 뒤 비교한다.
    /// 글자 뒤 배경(캐릭터·맵)이 매번 바뀌는 안내 문구용.
    /// </summary>
    public WhiteTextSpec? WhiteText { get; set; }
}

/// <summary>순백색 글자 판정: 세 채널 중 최솟값 ≥ Min 이고 (최댓값-최솟값) ≤ Spread 인 픽셀.</summary>
public sealed class WhiteTextSpec
{
    public int Min { get; set; } = 235;
    public int Spread { get; set; } = 30;
}

public sealed class ColorRule
{
    /// <summary>"green", "blue" 중 허용할 색.</summary>
    public List<string> Allowed { get; set; } = new() { "green" };

    /// <summary>우세 판정 기준. G-max(R,B) 또는 B-max(R,G)가 이 값 이상.</summary>
    public int MinDominance { get; set; } = 60;

    /// <summary>OCR 글자 영역으로 색을 잴 때 넓히는 배율.</summary>
    public double OcrExpandX { get; set; } = 1.8;
    public double OcrExpandY { get; set; } = 1.6;
}
