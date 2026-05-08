using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Settings.Stores;

internal sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString() ?? string.Empty;
            return ParseHex(raw);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            byte a = 255;
            byte r = 0;
            byte g = 0;
            byte b = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return Color.FromArgb(a, r, g, b);
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                var name = reader.GetString();
                reader.Read();
                var value = (byte)reader.GetInt32();

                switch (name)
                {
                    case "a" or "A":
                        a = value;
                        break;

                    case "r" or "R":
                        r = value;
                        break;

                    case "g" or "G":
                        g = value;
                        break;

                    case "b" or "B":
                        b = value;
                        break;
                }
            }

            return Color.FromArgb(a, r, g, b);
        }

        throw new JsonException($"Не удалось прочитать Color: неожиданный токен {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("a", value.A);
        writer.WriteNumber("r", value.R);
        writer.WriteNumber("g", value.G);
        writer.WriteNumber("b", value.B);
        writer.WriteEndObject();
    }

    private static Color ParseHex(string raw)
    {
        var hex = raw.StartsWith('#') ? raw[1..] : raw;

        return hex.Length switch
        {
            6 => Color.FromArgb(byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
            8 => Color.FromArgb(byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
            _ => throw new JsonException($"Не удалось прочитать Color из строки '{raw}'"),
        };
    }
}
