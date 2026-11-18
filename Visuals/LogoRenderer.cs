using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DrakesServerTeams;

/// <summary>The built-in bases and symbols of the logo designer. Everything is a coverage test over x, y in -1..1 (y up), so there is no art to ship.</summary>
public static class LogoShapes
{
    public static readonly string[] Bases = { "shield", "circle", "square", "diamond", "hexagon", "banner" };

    public static readonly string[] Symbols =
    {
        "star", "cross", "circle", "ring", "diamond", "triangle", "hexagon", "moon", "bolt", "chevron", "heart", "crown", "arrow", "bar", "stripe",
    };

    public static bool IsBase(string? name) => Array.Exists(Bases, b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase));

    public static bool IsSymbol(string? name) => Array.Exists(Symbols, b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase));

    static readonly Vector2[] Bolt = { new(0.15f, 0.95f), new(-0.55f, 0.1f), new(-0.05f, 0.1f), new(-0.2f, -0.95f), new(0.6f, -0.05f), new(0.1f, -0.05f) };
    static readonly Vector2[] Chevron = { new(-0.9f, 0.3f), new(0f, 0.9f), new(0.9f, 0.3f), new(0.9f, -0.1f), new(0f, 0.5f), new(-0.9f, -0.1f) };
    static readonly Vector2[] Crown = { new(-0.9f, -0.6f), new(-0.9f, 0.5f), new(-0.45f, 0f), new(0f, 0.7f), new(0.45f, 0f), new(0.9f, 0.5f), new(0.9f, -0.6f) };
    static readonly Vector2[] Arrow = { new(0f, 0.95f), new(0.8f, 0.1f), new(0.35f, 0.1f), new(0.35f, -0.9f), new(-0.35f, -0.9f), new(-0.35f, 0.1f), new(-0.8f, 0.1f) };
    static readonly Vector2[] Triangle = { new(0f, 0.9f), new(0.9f, -0.75f), new(-0.9f, -0.75f) };

    /// <summary>Is the point inside the base shape?</summary>
    public static bool InBase(string shape, float x, float y)
    {
        switch (shape)
        {
            case "circle":
                return x * x + y * y <= 0.95f * 0.95f;
            case "square":
                return Mathf.Abs(x) <= 0.92f && Mathf.Abs(y) <= 0.92f;
            case "diamond":
                return Mathf.Abs(x) + Mathf.Abs(y) <= 0.95f;
            case "hexagon":
                return Mathf.Abs(y) <= 0.9f && Mathf.Abs(x) * 0.5f + Mathf.Abs(y) * 0.866f <= 0.866f * 0.95f && Mathf.Abs(x) <= 0.95f;
            case "banner":
            {
                // a swallowtail banner: straight sides, a V cut up into the bottom edge
                if (Mathf.Abs(x) > 0.8f || y > 0.95f)
                    return false;
                var bottom = -0.95f + 0.55f * (1f - Mathf.Abs(x) / 0.8f);
                return y >= bottom;
            }
            default: // shield
                return TeamIcons.Inside("shield", x, y);
        }
    }

    /// <summary>Is the point inside the symbol? (Unknown names are empty.)</summary>
    public static bool InSymbol(string symbol, float x, float y)
    {
        switch (symbol)
        {
            case "star":
            case "cross":
            case "circle":
            case "diamond":
                return TeamIcons.Inside(symbol, x, y);
            case "ring":
            {
                var r2 = x * x + y * y;
                return r2 <= 0.92f * 0.92f && r2 >= 0.6f * 0.6f;
            }
            case "triangle":
                return InPolygon(Triangle, x, y);
            case "hexagon":
                return InBase("hexagon", x, y);
            case "moon":
                return x * x + y * y <= 0.85f * 0.85f && (x - 0.35f) * (x - 0.35f) + (y - 0.1f) * (y - 0.1f) > 0.7f * 0.7f;
            case "bolt":
                return InPolygon(Bolt, x, y);
            case "chevron":
                return InPolygon(Chevron, x, y);
            case "heart":
            {
                var hx = x * 1.25f;
                var hy = (y + 0.1f) * 1.25f;
                var a = hx * hx + hy * hy - 1f;
                return a * a * a - hx * hx * hy * hy * hy <= 0f;
            }
            case "crown":
                return InPolygon(Crown, x, y);
            case "arrow":
                return InPolygon(Arrow, x, y);
            case "bar":
                return Mathf.Abs(y) <= 0.2f && Mathf.Abs(x) <= 0.95f;
            case "stripe":
                return Mathf.Abs(x - y) <= 0.22f && Mathf.Abs(x) <= 0.95f && Mathf.Abs(y) <= 0.95f;
            default:
                return false;
        }
    }

    static bool InPolygon(Vector2[] poly, float x, float y)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > y) != (poly[j].y > y) && x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }
}

