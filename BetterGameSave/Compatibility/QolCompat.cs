using System;
using BepInEx;

namespace BetterGameSave.Compatibility;

/// <summary>
/// QoL Unknown mod compatibility layer.
/// BetterGameSave provides a superset of QoL's save functionality,
/// so when both are present we suppress QoL's save features and take over.
/// </summary>
internal sealed class QolCompat
{
    private const string QolGuid = "org.bepinex.plugins.qol_unknown";

    private readonly bool _isQolLoaded;

    public bool IsQolLoaded => _isQolLoaded;

    internal QolCompat()
    {
        _isQolLoaded = Chainloader.PluginInfos.ContainsKey(QolGuid);
    }

    /// <summary>
    /// If QoL is loaded, suppress its save-related functionality to avoid conflicts.
    /// BetterGameSave will handle all save/load operations instead.
    /// </summary>
    public void SuppressQolSaveFeatures()
    {
        if (!_isQolLoaded)
            return;
        // TODO: Patch or reflect QoL to disable its save hooks
    }
}
