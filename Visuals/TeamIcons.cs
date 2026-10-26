using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>
/// Team faction icons, drawn at runtime (white on transparent, tinted with the team color where used), so the mod
/// ships no art. A team picks one by name in teams.json (<see cref="TeamDefinition.Icon"/>).
/// </summary>
public static class TeamIcons
{
    public const string Default = "shield";
    public static readonly string[] Shapes = { "shield", "circle", "diamond", "star", "cross" };

    const int Size = 64;
    const float MissingRetrySeconds = 10f;
    static readonly Dictionary<string, Sprite> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, float> Missing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folders searched for custom icons (name.png): the config folder's icons\ and an icons\ folder next to the mod's DLL.</summary>
    public static IEnumerable<string> IconFolders()
    {
        yield return System.IO.Path.Combine(TeamStore.Folder, "icons");
        string? dll = null;
        try { dll = System.IO.Path.GetDirectoryName(typeof(TeamIcons).Assembly.Location); } catch { /* no location */ }
        if (!string.IsNullOrEmpty(dll))
            yield return System.IO.Path.Combine(dll!, "icons");
    }

    /// <summary>
    /// The icon name to store: a built-in shape (any case) or a plain file name (letters, digits, - and _) for a custom
    /// icon in an icons folder. Anything else becomes the default shape. The server cannot see client files, so it accepts any safe name.
    /// </summary>
    public static string Normalize(string? shape)
    {
        foreach (var s in Shapes)
            if (string.Equals(s, shape, StringComparison.OrdinalIgnoreCase))
                return s;
        var name = (shape ?? "").Trim();
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);
        if (name.Length == 0 || name.Length > 40)
            return Default;
        foreach (var c in name)
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                return Default;
        return name.ToLowerInvariant();
    }

    public static bool IsBuiltIn(string? shape) => Array.Exists(Shapes, s => string.Equals(s, shape, StringComparison.OrdinalIgnoreCase));

    /// <summary>Custom icons keep their own colors; built-in shapes are tinted with the team color.</summary>
    public static bool IsTinted(string? shape) => IsBuiltIn(Normalize(shape));

    /// <summary>"builtin", "file" (a custom icon found on this machine) or "missing" (drawn as the default shield).</summary>
    public static string Kind(string? shape)
    {
        var key = Normalize(shape);
        if (IsBuiltIn(key))
            return "builtin";
        return FindFile(key) != null ? "file" : "missing";
    }

    static string? FindFile(string name)
    {
        foreach (var folder in IconFolders())
        {
            var path = System.IO.Path.Combine(folder, name + ".png");
            if (System.IO.File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>Forget loaded custom icons (after files were added or changed).</summary>
    public static void ClearCache()
    {
        foreach (var s in Cache.Values)
            if (s != null)
                UnityEngine.Object.Destroy(s.texture);
        Cache.Clear();
        Missing.Clear();
    }

    // ImageConversion needs netstandard 2.1, which this project does not reference: call it by reflection.
    static readonly System.Reflection.MethodInfo? LoadImageMethod = HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("UnityEngine.ImageConversion"), "LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
    static readonly System.Reflection.MethodInfo? EncodePngMethod = HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("UnityEngine.ImageConversion"), "EncodeToPNG", new[] { typeof(Texture2D) });

    static bool LoadPng(Texture2D tex, byte[] bytes) => LoadImageMethod?.Invoke(null, new object[] { tex, bytes }) is true;

    internal static byte[] EncodePng(Texture2D tex) => EncodePngMethod?.Invoke(null, new object[] { tex }) as byte[] ?? new byte[0];

    static Sprite? Load(string key)
    {
        if (Missing.TryGetValue(key, out var until) && Time.unscaledTime < until)
            return null;
        var path = FindFile(key);
        if (path != null)
        {
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                if (LoadPng(tex, System.IO.File.ReadAllBytes(path)))
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), Mathf.Max(tex.width, tex.height));
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception ex)
            {
                TeamsNet.Warn($"Could not load team icon {path}: {ex.Message}");
            }
        }
        Missing[key] = Time.unscaledTime + MissingRetrySeconds;
        return null;
    }

    public static Sprite Get(string? shape)
    {
        var key = Normalize(shape);
        if (Cache.TryGetValue(key, out var sprite) && sprite != null)
            return sprite;
        if (!IsBuiltIn(key))
        {
            var custom = Load(key);
            if (custom != null)
            {
                Cache[key] = custom;
                return custom;
            }
            return Get(Default);
        }
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[Size * Size];
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            // -1..1, y up. A 3x3 sample grid gives soft edges.
            var cover = 0;
            for (var sy = 0; sy < 3; sy++)
            for (var sx = 0; sx < 3; sx++)
                if (Inside(key, (x + (sx + 0.5f) / 3f) / Size * 2f - 1f, (y + (sy + 0.5f) / 3f) / Size * 2f - 1f))
                    cover++;
            pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(cover * 255 / 9));
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
        Cache[key] = sprite;
        return sprite;
    }

    static bool Inside(string shape, float x, float y)
    {
        const float r = 0.92f;
        switch (shape)
        {
            case "circle":
                return x * x + y * y <= r * r;
            case "diamond":
                return Mathf.Abs(x) + Mathf.Abs(y) <= r;
            case "cross":
                return (Mathf.Abs(x) <= 0.3f && Mathf.Abs(y) <= r) || (Mathf.Abs(y) <= 0.3f && Mathf.Abs(x) <= r);
            case "star":
                return InStar(x, y, r);
            default: // shield: flat top, sides straight to the middle, then curving to a point at the bottom
            {
                if (y > r || y < -r || Mathf.Abs(x) > 0.8f)
                    return false;
                if (y >= -0.1f)
                    return true;
                var t = (y + r) / (r - 0.1f); // 0 at the tip, 1 at y = -0.1
                return Mathf.Abs(x) <= 0.8f * Mathf.Sqrt(Mathf.Clamp01(t));
            }
        }
    }

    // Five-point star as the union of its triangles' fan around the center.
    static bool InStar(float x, float y, float r)
    {
        var rIn = r * 0.42f;
        var pts = new Vector2[10];
        for (var i = 0; i < 10; i++)
        {
            var a = Mathf.PI / 2f + i * Mathf.PI / 5f;
            var rad = i % 2 == 0 ? r : rIn;
            pts[i] = new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad);
        }
        var p = new Vector2(x, y);
        for (var i = 0; i < 10; i++)
            if (InTriangle(p, Vector2.zero, pts[i], pts[(i + 1) % 10]))
                return true;
        return false;
    }

    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float Sign(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        var d1 = Sign(p, a, b);
        var d2 = Sign(p, b, c);
        var d3 = Sign(p, c, a);
        var neg = d1 < 0 || d2 < 0 || d3 < 0;
        var pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "C0392B", ["blue"] = "2980B9", ["green"] = "27AE60", ["gold"] = "F1C40F", ["yellow"] = "F1C40F",
        ["orange"] = "E67E22", ["purple"] = "8E44AD", ["pink"] = "E84393", ["teal"] = "16A085", ["white"] = "FFFFFF",
        ["black"] = "000000", ["grey"] = "95A5A6", ["gray"] = "95A5A6", ["brown"] = "8D6E63", ["cyan"] = "00BCD4",
    };

    /// <summary>True when the text is a color we understand: #RRGGBB, #RGB, RRGGBB, or a name (red, blue, green, gold ...).</summary>
    public static bool TryParseColor(string? text, out Color color)
    {
        color = Color.white;
        var h = (text ?? "").Trim();
        if (h.Length == 0)
            return false;
        if (Named.TryGetValue(h, out var named))
            h = named;
        h = h.TrimStart('#');
        if (h.Length == 3)
            h = "" + h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return false;
        color = new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        return true;
    }

    /// <summary>A color setting to a color; white when it does not parse.</summary>
    public static Color ParseColor(string? hex) => TryParseColor(hex, out var c) ? c : Color.white;

    public static string ToHex(Color c) =>
        "#" + ((int)Mathf.Round(c.r * 255)).ToString("X2") + ((int)Mathf.Round(c.g * 255)).ToString("X2") + ((int)Mathf.Round(c.b * 255)).ToString("X2");
}