/// <summary>An imported icon: a PNG from an icons folder, either recolored by the layer color (a mask) or drawn as is (static).</summary>
public sealed class ImportedIcon
{
    public int Width;
    public int Height;
    public Color32[] Pixels = Array.Empty<Color32>();
    /// <summary>True: the picture is a mask (its brightness times its alpha) multiplied by the layer color. False: static, its own colors are kept.</summary>
    public bool Recolor = true;
    public string Path = "";

    /// <summary>Bilinear sample at x, y in -1..1 (y up). Returns RGBA 0..1; outside the picture is transparent.</summary>
    public Color Sample(float x, float y)
    {
        if (Pixels.Length == 0 || x < -1f || x > 1f || y < -1f || y > 1f)
            return default;
        var fx = (x * 0.5f + 0.5f) * (Width - 1);
        var fy = (y * 0.5f + 0.5f) * (Height - 1);
        var x0 = Mathf.Clamp((int)fx, 0, Width - 1);
        var y0 = Mathf.Clamp((int)fy, 0, Height - 1);
        var x1 = Mathf.Min(x0 + 1, Width - 1);
        var y1 = Mathf.Min(y0 + 1, Height - 1);
        var tx = fx - x0;
        var ty = fy - y0;
        var a = Color.Lerp(Px(x0, y0), Px(x1, y0), tx);
        var b = Color.Lerp(Px(x0, y1), Px(x1, y1), tx);
        return Color.Lerp(a, b, ty);
    }

    Color Px(int x, int y)
    {
        var p = Pixels[y * Width + x];
        return new Color(p.r / 255f, p.g / 255f, p.b / 255f, p.a / 255f);
    }
}

/// <summary>Loads imported icons from the icons folders and remembers them. See the README for the file rules.</summary>
public static class ImportedIcons
{
    public const int MaxFileBytes = 512 * 1024;
    public const int MaxSide = 1024;
    static readonly Dictionary<string, ImportedIcon?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearCache() => Cache.Clear();

