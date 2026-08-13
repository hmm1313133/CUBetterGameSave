using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;

namespace BetterGameSave.Compatibility;

/// <summary>
/// Keeps QoL's unrelated features enabled while removing its complete save subsystem.
/// </summary>
internal sealed class QolCompat
{
    private const string QolGuid = "org.bepinex.plugins.qol_unknown";

    private static readonly string[] SavePatchOwners =
    {
        "org.bepinex.plugins.qol.savesavemenu",
        "org.bepinex.plugins.qol.savemenu",
        "org.bepinex.plugins.qol.savemanager",
        "org.bepinex.plugins.qol.savesystem",
        "org.bepinex.plugins.qol.savepanel",
        "org.bepinex.plugins.qol.multiplayer_save_load",
        "org.bepinex.plugins.qol.layertransition",
    };

    private readonly ManualLogSource _log;

    public bool IsQolLoaded { get; }

    internal QolCompat(ManualLogSource log)
    {
        _log = log;
        IsQolLoaded = Chainloader.PluginInfos.ContainsKey(QolGuid);
    }

    internal void SuppressQolSaveFeatures()
    {
        if (!IsQolLoaded)
            return;

        foreach (var owner in SavePatchOwners)
            new Harmony(owner).UnpatchSelf();

        var plugin = Chainloader.PluginInfos[QolGuid].Instance;
        var moduleManager = plugin.GetType().Assembly.GetType("QoL_Unknown.QoLModuleManager");
        var isEnabled = moduleManager?.GetMethod("IsEnabled", BindingFlags.Public | BindingFlags.Static);
        if (isEnabled != null)
        {
            var harmony = new Harmony(Plugin.ModGuid + ".qolcompat");
            harmony.Patch(isEnabled, postfix: new HarmonyMethod(
                typeof(QolCompat).GetMethod(nameof(FilterSaveModules), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        _log.LogInfo("QoL detected: disabled QoL save UI, autosave, slot, load and multiplayer-save patches.");
    }

    private static void FilterSaveModules(string id, ref bool __result)
    {
        if (id == "qol_save_menu" || id == "qol_autosave")
            __result = false;
    }
}
