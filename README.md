# DrakesServerTeams

Server-wide **teams** for Valheim: two or more sides defined by the server admin (Horde / Alliance style).
Part of DrakeMods.

**Teams** are server-wide sides (this mod). **Guilds** are player-formed (Smoothbrain Guilds). **Groups** are parties (Smoothbrain Groups). This mod does not replace or patch either.

> [!WARNING]
> ### ⚠️ 0.1.0 beta: brand new, early preview, not thoroughly tested
> This is the **first public build** of DrakesServerTeams. It has only been tested in a single test world with one real player and simulated players, never on a busy server. Expect bugs, rough edges and changes between betas.
>
> **Use it at your own risk.**
> - **Back up your world and your characters before installing** (the `worlds_local` folder and your character files), and do not try it first on a world you cannot afford to lose.
> - It must be on the server **and** every client (it is network-checked), so everyone needs the same version.
> - Teams, members and Team Wards are saved in the world and in the mod's own files (`BepInEx/config/DrakeMods.ServerTeams/`). Removing the mod later leaves Team Wards behind as unknown pieces.
> - Please report problems with your `LogOutput.log` on the [GitHub issues](https://github.com/drakethos/DrakesServerTeams/issues) page.

## What it does

- **Teams** defined by the admin in `teams.json` (name, color, icon, glow color). Assigned by an admin, picked by players, or auto-balanced.
- **Visuals:** names in the team color with the team icon above them (built-in shapes or your own PNG), a soft team glow, and team-marked graves.
- **Spawn points** per team (members respawn there, or are moved there when they first join a team), and an opt-in copy of the start stones at a team's spawn.
- **Team Ward:** an obsidian guard stone, admin-placed, that glows in its team's color. Its team has access; others are kept out of building and chests (doors are a switch). Shift+E opens its settings.
- **Death rules** (off by default): keep gear, drop coins or chosen items for the killer.
- **Admin tools:** the `teams` console command and an in-game admin panel (F6).
- **For other mods:** `TeamsAPI` (who is on which team, signed piece teams, spawn points, Team Ward hooks).

Not in this beta: capture-the-flag, wars and PvP rules (planned as a separate "War" extension), name hiding, Discord or whitelist assignment.

![Four players on four teams: name colors, icons and glow](https://raw.githubusercontent.com/drakethos/DrakesServerTeams/main/docs/images/players-day.png)
![Two Team Wards at night, glowing in their team colors](https://raw.githubusercontent.com/drakethos/DrakesServerTeams/main/docs/images/ward-night.png)

More pictures: [gallery](https://github.com/drakethos/DrakesServerTeams/blob/main/docs/gallery.md).

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
| `PlayerChoice` | A new character is shown a picker window the first time it loads in (`FirstJoinPicker`, on by default; `teams pick` reopens it) and can also use `teams join <team>`. After that only an admin moves them, unless `PlayersCanSwitchTeams` is on (then players not on a locked team may switch). |
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
- Admins: `teams panel` opens the admin panel (also the F6 key): players, teams, spawns, Team Ward, server settings
- `teams join <team>`: pick a team when the server allows it
- Admins only: `teams offer <player|all>` (show the team picker again), `teams who`, `teams assign <player> <team>`, `teams forget <player|id:N>`, `teams setspawn <team>` (uses where you stand), `teams clearspawn <team>`

The server decides everything; commands only ask. Admin means the server's `adminlist.txt`, or the host on a listen server.

## Spawn points

Each team can have its own spawn point. Stand where you want it and run `teams setspawn <team>` (admin); `teams clearspawn <team>` removes it. Members respawn there after death unless they have a bed or are logging back in. A character that gets its first team is moved there at once (`MoveToSpawnOnFirstTeam`, default on). Teams that share a point are logged as a warning.

## Death rules (optional)

Off by default. `BepInEx/config/DrakeMods.ServerTeams/deathrules.json` (a starter is written on first run); `teams deathrules on` turns it on.

| Field | Meaning |
|-------|---------|
| `AppliesTo` | `EnemyTeamKill` (killed by a player on another team, default), `AnyPlayerKill`, or `Always` |
| `KeepEquipped` | gear the player wears stays with them (they respawn with it) |
| `Keep` | more items that stay: a prefab name (`Flint`), `type:Material`, or `*` |
| `EnemyLootable` | dropped on the ground where they died, for the killer to take (default `Coins`) |
| `EnemyLootPercent` | share of each lootable stack dropped (1-100); the rest goes to the grave |

Anything not kept or lootable goes into the grave as usual. Keep beats lootable. Rules are applied by the dying player's client, so a modified client can ignore them (same limit as the rest of the mod).

## What is stored (privacy)

The server keeps **one file, `members.json`**, with one entry per character that has a team:

| Field | Meaning |
|-------|---------|
| `characterId` | The character's player ID (`Player.GetPlayerID()`), a number the game generates per character |
| `teamId` | Which team |
| `lastSeen` | UTC date (`yyyy-MM-dd`), the last day that character joined |
| `offered` | whether this character was already shown the first-join team picker |

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
