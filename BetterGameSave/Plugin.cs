using System;
using BepInEx;
using BepInEx.Logging;
using BetterGameSave.API;
using BetterGameSave.Compatibility;
using BetterGameSave.Core;
using HarmonyLib;
using UnityEngine;

namespace BetterGameSave;

[BepInPlugin(ModGuid, ModName, ModVersion)]
[BepInDependency("org.bepinex.plugins.qol_unknown", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.krokosha.casualties.mp", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string ModGuid = "com.cu.bettergamesave";
    public const string ModName = "Casualties: Unknown - Better Game Save";
    public const string ModVersion = "0.1.0";

    internal static ManualLogSource Log = null!;
    internal static Plugin Instance = null!;

    private SaveManager _saveManager = null!;
    private KrokMpCompat _krokMpCompat = null!;

    private void Awake()
    {
        Log = Logger;
        Instance = this;

        _saveManager = new SaveManager(Config, Logger);
        _krokMpCompat = new KrokMpCompat();

        var harmony = new Harmony(ModGuid);
        try
        {
            harmony.PatchAll();
        }
        catch (Exception ex)
        {
            Log.LogError($"PatchAll() threw: {ex}");
        }

        BetterSaveApi.Initialize(_saveManager);

        Log.LogInfo($"{ModName} v{ModVersion} loaded.");
        Log.LogInfo($"KrokMP detected: {_krokMpCompat.IsKrokMpActive}");
    }

    private void Update()
    {
        _saveManager?.Tick();
    }

    private void OnGUI()
    {
        // UI rendering will be implemented here
    }
}
