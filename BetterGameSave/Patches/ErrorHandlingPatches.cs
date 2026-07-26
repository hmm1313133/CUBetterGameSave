using System;
using HarmonyLib;

namespace BetterGameSave.Patches;

/// <summary>
/// Harmony patches for error handling during save/load.
/// These patches wrap save/load operations in try-catch blocks
/// to prevent crashes and provide graceful error recovery.
/// </summary>
internal static class ErrorHandlingPatches
{
    // TODO: Add patches that wrap save/load methods with error handling.
    //
    // When a save/load operation throws an exception, BetterGameSave will:
    // 1. Log the full exception with stack trace
    // 2. Attempt to recover by loading the previous valid save
    // 3. If recovery fails, present the user with a diagnostic report
    // 4. Optionally backup the corrupted save file for later analysis
}
