// Copyright (c) 2025 Agash Thamo. Licensed under the MIT License. See THIRD-PARTY-NOTICES.txt.

using PoproshaykaBot.Core.YouTube.Chat.InnerTube.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube;

internal sealed class MessageRunConverter : JsonConverter<MessageRun>
{
    public override MessageRun? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);

        if (doc.RootElement.TryGetProperty("emoji", out _))
        {
            return JsonSerializer.Deserialize(doc.RootElement, InnerTubeJsonSerializerContext.Default.MessageEmoji);
        }

        if (doc.RootElement.TryGetProperty("text", out _))
        {
            return JsonSerializer.Deserialize(doc.RootElement, InnerTubeJsonSerializerContext.Default.MessageText);
        }

        throw new JsonException("Неизвестный формат элемента runs.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        MessageRun value,
        JsonSerializerOptions options)
    {
        switch (value)
        {
            case MessageEmoji emoji:
                JsonSerializer.Serialize(writer, emoji, InnerTubeJsonSerializerContext.Default.MessageEmoji);
                break;
            case MessageText text:
                JsonSerializer.Serialize(writer, text, InnerTubeJsonSerializerContext.Default.MessageText);
                break;
            default:
                throw new JsonException($"Неизвестный тип MessageRun: {value.GetType().Name}");
        }
    }
}
