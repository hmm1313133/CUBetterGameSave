using System;
using UnityEngine;

namespace BetterGameSave.UI;

/// <summary>
/// In-game save management UI. Provides a visual interface for:
/// - Browsing save slots
/// - Loading saves with error recovery
/// - Viewing save diagnostics
/// - Managing mod save data
/// </summary>
internal sealed class SaveMenuUI
{
    private bool _visible;
    private Rect _windowRect = new(200, 100, 600, 500);

    internal void Toggle()
    {
        _visible = !_visible;
    }

    internal void OnGUI()
    {
        if (!_visible)
            return;
        // TODO: Implement IMGUI window
        // - Save slot list with thumbnails and timestamps
        // - Load / Delete / Backup buttons per slot
        // - Error diagnostics panel
        // - Mod save data overview
    }
}
