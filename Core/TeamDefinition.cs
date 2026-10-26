using System;

namespace DrakesServerTeams;

/// <summary>One side. Loaded from the server's teams file. Contains no personal data.</summary>
/// <remarks>Public fields on purpose: the teams file is read and written by field (Jotunn SimpleJson).</remarks>
[Serializable]
public sealed class TeamDefinition
{
    public string Id = "";
    public string DisplayName = "";
    /// <summary>Hex color, e.g. "#C0392B". Used for tint, emblem and map marker.</summary>
    public string Color = "#FFFFFF";
    /// <summary>Players may pick this team themselves.</summary>
    public bool Joinable = true;
    /// <summary>Once assigned, only an admin or the API can move the player out.</summary>
    public bool Locked;
    /// <summary>This team has its own spawn point (SpawnX/Y/Z). Off = members spawn where the game puts them.</summary>
    public bool HasSpawn;
    public float SpawnX;
    public float SpawnY;
    public float SpawnZ;
    /// <summary>Also put a copy of the game's start stones at this team's spawn point (opt-in; the original start location stays the true spawn).</summary>
    public bool SpawnStones;
    /// <summary>Faction icon above the name and on graves: shield, circle, diamond, star or cross (see TeamIcons).</summary>
    public string Icon = "shield";
    /// <summary>Color of the player glow and the grave light, "#RRGGBB". Empty = use <see cref="Color"/>.</summary>
    public string GlowColor = "";

    /// <summary>The glow color to use: GlowColor when set, else the team color.</summary>
    public string EffectiveGlowColor() => string.IsNullOrWhiteSpace(GlowColor) ? Color : GlowColor;

    // A method, not a property: the teams file is written by public member and Vector3 properties recurse.
    public UnityEngine.Vector3 GetSpawnPoint() => new(SpawnX, SpawnY, SpawnZ);

    public void SetSpawn(UnityEngine.Vector3 p)
    {
        HasSpawn = true;
        SpawnX = p.x;
        SpawnY = p.y;
        SpawnZ = p.z;
    }
}
