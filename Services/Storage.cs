using System.Text.Json;
using Eggbox.Models;
using Optional;

namespace Eggbox.Services;

public static class Storage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static void Set<T>(StorageKeys.StorageKey<T> key, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        Preferences.Set(key.Key, json);
    }

    public static Option<T> Get<T>(StorageKeys.StorageKey<T> key)
    {
        if (!Preferences.ContainsKey(key.Key))
            return Option.None<T>();

        var json = Preferences.Get(key.Key, "");
        return string.IsNullOrWhiteSpace(json)
            ? Option.None<T>()
            : JsonSerializer.Deserialize<T>(json, JsonOptions).Some()!;
    }

    public static void Remove<T>(StorageKeys.StorageKey<T> key)
        => Preferences.Remove(key.Key);

    public static bool Contains<T>(StorageKeys.StorageKey<T> key)
        => Preferences.ContainsKey(key.Key);
}



public static class StorageKeys
{
    public sealed record StorageKey<T>(string Key);
    
    public static readonly StorageKey<MixerInfo> LastMixer = new("last-mixer");
}