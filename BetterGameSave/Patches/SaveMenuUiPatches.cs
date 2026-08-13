using HarmonyLib;

namespace BetterGameSave.Patches;

/// <summary>
/// Drives the in-game save management panel through the player camera's
/// lifecycle, mirroring the approach QoL uses for its own menu additions.
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.Start))]
internal static class SaveMenuUiStartPatch
{
    private static void Postfix(PlayerCamera __instance) =>
        UI.SaveMenuUI.OnPlayerCameraStart(__instance);
}

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.Update))]
internal static class SaveMenuUiUpdatePatch
{
    private static void Postfix(PlayerCamera __instance) =>
        UI.SaveMenuUI.OnPlayerCameraUpdate(__instance);
}
