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
    /// Register a versioned opaque-data provider for mod-owned state such as NPCs,
    /// buffs, quests, custom items, and network-owned data.
    /// </summary>
    /// <param name="modId">Stable globally unique identifier for the mod.</param>
    /// <param name="provider">The save data provider implementation.</param>
    void RegisterSaveDataProvider(string modId, ISaveDataProvider provider);

    /// <summary>Register a custom item ID whose world placement is managed by BetterGameSave.</summary>
    void RegisterItem(string modId, string itemId, IItemSaveProvider provider);

    /// <summary>Unregister all item registrations owned by a mod.</summary>
    void UnregisterItems(string modId);

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

    /// <summary>Create and select an empty named save slot.</summary>
    SaveSlotInfo CreateSlot(string name);

    /// <summary>Select an existing slot for subsequent saves.</summary>
    bool SelectSlot(string slotId);

    /// <summary>Rename a save slot without changing its stable ID.</summary>
    bool RenameSlot(string slotId, string newName);

    /// <summary>Delete a save slot.</summary>
    bool DeleteSlot(string slotId);

    /// <summary>
    /// Trigger a manual save. Returns true if the save was successful.
    /// </summary>
    bool RequestSave();

    /// <summary>
    /// Trigger a manual load by current list index. Returns true if the load was successful.
    /// </summary>
    bool RequestLoad(int slotIndex);

    /// <summary>Trigger a manual load using a stable slot ID.</summary>
    bool RequestLoad(string slotId);

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
