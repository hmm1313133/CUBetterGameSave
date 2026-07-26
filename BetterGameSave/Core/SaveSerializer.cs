using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGameSave.Core;

/// <summary>
/// Handles serialization and deserialization of mod save data.
/// Uses Newtonsoft.Json for the wire format.
/// </summary>
internal static class SaveSerializer
{
    private static readonly JsonSerializerSettings _settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    public static string Serialize(object data)
    {
        return JsonConvert.SerializeObject(data, _settings);
    }

    public static T Deserialize<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json, _settings)!;
    }

    public static string SerializeProviders(Dictionary<string, Dictionary<string, object>> providerData)
    {
        var wrapper = new SaveDataWrapper
        {
            Version = Plugin.ModVersion,
            Providers = providerData,
        };
        return Serialize(wrapper);
    }

    public static Dictionary<string, Dictionary<string, object>> DeserializeProviders(string json)
    {
        var wrapper = Deserialize<SaveDataWrapper>(json);
        return wrapper.Providers ?? new();
    }

    private sealed class SaveDataWrapper
    {
        public string Version { get; set; } = string.Empty;
        public Dictionary<string, Dictionary<string, object>> Providers { get; set; } = new();
    }
}
