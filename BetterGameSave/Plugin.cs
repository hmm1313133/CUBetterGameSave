using System;
using BepInEx;
using BepInEx.Logging;
using BetterGameSave.API;
using BetterGameSave.Compatibility;
using BetterGameSave.Core;
using BetterGameSave.UI;
using HarmonyLib;
using UnityEngine;

namespace BetterGameSave;

[BepInPlugin(ModGuid, ModName, ModVersion)]
[BepInDependency("org.bepinex.plugins.qol_unknown", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("KrokoshaCasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string ModGuid = "com.cu.bettergamesave";
    public const string ModName = "Casualties: Unknown - Better Game Save";
    public const string ModVersion = "0.1.0";

    internal static ManualLogSource Log = null!;
    internal static Plugin Instance = null!;
    internal static SaveManager Manager { get; private set; } = null!;

    private SaveManager _saveManager = null!;
    private KrokMpCompat _krokMpCompat = null!;
    private QolCompat _qolCompat = null!;

    private void Awake()
    {
        Log = Logger;
        Instance = this;

        _krokMpCompat = new KrokMpCompat();
        _saveManager = new SaveManager(Config, Logger, _krokMpCompat);
        Manager = _saveManager;
        _qolCompat = new QolCompat(Logger);
        _qolCompat.SuppressQolSaveFeatures();

        var harmony = new Harmony(ModGuid);
        try
        {
            harmony.PatchAll();
            foreach (var method in harmony.GetPatchedMethods())
                Log.LogInfo($"Harmony patched: {method.DeclaringType?.FullName}.{method.Name}");
        }
        catch (Exception ex)
        {
            Log.LogError($"PatchAll() threw: {ex}");
        }

        BetterSaveApi.Initialize(_saveManager);
        SaveMenuUI.Initialize();

        Log.LogInfo($"{ModName} v{ModVersion} loaded.");
        Log.LogInfo($"KrokMP detected: {_krokMpCompat.IsKrokMpActive}");
    }

    private void Update()
    {
        _saveManager?.Tick();
    }

    private void OnApplicationQuit()
    {
        _saveManager?.RequestExitCheckpoint();
    }

}
