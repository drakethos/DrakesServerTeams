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
- Version policy: `Vahleim/Mod/CLAUDE.md`: never change a version number unless the owner asks.

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

## Phase 2 decisions (owner, 2026-10-22)
- **Approved: piece team stamp, with server validation.** Only the server writes it; it is `team|HMAC(server key, creator, team)`. ZDO ids are renumbered on every world load (`ZDO.Load` → `m_uid.SetID(++m_loadID)`), so they are not signed. Clients only read the team part.
- **Backfill (owner: "whatever makes most sense"):** an unstamped piece gets its builder's current team on the next sweep. A piece whose builder has no team stays unstamped until they get one.
- **Pieces keep their team** when the builder changes team (the signature still verifies). Team changes are expected to be rare: `PlayersCanSwitchTeams` (default off) makes switching admin-only after the first pick.
- **Ward permitted list accepts "my team"** (LockSmith side).
- The server also corrects every player's published team (`TeamZdoKey`) every 5 s, so the requester's team that a chest's owner peer reads is trustworthy within a few seconds.
- Limit (same as vanilla): access checks run on the client, so a modified client can still ignore locks entirely. Validation keeps honest clients from being fed fake teams.

## Open for Phase 2
- Approve the creator-team stamp above (or choose another way to answer "same team as owner" without IDs).
- Whether a ward's permitted list should also accept "my team" (vanilla wards have no creator-team concept beyond the creator and the permitted list).
- Libs could gain `DrakePlayerIdentity.IsAdminPeer(ZNetPeer)`; Teams has its own copy in `PeerIdentity` because changing Libs is a separate release.

## Not verified against the live game
Built against the Pfhoenix reference stubs only (what CI uses). Not decompiled from a Valheim 1.0 install, not run in game. Members to confirm: `Player.OnSpawned` (patched), `ZNet.GetPeer/GetPeers/IsServer/IsDedicated`, `ZNetPeer.m_uid/m_characterID/m_playerName/m_socket`, `ZDOMan.instance` (a property in the stubs), `ZDO.GetLong(int,long)` and the `"playerID"` ZDO key, `ZRoutedRpc.GetServerPeerID`, `ISocket.GetHostName`, `ZNet.m_adminList`/`ListContainsId` (reflection), `Console.instance`/`Print`. Jotunn `NetworkManager.AddRPC` / `CustomRPC.SendPackage` to the host's own peer ID (listen server) is also untested.

## Spawn points (Phase 6, started 2026-10-25)
- A team may have `HasSpawn` + `SpawnX/Y/Z` in `teams.json`. Admins set it in game: `teams setspawn <team>` (where the admin stands), `teams clearspawn <team>`. Saved to `teams.json`, pushed to every connected client.
- `Game.FindSpawnPoint` postfix: team point replaces the world-start default only. A bed and the logout point win.
- `MoveToSpawnOnFirstTeam` (synced, default on): a character that gets its FIRST team is teleported to the team point. Later moves do not teleport.
- Two teams within 1 m of each other log a warning (each team should have its own).
- Test: DrakesServerTeams suite T-10..T-21 (four players via Alice + Bjornita/Bob/Amy virtual players, two and three teams, auto-balance, spawn lookup, admin commands).

## Ideas from the owner (2026-10-25), not built; order to be decided
1. **Admin in-game tools**: add / move players between teams (exists: `teams assign`); a UI later.
2. **First-join choice**: on a brand-new character show a team picker (or auto-assign). Assignment sources a server may want, as pluggable "providers":
   - admin / config (today), player choice, auto-balance (today);
   - **whitelist-driven**: a file `whitelist, player name, team`;
   - **Discord role** mapping (out of process bot, see Discord section);
   - **Server Characters** mod link (store the team with the server-side character), or a special tag stored on the character itself. Needs a look at that mod's API before choosing.
