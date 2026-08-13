using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGameSave.API;
using Newtonsoft.Json;
using UnityEngine;

namespace BetterGameSave.Core;

internal sealed class SaveSlotStore
{
    private const string MetadataFileName = "slot.json";
    private const string ContentDirectoryName = "content";
    private const string NativeSaveName = "save.sv";
    private const string SlotGameSaveName = "game.bgs";
    private const string TransitionalNativeSaveName = "native.sv";
    private const string ProviderDataFileName = "providers.bgs";
    private const string MultiplayerDirectoryName = "mp_save";
    private const string ScreenshotFileName = "screenshot.png";
    internal const string WorldSnapshotFileName = "world.bgs";
    internal const string ItemSnapshotFileName = "items.bgs";
    internal const string PlayerSnapshotFileName = "player.bgs";

    private readonly string _rootPath;

    internal SaveSlotStore()
    {
        _rootPath = Path.Combine(Application.persistentDataPath, "BetterGameSave", "slots");
        Directory.CreateDirectory(_rootPath);
    }

    internal IReadOnlyList<SlotRecord> GetSlots() => Directory.GetDirectories(_rootPath)
        .Select(TryReadSlot)
        .Where(slot => slot != null)
        .Cast<SlotRecord>()
        .OrderByDescending(slot => slot.UpdatedAtUtc)
        .ThenBy(slot => slot.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal SlotRecord? GetSlot(string slotId)
    {
        if (!IsValidSlotId(slotId))
            return null;
        return TryReadSlot(GetSlotPath(slotId));
    }

    internal SlotRecord Create(string name)
    {
        var normalizedName = ValidateName(name);
        EnsureUniqueName(normalizedName, null);

        var now = DateTime.UtcNow;
        var slot = new SlotRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = normalizedName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var slotPath = GetSlotPath(slot.Id);
        Directory.CreateDirectory(Path.Combine(slotPath, ContentDirectoryName));
        WriteMetadata(slotPath, slot);
        return slot;
    }

    internal SlotRecord Rename(string slotId, string name)
    {
        var slot = GetRequiredSlot(slotId);
        var normalizedName = ValidateName(name);
        EnsureUniqueName(normalizedName, slotId);
        slot.Name = normalizedName;
        slot.UpdatedAtUtc = DateTime.UtcNow;
        WriteMetadata(GetSlotPath(slot.Id), slot);
        return slot;
    }

    internal void Delete(string slotId)
    {
        var slot = GetRequiredSlot(slotId);
        Directory.Delete(GetSlotPath(slot.Id), recursive: true);
    }

    internal SlotRecord Commit(string slotId, SlotMetadata metadata)
    {
        var slot = GetRequiredSlot(slotId);
        var slotPath = GetSlotPath(slot.Id);
        var contentPath = Path.Combine(slotPath, ContentDirectoryName);
        var temporaryPath = contentPath + ".tmp";
        var backupPath = contentPath + ".previous";

        DeleteDirectory(temporaryPath);
        Directory.CreateDirectory(temporaryPath);
        CopyLiveDataTo(temporaryPath);
        if (metadata.WorldSnapshot is { Length: > 0 })
            File.WriteAllBytes(Path.Combine(temporaryPath, WorldSnapshotFileName), metadata.WorldSnapshot);
        if (metadata.ItemSnapshot is { Length: > 0 })
            File.WriteAllBytes(Path.Combine(temporaryPath, ItemSnapshotFileName), metadata.ItemSnapshot);
        if (metadata.PlayerSnapshot is { Length: > 0 })
            File.WriteAllBytes(Path.Combine(temporaryPath, PlayerSnapshotFileName), metadata.PlayerSnapshot);

        ReplaceDirectory(contentPath, temporaryPath, backupPath);
        slot.UpdatedAtUtc = DateTime.UtcNow;
        slot.IsMultiplayer = Directory.Exists(Path.Combine(contentPath, MultiplayerDirectoryName));
        slot.GameVersion = metadata.GameVersion;
        slot.Seed = metadata.Seed;
        slot.SeedValue = metadata.SeedValue;
        slot.Layer = metadata.Layer;
        slot.LayerName = metadata.LayerName;
        slot.Character = metadata.Character;
        slot.LastCheckpoint = metadata.LastCheckpoint;
        slot.Mods = metadata.Mods;
        if (metadata.ScreenshotPng is { Length: > 0 })
        {
            var screenshotPath = Path.Combine(slotPath, ScreenshotFileName);
            File.WriteAllBytes(screenshotPath + ".tmp", metadata.ScreenshotPng);
            if (File.Exists(screenshotPath))
                File.Replace(screenshotPath + ".tmp", screenshotPath, null);
            else
                File.Move(screenshotPath + ".tmp", screenshotPath);
            slot.ScreenshotFile = ScreenshotFileName;
        }
        WriteMetadata(slotPath, slot);
        return slot;
    }

    internal SlotRecord Restore(string slotId)
    {
        var slot = GetRequiredSlot(slotId);
        var contentPath = Path.Combine(GetSlotPath(slot.Id), ContentDirectoryName);
        if (!Directory.Exists(contentPath))
            throw new InvalidDataException($"Save slot '{slot.Name}' has no content.");

        var stagingPath = Path.Combine(Application.persistentDataPath, ".bettergamesave.restore.tmp");
        var rollbackPath = Path.Combine(Application.persistentDataPath, ".bettergamesave.restore.rollback");
        DeleteDirectory(stagingPath);
        DeleteDirectory(rollbackPath);
        Directory.CreateDirectory(stagingPath);
        CopyDirectory(contentPath, stagingPath);

        try
        {
            CaptureLiveData(rollbackPath);
            DeleteLiveData();
            CopyLiveDataFrom(stagingPath, Application.persistentDataPath);
        }
        catch
        {
            DeleteLiveData();
            if (Directory.Exists(rollbackPath))
                CopyDirectoryContents(rollbackPath, Application.persistentDataPath);
            throw;
        }
        finally
        {
            DeleteDirectory(stagingPath);
            DeleteDirectory(rollbackPath);
        }

        return slot;
    }

    internal byte[]? ReadSnapshot(string slotId, string fileName)
    {
        if (GetSlot(slotId) == null || (fileName != WorldSnapshotFileName && fileName != ItemSnapshotFileName && fileName != PlayerSnapshotFileName))
            return null;
        var path = Path.Combine(GetSlotPath(slotId), ContentDirectoryName, fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    internal SaveSlotInfo ToInfo(SlotRecord slot, int index, IReadOnlyCollection<string> registeredMods)
    {
        var contentPath = Path.Combine(GetSlotPath(slot.Id), ContentDirectoryName);
        var saveFiles = Directory.Exists(contentPath)
            ? new[]
            {
                Path.Combine(contentPath, SlotGameSaveName),
                Path.Combine(contentPath, TransitionalNativeSaveName),
                Path.Combine(contentPath, NativeSaveName),
            }.Where(File.Exists).ToArray()
            : Array.Empty<string>();
        return new SaveSlotInfo
        {
            Index = index,
            SlotId = slot.Id,
            Name = slot.Name,
            FilePath = contentPath,
            FileSizeBytes = DirectorySize(contentPath),
            LastModified = slot.UpdatedAtUtc.ToLocalTime(),
            IsValid = saveFiles.Length > 0 && saveFiles.All(IsValidSave),
            IsMultiplayer = slot.IsMultiplayer,
            ModVersion = Plugin.ModVersion,
            GameVersion = slot.GameVersion,
            Seed = slot.Seed,
            SeedValue = slot.SeedValue,
            Layer = slot.Layer,
            LayerName = slot.LayerName,
            Character = slot.Character,
            ScreenshotPath = string.IsNullOrEmpty(slot.ScreenshotFile)
                ? string.Empty
                : Path.Combine(GetSlotPath(slot.Id), slot.ScreenshotFile),
            LastCheckpoint = slot.LastCheckpoint,
            Mods = slot.Mods.Select(mod => new SaveModInfo
            {
                Id = mod.Id,
                Name = mod.Name,
                Version = mod.Version,
            }).ToList(),
            RegisteredMods = registeredMods.ToList(),
        };
    }

    private void CopyLiveDataTo(string destination)
    {
        var nativePath = Path.Combine(Application.persistentDataPath, NativeSaveName);
        if (File.Exists(nativePath))
            File.Copy(nativePath, Path.Combine(destination, SlotGameSaveName), overwrite: true);

        var providerPath = nativePath + ".bettergamesave.dat";
        if (File.Exists(providerPath))
            File.Copy(providerPath, Path.Combine(destination, ProviderDataFileName), overwrite: true);

        var multiplayerPath = Path.Combine(Application.persistentDataPath, MultiplayerDirectoryName);
        if (Directory.Exists(multiplayerPath))
            CopyDirectory(multiplayerPath, Path.Combine(destination, MultiplayerDirectoryName));
    }

    private void CaptureLiveData(string destination)
    {
        Directory.CreateDirectory(destination);
        CopyLiveDataTo(destination);
    }

    private static void CopyLiveDataFrom(string source, string destination)
    {
        var slotGamePath = Path.Combine(source, SlotGameSaveName);
        var transitionalPath = Path.Combine(source, TransitionalNativeSaveName);
        var legacyNativePath = Path.Combine(source, NativeSaveName);
        var nativePath = File.Exists(slotGamePath)
            ? slotGamePath
            : File.Exists(transitionalPath) ? transitionalPath : legacyNativePath;
        if (File.Exists(nativePath))
            File.Copy(nativePath, Path.Combine(destination, NativeSaveName), overwrite: true);

        var providerPath = Path.Combine(source, ProviderDataFileName);
        if (File.Exists(providerPath))
            File.Copy(providerPath, Path.Combine(destination, NativeSaveName + ".bettergamesave.dat"), overwrite: true);

        var multiplayerPath = Path.Combine(source, MultiplayerDirectoryName);
        if (Directory.Exists(multiplayerPath))
            CopyDirectory(multiplayerPath, Path.Combine(destination, MultiplayerDirectoryName));
    }

    private static void DeleteLiveData()
    {
        foreach (var file in Directory.GetFiles(Application.persistentDataPath, NativeSaveName + "*", SearchOption.TopDirectoryOnly))
            File.Delete(file);
        DeleteDirectory(Path.Combine(Application.persistentDataPath, MultiplayerDirectoryName));
    }

    private static void ReplaceDirectory(string currentPath, string temporaryPath, string backupPath)
    {
        DeleteDirectory(backupPath);
        if (Directory.Exists(currentPath))
            Directory.Move(currentPath, backupPath);
        try
        {
            Directory.Move(temporaryPath, currentPath);
            DeleteDirectory(backupPath);
        }
        catch
        {
            DeleteDirectory(currentPath);
            if (Directory.Exists(backupPath))
                Directory.Move(backupPath, currentPath);
            throw;
        }
    }

    private SlotRecord GetRequiredSlot(string slotId) =>
        GetSlot(slotId) ?? throw new ArgumentException("Unknown save slot ID.", nameof(slotId));

    private SlotRecord? TryReadSlot(string slotPath)
    {
        try
        {
            var metadataPath = Path.Combine(slotPath, MetadataFileName);
            if (!File.Exists(metadataPath))
                return null;
            var slot = JsonConvert.DeserializeObject<SlotRecord>(File.ReadAllText(metadataPath));
            return slot != null && IsValidSlotId(slot.Id) ? slot : null;
        }
        catch
        {
            return null;
        }
    }

    private void WriteMetadata(string slotPath, SlotRecord slot)
    {
        Directory.CreateDirectory(slotPath);
        var metadataPath = Path.Combine(slotPath, MetadataFileName);
        var temporaryPath = metadataPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(slot, Formatting.Indented));
        if (File.Exists(metadataPath))
            File.Replace(temporaryPath, metadataPath, null);
        else
            File.Move(temporaryPath, metadataPath);
    }

    private void EnsureUniqueName(string name, string? exceptSlotId)
    {
        if (GetSlots().Any(slot => slot.Id != exceptSlotId && string.Equals(slot.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"A save slot named '{name}' already exists.", nameof(name));
    }

    private static string ValidateName(string name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > 64)
            throw new ArgumentException("Save name must contain 1 to 64 characters.", nameof(name));
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Save name cannot contain control characters.", nameof(name));
        return normalized;
    }

    private string GetSlotPath(string slotId) => Path.Combine(_rootPath, slotId);

    private static bool IsValidSlotId(string slotId) =>
        Guid.TryParseExact(slotId, "N", out _);

    private static bool IsValidSave(string path)
    {
        try
        {
            Newtonsoft.Json.Linq.JObject.Parse(SaveSystem.Unzip(File.ReadAllBytes(path)));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long DirectorySize(string path) => Directory.Exists(path)
        ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
        : 0;

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        CopyDirectoryContents(source, destination);
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
            var targetPath = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    internal sealed class SlotRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public bool IsMultiplayer { get; set; }
        public string GameVersion { get; set; } = string.Empty;
        public string Seed { get; set; } = string.Empty;
        public int SeedValue { get; set; }
        public int Layer { get; set; }
        public string LayerName { get; set; } = string.Empty;
        public string Character { get; set; } = string.Empty;
        public string ScreenshotFile { get; set; } = string.Empty;
        public string LastCheckpoint { get; set; } = string.Empty;
        public List<SlotModRecord> Mods { get; set; } = new();
    }

    internal sealed class SlotMetadata
    {
        internal string GameVersion { get; set; } = string.Empty;
        internal string Seed { get; set; } = string.Empty;
        internal int SeedValue { get; set; }
        internal int Layer { get; set; }
        internal string LayerName { get; set; } = string.Empty;
        internal string Character { get; set; } = string.Empty;
        internal string LastCheckpoint { get; set; } = string.Empty;
        internal List<SlotModRecord> Mods { get; set; } = new();
        internal byte[]? ScreenshotPng { get; set; }
        internal byte[]? WorldSnapshot { get; set; }
        internal byte[]? ItemSnapshot { get; set; }
        internal byte[]? PlayerSnapshot { get; set; }
    }

    internal sealed class SlotModRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}
