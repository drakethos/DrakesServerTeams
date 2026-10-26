using System;
using System.Collections.Generic;

namespace DrakesServerTeams;

/// <summary>teams.json: admin-edited definitions. No personal data.</summary>
[Serializable]
public sealed class TeamsFile
{
    public List<TeamDefinition> teams = new();
}

/// <summary>One stored player. This is everything the mod keeps about a person.</summary>
[Serializable]
public sealed class MemberEntry
{
    /// <summary>Player.GetPlayerID() of the character. Not a Steam/platform ID.</summary>
    public long characterId;
    public string teamId = "";
    /// <summary>UTC date, yyyy-MM-dd. Used only to purge idle entries.</summary>
    public string lastSeen = "";
    /// <summary>True once the server has shown this character the team picker (so it only appears the first time).</summary>
    public bool offered;
}

/// <summary>members.json: server-only, never sent to clients.</summary>
[Serializable]
public sealed class MembersFile
{
    public List<MemberEntry> members = new();
}
