using System;
using System.Collections.Generic;
using System.Reflection;
using BetterGameSave.API;
using BetterGameSave.Core;
using HarmonyLib;

namespace BetterGameSave.Patches;

/// <summary>
/// Harmony patches for the game's save/load system.
/// These patches intercept the game's native save and load operations
/// to inject custom mod data and provide error recovery.
/// </summary>
internal static class SaveLoadPatches
{
    // TODO: Add [HarmonyPatch] annotations targeting the game's save/load methods.
    //
    // Example targets (to be filled in once game save classes are identified):
    //   [HarmonyPatch(typeof(GameSaveManager), nameof(GameSaveManager.Save))]
    //   internal static class SavePatch { ... }
    //
    //   [HarmonyPatch(typeof(GameSaveManager), nameof(GameSaveManager.Load))]
    //   internal static class LoadPatch { ... }
    //
    //   [HarmonyPatch(typeof(GameSaveManager), nameof(GameSaveManager.DeleteSlot))]
    //   internal static class DeleteSlotPatch { ... }
}
