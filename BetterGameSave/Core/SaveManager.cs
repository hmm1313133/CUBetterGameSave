using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BetterGameSave.API;

namespace BetterGameSave.Core;

/// <summary>
/// Core save manager. Handles save/load orchestration, custom data provider
/// registration, and multiplayer compatibility detection.
/// </summary>
internal sealed class SaveManager
{
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, ISaveDataProvider> _dataProviders = new();

    public bool IsMultiplayerActive { get; set; }

    public event SaveEventHandler? OnPreSave;
    public event SaveEventHandler? OnPostSave;
    public event LoadEventHandler? OnPreLoad;
    public event LoadEventHandler? OnPostLoad;

    internal SaveManager(ConfigFile config, ManualLogSource log)
    {
        _log = log;
        // TODO: Bind config entries
    }

    internal void RegisterDataProvider(string modId, ISaveDataProvider provider)
    {
        _dataProviders[modId] = provider;
        _log.LogInfo($"Save data provider registered: {modId}");
    }

    internal void UnregisterDataProvider(string modId)
    {
        if (_dataProviders.Remove(modId))
            _log.LogInfo($"Save data provider unregistered: {modId}");
    }

    internal SaveSlotInfo GetCurrentSlotInfo()
    {
        // TODO: Implement
        return new SaveSlotInfo();
    }

    internal IReadOnlyList<SaveSlotInfo> GetAllSlots()
    {
        // TODO: Implement
        return Array.Empty<SaveSlotInfo>();
    }

    internal bool RequestSave()
    {
        // TODO: Implement
        return false;
    }

    internal bool RequestLoad(int slotIndex)
    {
        // TODO: Implement
        return false;
    }

    internal void Tick()
    {
        // TODO: Per-frame logic if needed
    }

    internal void RaisePreSave(int slotIndex) => OnPreSave?.Invoke(slotIndex);
    internal void RaisePostSave(int slotIndex) => OnPostSave?.Invoke(slotIndex);
    internal void RaisePreLoad(int slotIndex) => OnPreLoad?.Invoke(slotIndex);
    internal void RaisePostLoad(int slotIndex) => OnPostLoad?.Invoke(slotIndex);
}
