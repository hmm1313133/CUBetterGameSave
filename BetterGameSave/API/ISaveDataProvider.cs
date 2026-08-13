using System.Collections.Generic;

namespace BetterGameSave.API;

/// <summary>
/// Contract for mod-owned save data. Each mod owns its payload format, schema migration,
/// object identity, and restoration semantics. BetterGameSave treats the payload as opaque data.
/// </summary>
public interface ISaveDataProvider
{
    /// <summary>Current payload schema version produced by this provider.</summary>
    int DataVersion { get; }

    /// <summary>Capture mod-owned state. Return null when no state should be stored.</summary>
    byte[]? Save(SaveProviderContext context);

    /// <summary>
    /// Restore mod-owned state at the requested phase. Providers that need spawned world objects,
    /// NPCs, or inventories should restore only during <see cref="SaveLoadPhase.WorldReady"/>.
    /// </summary>
    void Load(LoadProviderContext context, byte[] data);

    /// <summary>Called with the stable ID of a deleted slot.</summary>
    void OnSlotDeleted(string slotId);
}

/// <summary>Reason a save operation was requested.</summary>
public enum SaveCheckpointReason
{
    Manual,
    Timed,
    LayerTransition,
    Exit,
    Unknown,
}

/// <summary>Phase in which provider data is being restored.</summary>
public enum SaveLoadPhase
{
    NativeDataLoaded,
    WorldReady,
}

/// <summary>Context supplied when a provider captures state.</summary>
public sealed class SaveProviderContext
{
    public string SlotId { get; internal set; } = string.Empty;
    public int SlotIndex { get; internal set; }
    public SaveCheckpointReason Reason { get; internal set; }
    public bool IsMultiplayer { get; internal set; }
    public bool IsHost { get; internal set; }
}

/// <summary>Context supplied when a provider restores state.</summary>
public sealed class LoadProviderContext
{
    public string SlotId { get; internal set; } = string.Empty;
    public int SlotIndex { get; internal set; }
    public int SavedDataVersion { get; internal set; }
    public SaveLoadPhase Phase { get; internal set; }
    public bool IsMultiplayer { get; internal set; }
    public bool IsHost { get; internal set; }
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
    public string SlotId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public System.DateTime LastModified { get; set; }
    public bool IsValid { get; set; }
    public bool IsMultiplayer { get; set; }
    public string ModVersion { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public string Seed { get; set; } = string.Empty;
    public int SeedValue { get; set; }
    public int Layer { get; set; }
    public string LayerName { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public string ScreenshotPath { get; set; } = string.Empty;
    public string LastCheckpoint { get; set; } = string.Empty;
    public List<SaveModInfo> Mods { get; set; } = new();
    public List<string> RegisteredMods { get; set; } = new();
}

/// <summary>Identifies a BepInEx plugin recorded with a save slot.</summary>
public sealed class SaveModInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
