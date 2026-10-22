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
- **Libs is not a dependency (changed from the original brief).** Phase 1 used only `DrakeConfigSync`, one HUD-message call and an empty CompatHost, so config now syncs through Jotunn (`IsAdminOnly`) and messages use `MessageHud`. If Libs is needed later (LockSmith/RenameIt adapters), it comes in as a **soft dependency** isolated in one class, and no UI from Libs is needed: Teams has no UI beyond console commands.
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

## Decisions for Phase 2 (made with the owner)
- **Teams are visible on players.** The owning client publishes its team id on its player ZDO (`TeamsAPI.TeamZdoKey`); `TeamsAPI.GetTeamId(Player)` / `AreSameTeam(Player, Player)` read it on any client. It is a display and hint value: a modified client can fake its own. Character IDs still never leave the server.
- **Vocabulary.** In player-facing text "Team" means the server team. LockSmith's existing per-chest Personal/**Team** guest list is renamed **Group** (its code already says `GroupAccessState`, `GroupStore`, `locksmith_group_mode`). Note this "Group" is LockSmith's per-chest list, not Smoothbrain Groups parties. The LockSmith rename is its own LockSmith release.

## What reading LockSmith 0.6.0-beta.3 changed
- `IWardCompatModule` / `LockSmithCompatApi.Register` is **not** the team hook. It models ward *stacks* (vanilla, WardIsLove, ProtectiveWards...): "is this point inside an enabled ward, and what access does a player have". A Teams module there would only answer "ward coverage", not "same team as the owner".
- The real decision points are: `PrivateAreaAccess.IsPermitted(area, playerId)` (ward permitted list, reflection wrapper because the live members are private), `GroupAccessState.HasTeamAccess(container, playerId)` and `PieceGuestAccess.IsGuest(nview, playerId)` (chest/door guest lists), and `Patches/PrivateAreaPatches.cs`, `ContainerAccessPatches.cs`, `DoorAccessPatches.cs`.
- All of them decide by **creator ID** (`Piece.GetCreator()`, a stored player ID). Clients must not receive character IDs, and in Valheim the check often runs on the *owner peer's* client, not the server. So "is the creator on my team" cannot be answered from IDs on an arbitrary client.
- **Proposed fix: stamp the creator's team on the piece.** When a player places a ward/chest/door, their client writes `creatorTeam` (team id) on the piece ZDO next to the creator ID. The check becomes "my team == piece.creatorTeam", needs no IDs, works on any peer, and reads the same value on hover. Trade-offs: pieces keep the team they were placed under (moving teams does not transfer them; wards/chests of a player who changes teams should probably stop matching, so compare against the stamp, not the creator's current team); a modified client could stamp a fake team on its own pieces, which only affects its own pieces.
- Proposed LockSmith change (separate LockSmith release): add a "Team" access option on chests/doors/wards that is true when `TeamsAPI.GetTeamId(player) == piece.creatorTeam`, provided by a soft-dependency class (LockSmith never hard-requires Teams). Teams ships the stamping patch only while LockSmith or RenameIt is present (dormant otherwise).
- RenameIt: not read yet. Same question for "whoever can build/remove".

## Open for Phase 2
- Approve the creator-team stamp above (or choose another way to answer "same team as owner" without IDs).
- Whether a ward's permitted list should also accept "my team" (vanilla wards have no creator-team concept beyond the creator and the permitted list).
- Libs could gain `DrakePlayerIdentity.IsAdminPeer(ZNetPeer)`; Teams has its own copy in `PeerIdentity` because changing Libs is a separate release.

## Not verified against the live game
Built against the Pfhoenix reference stubs only (what CI uses). Not decompiled from a Valheim 1.0 install, not run in game. Members to confirm: `Player.OnSpawned` (patched), `ZNet.GetPeer/GetPeers/IsServer/IsDedicated`, `ZNetPeer.m_uid/m_characterID/m_playerName/m_socket`, `ZDOMan.instance` (a property in the stubs), `ZDO.GetLong(int,long)` and the `"playerID"` ZDO key, `ZRoutedRpc.GetServerPeerID`, `ISocket.GetHostName`, `ZNet.m_adminList`/`ListContainsId` (reflection), `Console.instance`/`Print`. Jotunn `NetworkManager.AddRPC` / `CustomRPC.SendPackage` to the host's own peer ID (listen server) is also untested.
