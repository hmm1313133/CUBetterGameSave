using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;

namespace BetterGameSave.Compatibility;

/// <summary>
/// KrokMP (KrokoshaCasualtiesMP) multiplayer mod compatibility layer.
/// Uses reflection to detect and interact with the multiplayer mod without
/// hard-referencing its assemblies.
/// </summary>
internal sealed class KrokMpCompat
{
    private const string KrokMpGuid = "KrokoshaCasualtiesMP";

    private readonly bool _isKrokMpLoaded;
    private readonly Type? _multiplayerType;
    private readonly Type? _netPlayerType;

    public bool IsKrokMpActive => _isKrokMpLoaded;

    internal KrokMpCompat()
    {
        _isKrokMpLoaded = Chainloader.PluginInfos.ContainsKey(KrokMpGuid);
        if (!_isKrokMpLoaded)
            return;

        var assembly = Chainloader.PluginInfos[KrokMpGuid].Instance.GetType().Assembly;
        _multiplayerType = assembly.GetType("KrokoshaCasualtiesMP.KrokoshaScavMultiplayer");
        _netPlayerType = assembly.GetType("KrokoshaCasualtiesMP.NetPlayer");
    }

    /// <summary>
    /// Check whether the current session is a multiplayer session.
    /// </summary>
    public bool IsMultiplayerSession() => InvokeBool("IsNetworkActiveAndIsServer") ||
                                         InvokeBool("IsNetworkActiveAndIsClient");

    /// <summary>
    /// Check whether the local player is the host or no multiplayer session is active.
    /// </summary>
    public bool IsHost() => !IsMultiplayerSession() || InvokeBool("IsNetworkActiveAndIsServer");

    /// <summary>
    /// Get the number of players known to KrokMP.
    /// </summary>
    public int GetConnectedPlayerCount()
    {
        if (!IsMultiplayerSession())
            return 1;

        try
        {
            var property = _netPlayerType?.GetProperty("ClientIdToPlayerDict", BindingFlags.Public | BindingFlags.Static);
            return property?.GetValue(null) is IDictionary players ? Math.Max(1, players.Count) : 1;
        }
        catch
        {
            return 1;
        }
    }

    private bool InvokeBool(string methodName)
    {
        try
        {
            var method = _multiplayerType?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            return method?.Invoke(null, null) is bool result && result;
        }
        catch
        {
            return false;
        }
    }
}
