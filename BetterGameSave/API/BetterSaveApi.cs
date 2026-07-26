using System;
using System.Collections.Generic;
using BetterGameSave.Core;
using UnityEngine;

namespace BetterGameSave.API;

/// <summary>
/// Static accessor for the BetterGameSave API. Other mods should use
/// <c>BetterSaveApi.Api</c> to access the runtime API instance.
/// </summary>
public static class BetterSaveApi
{
    private static IBetterSaveApi? _api;

    /// <summary>
    /// Gets the runtime API instance. Returns null if BetterGameSave
    /// is not installed or not yet initialized.
    /// </summary>
    public static IBetterSaveApi? Api => _api;

    /// <summary>
    /// Gets whether the API is available (BetterGameSave is installed and initialized).
    /// </summary>
    public static bool IsAvailable => _api != null;

    internal static void Initialize(SaveManager saveManager)
    {
        _api = new ApiImplementation(saveManager);
    }

    internal static void Shutdown()
    {
        _api = null;
    }

    private sealed class ApiImplementation : IBetterSaveApi
    {
        private readonly SaveManager _saveManager;

        public ApiImplementation(SaveManager saveManager)
        {
            _saveManager = saveManager;
        }

        public void RegisterSaveDataProvider(string modId, ISaveDataProvider provider)
        {
            _saveManager.RegisterDataProvider(modId, provider);
        }

        public void UnregisterSaveDataProvider(string modId)
        {
            _saveManager.UnregisterDataProvider(modId);
        }

        public SaveSlotInfo GetCurrentSlotInfo()
        {
            return _saveManager.GetCurrentSlotInfo();
        }

        public IReadOnlyList<SaveSlotInfo> GetAllSlots()
        {
            return _saveManager.GetAllSlots();
        }

        public bool RequestSave()
        {
            return _saveManager.RequestSave();
        }

        public bool RequestLoad(int slotIndex)
        {
            return _saveManager.RequestLoad(slotIndex);
        }

        public bool IsMultiplayerActive => _saveManager.IsMultiplayerActive;

        public event SaveEventHandler? OnPreSave
        {
            add => _saveManager.OnPreSave += value;
            remove => _saveManager.OnPreSave -= value;
        }

        public event SaveEventHandler? OnPostSave
        {
            add => _saveManager.OnPostSave += value;
            remove => _saveManager.OnPostSave -= value;
        }

        public event LoadEventHandler? OnPreLoad
        {
            add => _saveManager.OnPreLoad += value;
            remove => _saveManager.OnPreLoad -= value;
        }

        public event LoadEventHandler? OnPostLoad
        {
            add => _saveManager.OnPostLoad += value;
            remove => _saveManager.OnPostLoad -= value;
        }
    }
}
