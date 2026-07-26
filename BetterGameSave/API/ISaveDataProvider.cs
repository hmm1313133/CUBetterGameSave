using System.Collections.Generic;

namespace BetterGameSave.API;

/// <summary>
/// Interface for mods to provide custom save data that will be
/// managed by BetterGameSave alongside the game's native save data.
/// </summary>
public interface ISaveDataProvider
{
    /// <summary>
    /// Called when the game is saving. Return your mod's serializable data.
    /// </summary>
    IReadOnlyDictionary<string, object> OnSave();

    /// <summary>
    /// Called when the game is loading. The data dictionary contains
    /// the data your mod previously returned from OnSave().
    /// </summary>
    void OnLoad(IReadOnlyDictionary<string, object> data);

    /// <summary>
    /// Called when a save slot is deleted. Clean up any per-slot data.
    /// </summary>
    void OnSlotDeleted(int slotIndex);
}

/// <summary>
/// Delegate for save-related events.
/// </summary>
/// <param name="slotIndex">The save slot index.</param>
public delegate void SaveEventHandler(int slotIndex);

/// <summary>
/// Delegate for load-related events.
/// </summary>
/// <param name="slotIndex">The save slot index.</param>
public delegate void LoadEventHandler(int slotIndex);

/// <summary>
/// Represents information about a save slot.
/// </summary>
public sealed class SaveSlotInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public System.DateTime LastModified { get; set; }
    public bool IsValid { get; set; }
    public bool IsMultiplayer { get; set; }
    public string ModVersion { get; set; } = string.Empty;
    public List<string> RegisteredMods { get; set; } = new();
}
