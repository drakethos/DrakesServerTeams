# DrakesServerTeams

Server-wide **teams** for Valheim: two or more sides defined by the server admin (Horde / Alliance style).
Part of DrakeMods.

**Teams** are server-wide sides (this mod). **Guilds** are player-formed (Smoothbrain Guilds). **Groups** are parties (Smoothbrain Groups). This mod does not replace or patch either.

> **Status: Phase 1 (core) only.** Teams exist, players can be assigned, other mods can ask. Nothing in the world reacts to teams yet (no ward/lock integration, name hiding, PvP rules, spawn points).

## Setup

Install on the server and on every client (it is network-checked). Needs BepInEx and Jotunn only; DrakeModsLibs is not required.

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
| `PlayerChoice` | A new character picks a joinable team with `teams join <team>`. After that only an admin moves them, unless `PlayersCanSwitchTeams` is on (then players not on a locked team may switch). |
| `AutoBalance` | The server puts a new character on the joinable team with the fewest members. |

"New character" means a character the server has no stored entry for.

## Piece teams (wards, chests, doors)

When a mod that uses them is installed (LockSmith), every ward, chest and door carries the **team it was built under**. Other mods use it for "same team as the builder" access.

- **The server writes it**, signed with a key only the server has (`stamp.key` in the teams folder). The server re-checks every stamped piece at most every `StampSweepSeconds` (default 60): a stamp it did not sign is replaced with the builder's team, so a modified client cannot give its team someone else's chest.
- **Pieces keep their team** when their builder changes team.
- **Pieces built before the mod** (or while the builder had no team) get the builder's current team on the next check.
- The stamp holds the team id only. Deleting `stamp.key` re-stamps everything with each builder's current team.

The team each player shows to other clients is also corrected by the server every few seconds.

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

Piece teams (call `TeamsAPI.UsePieceTeams()` once at startup to turn them on):

```csharp
string? pieceTeam = TeamsAPI.GetPieceTeamId(nview);
bool teammate     = TeamsAPI.IsOnPieceTeam(playerId, nview);
```

The server (dedicated server or host) answers for any player from its registry. A client knows its own team and the published team of players it can see (kept honest by the server). See `docs/design.md`.

## Files

`BepInEx/config/DrakeMods.ServerTeams/teams.json` (definitions), `members.json` (above), `stamp.key` (server secret for piece teams, no personal data), and the usual `com.drakemods.serverteams.cfg` for settings.
