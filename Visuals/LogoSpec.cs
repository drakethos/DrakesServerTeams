using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DrakesServerTeams;

/// <summary>One symbol on a logo: a built-in symbol or an imported icon, placed, scaled, rotated and colored.</summary>
public sealed class LogoLayer
{
    /// <summary>A built-in symbol (<see cref="LogoShapes.Symbols"/>) or the name of an imported icon file in an icons folder.</summary>
    public string Symbol = "star";
    /// <summary>"#RRGGBB". Ignored by imported icons that are static.</summary>
    public string Color = "#FFFFFF";
    /// <summary>Position on the logo, -1 (left / bottom) to 1 (right / top).</summary>
    public float X;
    public float Y;
    /// <summary>1 = fills the logo, 0.1 = a tenth of it.</summary>
    public float Scale = 0.5f;
    /// <summary>Degrees, counter-clockwise.</summary>
    public float Rotation;
    public bool Flip;

    public LogoLayer Copy() => (LogoLayer)MemberwiseClone();
}

/// <summary>
/// A team logo recipe, like the Rock Band / Guitar Hero logo editor: a base shape and color, a border, and up to
/// <see cref="MaxLayers"/> symbols on top. Stored as one short string (<see cref="ToString"/>) so it fits in teams.json and the team list sent to clients:
/// <c>base:baseColor:borderColor:borderWidth|symbol:color:x:y:scale:rotation:flip|...</c>
/// </summary>
public sealed class LogoSpec
{
    public const int MaxLayers = 4;
    public const int MaxLength = 600;

    public string Base = "shield";
    public string BaseColor = "#C0392B";
    public string BorderColor = "#FFFFFF";
    /// <summary>0 = no border; 0.2 = a thick one (share of the logo's radius).</summary>
    public float BorderWidth = 0.06f;
    public List<LogoLayer> Layers = new();

    public LogoSpec Copy()
    {
        var c = (LogoSpec)MemberwiseClone();
        c.Layers = new List<LogoLayer>();
        foreach (var l in Layers)
            c.Layers.Add(l.Copy());
        return c;
    }

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Base).Append(':').Append(BaseColor).Append(':').Append(BorderColor).Append(':').Append(F(BorderWidth));
        foreach (var l in Layers)
            sb.Append('|').Append(l.Symbol).Append(':').Append(l.Color).Append(':').Append(F(l.X)).Append(':').Append(F(l.Y)).Append(':')
              .Append(F(l.Scale)).Append(':').Append(F(l.Rotation)).Append(':').Append(l.Flip ? '1' : '0');
        return sb.ToString();
    }

    /// <summary>
    /// Reads a spec. Strict about structure (a spec that does not parse is refused), forgiving about numbers (clamped to sane ranges),
    /// and only accepts safe names and #RRGGBB colors, so a spec from a file or the network can never point outside the icons folders.
    /// </summary>
    public static bool TryParse(string? text, out LogoSpec spec, out string error)
    {
        spec = new LogoSpec();
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty";
            return false;
        }
        if (text!.Length > MaxLength)
        {
            error = $"longer than {MaxLength} characters";
            return false;
        }
        var parts = text.Trim().Split('|');
        if (parts.Length - 1 > MaxLayers)
        {
            error = $"more than {MaxLayers} symbols";
            return false;
        }
        var head = parts[0].Split(':');
        if (head.Length != 4 || !SafeName(head[0]) || !Color(head[1]) || !Color(head[2]) || !Num(head[3], 0f, 0.3f, out var bw))
        {
            error = "the first part must be base:#baseColor:#borderColor:borderWidth";
            return false;
        }
        spec.Base = head[0].ToLowerInvariant();
        spec.BaseColor = head[1].ToUpperInvariant();
        spec.BorderColor = head[2].ToUpperInvariant();
        spec.BorderWidth = bw;
        for (var i = 1; i < parts.Length; i++)
        {
            var f = parts[i].Split(':');
            if (f.Length != 7 || !SafeName(f[0]) || !Color(f[1]) || !Num(f[2], -1.5f, 1.5f, out var x) || !Num(f[3], -1.5f, 1.5f, out var y)
                || !Num(f[4], 0.05f, 2f, out var s) || !Num(f[5], -360f, 360f, out var r) || (f[6] != "0" && f[6] != "1"))
            {
                error = $"symbol {i} must be symbol:#color:x:y:scale:rotation:flip";
                return false;
            }
            spec.Layers.Add(new LogoLayer { Symbol = f[0].ToLowerInvariant(), Color = f[1].ToUpperInvariant(), X = x, Y = y, Scale = s, Rotation = r, Flip = f[6] == "1" });
        }
        return true;
    }

    /// <summary>Letters, digits, - and _ only (an icon file name or a built-in name).</summary>
    public static bool SafeName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 40)
            return false;
        foreach (var c in name)
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                return false;
        return true;
    }

    static bool Color(string c) =>
        c.Length == 7 && c[0] == '#' && int.TryParse(c.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    static bool Num(string s, float min, float max, out float value)
    {
        if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || float.IsNaN(value) || float.IsInfinity(value))
            return false;
        value = Math.Max(min, Math.Min(max, value));
        return true;
    }

    /// <summary>A starting logo for a team: its shield in the team color with one white star. Also what an old single-icon team becomes.</summary>
    public static LogoSpec Starter(string baseShape, string color, string? symbol = null)
    {
        var s = new LogoSpec { Base = LogoShapes.IsBase(baseShape) ? baseShape : "shield", BaseColor = color };
        s.Layers.Add(new LogoLayer { Symbol = symbol != null && LogoShapes.IsSymbol(symbol) ? symbol : "star", Color = "#FFFFFF", Scale = 0.5f });
        return s;
    }
}
