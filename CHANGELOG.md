# DrakesServerTeams

## 0.1.0
- First build: **Phase 1 (core)**.
- Admin-defined teams in `BepInEx/config/DrakeMods.ServerTeams/teams.json` (a starter red/blue file is written on first run). Each team: id, display name, color, `Joinable`, `Locked`.
- Server-only `members.json`: character ID, team ID, last-seen date. Nothing else. Idle entries are purged after `PurgeAfterDays` (default 90).
- Assignment modes (synced config `AssignmentMode`): `AdminOnly`, `PlayerChoice`, `AutoBalance`.
- `Locked` teams: a player on one cannot leave on their own; an admin or the API can still move them.
- Console command `teams` (F5): `teams`, `teams join <team>`; admins: `teams who`, `teams assign <player> <team>`, `teams forget <player|id:N>`.
- `TeamsAPI`: `GetTeamId`, `GetTeam`, `GetTeams`, `AreSameTeam`, `TryAssign`, `Forget`, `RegisterOnTeamChanged` (authority), `RegisterOnOwnTeamChanged` (client).
- Config through DrakeModsLibs `DrakeConfigSync`. Requires DrakeModsLibs 0.11.2.
- The player's team is published on their ZDO (display hint); `TeamsAPI.GetTeamId(Player)` / `AreSameTeam(Player, Player)` work on clients for any visible player.
- Not yet: compat with LockSmith / RenameIt, name hiding, PvP rules, team wards, spawn points, banners, creature factions.
