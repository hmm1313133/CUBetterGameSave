using System;
using System.Reflection;
using BepInEx;

namespace BetterGameSave.Compatibility;

/// <summary>
/// KrokMP (KrokoshaCasualtiesMP) multiplayer mod compatibility layer.
/// Uses reflection to detect and interact with the multiplayer mod without
/// hard-referencing its assemblies.
/// </summary>
internal sealed class KrokMpCompat
{
    private const string KrokMpGuid = "com.krokosha.casualties.mp";

    private readonly bool _isKrokMpLoaded;
    private readonly Type? _krokMpApiType;

    public bool IsKrokMpActive => _isKrokMpLoaded;

    internal KrokMpCompat()
    {
        _isKrokMpLoaded = Chainloader.PluginInfos.ContainsKey(KrokMpGuid);
        if (_isKrokMpLoaded)
        {
            // TODO: Resolve KrokMP API type via reflection for deeper integration
            _krokMpApiType = null;
        }
    }

    /// <summary>
    /// Check whether the current session is a multiplayer session.
    /// </summary>
    public bool IsMultiplayerSession()
    {
        if (!_isKrokMpLoaded)
            return false;
        // TODO: Reflect into KrokMP to determine if a multiplayer session is active
        return false;
    }

    /// <summary>
    /// Check whether the local player is the host.
    /// </summary>
    public bool IsHost()
    {
        if (!_isKrokMpLoaded)
            return true;
        // TODO: Reflect into KrokMP to determine host status
        return true;
    }

    /// <summary>
    /// Get the list of connected player IDs.
    /// </summary>
    public int GetConnectedPlayerCount()
    {
        if (!_isKrokMpLoaded)
            return 1;
        // TODO: Reflect into KrokMP for player count
        return 1;
    }
}
