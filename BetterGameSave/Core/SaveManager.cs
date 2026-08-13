using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using BetterGameSave.API;
using BetterGameSave.Compatibility;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterGameSave.Core;

/// <summary>
/// Coordinates durable native saves and per-save data supplied by other mods.
/// </summary>
internal sealed class SaveManager
{
    private const string NativeSaveName = "save.sv";
    private const string CompanionSuffix = ".bettergamesave.dat";
    private const string BackupSuffix = ".bettergamesave.bak";

    private readonly ManualLogSource _log;
    private readonly KrokMpCompat _krokMpCompat;
    private readonly SaveSlotStore _slotStore;
    private readonly ItemSnapshotStore _itemSnapshotStore;
    private readonly Dictionary<string, ISaveDataProvider> _dataProviders = new(StringComparer.Ordinal);
    private readonly Stack<LoadContext> _loadContexts = new();
    private Dictionary<string, SaveSerializer.BinaryProviderData>? _loadedProviderData;
    private readonly ConfigEntry<bool> _timedCheckpoints;
    private readonly ConfigEntry<float> _checkpointIntervalMinutes;
    private readonly ConfigEntry<bool> _layerCheckpoints;
    private readonly ConfigEntry<bool> _exitCheckpoints;
    private readonly ConfigEntry<bool> _captureScreenshots;
    private string? _lastSavePath;
    private string? _currentSlotId;
    private string _pendingCheckpoint = "manual";
    private float _checkpointTimer;
    private int _saveDepth;
    private bool _saveTransactionFailed;
    private bool _checkpointInProgress;
    private bool _snapshotRestorePending;

    public bool IsMultiplayerActive => _krokMpCompat.IsMultiplayerSession();

    /// <summary>Whether the local player may write saves right now (host or offline).</summary>
    internal bool CanWriteSaves => !IsMultiplayerActive || _krokMpCompat.IsHost();

    /// <summary>Whether save slots may be loaded right now (never during multiplayer).</summary>
    internal bool CanLoadSaves => !IsMultiplayerActive;

    public event SaveEventHandler? OnPreSave;
    public event SaveEventHandler? OnPostSave;
    public event LoadEventHandler? OnPreLoad;
    public event LoadEventHandler? OnPostLoad;

    internal SaveManager(ConfigFile config, ManualLogSource log, KrokMpCompat krokMpCompat)
    {
        _log = log;
        _krokMpCompat = krokMpCompat;
        _slotStore = new SaveSlotStore();
        _itemSnapshotStore = new ItemSnapshotStore(log);
        _currentSlotId = _slotStore.GetSlots().FirstOrDefault()?.Id;
        _timedCheckpoints = config.Bind("Checkpoints", "TimedEnabled", true,
            "Create periodic checkpoints while a living run is active.");
        _checkpointIntervalMinutes = config.Bind("Checkpoints", "IntervalMinutes", 3f,
            "Minutes between timed checkpoints (minimum 0.5).");
        _layerCheckpoints = config.Bind("Checkpoints", "OnLayerTransition", true,
            "Create a checkpoint before changing layers.");
        _exitCheckpoints = config.Bind("Checkpoints", "OnExit", true,
            "Create a checkpoint before returning to the menu or quitting.");
        _captureScreenshots = config.Bind("Metadata", "CaptureScreenshot", true,
            "Capture a thumbnail screenshot whenever a slot is committed.");
    }

    internal void RegisterDataProvider(string modId, ISaveDataProvider provider)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("A non-empty mod ID is required.", nameof(modId));
        if (provider == null)
            throw new ArgumentNullException(nameof(provider));

