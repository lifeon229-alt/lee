using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;

namespace AbyssBot.Core.Config;

/// <summary>
/// 인식 영역. JSON 표기는 [왼쪽, 위, 오른쪽, 아래] (x,y,너비,높이 아님).
/// 각 값은 캡처 이미지(게임 창) 기준 픽셀이며, 숫자 또는 "W", "H", "W-80", "H-110", "W/2" 같은 식을 쓸 수 있다.
/// W/H는 캡처 이미지의 너비/높이다.
/// </summary>
[JsonConverter(typeof(RegionSpecConverter))]
public sealed class RegionSpec
{
    public RegionSpec(string left, string top, string right, string bottom)
    {
        Left = left; Top = top; Right = right; Bottom = bottom;
        // 형식 오류를 로드 시점에 드러낸다.
        Resolve(100, 100);
    }

    public string Left { get; }
    public string Top { get; }
    public string Right { get; }
    public string Bottom { get; }

    public static RegionSpec FromLtrb(int l, int t, int r, int b) =>
        new(l.ToString(CultureInfo.InvariantCulture), t.ToString(CultureInfo.InvariantCulture),
            r.ToString(CultureInfo.InvariantCulture), b.ToString(CultureInfo.InvariantCulture));

    /// <summary>캡처 크기에 맞춰 사각형으로 변환하고 이미지 안으로 제한한다.</summary>
    public Rect Resolve(int width, int height)
    {
        int l = Eval(Left, width, height), t = Eval(Top, width, height);
        int r = Eval(Right, width, height), b = Eval(Bottom, width, height);
        l = Math.Clamp(l, 0, width); r = Math.Clamp(r, 0, width);
        t = Math.Clamp(t, 0, height); b = Math.Clamp(b, 0, height);
        if (r < l) (l, r) = (r, l);
        if (b < t) (t, b) = (b, t);
        return new Rect(l, t, r - l, b - t);
    }

    internal static int Eval(string expr, int w, int h)
    {
        var s = expr.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
        if (s.Length == 0) throw new FormatException("빈 영역 값");
        int baseVal = s[0] switch
        {
            'W' => w,
            'H' => h,
            _ => throw new FormatException($"영역 식을 해석할 수 없습니다: '{expr}' (숫자, W, H, W-80, H-110, W/2 형식만 허용)")
        };
        var rest = s[1..];
        if (rest.Length == 0) return baseVal;
        if (!int.TryParse(rest[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var operand))
            throw new FormatException($"영역 식을 해석할 수 없습니다: '{expr}'");
        return rest[0] switch
        {
            '-' => baseVal - operand,
            '+' => baseVal + operand,
            '/' when operand != 0 => baseVal / operand,
            _ => throw new FormatException($"영역 식을 해석할 수 없습니다: '{expr}'")
        };
    }

    public override string ToString() => $"[{Left},{Top},{Right},{Bottom}]";
}

internal sealed class RegionSpecConverter : JsonConverter<RegionSpec>
{
    public override RegionSpec? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("region은 [왼쪽, 위, 오른쪽, 아래] 배열이어야 합니다.");
        var parts = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            parts.Add(reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetInt32().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.String => reader.GetString() ?? "",
                _ => throw new JsonException("region 값은 숫자 또는 문자열 식이어야 합니다.")
            });
        }
        if (parts.Count != 4) throw new JsonException("region은 값 4개 [왼쪽, 위, 오른쪽, 아래]가 필요합니다.");
        try { return new RegionSpec(parts[0], parts[1], parts[2], parts[3]); }
        catch (FormatException e) { throw new JsonException(e.Message); }
    }

    public override void Write(Utf8JsonWriter writer, RegionSpec value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var p in new[] { value.Left, value.Top, value.Right, value.Bottom })
        {
            if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) writer.WriteNumberValue(n);
            else writer.WriteStringValue(p);
        }
        writer.WriteEndArray();
    }
}