    /// <summary>The icon files available on this machine: name and how they are used.</summary>
    public static List<(string name, bool recolor)> List()
    {
        var found = new List<(string, bool)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in TeamIcons.IconFolders())
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var file in Directory.GetFiles(folder, "*.png"))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                var mode = ModeOf(file, out stem);
                if (LogoSpec.SafeName(stem) && seen.Add(stem))
                    found.Add((stem, mode));
            }
        }
        found.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase));
        return found;
    }

    /// <summary>
    /// recolor or static, from the file name (<c>wolf.static.png</c>, <c>wolf.recolor.png</c>) or a sidecar <c>wolf.icon.json</c> containing "static".
    /// A plain <c>wolf.png</c> is a recolor mask.
    /// </summary>
    static bool ModeOf(string file, out string stem)
    {
        stem = Path.GetFileNameWithoutExtension(file);
        if (stem.EndsWith(".static", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem.Substring(0, stem.Length - ".static".Length);
            return false;
        }
        if (stem.EndsWith(".recolor", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem.Substring(0, stem.Length - ".recolor".Length);
            return true;
        }
        try
        {
            var sidecar = Path.Combine(Path.GetDirectoryName(file) ?? "", stem + ".icon.json");
            if (File.Exists(sidecar) && File.ReadAllText(sidecar).IndexOf("static", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }
        catch
        {
            // an unreadable sidecar means the default
        }
        return true;
    }

    /// <summary>The imported icon with this name, or null when no such file (or a bad one) is on this machine.</summary>
    public static ImportedIcon? Get(string name)
    {
        if (!LogoSpec.SafeName(name))
            return null;
        if (Cache.TryGetValue(name, out var cached))
            return cached;
        var icon = Load(name);
        Cache[name] = icon;
        return icon;
    }

    static ImportedIcon? Load(string name)
    {
        foreach (var folder in TeamIcons.IconFolders())
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var suffix in new[] { ".static", ".recolor", "" })
            {
                var path = Path.Combine(folder, name + suffix + ".png");
                if (!File.Exists(path))
                    continue;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > MaxFileBytes)
                    {
                        TeamsNet.Warn($"Team icon {path} is {info.Length / 1024} KB; the limit is {MaxFileBytes / 1024} KB. Ignored.");
                        return null;
                    }
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!TeamIcons.LoadPngInto(tex, File.ReadAllBytes(path)) || tex.width > MaxSide || tex.height > MaxSide)
                    {
                        TeamsNet.Warn($"Team icon {path} is not a PNG, or is bigger than {MaxSide}x{MaxSide}. Ignored.");
                        UnityEngine.Object.Destroy(tex);
                        return null;
                    }
                    var icon = new ImportedIcon { Width = tex.width, Height = tex.height, Pixels = tex.GetPixels32(), Recolor = ModeOf(path, out _), Path = path };
                    UnityEngine.Object.Destroy(tex);
                    return icon;
                }
                catch (Exception ex)
                {
                    TeamsNet.Warn($"Could not read team icon {path}: {ex.Message}");
                    return null;
                }
            }
        }
        return null;
    }
}

