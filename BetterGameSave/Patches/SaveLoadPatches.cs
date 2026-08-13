using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BetterGameSave.Patches;

/// <summary>
/// Wraps the game's file operations without replacing its save format or KrokMP path patches.
/// </summary>
internal static class SaveLoadPatches
{
    private static readonly MethodInfo FileWriteAllBytes = AccessTools.Method(
        typeof(File), nameof(File.WriteAllBytes), new[] { typeof(string), typeof(byte[]) });
    private static readonly MethodInfo FileReadAllBytes = AccessTools.Method(
        typeof(File), nameof(File.ReadAllBytes), new[] { typeof(string) });
    private static readonly MethodInfo FileDelete = AccessTools.Method(
        typeof(File), nameof(File.Delete), new[] { typeof(string) });
    private static readonly MethodInfo SafeWriteAllBytes = AccessTools.Method(
        typeof(SaveLoadPatches), nameof(WriteAllBytes));
    private static readonly MethodInfo SafeReadAllBytes = AccessTools.Method(
        typeof(SaveLoadPatches), nameof(ReadAllBytes));
    private static readonly MethodInfo PreserveSave = AccessTools.Method(
        typeof(SaveLoadPatches), nameof(Delete));

    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGame))]
    [HarmonyPriority(Priority.First)]
    private static class SavePatch
    {
        private static void Prefix() => Plugin.Manager.BeginSave();

        private static void Finalizer(Exception? __exception) =>
            Plugin.Manager.CompleteSave(__exception == null);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceCall(instructions, FileWriteAllBytes, SafeWriteAllBytes);
        }
    }

    [HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.RegenerateWorld))]
    [HarmonyPriority(Priority.First)]
    private static class LayerCheckpointPatch
    {
        private static void Prefix() => Plugin.Manager.RequestLayerCheckpoint();
    }

    [HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.SaveAndExit))]
    [HarmonyPriority(Priority.First)]
    private static class SaveAndExitCheckpointPatch
    {
        private static void Prefix() => Plugin.Manager.PrepareExitCheckpoint();
    }

    [HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.FinishWorldGeneration))]
    [HarmonyPriority(Priority.Last)]
    private static class WorldGeneratedPatch
    {
        private static void Postfix(ref System.Collections.IEnumerator __result)
        {
            __result = AppendSnapshotRestore(__result);
        }
    }

    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.TryLoadGame))]
    [HarmonyPriority(Priority.First)]
    private static class LoadPatch
    {
        private static void Prefix() => Plugin.Manager.BeginLoad();

        private static void Finalizer(Exception? __exception) =>
            Plugin.Manager.CompleteLoad();

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in ReplaceCall(instructions, FileReadAllBytes, SafeReadAllBytes))
            {
                if (instruction.Calls(FileDelete))
                    instruction.operand = PreserveSave;
                yield return instruction;
            }
        }
    }

    private static System.Collections.IEnumerator AppendSnapshotRestore(System.Collections.IEnumerator original)
    {
        while (original.MoveNext())
            yield return original.Current;

        var restore = Plugin.Manager.RestoreSnapshotsAfterWorldGeneration();
        while (restore.MoveNext())
            yield return restore.Current;
    }

    private static IEnumerable<CodeInstruction> ReplaceCall(
        IEnumerable<CodeInstruction> instructions,
        MethodInfo original,
        MethodInfo replacement)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(original))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            yield return instruction;
        }
    }

    private static void WriteAllBytes(string path, byte[] data) => Plugin.Manager.WriteNativeSave(path, data);
    private static byte[] ReadAllBytes(string path) => Plugin.Manager.ReadNativeSave(path);

    private static void Delete(string path)
    {
        if (!Plugin.Manager.SuppressNativeDelete(path))
            File.Delete(path);
    }
}
