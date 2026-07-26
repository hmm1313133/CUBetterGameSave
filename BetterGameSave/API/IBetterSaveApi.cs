using System.Collections.Generic;

namespace BetterGameSave.API;

/// <summary>
/// Public API interface for other mods to integrate with BetterGameSave.
/// Other mod authors can use this to register custom save data,
/// query save slot info, and hook into save/load events.
/// </summary>
public interface IBetterSaveApi
{
    /// <summary>
    /// Register a custom save data provider. The provider's data will be
    /// automatically serialized/deserialized alongside the game's save data.
    /// </summary>
    /// <param name="modId">Unique identifier for your mod.</param>
    /// <param name="provider">The save data provider implementation.</param>
    void RegisterSaveDataProvider(string modId, ISaveDataProvider provider);

    /// <summary>
    /// Unregister a previously registered save data provider.
    /// </summary>
    /// <param name="modId">The mod identifier used during registration.</param>
    void UnregisterSaveDataProvider(string modId);

    /// <summary>
    /// Get the current save slot information.
    /// </summary>
    SaveSlotInfo GetCurrentSlotInfo();

    /// <summary>
    /// Get a list of all available save slots.
    /// </summary>
    IReadOnlyList<SaveSlotInfo> GetAllSlots();

    /// <summary>
    /// Trigger a manual save. Returns true if the save was successful.
    /// </summary>
    bool RequestSave();

    /// <summary>
    /// Trigger a manual load. Returns true if the load was successful.
    /// </summary>
    bool RequestLoad(int slotIndex);

    /// <summary>
    /// Check whether KrokMP (multiplayer mod) is active.
    /// </summary>
    bool IsMultiplayerActive { get; }

    /// <summary>
    /// Event raised before the game saves. Other mods can subscribe
    /// to perform pre-save operations.
    /// </summary>
    event SaveEventHandler? OnPreSave;

    /// <summary>
    /// Event raised after the game saves.
    /// </summary>
    event SaveEventHandler? OnPostSave;

    /// <summary>
    /// Event raised before the game loads a save.
    /// </summary>
    event LoadEventHandler? OnPreLoad;

    /// <summary>
    /// Event raised after the game loads a save.
    /// </summary>
    event LoadEventHandler? OnPostLoad;
}
