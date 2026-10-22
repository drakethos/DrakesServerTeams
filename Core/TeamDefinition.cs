using System;

namespace DrakesServerTeams;

/// <summary>One side. Loaded from the server's teams file. Contains no personal data.</summary>
/// <remarks>Public fields on purpose: the teams file is read and written with Unity's JsonUtility.</remarks>
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
    // Later phases: SpawnPoint (x,y,z), emblem id.
}
