# DrakesServerTeams

## Unreleased
- **Piece teams** (Phase 2, Teams side): wards, chests and doors carry the team they were built under. Written and signed by the server; a sweep (`StampSweepSeconds`) stamps pieces from before the mod and replaces stamps the server did not sign. Pieces keep their team when the builder changes team. Dormant until a mod calls `TeamsAPI.UsePieceTeams()`.
- `TeamsAPI`: `UsePieceTeams`, `PieceTeamsActive`, `PieceTeamZdoKey`, `GetPieceTeamId`, `IsOnPieceTeam`. `GetTeamId(long)` on a client now also answers for visible players.
- The server corrects every player's published team every few seconds.
- New synced setting `PlayersCanSwitchTeams` (default off): in `PlayerChoice`, players pick once and only an admin moves them afterwards unless this is on.
- A client notices when its character changes (or a test tool swaps the player id) and asks the server for its team again.
- Fixed two calls that broke on Valheim 1.0 (`ZRoutedRpc.GetServerPeerID` is private; `MessageHud.ShowMessage` gained a parameter). Jotunn reference 2.30.0.
- Test Lab suite (`Tests/`, 9 tests) and `teams.*` test hooks.

## 0.1.0
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
