# DrakesServerTeams

Server-wide **teams** for Valheim: two or more sides defined by the server admin (Horde / Alliance style).
Part of DrakeMods, built on DrakeModsLibs.

**Teams** are server-wide sides (this mod). **Guilds** are player-formed (Smoothbrain Guilds). **Groups** are parties (Smoothbrain Groups). This mod does not replace or patch either.

> **Status: Phase 1 (core) only.** Teams exist, players can be assigned, other mods can ask. Nothing in the world reacts to teams yet (no ward/lock integration, name hiding, PvP rules, spawn points).

## Setup

Install on the server and on every client (it is network-checked). Requires DrakeModsLibs.

On first start the server writes `BepInEx/config/DrakeMods.ServerTeams/teams.json`:

```json
{ "teams": [
  { "Id": "red",  "DisplayName": "Red",  "Color": "#C0392B", "Joinable": true, "Locked": false },
  { "Id": "blue", "DisplayName": "Blue", "Color": "#2980B9", "Joinable": true, "Locked": false }
] }
```

Edit it and restart the server. `Joinable`: a player may pick this team themself. `Locked`: once a player is on it they cannot leave on their own (an admin or the API can still move them).

## How players get a team (`AssignmentMode`, synced)

| Mode | Behavior |
|------|----------|
| `AdminOnly` (default) | Only an admin command or the server API assigns teams. |
| `PlayerChoice` | A new character picks a joinable team with `teams join <team>`. Players not on a locked team may switch later. |
| `AutoBalance` | The server puts a new character on the joinable team with the fewest members. |

"New character" means a character the server has no stored entry for.

## Console commands (F5)

- `teams`: your team and the team list
- `teams join <team>`: pick a team when the server allows it
- Admins only: `teams who`, `teams assign <player> <team>`, `teams forget <player|id:N>`

The server decides everything; commands only ask. Admin means the server's `adminlist.txt`, or the host on a listen server.

## What is stored (privacy)

The server keeps **one file, `members.json`**, with one entry per character that has a team:

| Field | Meaning |
|-------|---------|
| `characterId` | The character's player ID (`Player.GetPlayerID()`), a number the game generates per character |
| `teamId` | Which team |
| `lastSeen` | UTC date (`yyyy-MM-dd`), the last day that character joined |

That is all. **No Steam / platform ID, no account name, no IP, no character name, no location.** The character ID is not shown anywhere in the game. Character IDs are never written to the log (team changes log team names and the character name only), never sent to other clients, and never stored client-side. A client only ever receives its own team and the team list (id, name, color, flags).

- Entries not seen for `PurgeAfterDays` days (default 90, `0` = never) are deleted automatically.
- An admin can delete a player's entry at any time: `teams forget <player>` (or `id:N` for an offline character).
- `teams.json` holds team definitions only, no personal data.
- Because the key is the character, not the person: a player with a new character is a new entry. (Keying on the account for locked-team servers is a possible later option; it is not in 0.1.0.)

## For other mods

```csharp
string? team = TeamsAPI.GetTeamId(player);
bool same   = TeamsAPI.AreSameTeam(a, b);
TeamsAPI.RegisterOnTeamChanged((playerId, oldTeam, newTeam) => { });
```

The server (dedicated server or host) answers for any player. A plain client only knows its **own** team: other players read as unassigned there. See `docs/design.md` for why and what Phase 2 does about it.

## Files

`BepInEx/config/DrakeMods.ServerTeams/teams.json` (definitions), `members.json` (above), and the usual `com.drakemods.serverteams.cfg` for settings.
