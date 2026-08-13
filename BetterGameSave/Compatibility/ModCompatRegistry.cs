using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;

namespace BetterGameSave.Compatibility;

/// <summary>
/// Registry that tracks which known mods are loaded, so BetterGameSave
/// can apply targeted compatibility patches.
/// </summary>
internal static class ModCompatRegistry
{
    private static readonly Dictionary<string, bool> _knownMods = new()
    {
        ["org.bepinex.plugins.qol_unknown"] = false,     // QoL Unknown
        ["com.krokosha.casualties.mp"] = false,            // KrokMP
        ["net.cucorelib"] = false,                         // CUCoreLib
        ["com.yourname.cu.tarkovmedicalmod"] = false,      // CUTarkovMedicalMod
        ["com.cu.morenpcmod"] = false,                     // CUMoreNpcMod
    };

    public static void Initialize()
    {
        foreach (var guid in _knownMods.Keys)
        {
            _knownMods[guid] = Chainloader.PluginInfos.ContainsKey(guid);
        }
    }

    public static bool IsLoaded(string modGuid)
    {
        return _knownMods.TryGetValue(modGuid, out var loaded) && loaded;
    }

    public static bool IsQolLoaded => IsLoaded("org.bepinex.plugins.qol_unknown");
    public static bool IsKrokMpLoaded => IsLoaded("KrokoshaCasualtiesMP");
    public static bool IsCUCoreLibLoaded => IsLoaded("net.cucorelib");
}
