using System;
using System.Collections.Generic;
using System.IO;
using BetterGameSave.API;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BetterGameSave.UI;

/// <summary>
/// In-game save management panel built with the game's own UI style.
/// Driven by Harmony patches on PlayerCamera.Start / PlayerCamera.Update
/// (the same mechanism QoL uses, which is proven to work in-game).
/// Attaches to the pause menu canvas and offers an unlimited, scrollable
/// list of named save slots with load / save / rename / delete actions.
/// </summary>
internal static class SaveMenuUI
{
    private const float MaxPanelWidth = 640f;
    private const float MaxPanelHeight = 470f;
    private const float PanelGap = 24f;
    private const float ConfirmTimeoutSeconds = 5f;

    private static float _panelWidth = MaxPanelWidth;
    private static float _panelHeight = MaxPanelHeight;

    private static float BodySize => GameUiResources.BodyFontSize;
    private static float RowHeight => Mathf.Max(64f, BodySize * 2f + 30f);
    private static float ButtonHeight => Mathf.Max(26f, BodySize + 10f);
    private static float ButtonWidth => Mathf.Max(54f, BodySize * 3.2f);

    private static readonly Color PanelColor = new(0f, 0f, 0f, 0.92f);
    private static readonly Color RowColor = new(0f, 0f, 0f, 0.5f);
    private static readonly Color ButtonColor = new(0.16f, 0.16f, 0.16f, 1f);
    private static readonly Color ButtonHighlight = new(0.32f, 0.32f, 0.32f, 1f);
    private static readonly Color ButtonPressed = new(0.08f, 0.08f, 0.08f, 1f);
    private static readonly Color ButtonDisabled = new(0.16f, 0.16f, 0.16f, 0.45f);
    private static readonly Color ConfirmColor = new(0.55f, 0.1f, 0.1f, 1f);
    private static readonly Color HeaderColor = Color.yellow;
    private static readonly Color TextColor = Color.white;
    private static readonly Color SubTextColor = new(0.62f, 0.62f, 0.62f, 1f);

    private static bool _initialized;
    private static GameObject? _panel;
    private static RectTransform? _panelRect;
    private static RectTransform? _slotContainer;
    private static TextMeshProUGUI? _statusText;
    private static TMP_InputField? _newSlotInput;
    private static Button? _createButton;
    private static GameObject? _pauseMenuRef;
    private static PauseHandler? _pauseHandler;
    private static bool _loggedShow;
    private static float _nextResolveRetry;
    private static string? _pendingSlotId;
    private static PendingAction _pendingAction;
    private static float _pendingSince;
    private static readonly Dictionary<string, Sprite> ScreenshotCache = new(StringComparer.Ordinal);

    private enum PendingAction { None, Load, Save, Delete }

