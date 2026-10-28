# DrakesServerTeams

## 0.1.0-beta.4

- **Packaging fix:** the zip now ships the DLL under `plugins/` so Gale and r2modman keep the folder layout (they flatten anything outside `plugins/`). A build guard (`check-gale-layout.py`) fails the release if that regresses. No code or gameplay change.

## 0.1.0-beta.3
- **Start stones are now experimental and OFF** (`7 Experimental/SpawnStonesExperimental`). The game places a copy of the start location with a mid-game terrain edit, which can leave seams and height jumps between zones, and it is saved in the world. `teams spawnstones` explains this and does nothing until the setting is on. Do not enable it on a world you care about.
- **The admin panel has tabs** (Players, Teams, Ward, Server). Shift+E on a Team Ward opens the same panel on the Ward tab for that ward (the separate ward window is gone).
- **Fix:** panels now block the game's attack, look and scroll input, so clicking a button no longer punches or turns the camera.
- Coexists with RenameIt: the Team Ward registers with DrakeModsLibs 1.0.3 (`PieceInteractionClaims`), so RenameIt's Shift+Use rename window leaves it alone (needs a RenameIt release that checks the claim).

## 0.1.0-beta.2
- **Fix:** the build referenced `Piece.SetCreator(long)`, which the current game version no longer has (Hexium flagged beta.1 as broken on it). The only use was the test-bench "Place a Team Ward in front of me" button; it no longer calls it. New mod icon.

## 0.1.0-beta.1
**First public beta: early preview, not thoroughly tested. Back up your world and characters before using it.**
- **First-join team picker** (`AssignmentMode` = PlayerChoice, setting `FirstJoinPicker`, default on): a character with no team gets a window listing the joinable teams the first time it loads in. The server writes a flag (`offered` in `members.json`) so it only appears once. `teams pick` reopens it; admins can show it again with `teams offer <player|all>`.
- **Shift+E on a Team Ward** opens its settings window (admins): team, protect build / chests / doors (yes / no / server default). Plain E shows the ward's team and what it protects. Extensions add sections with `TeamsAPI.RegisterTeamWardSection`.
- Screenshot gallery (`docs/gallery.md`, pictures taken by the `DrakesServerTeams-Visuals` Test Lab suite).
- **Admin panel**: `teams panel` in the console or the panel key (config `6 Admin panel/PanelKey`, default F6). Put any player on a team (or none), set team spawn points and start stones, edit team color / glow / icon, set a nearby Team Ward's team and protection switches, switch assignment mode and death rules. Every button sends the same command as the console and the server checks admin rights.
- Console: `teams assign me <team>`, `teams assign all <team>`, `teams forget me`, `teams panel`.
- **DrakesTestLab tab**: when DrakesTestLab is installed, a "Teams" tab in its toolbox (F9) shows the same panel plus "Spread everyone over the teams" and "Place a Team Ward in front of me". DrakesTestLab gained a small public API (`DrakesTestLab.TestLabTabs.Register`) so any mod can add a tab (found by name, no reference needed).
- **Team Ward** (Hammer > Misc, admin only): obsidian-dark guard stone, glows in the team's glow color, team icon and name above it, always on. Team members have access; others are blocked from building and chests (server defaults `5 Team Ward`: ProtectBuild, ProtectChests, ProtectDoors off), per ward overrides with `teams ward build|chests|doors on|off|inherit`, `teams ward team <team|none>`.
- Optional `GlowColor` per team ("#RRGGBB", empty = team color) for the player glow and the grave light; names and icons keep the team color. Admin: `teams setglow <team> <#RRGGBB|none>`.
- **Death rules** (off by default, `deathrules.json`): when a player dies to an enemy team (or any player, or always), equipped gear and a Keep list stay with them, an EnemyLootable list (default Coins, a percentage of each stack) drops on the ground for the killer, everything else goes into the grave. Admin: `teams deathrules [on|off]`. Rules sync to clients. Tests T-26..T-32.
- **Team spawn points** (Phase 6 start): `HasSpawn`/`SpawnX/Y/Z` per team in `teams.json`; members respawn there unless they have a bed or logout point. Admin commands `teams setspawn <team>` (where you stand) and `teams clearspawn <team>`. `TeamsAPI.GetSpawnPoint(playerId)`. Warns when two teams share a point.
- New synced setting `MoveToSpawnOnFirstTeam` (default on): a character that gets its first team is moved to the team's spawn point.
- Test Lab suite grows to 21 tests (T-10..T-21): four players (Alice + virtual Bjornita, Bob, Amy) on two and three teams, auto-balance, unique spawn points, admin spawn commands.
- **Piece teams** (Phase 2, Teams side): wards, chests and doors carry the team they were built under. Written and signed by the server; a sweep (`StampSweepSeconds`) stamps pieces from before the mod and replaces stamps the server did not sign. Pieces keep their team when the builder changes team. Dormant until a mod calls `TeamsAPI.UsePieceTeams()`.
- `TeamsAPI`: `UsePieceTeams`, `PieceTeamsActive`, `PieceTeamZdoKey`, `GetPieceTeamId`, `IsOnPieceTeam`. `GetTeamId(long)` on a client now also answers for visible players.
- The server corrects every player's published team every few seconds.
- New synced setting `PlayersCanSwitchTeams` (default off): in `PlayerChoice`, players pick once and only an admin moves them afterwards unless this is on.
- A client notices when its character changes (or a test tool swaps the player id) and asks the server for its team again.
- Fixed two calls that broke on Valheim 1.0 (`ZRoutedRpc.GetServerPeerID` is private; `MessageHud.ShowMessage` gained a parameter). Jotunn reference 2.30.0.
- Test Lab suite (`Tests/`, 9 tests) and `teams.*` test hooks.

## Earlier internal builds (before the first public beta)
- First build: **Phase 1 (core)**.
- Admin-defined teams in `BepInEx/config/DrakeMods.ServerTeams/teams.json` (a starter red/blue file is written on first run). Each team: id, display name, color, `Joinable`, `Locked`.
- Server-only `members.json`: character ID, team ID, last-seen date. Nothing else. Idle entries are purged after `PurgeAfterDays` (default 90).
- Assignment modes (synced config `AssignmentMode`): `AdminOnly`, `PlayerChoice`, `AutoBalance`.
- `Locked` teams: a player on one cannot leave on their own; an admin or the API can still move them.
- Console command `teams` (F5): `teams`, `teams join <team>`; admins: `teams who`, `teams assign <player> <team>`, `teams forget <player|id:N>`.
- `TeamsAPI`: `GetTeamId`, `GetTeam`, `GetTeams`, `AreSameTeam`, `TryAssign`, `Forget`, `RegisterOnTeamChanged` (authority), `RegisterOnOwnTeamChanged` (client).
- No DrakeModsLibs dependency. `AssignmentMode` syncs through Jotunn (admin-only entry).
- The player's team is published on their ZDO (display hint); `TeamsAPI.GetTeamId(Player)` / `AreSameTeam(Player, Player)` work on clients for any visible player.
- Not yet: compat with LockSmith / RenameIt, name hiding, PvP rules, team wards, spawn points, banners, creature factions.