/// <summary>Draws a <see cref="LogoSpec"/> into a texture. CPU only, run once per logo and cached by the caller.</summary>
public static class LogoRenderer
{
    public static Texture2D Render(LogoSpec spec, int size = 256)
    {
        var px = new Color32[size * size];
        RenderInto(spec, size, px);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    /// <summary>The pixels as RGBA bytes, row 0 at the bottom (the texture's own order). Used by tests and by <see cref="Render"/>.</summary>
    public static void RenderInto(LogoSpec spec, int size, Color32[] output)
    {
        var baseColor = TeamIcons.ParseColor(spec.BaseColor);
        var borderColor = TeamIcons.ParseColor(spec.BorderColor);
        var layers = new List<(LogoLayer layer, Color color, ImportedIcon? icon, float cos, float sin)>();
        foreach (var l in spec.Layers)
        {
            var rad = -l.Rotation * Mathf.Deg2Rad;
            var icon = LogoShapes.IsSymbol(l.Symbol) ? null : ImportedIcons.Get(l.Symbol);
            // an imported icon that is missing on this machine is skipped, the rest of the logo still shows
            if (icon == null && !LogoShapes.IsSymbol(l.Symbol))
                continue;
            layers.Add((l, TeamIcons.ParseColor(l.Color), icon, Mathf.Cos(rad), Mathf.Sin(rad)));
        }
        var inner = Mathf.Max(0.05f, 1f - spec.BorderWidth * 2f);
        var samples = new[] { new Vector2(-0.25f, -0.25f), new Vector2(0.25f, -0.25f), new Vector2(-0.25f, 0.25f), new Vector2(0.25f, 0.25f) };

        for (var py = 0; py < size; py++)
        for (var pxl = 0; pxl < size; pxl++)
        {
            float r = 0f, g = 0f, b = 0f, a = 0f;
            foreach (var s in samples)
            {
                var u = ((pxl + 0.5f + s.x) / size) * 2f - 1f;
                var v = ((py + 0.5f + s.y) / size) * 2f - 1f;
                var c = Shade(spec, layers, baseColor, borderColor, inner, u, v);
                r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
            }
            var al = a / samples.Length;
            output[py * size + pxl] = al <= 0.001f
                ? new Color32(0, 0, 0, 0)
                : new Color32((byte)Mathf.Clamp(r / a * 255f, 0, 255), (byte)Mathf.Clamp(g / a * 255f, 0, 255), (byte)Mathf.Clamp(b / a * 255f, 0, 255), (byte)Mathf.Clamp(al * 255f, 0, 255));
        }
    }

    static Color Shade(LogoSpec spec, List<(LogoLayer layer, Color color, ImportedIcon? icon, float cos, float sin)> layers,
        Color baseColor, Color borderColor, float inner, float u, float v)
    {
        if (!LogoShapes.InBase(spec.Base, u, v))
            return default;
        var c = spec.BorderWidth > 0.001f && !LogoShapes.InBase(spec.Base, u / inner, v / inner) ? borderColor : baseColor;
        foreach (var (layer, color, icon, cos, sin) in layers)
        {
            // into the symbol's own space: undo the position, then the rotation, then the scale
            var dx = u - layer.X;
            var dy = v - layer.Y;
            var sx = (dx * cos - dy * sin) / layer.Scale;
            var sy = (dx * sin + dy * cos) / layer.Scale;
            if (layer.Flip)
                sx = -sx;
            if (sx < -1f || sx > 1f || sy < -1f || sy > 1f)
                continue;
            Color top;
            if (icon == null)
            {
                if (!LogoShapes.InSymbol(layer.Symbol, sx, sy))
                    continue;
                top = new Color(color.r, color.g, color.b, 1f);
            }
            else
            {
                var p = icon.Sample(sx, sy);
                if (p.a <= 0.001f)
                    continue;
                if (icon.Recolor)
                {
                    // a mask: brightness times alpha decides how much of the layer color shows
                    var m = Mathf.Clamp01(Mathf.Max(p.r, Mathf.Max(p.g, p.b))) * p.a;
                    top = new Color(color.r, color.g, color.b, m);
                }
                else
                    top = p;
            }
            c = new Color(Mathf.Lerp(c.r, top.r, top.a), Mathf.Lerp(c.g, top.g, top.a), Mathf.Lerp(c.b, top.b, top.a), 1f);
        }
        return c;
    }
}

/// <summary>The sprite to show for a team: its logo when it has one, otherwise the old single icon.</summary>
public static class TeamLogos
{
    static readonly Dictionary<string, Sprite> Cache = new();
    static readonly HashSet<string> Failed = new();

    public static void ClearCache()
    {
        foreach (var s in Cache.Values)
            if (s != null)
                UnityEngine.Object.Destroy(s.texture);
        Cache.Clear();
        Failed.Clear();
        ImportedIcons.ClearCache();
    }

    /// <summary>The team's picture. <paramref name="tinted"/> says whether the caller should multiply it by the team color (only the old plain icons).</summary>
    public static Sprite GetSprite(TeamDefinition team, out bool tinted)
    {
        var logo = team.Logo ?? "";
        if (logo.Length > 0 && !Failed.Contains(logo))
        {
            tinted = false;
            if (Cache.TryGetValue(logo, out var cached) && cached != null)
                return cached;
            if (LogoSpec.TryParse(logo, out var spec, out _))
            {
                var tex = LogoRenderer.Render(spec);
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
                Cache[logo] = sprite;
                return sprite;
            }
            Failed.Add(logo);
        }
        tinted = TeamIcons.IsTinted(team.Icon);
        return TeamIcons.Get(team.Icon);
    }

    public static Sprite GetSprite(TeamDefinition team) => GetSprite(team, out _);
}
