using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BepInEx.Logging;
using BetterGameSave.API;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace BetterGameSave.Core;

internal sealed class ItemSnapshotStore
{
    private const int Magic = 0x42495331; // BIS1
    private const int FormatVersion = 1;
    private readonly Dictionary<string, ItemRegistration> _registrations = new(StringComparer.Ordinal);
    private readonly ManualLogSource _log;

    internal ItemSnapshotStore(ManualLogSource log)
    {
        _log = log;
    }

    internal void Register(string modId, string itemId, IItemSaveProvider provider)
    {
        if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("Both modId and itemId are required.");
        if (provider == null)
            throw new ArgumentNullException(nameof(provider));
        var key = itemId.Trim();
        if (_registrations.ContainsKey(key))
            throw new InvalidOperationException($"Item '{key}' is already registered.");
        _registrations.Add(key, new ItemRegistration(modId.Trim(), key, provider));
        _log.LogInfo($"Item save provider registered: {key} ({modId})");
    }

    internal void Unregister(string modId)
    {
        foreach (var key in _registrations.Where(entry => entry.Value.ModId == modId).Select(entry => entry.Key).ToArray())
            _registrations.Remove(key);
    }

    internal IReadOnlyCollection<string> RegisteredIds => _registrations.Keys.ToArray();

    internal byte[]? Capture(string slotId, SaveCheckpointReason reason)
    {
        var body = PlayerCamera.main?.body;
        if (body == null || Item.allItems == null)
            return null;

        var playerItems = new HashSet<Item>(GetPlayerItems(body));
        var records = new List<ItemRecord>();
        var keys = new Dictionary<Item, string>();
        var occurrence = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in Item.allItems.ToArray())
        {
            if (item == null || playerItems.Contains(item) || !_registrations.TryGetValue(item.id, out var registration))
                continue;
            var index = occurrence.TryGetValue(item.id, out var count) ? count : 0;
            occurrence[item.id] = index + 1;
            var key = item.id + "#" + index;
            keys[item] = key;
            var parent = GetParentItem(item);
            byte[]? data = null;
            try
            {
                data = registration.Provider.Capture(item, new ItemSaveContext
                {
                    SlotId = slotId,
                    Reason = reason,
                });
            }
            catch (Exception ex)
            {
                _log.LogError($"Item provider '{registration.ModId}' failed while capturing '{item.id}': {ex}");
            }
            records.Add(new ItemRecord
            {
                ModId = registration.ModId,
                ItemId = item.id,
                ItemKey = key,
                ParentItemKey = parent != null && keys.TryGetValue(parent, out var parentKey) ? parentKey : string.Empty,
                Position = item.transform.position,
                Rotation = item.transform.eulerAngles.z,
                Scale = item.transform.localScale,
                Condition = item.condition,
                Favourited = item.favourited,
                ProviderData = data,
            });
        }