        if (_dataProviders.ContainsKey(modId))
            throw new InvalidOperationException($"A save data provider is already registered for '{modId}'.");
        if (provider.DataVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(provider), "Provider DataVersion must be at least 1.");
        _dataProviders.Add(modId, provider);
        _log.LogInfo($"Save data provider registered: {modId} (schema {provider.DataVersion})");
    }

    internal void RegisterItem(string modId, string itemId, IItemSaveProvider provider)
    {
        _itemSnapshotStore.Register(modId, itemId, provider);
    }

    internal void UnregisterItems(string modId)
    {
        _itemSnapshotStore.Unregister(modId);
    }

    internal void UnregisterDataProvider(string modId)
    {
        if (_dataProviders.Remove(modId))
            _log.LogInfo($"Save data provider unregistered: {modId}");
    }

    internal void WriteNativeSave(string path, byte[] data)
    {
        if (!IsNativeSave(path))
        {
            File.WriteAllBytes(path, data);
            return;
        }

        EnsureCurrentSlot();
        AtomicWrite(path, data, keepBackup: true);
        _lastSavePath = Path.GetFullPath(path);
        SaveProviderData(path);
    }

    internal byte[] ReadNativeSave(string path)
    {
        if (!IsNativeSave(path))
            return File.ReadAllBytes(path);

        var fullPath = Path.GetFullPath(path);
        _lastSavePath = fullPath;
        SetCurrentLoad(new LoadContext(fullPath, useBackup: false));
        try
        {
            return ReadAndValidate(path);
        }
        catch (Exception primaryError)
        {
            var backupPath = GetBackupPath(path);
            if (!File.Exists(backupPath))
                throw;

            try
            {
                var backup = ReadAndValidate(backupPath);
                SetCurrentLoad(new LoadContext(fullPath, useBackup: true));
                _log.LogWarning($"Native save '{path}' is invalid; loading backup. {primaryError.Message}");
                return backup;
            }
            catch (Exception backupError)
            {
                throw new InvalidDataException(
                    $"Both the native save and its backup are invalid. Primary: {primaryError.Message}; Backup: {backupError.Message}",
                    backupError);
            }
        }
    }

    internal bool SuppressNativeDelete(string path)
    {
        if (!IsNativeSave(path))
            return false;

        _lastSavePath = Path.GetFullPath(path);
        _log.LogDebug($"Preserved loaded save: {path}");
        return true;
    }

    internal void BeginLoad()
    {
        if (_loadContexts.Count == 0)
        {
            _loadedProviderData = null;
            RaisePreLoad(CurrentSlotIndex());
        }
        _loadContexts.Push(default);
    }

    internal bool CompleteLoad()
    {
        if (_loadContexts.Count == 0)
            return false;

        var context = _loadContexts.Pop();
        var loaded = context.Path != null;
        if (loaded)
            LoadProviderData(context.Path!, context.UseBackup, SaveLoadPhase.NativeDataLoaded);

        if (_loadContexts.Count == 0 && loaded)
            RaisePostLoad(CurrentSlotIndex());
        return loaded;
    }

    private void LoadProviderData(string path, bool useBackup, SaveLoadPhase phase)
    {
        var companionPath = useBackup
            ? GetBackupPath(GetCompanionPath(path))
            : GetCompanionPath(path);

        Dictionary<string, SaveSerializer.BinaryProviderData> savedData;
        try
        {
            if (phase == SaveLoadPhase.WorldReady && _loadedProviderData != null)
            {
                savedData = _loadedProviderData;
            }
            else
            {
                if (!File.Exists(companionPath))
                    return;
                savedData = SaveSerializer.DeserializeProviders(File.ReadAllBytes(companionPath));
                _loadedProviderData = savedData;
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Could not read mod save data '{companionPath}': {ex}");
            return;
        }

        foreach (var entry in _dataProviders.ToArray())
        {
            if (!savedData.TryGetValue(entry.Key, out var providerData))
                continue;

            try
            {
                entry.Value.Load(CreateLoadProviderContext(providerData.DataVersion, phase), providerData.Data);
            }
            catch (Exception ex)
            {
                _log.LogError($"Save provider '{entry.Key}' failed while loading ({phase}): {ex}");
            }
        }
    }

    internal SaveSlotInfo GetCurrentSlotInfo()
    {
        var slots = GetAllSlots();
        var current = slots.FirstOrDefault(slot => slot.SlotId == _currentSlotId);
        return current ?? new SaveSlotInfo { Index = -1, Name = "No slot selected" };
    }

    internal IReadOnlyList<SaveSlotInfo> GetAllSlots()
    {
        return _slotStore.GetSlots()
            .Select((slot, index) => _slotStore.ToInfo(slot, index, _dataProviders.Keys.ToArray()))
            .ToArray();
    }

    internal SaveSlotInfo CreateSlot(string name)
    {
        var slot = _slotStore.Create(name);
        _currentSlotId = slot.Id;
        return GetAllSlots().First(info => info.SlotId == slot.Id);
    }

    internal bool SelectSlot(string slotId)
    {
        if (_slotStore.GetSlot(slotId) == null)
            return false;
        _currentSlotId = slotId;
        return true;
    }

    internal bool RenameSlot(string slotId, string newName)
    {
        try
        {
            _slotStore.Rename(slotId, newName);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError($"Could not rename save slot: {ex.Message}");
            return false;
        }
    }

    internal bool DeleteSlot(string slotId)
    {
        try
        {
            if (_slotStore.GetSlot(slotId) == null)
                return false;
            _slotStore.Delete(slotId);
            if (_currentSlotId == slotId)
                _currentSlotId = null;
            foreach (var entry in _dataProviders.ToArray())
            {
                try
                {
                    entry.Value.OnSlotDeleted(slotId);
                }
                catch (Exception ex)
                {
                    _log.LogError($"Save provider '{entry.Key}' failed after deleting slot '{slotId}': {ex}");
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError($"Could not delete save slot: {ex.Message}");
            return false;
        }
    }

    internal void BeginSave()
    {
        if (_saveDepth++ == 0)
        {
            _saveTransactionFailed = false;
            RaisePreSave(CurrentSlotIndex());
        }
    }

    internal void CompleteSave(bool succeeded)
    {
        if (_saveDepth == 0)
            return;
        if (!succeeded)
            _saveTransactionFailed = true;
        if (--_saveDepth != 0)
            return;
        if (_saveTransactionFailed)
        {
            _pendingCheckpoint = "manual";
            return;
        }

        try
        {
            EnsureCurrentSlot();
            _slotStore.Commit(_currentSlotId!, CaptureMetadata(_pendingCheckpoint));
            _checkpointTimer = 0f;
            _pendingCheckpoint = "manual";
            RaisePostSave(CurrentSlotIndex());
        }
        catch (Exception ex)
        {
            _pendingCheckpoint = "manual";
            _log.LogError($"Could not commit the current save slot: {ex}");
        }
    }

    internal bool RequestSave() => RequestCheckpoint("manual");

    internal bool RequestCheckpoint(string reason)
    {
        if (_checkpointInProgress || !CanCheckpoint())
            return false;
        if (IsMultiplayerActive && !_krokMpCompat.IsHost())
            return false;

        _checkpointInProgress = true;
        _pendingCheckpoint = reason;
        try
        {
            SaveSystem.SaveGame();
            return true;
        }
        catch (Exception ex)
        {
            _pendingCheckpoint = "manual";
            _log.LogError($"Checkpoint '{reason}' failed: {ex}");
            return false;
        }
        finally
        {
            _checkpointInProgress = false;
        }
    }

    internal void RequestLayerCheckpoint()
    {
        if (_layerCheckpoints.Value)
            RequestCheckpoint("layer");
    }

    internal void PrepareExitCheckpoint()
    {
        if (_exitCheckpoints.Value && CanCheckpoint() && (!IsMultiplayerActive || _krokMpCompat.IsHost()))
            _pendingCheckpoint = "exit";
    }

    internal void RequestExitCheckpoint()
    {
        if (_exitCheckpoints.Value)
            RequestCheckpoint("exit");
    }

    internal bool RequestLoad(int slotIndex)
    {
        var slots = GetAllSlots();
        return slotIndex >= 0 && slotIndex < slots.Count && RequestLoad(slots[slotIndex].SlotId);
    }

    internal bool RequestLoad(string slotId)
    {
        if (IsMultiplayerActive)
        {
            _log.LogWarning("A save slot cannot be switched while a KrokMP session is active.");
            return false;
        }
        if (_slotStore.GetSlot(slotId) == null)
            return false;

        try
        {
            _slotStore.Restore(slotId);
            _currentSlotId = slotId;
            _snapshotRestorePending = true;
            SaveSystem.loadedRun = true;

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrWhiteSpace(scene.name))
                throw new InvalidOperationException("The active game scene is not available.");

            _log.LogInfo($"Reloading scene '{scene.name}' for save slot '{slotId}'.");
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene.name);
            return true;
        }
        catch (Exception ex)
        {
            _snapshotRestorePending = false;
            _log.LogError($"Manual load failed: {ex}");
            return false;
        }
    }

    internal IEnumerator RestoreSnapshotsAfterWorldGeneration()
    {
        if (!_snapshotRestorePending || _currentSlotId == null)
            yield break;
        _snapshotRestorePending = false;

        yield return null;
        var worldData = _slotStore.ReadSnapshot(_currentSlotId, SaveSlotStore.WorldSnapshotFileName);
        var worldRestored = false;
        if (worldData is { Length: > 0 })
        {
            try
            {
                worldRestored = WorldSnapshotStore.Restore(worldData);
            }
            catch (Exception ex)
            {
                _log.LogError($"Could not restore world terrain snapshot: {ex}");
            }
        }
        if (worldRestored)
        {
            var refresh = WorldSnapshotStore.RefreshChunks();
            while (refresh.MoveNext())
                yield return refresh.Current;
            _log.LogInfo("World terrain snapshot restored.");
        }

        var itemData = _slotStore.ReadSnapshot(_currentSlotId, SaveSlotStore.ItemSnapshotFileName);
        if (itemData is { Length: > 0 })
        {
            try
            {
                _itemSnapshotStore.Restore(itemData, _currentSlotId, IsMultiplayerActive, !IsMultiplayerActive || _krokMpCompat.IsHost());
            }
            catch (Exception ex)
            {
                _log.LogError($"Could not restore registered item snapshot: {ex}");
            }
        }
        if (_lastSavePath != null)
            LoadProviderData(_lastSavePath, useBackup: false, SaveLoadPhase.WorldReady);
        _loadedProviderData = null;
    }

    internal void Tick()
    {
        if (!_timedCheckpoints.Value || !CanCheckpoint() ||
            (IsMultiplayerActive && !_krokMpCompat.IsHost()))
        {
            _checkpointTimer = 0f;
            return;
        }

        _checkpointTimer += Time.unscaledDeltaTime;
        var intervalSeconds = Mathf.Max(0.5f, _checkpointIntervalMinutes.Value) * 60f;
        if (_checkpointTimer >= intervalSeconds)
            RequestCheckpoint("timed");
    }

    private static bool CanCheckpoint()
    {
        var world = WorldGeneration.world;
        var camera = PlayerCamera.main;
        return world != null && world.worldExists && camera != null && camera.body != null && camera.body.alive;
    }

    internal void RaisePreSave(int slotIndex) => InvokeSafely(OnPreSave, slotIndex, "pre-save");
    internal void RaisePostSave(int slotIndex) => InvokeSafely(OnPostSave, slotIndex, "post-save");
    internal void RaisePreLoad(int slotIndex) => InvokeSafely(OnPreLoad, slotIndex, "pre-load");
    internal void RaisePostLoad(int slotIndex) => InvokeSafely(OnPostLoad, slotIndex, "post-load");

    private SaveSlotStore.SlotMetadata CaptureMetadata(string checkpoint)
    {
        var world = WorldGeneration.world;
        var metadata = new SaveSlotStore.SlotMetadata
        {
            GameVersion = Application.version ?? string.Empty,
            Layer = world != null ? world.totalTraveled + 1 : 0,
            LayerName = world != null ? world.TotalDepthString() : string.Empty,
            Character = CaptureCharacter(),
            LastCheckpoint = checkpoint,
            Mods = Chainloader.PluginInfos
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new SaveSlotStore.SlotModRecord
                {
                    Id = entry.Key,
                    Name = entry.Value.Metadata.Name ?? string.Empty,
                    Version = entry.Value.Metadata.Version?.ToString() ?? string.Empty,
                }).ToList(),
        };

        CaptureSeed(metadata);
        try
        {
            metadata.ItemSnapshot = _itemSnapshotStore.Capture(
                _currentSlotId ?? string.Empty,
                CreateSaveProviderContext().Reason);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Could not capture registered item snapshot: {ex.Message}");
        }
        try
        {
            metadata.WorldSnapshot = WorldSnapshotStore.Capture();
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Could not capture world terrain snapshot: {ex.Message}");
        }
        if (_captureScreenshots.Value)
            metadata.ScreenshotPng = CaptureScreenshot();
        return metadata;
    }

    private static string CaptureCharacter()
    {
        var info = WoundView.view?.cInfo;
        if (info == null || info.Length <= 2)
            return string.Empty;

        return info[2] switch
        {
            0 => "experiment",
            1 => "milky",
            2 => "dune",
            3 => "shelly",
            _ => info[2].ToString(),
        };
    }

    private static void CaptureSeed(SaveSlotStore.SlotMetadata metadata)
    {
        var seedManager = AccessTools.TypeByName("QoL_Unknown.SeedManager");
        if (seedManager == null)
        {
            metadata.Seed = "random";
            return;
        }

        metadata.Seed = AccessTools.Field(seedManager, "InputString")?.GetValue(null) as string ?? string.Empty;
        if (AccessTools.Field(seedManager, "CurrentSeed")?.GetValue(null) is int value)
            metadata.SeedValue = value;
        if (string.IsNullOrWhiteSpace(metadata.Seed))
            metadata.Seed = metadata.SeedValue != 0 ? metadata.SeedValue.ToString() : "random";
    }

    private byte[]? CaptureScreenshot()
    {
        try
        {
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null)
                return null;
            try
            {
                return ImageConversion.EncodeToPNG(texture);
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Could not capture save screenshot: {ex.Message}");
            return null;
        }
    }

    private SaveProviderContext CreateSaveProviderContext()
    {
        return new SaveProviderContext
        {
            SlotId = _currentSlotId ?? string.Empty,
            SlotIndex = CurrentSlotIndex(),
            Reason = _pendingCheckpoint switch
            {
                "manual" => SaveCheckpointReason.Manual,
                "timed" => SaveCheckpointReason.Timed,
                "layer" => SaveCheckpointReason.LayerTransition,
                "exit" => SaveCheckpointReason.Exit,
                _ => SaveCheckpointReason.Unknown,
            },
            IsMultiplayer = IsMultiplayerActive,
            IsHost = !IsMultiplayerActive || _krokMpCompat.IsHost(),
        };
    }

    private LoadProviderContext CreateLoadProviderContext(int dataVersion, SaveLoadPhase phase)
    {
        return new LoadProviderContext
        {
            SlotId = _currentSlotId ?? string.Empty,
            SlotIndex = CurrentSlotIndex(),
            SavedDataVersion = dataVersion,
            Phase = phase,
            IsMultiplayer = IsMultiplayerActive,
            IsHost = !IsMultiplayerActive || _krokMpCompat.IsHost(),
        };
    }

    private void SaveProviderData(string nativePath)
    {
        var companionPath = GetCompanionPath(nativePath);
        var providerData = new Dictionary<string, SaveSerializer.BinaryProviderData>(StringComparer.Ordinal);
        if (File.Exists(companionPath))
        {
            try
            {
                providerData = SaveSerializer.DeserializeProviders(File.ReadAllBytes(companionPath));
            }
            catch (Exception ex)
            {
                _log.LogWarning($"Could not preserve existing provider data from '{companionPath}': {ex.Message}");
            }
        }

        var context = CreateSaveProviderContext();
        foreach (var entry in _dataProviders.ToArray())
        {
            try
            {
                var data = entry.Value.Save(context);
                if (data is { Length: > 0 })
                {
                    providerData[entry.Key] = new SaveSerializer.BinaryProviderData
                    {
                        DataVersion = entry.Value.DataVersion,
                        Data = data,
                    };
                }
                else
                {
                    providerData.Remove(entry.Key);
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"Save provider '{entry.Key}' failed while saving: {ex}");
            }
        }

        try
        {
            AtomicWrite(GetCompanionPath(nativePath), SaveSerializer.SerializeProviders(providerData), keepBackup: true);
        }
        catch (Exception ex)
        {
            _log.LogError($"Could not write mod save data for '{nativePath}': {ex}");
        }
    }

    private static byte[] ReadAndValidate(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var json = SaveSystem.Unzip(bytes);
        Newtonsoft.Json.Linq.JObject.Parse(json);
        return bytes;
    }

    private static void AtomicWrite(string path, byte[] data, bool keepBackup)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".tmp";
        File.WriteAllBytes(tempPath, data);
        try
        {
            if (!File.Exists(fullPath))
            {
                File.Move(tempPath, fullPath);
                return;
            }

            var backupPath = keepBackup ? GetBackupPath(fullPath) : null;
            File.Replace(tempPath, fullPath, backupPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private void EnsureCurrentSlot()
    {
        if (_currentSlotId != null && _slotStore.GetSlot(_currentSlotId) != null)
            return;
        _currentSlotId = _slotStore.Create("Default").Id;
    }

    private int CurrentSlotIndex()
    {
        var slots = GetAllSlots();
        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index].SlotId == _currentSlotId)
                return index;
        }
        return -1;
    }

    private void InvokeSafely(Delegate? handlers, int slotIndex, string phase)
    {
        if (handlers == null)
            return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                handler.DynamicInvoke(slotIndex);
            }
            catch (Exception ex)
            {
                _log.LogError($"A save API event handler failed during {phase}: {ex}");
            }
        }
    }

    private void SetCurrentLoad(LoadContext context)
    {
        if (_loadContexts.Count > 0)
            _loadContexts.Pop();
        _loadContexts.Push(context);
    }

    private static bool IsNativeSave(string path) =>
        string.Equals(Path.GetFileName(path), NativeSaveName, StringComparison.OrdinalIgnoreCase);

    private static string GetCompanionPath(string nativePath) => nativePath + CompanionSuffix;
    private static string GetBackupPath(string nativePath) => nativePath + BackupSuffix;

    private readonly struct LoadContext
    {
        internal LoadContext(string path, bool useBackup)
        {
            Path = path;
            UseBackup = useBackup;
        }

        internal string? Path { get; }
        internal bool UseBackup { get; }
    }
}
