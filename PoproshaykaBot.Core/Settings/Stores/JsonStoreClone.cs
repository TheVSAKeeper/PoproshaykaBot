using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

public static class JsonStoreClone
{
    public static T DeepClone<T>(T value) where T : class
    {
        var json = JsonSerializer.Serialize(value, JsonStoreOptions.Default);
        return JsonSerializer.Deserialize<T>(json, JsonStoreOptions.Default)
               ?? throw new InvalidOperationException($"Не удалось клонировать значение типа {typeof(T).Name}: десериализация вернула null");
    }

    public static T? DeepCloneNullable<T>(T? value) where T : class
    {
        return value == null ? null : DeepClone(value);
    }
}
