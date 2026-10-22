# DrakesServerTeams: design and scope

Rule for this doc: if a feature is not listed under a phase, it is not in that phase.

## What it is
Server-wide **teams** (2+ sides). Admins define them; players are assigned by config or API. Other mods ask "what team is this player on?" and "are these two on the same team?".

Not a guild or party system. Guilds (Smoothbrain) and Groups stay as they are. LockSmith/RenameIt permission checks can accept any of Team, Guild, Group, each from its own provider.

## Phases (gate between each)
1. **Core**: team file, registry, assignment (at character creation / admin command / server API), locked vs changeable, `TeamsAPI` + `TeamChanged` event, synced config via `DrakeConfigSync`.
2. **Compat**: team option in LockSmith (ward/lock access) and RenameIt ("whoever can build/remove" gains "same team"). One `ICompatModule` per mod, registered on this plugin's `CompatHost`.
3. **Identity display**: per-surface name visibility (nameplate, map, chat, pings, join/leave text, tombstones). Team color + emblem replace names. Teammates may still see names (config). Force-share location with teammates.
4. **PvP rules**: team-vs-team on, same-team protection (own Harmony patch, small).
5. **Team wards**: no opt-in, admin-placed or recipe-defined. Capture mechanics later; CTF is a separate mod on this API.
6. **Spawn points** per team.
7. **Worn banner** as a visual attachment (not an inventory item).
8. **Creature factions**: bridge to CreatureManager `factions.yml`. Last, highest drift risk.

## Not in v1
Database, Discord libraries inside the mod, creature reputation, ship protection, patching Smoothbrain Guilds, an RP "introduction" mechanic.

## Storage and privacy
- Key = character player ID (long). No Steam/platform ID stored. Optional config to key on account instead (locked teams).
- Per player: character ID, team ID, last-seen date. Purge after N days (config). Admin command to delete a player's entry.
- Server is the only authority. Clients receive their own team and teammates' display data, never IDs.
- Do not log IDs. README documents exactly what is stored.
- Note: Libs `DrakePlayerIdentity.Keys` and `VipList` already use platform host IDs for VIPs. Teams deliberately does not.

## Discord
Out of process. Teams emits events and accepts server-side commands (optional local webhook/endpoint later). A separate bot maps teams to Discord roles or the reverse.

## Guilds mirror (optional adapter, off by default, decide after prototype)
Smoothbrain Guilds API (v1.1.x) allows `CreateGuild`, `AddPlayerToGuild`, `RemovePlayerFromGuild`, join/leave events and `Get/SetCustomData`. Gaps: no closed-guild flag, `CreateGuild` needs a leader. Mirror only for free friendly-fire, colors and map markers for third-party mods; core never depends on it. Calls isolated in one class behind a soft dependency.

## Conventions followed (from DrakeModsLibs)
- Consumer of Libs: no ServerSync reference, no ILRepack of Libs; config via `CustomizeLibsAPI.CreateConfigSync`.
- One `CompatHost` per plugin GUID; soft deps never hard-required.
- Features stay dormant until used; no always-on patches for unused features.
- Version policy: see `Vahleim/Mod/CLAUDE.md` (local; not read yet).

## Open questions
- LockSmith hook point: handover doc names `IWardCompatModule` and `LockSmithCompatApi`; LockSmith source needed.
- Csproj/Gale deploy: copy from RenameIt's csproj (not available yet).
- Does attaching a model to the player visuals work cleanly (banner)? Needs decompile check.

## Phase 1 status (0.1.0): implemented
Files: `Core/` (definitions, registry, JSON store), `Config/TeamsConfig.cs`, `Net/` (one Jotunn RPC, peer identity, admin commands, client state), `Commands/TeamsCommand.cs`, `Patches/PlayerSpawnPatch.cs`, `Api/TeamsAPI.cs`.

Decisions made while building it (change them here if wrong):
- **Two files.** `teams.json` (definitions, admin-edited, no personal data) and `members.json` (character ID, team ID, last-seen date). Unity `JsonUtility`, no extra JSON dependency.
- **`Locked` means the player cannot leave on their own.** Admin commands and the API (`TryAssign`) always work, they ignore `Joinable` and `Locked`. `Joinable` only limits self-service.
- **Assignment modes:** `AdminOnly` (default), `PlayerChoice`, `AutoBalance`. "New character" = no stored entry. There is no character-creation screen hook in v1: the first spawn is the moment, because the character ID only exists once the player's ZDO does.
- **Identity is read server-side from the sender's player ZDO**, never from the message, so a client cannot claim another character's ID.
- **No in-game picker UI yet:** console commands only (`teams join <team>`).
- **Account keying** (config option in the brief) is **not** in 0.1.0. It needs a different key type end to end and a policy for what a "same person, new character" does.
- **Privacy self-service erase** is admin-mediated (`teams forget`). A player-run erase would bypass `Locked`; decide before adding it.

## Open for Phase 2 (decide before starting)
- **Clients only know their own team.** That honors "clients get only their own team and teammates' display data". But LockSmith/RenameIt hover text ("can I open this ward?") runs on the client and needs "is the ward owner on my team". Options: (a) team checks only on the server, client UI shows a neutral hint; (b) the server sends each client an opaque per-session token set for its teammates; (c) publish the team id on every player's ZDO (also what Phase 3 name-hiding and Phase 4 PvP need, but every client then sees every visible player's team). (c) is the simplest and probably wanted for PvP; it is a privacy decision, so it is yours.
- LockSmith hook points (`IWardCompatModule`, `LockSmithCompatApi`) have not been read yet: the LockSmith repo is available but was out of scope for Phase 1.
- Libs could gain `DrakePlayerIdentity.IsAdminPeer(ZNetPeer)`; Teams has its own copy in `PeerIdentity` because changing Libs is a separate release.

## Not verified against the live game
Built against the Pfhoenix reference stubs only (what CI uses). Not decompiled from a Valheim 1.0 install, not run in game. Members to confirm: `Player.OnSpawned` (patched), `ZNet.GetPeer/GetPeers/IsServer/IsDedicated`, `ZNetPeer.m_uid/m_characterID/m_playerName/m_socket`, `ZDOMan.instance` (a property in the stubs), `ZDO.GetLong(int,long)` and the `"playerID"` ZDO key, `ZRoutedRpc.GetServerPeerID`, `ISocket.GetHostName`, `ZNet.m_adminList`/`ListContainsId` (reflection), `Console.instance`/`Print`. Jotunn `NetworkManager.AddRPC` / `CustomRPC.SendPackage` to the host's own peer ID (listen server) is also untested.