3. **Visual identity** (Phase 3 expands): team color glow on the player, team-colored name / text, faction icon above the nameplate, grave distinction (team-colored tombstone / marker).
4. **Death and looting rules** per team pairing: gear stays with the dead player, but enemies may loot gold and a configurable set of droppables. Needs a definition (item prefabs / categories / % ) in config, and a patch on tombstone / inventory drop.

Open questions before 2-4: where team data for the whitelist provider lives (file next to teams.json is simplest); whether the picker needs Libs UI (UI Toolkit redesign in progress) or stays console-first; which loot rules are per team vs global.

## Admin GUI (owner, 2026-10-25)
Console stays the base; a command (e.g. `teams admin`) can also open a GUI for admins: team list, members, assign / move players, spawn points, icons, assignment mode. Not built. It should sit on top of the same server-checked commands (no new authority path). UI toolkit choice depends on the UI Toolkit redesign (Libs vs. standalone copy; Teams has no Libs dependency today).

## Visuals (Phase 3 start, 2026-10-25)
- Team `Icon` in `teams.json` (shield, circle, diamond, star, cross; drawn at runtime, no art shipped); admin command `teams seticon`.
- Other players: nameplate name in team color + icon above it; a soft team-colored light on every player with a team.
- Graves: the dying player's client writes the team on the grave; everyone sees a team light, floating icon, colored name text and a team line in the hover text.
- Synced admin settings (section `4 Visuals`): TeamNameColor, TeamIcon, TeamGlow, TeamGlowRange, TeamGraveMarker. All client-side display only (a modified client can fake its own look; nothing is enforced by it).

