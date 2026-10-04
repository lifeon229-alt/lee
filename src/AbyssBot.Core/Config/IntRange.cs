using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbyssBot.Core.Config;

/// <summary>[최소, 최대] 밀리초 범위. JSON은 [380, 520] 또는 단일 숫자.</summary>
[JsonConverter(typeof(IntRangeConverter))]
public readonly record struct IntRange(int Min, int Max)
{
    public static IntRange Fixed(int v) => new(v, v);
    public int Next(Random rng) => Max <= Min ? Min : rng.Next(Min, Max + 1);
    public override string ToString() => Min == Max ? $"{Min}" : $"{Min}~{Max}";
}

internal sealed class IntRangeConverter : JsonConverter<IntRange>
{
    public override IntRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return IntRange.Fixed(reader.GetInt32());
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("범위는 [최소, 최대] 또는 숫자여야 합니다.");
        reader.Read(); var a = reader.GetInt32();
        reader.Read(); var b = reader.GetInt32();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("범위는 값 2개여야 합니다.");
        if (b < a) throw new JsonException($"범위 [{a},{b}]의 최대가 최소보다 작습니다.");
        return new IntRange(a, b);
    }

    public override void Write(Utf8JsonWriter writer, IntRange value, JsonSerializerOptions options)
    {
        writer.WriteStartArray(); writer.WriteNumberValue(value.Min); writer.WriteNumberValue(value.Max); writer.WriteEndArray();
    }
}
