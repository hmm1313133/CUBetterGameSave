using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterGameSave.API;

/// <summary>Registers a custom item ID so BetterGameSave can preserve its world placement.</summary>
public interface IItemSaveProvider
{
    /// <summary>Called for each registered item found in the world before it is saved.</summary>
    byte[]? Capture(Item item, ItemSaveContext context);

    /// <summary>
    /// Creates one world item from a saved record. Return null when the item cannot be created yet.
    /// The returned item is subsequently placed by BetterGameSave.
    /// </summary>
    Item? Create(ItemSaveRecord record, ItemLoadContext context);

    /// <summary>Restores provider-owned state onto an already-created item.</summary>
    void Restore(Item item, ItemSaveRecord record, ItemLoadContext context, byte[]? data);
}

public enum ItemSaveLocation
{
    World,
    PlayerSlot,
    Wearable,
    Container,
}

public sealed class ItemSaveContext
{
    public string SlotId { get; internal set; } = string.Empty;
    public SaveCheckpointReason Reason { get; internal set; }
}

public sealed class ItemLoadContext
{
    public string SlotId { get; internal set; } = string.Empty;
    public bool IsMultiplayer { get; internal set; }
    public bool IsHost { get; internal set; }
}

/// <summary>Opaque record passed to an item provider during restore.</summary>
public sealed class ItemSaveRecord
{
    public string ItemId { get; internal set; } = string.Empty;
    public ItemSaveLocation Location { get; internal set; }
    public Vector2 Position { get; internal set; }
    public float Rotation { get; internal set; }
    public Vector2 Scale { get; internal set; }
    public float Condition { get; internal set; }
    public bool Favourited { get; internal set; }
    public string ParentItemKey { get; internal set; } = string.Empty;
    public string ItemKey { get; internal set; } = string.Empty;
    public byte[]? ProviderData { get; internal set; }
}