## Team Ward (owner brief 2026-10-26; Phase 5), plan
A custom ward ("Team Ward") for team play and later capture-the-flag. Built at runtime from vanilla `guard_stone` (Jotunn clone + material swap, no art shipped, no AssetForge dependency; same recipe idea as AssetForge's "drakesis_guard_stone" with the obsidian material).

**Look.** Obsidian material instead of the gold/dwarf one so it cannot be mistaken for a normal ward. Rune glow and a point light in the owning team's glow color, team label (name in team color + icon) floating above it, like players and graves.

**Placing.** Admin only by default (`TeamWardAdminOnly`, synced). The team of a new ward = the placer's team (admin can change it).

**Access.** Anyone on the ward's team is permitted (build, open, use inside it); everyone else is blocked like a normal ward. It ignores LockSmith's per-player lists and menus: only the owner or an admin changes its settings, from its own TeamWard panel (Shift+E). First version of the panel can be console/hover toggles; the proper tab comes with the admin GUI.

**Capture (per ward flags + global defaults, both server-synced).**
- `Capturable` flag, set by an admin (Shift+E).
- Hold capture: an enemy-team player channels the interact key for `CaptureSeconds` (default 30) without interruption; leaving range, taking a hit, or letting go resets it. On success the ward's team flips (stamp rewritten by the server) and colors/label follow.
- Capture health: optional `CaptureHealth`. Enemy damage lowers it; at 0 the ward becomes capturable / flips (mode per ward: Hold, Damage, Both). Same-team players repair by channeling interact (slow heal); a hit interrupts.
- Conditions (later): only while the defenders are away, cooldown after capture, etc.

**Server authority.** The server owns the capture timer and the team stamp; clients only send "I am channeling" and see progress. Reuses the existing signed piece-team stamp.

**Phases.** A: piece + look + admin-only placing + team access. B: Shift+E flags (capturable, team, times). C: hold capture. D: capture health + repair. E: per-ward vs global configs and the panel UI. Each phase gets TestLab tests (virtual players as capturers/defenders) and a screenshot pass.

**What a Team Ward protects (owner, 2026-10-26).** Per ward and as a global default (server-synced), each switch is on/off:
- `BlockEnemyBuild`: enemies cannot hammer (place, repair, remove) inside it. On by default.
- `ProtectChests`: enemies cannot open chests inside. On by default.
- `ProtectDoors`: enemies cannot open doors inside. Off by default, so a team can leave doors open to raiders and still keep chests safe.
- Further switches follow the same pattern (crafting stations, beds, ships, item stands). A per-ward value overrides the global default; "inherit" means use the global one. Team members always have access.

## Scope split: Teams vs. "Server Teams - War" (owner, 2026-10-26)
- **DrakesServerTeams (this mod) ships:** configurable colored Team Wards. Team label and glow, admin-only placing (setting), team members always have access, enemies are blocked from hammer (build/repair/remove) and from chests by default; doors and other things switchable (per ward + global). Nothing capturable, no wars.
- **Server Teams - War (separate sub-mod, same GitHub repo, depends on the Teams API):** capture-the-flag wards (hold-to-capture, capture health, repair channel), crumbling/rebuildable structures under a ward, war windows, the wall rules below. Teams gets only the hooks War needs (ward team change, ward-protection query) and nothing War-specific.

### The wall problem (open, owner to pick)
Vanilla wards make structures inside them immune to player damage, so a team can wall in its flag and no one can ever reach it. Options written up in the chat: crumble instead of destroy, shield phase, war windows, clear zone around the flag. Not built.

**Chosen (2026-10-26): Crumble + Shield phase.** Designed together in the War extension:
- *Shield phase:* a flag ward has a shield (capture health). Shield up: structures inside are immune to enemies, as in vanilla. Shield broken: protection drops for a siege window (`SiegeMinutes`), then the shield regenerates (or the flag flips if captured).
- *Crumble:* while protection is down, a piece that reaches 0 health inside the ward does not get deleted. It crumbles: broken visual state, colliders off (walkable), stays in the world. Only an authorized team member (hammer) can remove or repair it. Repair rules (no cheese): material cost, channel time per piece, per-piece cooldown after a crumble, and no repair while an enemy is within N m or the ward was hit in the last few seconds.
- Open for the build: whether crumbled pieces count toward the build-piece limit, and whether crumbled pieces can be re-crumbled while still down (no).

## War spike results (2026-10-26, debug-only code in Spikes/, suite DrakesServerTeams-Spike)
Measured in the live game with a vanilla ward carrying a team stamp (no custom Team Ward piece needed yet):
- **Where ward immunity lives:** NOT in `WearNTear.Damage`. A hit sent straight to a piece inside an enabled ward lands fully (400 -> 275 for a 50 blunt + chop hit), same as outside. The immunity is in the attack layer, so shield/crumble can hook `WearNTear.ApplyDamage` / `Destroy` / `Repair` (Harmony prefixes) without fighting it.
- **Shield (works):** state on the ward ZDO (`DrakesWar_shield`, `DrakesWar_shieldDownAt`). Enemy-team hits on pieces inside the ward lower the shield and the piece stays untouched; at 0 the ward is DOWN and pieces take damage; after `down` seconds the shield is back to max (lazy regen on read). Same-team hits are vanilla.
- **Crumble (works):** `Destroy` prefix for enemy hits while the shield is down: set `DrakesWar_crumbled`, health 1, colliders off (pieces have one non-trigger collider in `m_colliders`), return false so the piece is not deleted. Further hits ignored. A small component re-applies the collider state on every client from the ZDO (late joiners too). Aiming tools cannot hit a crumbled piece (no collider), which is expected.
- **Repair rules (work):** `WearNTear.Repair` prefix: refuse unless the player is on the ward's team, refuse during `cooldown` after the crumble, then restore only `fraction` (30%) of max health.
- **Not yet:** server-authoritative writes (spike writes the ward ZDO from whoever hits; the real version routes through the server), shield flash/sound/HUD bar, repair channel + material cost, "no repair while enemies near", crumbled broken-look screenshot (state exists, picture not captured), interplay with LockSmith and other ward mods, multi-ward overlap.
- **Spawn stones note:** SpawnLocationMidGame snaps to a fixed spot per zone, so a team's spawn point moves to wherever the stones really land.