        if (records.Count == 0)
            return null;
        var json = JsonConvert.SerializeObject(new Snapshot { Version = FormatVersion, Records = records });
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        return output.ToArray();
    }

    internal void Restore(byte[] data, string slotId, bool isMultiplayer, bool isHost)
    {
        var snapshot = Read(data);
        if (snapshot == null || Item.allItems == null)
            return;

        var records = snapshot.Records.Where(record => _registrations.ContainsKey(record.ItemId)).ToList();
        foreach (var item in Item.allItems.ToArray())
        {
            if (item != null && records.Any(record => record.ItemId == item.id) && !GetPlayerItems(PlayerCamera.main.body).Contains(item))
                UnityEngine.Object.Destroy(item.gameObject);
        }

        var created = new Dictionary<string, Item>(StringComparer.Ordinal);
        var pending = records.ToDictionary(record => record.ItemKey, StringComparer.Ordinal);
        var context = new ItemLoadContext { SlotId = slotId, IsMultiplayer = isMultiplayer, IsHost = isHost };
        for (var pass = 0; pending.Count > 0 && pass <= records.Count; pass++)
        {
            foreach (var record in pending.Values.ToArray())
            {
                if (!string.IsNullOrEmpty(record.ParentItemKey) && !created.ContainsKey(record.ParentItemKey))
                    continue;
                if (!_registrations.TryGetValue(record.ItemId, out var registration))
                {
                    pending.Remove(record.ItemKey);
                    continue;
                }
                try
                {
                    var apiRecord = ToApiRecord(record);
                    var item = registration.Provider.Create(apiRecord, context);
                    if (item == null)
                        continue;
                    item.condition = record.Condition;
                    item.favourited = record.Favourited;
                    item.transform.position = record.Position;
                    item.transform.eulerAngles = new Vector3(0f, 0f, record.Rotation);
                    item.transform.localScale = record.Scale;
                    if (!string.IsNullOrEmpty(record.ParentItemKey) && created.TryGetValue(record.ParentItemKey, out var parent))
                    {
                        var container = parent.GetComponent<Container>();
                        if (container != null)
                            container.LoadItem(item);
                    }
                    registration.Provider.Restore(item, apiRecord, context, record.ProviderData);
                    created[record.ItemKey] = item;
                    pending.Remove(record.ItemKey);
                }
                catch (Exception ex)
                {
                    _log.LogError($"Item provider '{registration.ModId}' failed while restoring '{record.ItemId}': {ex}");
                    pending.Remove(record.ItemKey);
                }
            }
        }
        if (pending.Count > 0)
            _log.LogWarning($"Could not restore {pending.Count} registered item(s); their parent container or provider was unavailable.");
    }

    private static Snapshot? Read(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);
        if (reader.ReadInt32() != Magic || reader.ReadInt32() != FormatVersion)
            throw new InvalidDataException("Unsupported item snapshot format.");
        var length = reader.ReadInt32();
        if (length < 0 || length > 128 * 1024 * 1024)
            throw new InvalidDataException("Invalid item snapshot length.");
        var json = reader.ReadBytes(length);
        if (json.Length != length)
            throw new EndOfStreamException("Item snapshot ended unexpectedly.");
        return JsonConvert.DeserializeObject<Snapshot>(System.Text.Encoding.UTF8.GetString(json));
    }

    private static ItemSaveRecord ToApiRecord(ItemRecord record) => new()
    {
        ItemId = record.ItemId,
        ItemKey = record.ItemKey,
        ParentItemKey = record.ParentItemKey,
        Location = string.IsNullOrEmpty(record.ParentItemKey) ? ItemSaveLocation.World : ItemSaveLocation.Container,
        Position = record.Position,
        Rotation = record.Rotation,
        Scale = record.Scale,
        Condition = record.Condition,
        Favourited = record.Favourited,
        ProviderData = record.ProviderData,
    };

    private static IEnumerable<Item> GetPlayerItems(Body body)
    {
        var items = new HashSet<Item>();
        for (var index = 0; index < body.slots.Length; index++)
            AddItemTree(body.GetItem(index), items);
        foreach (var item in body.GetAllWearables())
            AddItemTree(item, items);
        return items;
    }

    private static void AddItemTree(Item? item, ISet<Item> items)
    {
        if (item == null || !items.Add(item))
            return;
        foreach (var child in item.GetComponentsInChildren<Item>(includeInactive: true))
            if (child != item)
                AddItemTree(child, items);
    }

    private static Item? GetParentItem(Item item)
    {
        var container = item.container;
        return container == null ? null : AccessTools.Field(typeof(Container), "mItem")?.GetValue(container) as Item;
    }

    private sealed class ItemRegistration
    {
        internal ItemRegistration(string modId, string itemId, IItemSaveProvider provider)
        {
            ModId = modId;
            ItemId = itemId;
            Provider = provider;
        }
        internal string ModId { get; }
        internal string ItemId { get; }
        internal IItemSaveProvider Provider { get; }
    }

    [Serializable]
    private sealed class Snapshot
    {
        public int Version { get; set; }
        public List<ItemRecord> Records { get; set; } = new();
    }

    [Serializable]
    private sealed class ItemRecord
    {
        public string ModId { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string ItemKey { get; set; } = string.Empty;
        public string ParentItemKey { get; set; } = string.Empty;
        public Vector2 Position { get; set; }
        public float Rotation { get; set; }
        public Vector2 Scale { get; set; }
        public float Condition { get; set; }
        public bool Favourited { get; set; }
        public byte[]? ProviderData { get; set; }
    }
}