    /// <summary>Called once from Plugin.Awake. Registers scene cleanup.</summary>
    internal static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        SceneManager.sceneUnloaded += _ => ResetSceneState();
    }

    private static void ResetSceneState()
    {
        _panel = null;
        _panelRect = null;
        _slotContainer = null;
        _statusText = null;
        _newSlotInput = null;
        _createButton = null;
        _pauseMenuRef = null;
        _pauseHandler = null;
        _loggedShow = false;
        ClearPending();
    }

    // ------------------------------------------------------------------
    // Harmony entry points (see Patches/SaveMenuUiPatches)
    // ------------------------------------------------------------------

    internal static void OnPlayerCameraStart(PlayerCamera camera)
    {
        try
        {
            var menu = FindPauseMenu(camera);
            if (menu == null)
            {
                Plugin.Log.LogWarning("Save UI: PauseMenu not found under the player camera; save panel disabled for this scene.");
                return;
            }
            _pauseMenuRef = menu.gameObject;
            _pauseHandler = menu.GetComponent<PauseHandler>() ?? PauseHandler.main;
            Plugin.Log.LogInfo($"Save UI: pause menu located at '{GetPath(menu)}'.");
            BuildPanel();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Save UI: could not build the save panel: {ex}");
        }
    }

    internal static void OnPlayerCameraUpdate(PlayerCamera camera)
    {
        // Mirror QoL: if the pause menu was not resolved in Start (e.g. the
        // camera was created before our patches applied), keep retrying.
        if (_pauseMenuRef == null)
        {
            if (Time.unscaledTime < _nextResolveRetry)
                return;
            _nextResolveRetry = Time.unscaledTime + 1f;
            OnPlayerCameraStart(camera);
            return;
        }
        if (_panel == null || _panelRect == null)
            return;

        var handler = _pauseHandler != null ? _pauseHandler : PauseHandler.main;
        var paused = handler != null
            ? handler.pauseProgress > 0f ||
              (handler.pauseContainer != null && handler.pauseContainer.activeSelf)
            : _pauseMenuRef.activeInHierarchy;
        var show = paused && SettingsMenu.instance == null;

        if (_panel.activeSelf != show)
        {
            _panel.SetActive(show);
            if (show)
            {
                if (!_loggedShow)
                {
                    _loggedShow = true;
                    Plugin.Log.LogInfo($"Save UI: panel shown at pos={_panelRect.anchoredPosition}.");
                }
                Refresh();
            }
        }
        if (!show)
            return;

        UpdatePanelTransform(handler);
        if (_pendingSlotId != null && Time.unscaledTime - _pendingSince > ConfirmTimeoutSeconds)
        {
            ClearPending();
            Refresh();
        }
    }

    private static Transform? FindPauseMenu(PlayerCamera camera)
    {
        var menu = camera.transform.Find("Canvas/PauseMenu");
        if (menu == null)
        {
            var byPath = GameObject.Find("Main Camera/Canvas/PauseMenu");
            if (byPath != null)
                menu = byPath.transform;
        }
        if (menu == null)
        {
            var canvas = camera.transform.Find("Canvas");
            if (canvas == null)
            {
                var canvasGo = GameObject.Find("Canvas");
                if (canvasGo != null)
                    canvas = canvasGo.transform;
            }
            menu = canvas != null ? canvas.Find("PauseMenu") : null;
        }
        return menu;
    }

    // ------------------------------------------------------------------
    // Panel construction
    // ------------------------------------------------------------------

    private static void BuildPanel()
    {
        if (_panel != null)
            UnityEngine.Object.Destroy(_panel);
        _slotContainer = null;
        _newSlotInput = null;
        ClearPending();

        var parent = FindCanvasTransform(_pauseMenuRef!.transform);
        if (parent == null)
        {
            Plugin.Log.LogWarning("Save UI: no Canvas found above the pause menu; panel cannot be built.");
            return;
        }

        _panel = CreateUiObject("BetterGameSavePanel", parent, typeof(Image));
        _panelRect = (RectTransform)_panel.transform;
        // Dock to the right edge of the canvas: always fully on screen.
        _panelRect.anchorMin = new Vector2(1f, 0.5f);
        _panelRect.anchorMax = new Vector2(1f, 0.5f);
        _panelRect.pivot = new Vector2(1f, 0.5f);

        // Fit the panel to the actual canvas size (pixel-art games often use
        // a much smaller reference resolution than the screen).
        var canvasSize = ((RectTransform)parent).rect.size;
        _panelWidth = canvasSize.x > 10f
            ? Mathf.Min(MaxPanelWidth, Mathf.Max(360f, canvasSize.x * 0.5f))
            : MaxPanelWidth;
        _panelHeight = canvasSize.y > 10f
            ? Mathf.Min(MaxPanelHeight, canvasSize.y - 32f)
            : MaxPanelHeight;
        _panelRect.sizeDelta = new Vector2(_panelWidth + 4f, _panelHeight + 4f);
        _panelRect.anchoredPosition = new Vector2(-PanelGap, 0f);
        _panelRect.SetAsLastSibling();

        // Light frame around the panel, matching the game's white-outline-on-black style.
        var frame = _panel.GetComponent<Image>();
        frame.sprite = GameUiResources.FindSpriteContains("uiBlockNano");
        if (frame.sprite != null)
            frame.type = Image.Type.Sliced;
        frame.color = new Color(1f, 1f, 1f, 0.22f);

        var inner = CreateUiObject("Inner", _panel.transform, typeof(Image));
        var innerRect = (RectTransform)inner.transform;
        Stretch(innerRect);
        innerRect.offsetMin = new Vector2(2f, 2f);
        innerRect.offsetMax = new Vector2(-2f, -2f);
        var background = inner.GetComponent<Image>();
        background.sprite = GameUiResources.FindSpriteContains("uiBlockNano");
        if (background.sprite != null)
            background.type = Image.Type.Sliced;
        background.color = PanelColor;

        var layout = inner.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 12, 12);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        BuildHeader(inner.transform);
        BuildScrollArea(inner.transform);
        BuildFooter(inner.transform);

        UpdatePanelTransform(_pauseHandler);
        _panel.SetActive(_pauseMenuRef!.activeInHierarchy);
        Plugin.Log.LogInfo($"Save UI: panel built under '{parent.name}'.");
    }

    private static void BuildHeader(Transform parent)
    {
        var header = CreateText(parent, "Header", SaveMenuLocale.Get("MenuTitle"),
            GameUiResources.TitleFontSize, HeaderColor);
        header.alignment = TextAlignmentOptions.MidlineLeft;
        var headerLayout = header.gameObject.AddComponent<LayoutElement>();
        headerLayout.minHeight = GameUiResources.TitleFontSize + 10f;
        headerLayout.preferredHeight = GameUiResources.TitleFontSize + 10f;

        _statusText = CreateText(parent, "Status", string.Empty, BodySize, SubTextColor);
        _statusText.alignment = TextAlignmentOptions.MidlineLeft;
        var statusLayout = _statusText.gameObject.AddComponent<LayoutElement>();
        statusLayout.minHeight = BodySize + 6f;
        statusLayout.preferredHeight = BodySize + 6f;
    }

    private static void BuildScrollArea(Transform parent)
    {
        var scrollRoot = CreateUiObject("SlotScroll", parent, typeof(Image), typeof(ScrollRect));
        var scrollLayout = scrollRoot.AddComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1f;
        scrollLayout.minHeight = 120f;
        var scrollBackground = scrollRoot.GetComponent<Image>();
        scrollBackground.sprite = GameUiResources.FindSpriteContains("uiBlockNano");
        if (scrollBackground.sprite != null)
            scrollBackground.type = Image.Type.Sliced;
        scrollBackground.color = new Color(0f, 0f, 0f, 0.35f);

        var viewport = CreateUiObject("Viewport", scrollRoot.transform, typeof(Image), typeof(RectMask2D));
        var viewportRect = (RectTransform)viewport.transform;
        Stretch(viewportRect);
        viewportRect.offsetMin = new Vector2(4f, 4f);
        viewportRect.offsetMax = new Vector2(-4f, -4f);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

        var content = CreateUiObject("Content", viewport.transform);
        _slotContainer = (RectTransform)content.transform;
        _slotContainer.anchorMin = new Vector2(0f, 1f);
        _slotContainer.anchorMax = new Vector2(1f, 1f);
        _slotContainer.pivot = new Vector2(0.5f, 1f);
        _slotContainer.anchoredPosition = Vector2.zero;
        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 6f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollRoot.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = _slotContainer;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
    }

    private static void BuildFooter(Transform parent)
    {
        var footer = CreateUiObject("Footer", parent);
        var footerLayout = footer.AddComponent<LayoutElement>();
        footerLayout.minHeight = ButtonHeight + 8f;
        footerLayout.preferredHeight = ButtonHeight + 8f;
        var row = footer.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;

        _newSlotInput = CreateInputField(footer.transform, SaveMenuLocale.Get("NewSlotPlaceholder"));
        var inputLayout = _newSlotInput.gameObject.AddComponent<LayoutElement>();
        inputLayout.flexibleWidth = 1f;
        inputLayout.minWidth = 200f;

        _createButton = CreateTextButton(footer.transform, SaveMenuLocale.Get("NewSlotButton"), ButtonWidth * 2.4f, () =>
        {
            var name = _newSlotInput != null ? _newSlotInput.text : string.Empty;
            CreateNewSlot(name);
        });
        _createButton.name = "NewSlotButton";
    }

    // ------------------------------------------------------------------
    // Slot list
    // ------------------------------------------------------------------

    private static void Refresh()
    {
        if (_slotContainer == null)
            return;

        foreach (Transform child in _slotContainer)
            UnityEngine.Object.Destroy(child.gameObject);
        PruneScreenshotCache();

        var manager = Plugin.Manager;
        var slots = manager.GetAllSlots();
        var current = manager.GetCurrentSlotInfo();
        var canWrite = manager.CanWriteSaves && CanSaveNow();
        var canLoad = manager.CanLoadSaves;

        if (_statusText != null)
        {
            var status = slots.Count == 0
                ? SaveMenuLocale.Get("NoSavesFound")
                : SaveMenuLocale.Format("CurrentSlotLabel", current.SlotId.Length > 0 ? current.Name : "-", slots.Count);
            if (manager.IsMultiplayerActive)
                status += "  " + SaveMenuLocale.Get("MultiplayerActive");
            _statusText.text = status;
        }

        foreach (var slot in slots)
            BuildSlotRow(slot, current.SlotId, canWrite, canLoad);

        if (_newSlotInput != null)
            _newSlotInput.interactable = canWrite;
        if (_createButton != null)
            _createButton.interactable = canWrite;
    }

    private static void BuildSlotRow(SaveSlotInfo slot, string currentSlotId, bool canWrite, bool canLoad)
    {
        var row = CreateUiObject("Slot_" + slot.SlotId, _slotContainer!, typeof(Image));
        var rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.minHeight = RowHeight;
        rowLayout.preferredHeight = RowHeight;
        var background = row.GetComponent<Image>();
        background.sprite = GameUiResources.FindSpriteContains("uiBlockNano");
        if (background.sprite != null)
            background.type = Image.Type.Sliced;
        background.color = RowColor;

        var horizontal = row.AddComponent<HorizontalLayoutGroup>();
        horizontal.padding = new RectOffset(6, 8, 6, 6);
        horizontal.spacing = 8f;
        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = false;
        horizontal.childForceExpandHeight = true;
        horizontal.childAlignment = TextAnchor.MiddleCenter;

        BuildThumbnail(row.transform, slot);
        BuildSlotInfo(row.transform, slot, currentSlotId == slot.SlotId);
        BuildSlotButtons(row.transform, slot, canWrite, canLoad);
    }

    private static void BuildThumbnail(Transform parent, SaveSlotInfo slot)
    {
        var thumbnail = CreateUiObject("Thumbnail", parent, typeof(Image));
        var layout = thumbnail.AddComponent<LayoutElement>();
        layout.minWidth = 88f;
        layout.preferredWidth = 88f;
        var image = thumbnail.GetComponent<Image>();
        image.preserveAspect = false;
        var sprite = GetScreenshot(slot);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
        }
        else
        {
            image.sprite = GameUiResources.FindSpriteContains("uiBlockNano");
            if (image.sprite != null)
                image.type = Image.Type.Sliced;
            image.color = new Color(1f, 1f, 1f, 0.06f);
        }
    }

    private static void BuildSlotInfo(Transform parent, SaveSlotInfo slot, bool isCurrent)
    {
        var hasContent = slot.FileSizeBytes > 0;

        var nameColor = isCurrent ? "yellow" : "white";
        var line1 = $"<b><color={nameColor}>{Escape(slot.Name)}</color></b>";
        if (isCurrent)
            line1 += $" <size=70%><color=yellow>[{SaveMenuLocale.Get("TagCurrent")}]</color></size>";
        if (slot.IsMultiplayer)
            line1 += $" <size=70%><color=#66ccff>[{SaveMenuLocale.Get("TagMultiplayer")}]</color></size>";
        if (hasContent && !slot.IsValid)
            line1 += $" <size=70%><color=#ff5555>[{SaveMenuLocale.Get("TagCorrupted")}]</color></size>";

        string line2;
        if (hasContent)
        {
            var depth = string.IsNullOrEmpty(slot.LayerName) ? "?" : slot.LayerName;
            var character = string.IsNullOrEmpty(slot.Character) ? "?" : slot.Character;
            var seed = string.IsNullOrEmpty(slot.Seed) || slot.Seed == "random"
                ? SaveMenuLocale.Get("SeedRandom")
                : slot.Seed;
            line2 = $"<size=75%><color=grey>{depth} · {character} · " +
                    $"{SaveMenuLocale.Get("SeedLabel")} {seed} · {slot.LastModified:g}</color></size>";
        }
        else
        {
            line2 = $"<size=75%><color=grey>{SaveMenuLocale.Get("EmptySlot")}</color></size>";
        }

        var info = CreateText(parent, "Info", line1 + "\n" + line2, BodySize, TextColor);
        info.alignment = TextAlignmentOptions.MidlineLeft;
        info.enableWordWrapping = false;
        info.overflowMode = TextOverflowModes.Ellipsis;
        var layout = info.gameObject.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.minWidth = 160f;
    }

    private static void BuildSlotButtons(Transform parent, SaveSlotInfo slot, bool canWrite, bool canLoad)
    {
        var hasContent = slot.FileSizeBytes > 0;

        var load = CreateTextButton(parent, SaveMenuLocale.Get("LoadButton"), ButtonWidth,
            () => ConfirmThen(slot.SlotId, PendingAction.Load, () => DoLoad(slot)));
        load.interactable = canLoad && hasContent;
        ApplyArmedState(load, slot.SlotId, PendingAction.Load);

        var save = CreateTextButton(parent, SaveMenuLocale.Get("SaveButton"), ButtonWidth,
            () => ConfirmThen(slot.SlotId, PendingAction.Save, () => DoSaveTo(slot)));
        save.interactable = canWrite;
        ApplyArmedState(save, slot.SlotId, PendingAction.Save);

        CreateTextButton(parent, SaveMenuLocale.Get("RenameButton"), ButtonWidth, () => BeginRename(slot));

        var delete = CreateTextButton(parent, SaveMenuLocale.Get("DeleteButton"), ButtonWidth,
            () => ConfirmThen(slot.SlotId, PendingAction.Delete, () => DoDelete(slot)));
        ApplyArmedState(delete, slot.SlotId, PendingAction.Delete);
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    private static void ConfirmThen(string slotId, PendingAction action, Action execute)
    {
        if (_pendingSlotId == slotId && _pendingAction == action)
        {
            ClearPending();
            execute();
        }
        else
        {
            _pendingSlotId = slotId;
            _pendingAction = action;
            _pendingSince = Time.unscaledTime;
            PlayClick();
            Refresh();
        }
    }

    private static void ClearPending()
    {
        _pendingSlotId = null;
        _pendingAction = PendingAction.None;
    }

    private static void DoLoad(SaveSlotInfo slot)
    {
        if (!Plugin.Manager.RequestLoad(slot.SlotId))
        {
            Alert(SaveMenuLocale.Format("ErrorLoadFailed", slot.Name));
            Refresh();
        }
    }

    private static void DoSaveTo(SaveSlotInfo slot)
    {
        var manager = Plugin.Manager;
        manager.SelectSlot(slot.SlotId);
        if (manager.RequestSave())
            Alert(SaveMenuLocale.Format("AlertSavedToSlot", slot.Name));
        else
            Alert(SaveMenuLocale.Format("ErrorSaveFailed", slot.Name));
        Refresh();
    }

    private static void DoDelete(SaveSlotInfo slot)
    {
        Plugin.Manager.DeleteSlot(slot.SlotId);
        Refresh();
    }

    private static void CreateNewSlot(string name)
    {
        var manager = Plugin.Manager;
        if (string.IsNullOrWhiteSpace(name))
            name = SaveMenuLocale.Format("DefaultSlotName", manager.GetAllSlots().Count + 1);
        try
        {
            var slot = manager.CreateSlot(name.Trim());
            if (manager.RequestSave())
                Alert(SaveMenuLocale.Format("AlertSavedToSlot", slot.Name));
        }
        catch (Exception ex)
        {
            Alert(ex.Message);
        }
        if (_newSlotInput != null)
            _newSlotInput.text = string.Empty;
        Refresh();
    }

    private static void BeginRename(SaveSlotInfo slot)
    {
        PlayClick();
        if (_slotContainer == null)
            return;

        var row = _slotContainer.Find("Slot_" + slot.SlotId);
        var info = row?.Find("Info");
        if (info == null)
            return;
        info.gameObject.SetActive(false);

        var input = CreateInputField(row!, SaveMenuLocale.Get("RenamePlaceholder"));
        input.text = slot.Name;
        var layout = input.gameObject.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.minWidth = 160f;
        input.transform.SetSiblingIndex(info.GetSiblingIndex());

        input.onEndEdit.AddListener(newName =>
        {
            var trimmed = (newName ?? string.Empty).Trim();
            if (trimmed.Length > 0 && !string.Equals(trimmed, slot.Name, StringComparison.Ordinal))
                Plugin.Manager.RenameSlot(slot.SlotId, trimmed);
            Refresh();
        });
        input.Select();
        input.ActivateInputField();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void UpdatePanelTransform(PauseHandler? handler)
    {
        if (_panelRect == null)
            return;
        // Position is fixed (docked to the canvas right edge); only the
        // pause open/close animation needs updating.
        var progress = handler != null ? Mathf.Clamp01(handler.pauseProgress) : 1f;
        _panelRect.localScale = Vector3.one * Mathf.Max(0.0001f, Utils.OutCubic(progress));
    }

    private static void ApplyArmedState(Button button, string slotId, PendingAction action)
    {
        if (_pendingSlotId != slotId || _pendingAction != action)
            return;
        var image = button.GetComponent<Image>();
        image.color = ConfirmColor;
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
            label.text = SaveMenuLocale.Get("ConfirmLabel");
    }

    private static bool CanSaveNow()
    {
        var camera = PlayerCamera.main;
        return camera != null && camera.body != null && camera.body.alive && camera.body.standing;
    }

    private static Sprite? GetScreenshot(SaveSlotInfo slot)
    {
        if (string.IsNullOrEmpty(slot.ScreenshotPath) || !File.Exists(slot.ScreenshotPath))
            return null;
        if (ScreenshotCache.TryGetValue(slot.SlotId, out var cached) && cached != null)
            return cached;
        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(slot.ScreenshotPath)))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            ScreenshotCache[slot.SlotId] = sprite;
            return sprite;
        }
        catch
        {
            return null;
        }
    }

    private static void PruneScreenshotCache()
    {
        var alive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in Plugin.Manager.GetAllSlots())
            alive.Add(slot.SlotId);
        var stale = new List<string>();
        foreach (var key in ScreenshotCache.Keys)
        {
            if (!alive.Contains(key))
                stale.Add(key);
        }
        foreach (var key in stale)
        {
            var sprite = ScreenshotCache[key];
            if (sprite != null)
            {
                if (sprite.texture != null)
                    UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }
            ScreenshotCache.Remove(key);
        }
    }

    private static Transform? FindCanvasTransform(Transform start)
    {
        // Walk up manually: GetComponentInParent skips inactive objects,
        // and the pause menu may be inactive when we first resolve it.
        var current = start;
        while (current != null)
        {
            if (current.GetComponent<Canvas>() != null)
                return current;
            current = current.parent;
        }
        return null;
    }

    private static string GetPath(Transform transform)
    {
        var path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static GameObject CreateUiObject(string name, Transform parent, params Type[] components)
    {
        var all = new Type[components.Length + 1];
        all[0] = typeof(RectTransform);
        Array.Copy(components, 0, all, 1, components.Length);
        var go = new GameObject(name, all);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float size, Color color)
    {
        var go = CreateUiObject(name, parent, typeof(TextMeshProUGUI));
        var text = go.GetComponent<TextMeshProUGUI>();
        var font = GameUiResources.Font;
        if (font != null)
            text.font = font;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.richText = true;
        return text;
    }

    private static Button CreateTextButton(Transform parent, string label, float width, UnityAction onClick)
    {
        var go = CreateUiObject("Btn_" + label, parent, typeof(Image), typeof(Button));
        var layout = go.AddComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.minHeight = ButtonHeight;
        layout.preferredHeight = ButtonHeight;

        var image = go.GetComponent<Image>();
        image.sprite = GameUiResources.FindSpriteContains("uiBlockNano");
        if (image.sprite != null)
            image.type = Image.Type.Sliced;
        image.color = ButtonColor;

        var button = go.GetComponent<Button>();
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = ButtonHighlight;
        colors.pressedColor = ButtonPressed;
        colors.disabledColor = ButtonDisabled;
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.onClick.AddListener(() =>
        {
            PlayClick();
            onClick();
        });

        var text = CreateText(go.transform, "Label", label, BodySize, TextColor);
        text.alignment = TextAlignmentOptions.Center;
        Stretch((RectTransform)text.transform);
        return button;
    }

    private static TMP_InputField CreateInputField(Transform parent, string placeholder)
    {
        var go = CreateUiObject("Input", parent, typeof(Image), typeof(TMP_InputField));
        var image = go.GetComponent<Image>();
        image.sprite = GameUiResources.FindSpriteContains("uiBlockNano");
        if (image.sprite != null)
            image.type = Image.Type.Sliced;
        image.color = new Color(0f, 0f, 0f, 0.6f);

        var textArea = CreateUiObject("TextArea", go.transform, typeof(RectMask2D));
        var textAreaRect = (RectTransform)textArea.transform;
        Stretch(textAreaRect);
        textAreaRect.offsetMin = new Vector2(8f, 2f);
        textAreaRect.offsetMax = new Vector2(-8f, -2f);

        var placeholderText = CreateText(textArea.transform, "Placeholder", placeholder, BodySize, SubTextColor);
        placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch((RectTransform)placeholderText.transform);

        var inputText = CreateText(textArea.transform, "Text", string.Empty, BodySize, TextColor);
        inputText.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch((RectTransform)inputText.transform);

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = textAreaRect;
        input.textComponent = inputText;
        input.placeholder = placeholderText;
        return input;
    }

    private static string Escape(string value) =>
        value.Replace("<", "‹").Replace(">", "›");

    private static void Alert(string message)
    {
        var camera = PlayerCamera.main;
        if (camera != null)
            camera.DoAlert(message);
    }

    private static void PlayClick() => PlayerCamera.PlayUISound("miniClick");
}
