using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BetterGameSave.UI;

/// <summary>
/// Locates and caches the game's own UI resources (sprites, fonts) so the
/// mod interface visually matches the native menus.
/// </summary>
internal static class GameUiResources
{
    private static readonly Dictionary<string, Sprite> SpritesByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite?> SpriteSearchCache = new(StringComparer.OrdinalIgnoreCase);
    private static bool _spritesLoaded;
    private static bool _fontSearched;
    private static TMP_FontAsset? _font;

    internal static TMP_FontAsset? Font
    {
        get
        {
            EnsureFont();
            return _font;
        }
    }

    /// <summary>Font size the game uses for its pause menu title (crisp for the pixel font).</summary>
    internal static float TitleFontSize { get; private set; } = 24f;

    /// <summary>Font size derived from the game's own UI text, safe for the pixel font.</summary>
    internal static float BodyFontSize { get; private set; } = 16f;

    internal static Sprite? FindSpriteContains(string fragment)
    {
        if (string.IsNullOrEmpty(fragment))
            return null;
        EnsureSpritesLoaded();
        if (SpriteSearchCache.TryGetValue(fragment, out var cached))
            return cached;
        foreach (var pair in SpritesByName)
        {
            if (pair.Value != null && pair.Key.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SpriteSearchCache[fragment] = pair.Value;
                return pair.Value;
            }
        }
        SpriteSearchCache[fragment] = null;
        return null;
    }

    private static void EnsureSpritesLoaded()
    {
        if (_spritesLoaded)
            return;
        _spritesLoaded = true;
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite != null && !string.IsNullOrEmpty(sprite.name) && !SpritesByName.ContainsKey(sprite.name))
                SpritesByName.Add(sprite.name, sprite);
        }
    }

    private static void EnsureFont()
    {
        if (_fontSearched)
            return;
        _fontSearched = true;

        // Prefer the font actually used by the pause menu so the panel matches the game.
        var handler = PauseHandler.main;
        if (handler != null)
        {
            var maxFontSize = 0f;
            foreach (var text in handler.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (text == null || text.font == null)
                    continue;
                if (text.fontSize > maxFontSize)
                    maxFontSize = text.fontSize;
                if (_font == null &&
                    text.font.name.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _font = text.font;
                }
            }
            if (handler.pauseText != null && handler.pauseText.fontSize > 0f)
                TitleFontSize = handler.pauseText.fontSize;
            else if (maxFontSize > 0f)
                TitleFontSize = maxFontSize;
            BodyFontSize = Mathf.Max(10f, Mathf.Round(TitleFontSize * 0.66f));
            if (_font != null)
                return;
        }

        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font != null && font.name.IndexOf("Retro", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _font = font;
                return;
            }
        }
    }
}
