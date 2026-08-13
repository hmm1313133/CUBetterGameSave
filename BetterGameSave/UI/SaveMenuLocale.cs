using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace BetterGameSave.UI;

/// <summary>
/// Loads the mod's UI strings from Lang/*.json next to the plugin assembly,
/// following the game's currently selected language.
/// </summary>
internal static class SaveMenuLocale
{
    private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private static bool _loaded;

    internal static string Get(string key)
    {
        EnsureLoaded();
        return _strings.TryGetValue(key, out var value) ? value : key;
    }

    internal static string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(Get(key), args);
        }
        catch (FormatException)
        {
            return Get(key);
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            var directory = Path.GetDirectoryName(typeof(SaveMenuLocale).Assembly.Location);
            if (string.IsNullOrEmpty(directory))
                return;
            var file = IsChineseLanguage() ? "zh_CN.json" : "EN.json";
            var path = Path.Combine(directory, "Lang", file);
            if (!File.Exists(path))
                path = Path.Combine(directory, "Lang", "EN.json");
            if (File.Exists(path))
                _strings = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path))
                           ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Could not load save menu language file: {ex.Message}");
        }
    }

    private static bool IsChineseLanguage()
    {
        var name = Locale.currentLangName ?? string.Empty;
        if (name.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("chinese", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.Contains("中文") || name.Contains("汉"))
        {
            return true;
        }

        // The Chinese translation supplement patches game text globally, so the
        // game's own locale may stay English; detect the supplement instead.
        try
        {
            foreach (var plugin in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                var identity = (plugin.Metadata.GUID + " " + plugin.Metadata.Name) ?? string.Empty;
                if (identity.IndexOf("chinese", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    identity.Contains("汉化") || identity.Contains("中文"))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // Chainloader not ready yet; fall back to English.
        }
        return false;
    }
}
