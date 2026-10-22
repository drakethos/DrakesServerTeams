namespace DrakesServerTeams;

/// <summary>How a character with no team gets one.</summary>
public enum AssignmentMode
{
    /// <summary>Only an admin command or the server API assigns teams. New characters stay unassigned.</summary>
    AdminOnly,
    /// <summary>A new character picks any joinable team (console: <c>teams join</c>). Unlocked players may switch later.</summary>
    PlayerChoice,
    /// <summary>The server puts a new character on the joinable team with the fewest members.</summary>
    AutoBalance,
}
