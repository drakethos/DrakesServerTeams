"""Writes the DrakesTestLab suite for DrakesServerTeams into Tests/suites/DrakesServerTeams (run: python gen_teams_suite.py).
The build copies it into the test profiles; Tests/ is never packaged."""
import json, os, shutil

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'suites', 'DrakesServerTeams')
GUID = 'com.drakemods.serverteams'
NOERR = {'check': 'noErrors', 'source': 'DrakesServerTeams'}
# exceptions thrown inside Harmony patches (nameplates, graves, spawn) are logged by Unity, not by the mod's own logger
NOERR_UNITY = {'check': 'noErrors', 'source': 'Unity Log'}


def H(name, expect=None, label=None, contains=None, stop=False, **args):
    s = {'do': 'hook', 'name': 'teams.' + name}
    if args:
        s['args'] = args
    for k, v in (('expect', expect), ('contains', contains), ('label', label)):
        if v is not None:
            s[k] = v
    if stop:
        s['stop'] = True
    return s


def C(values):
    return {'do': 'setConfig', 'values': values}


def play(who):
    return {'do': 'playAs', 'who': who}


def wait(s=0.3):
    return {'do': 'wait', 'seconds': s}


def on_team(who, team):
    return [play(who), H('assign', expect=team, label=f'{who} is on {team}', team=team)]


def alice_builds(piece='piece_chest_wood', tag='chest', team='red'):
    """Alice (on a team) builds the piece with the hammer: real placement, so the stamp request path runs."""
    return [H('usePieceTeams', expect=True), *on_team('Alice', team),
            {'do': 'place', 'prefab': piece, 'dz': 2.5, 'as': tag}, wait(0.6)]


tests = {}


def T(tid, title, steps, desc=''):
    tests[tid] = {'name': f'{tid} {title}', 'description': desc, 'steps': steps + [play('me')], 'asserts': [NOERR, NOERR_UNITY]}


T('T-01', 'admin assignment and the published team', [
    *on_team('Alice', 'red'),
    H('myTeam', expect='red', label='Alice reads her own team'),
    H('guardNow', expect='red', label='the server publishes red on her player'),
    *on_team('Alice', 'blue'),
    H('myTeam', expect='blue', label='an admin move takes effect'),
    H('forget', expect='', label='forget leaves her without a team'),
])

T('T-02', 'players pick once; switching is admin-only unless allowed', [
    C({'1 Assignment/AssignmentMode': 'PlayerChoice', '1 Assignment/PlayersCanSwitchTeams': 'false'}),
    play('Bob'),
    H('forget'),
    H('selfJoin', expect='ok', label='Bob (no team) picks red', team='red'),
    H('selfJoin', contains='Only an admin', label='Bob cannot switch to blue himself', team='blue'),
    H('myTeam', expect='red', label='Bob is still red'),
    C({'1 Assignment/PlayersCanSwitchTeams': 'true'}),
    H('selfJoin', expect='ok', label='with PlayersCanSwitchTeams on, Bob switches to blue', team='blue'),
    H('myTeam', expect='blue'),
    C({'1 Assignment/AssignmentMode': 'AdminOnly', '1 Assignment/PlayersCanSwitchTeams': 'false'}),
    H('selfJoin', contains='cannot pick', label='AdminOnly: players cannot pick at all', team='red'),
])

T('T-03', 'a built chest carries its builder\'s team', [
    *alice_builds(),
    H('pieceTeam', expect='red', label='the server stamped the chest red', target='chest'),
    H('isOnPieceTeam', expect=True, label='Alice is on the chest\'s team', target='chest'),
    *on_team('Bob', 'blue'),
    H('isOnPieceTeam', expect=False, label='Bob (blue) is not', target='chest'),
    *on_team('Carol', 'red'),
    H('isOnPieceTeam', expect=True, label='Carol (red) is', target='chest'),
])

T('T-04', 'pieces keep the team they were built under', [
    *alice_builds(),
    *on_team('Alice', 'blue'),
    H('checkPiece', expect='red', label='after Alice moves to blue, the server keeps the chest red', target='chest'),
    H('isOnPieceTeam', expect=False, label='so blue Alice is no longer on her old chest\'s team', target='chest'),
])

T('T-05', 'the server removes a stamp it did not sign', [
    *alice_builds(),
    *on_team('Bob', 'blue'),
    H('forgeStamp', expect='blue', label='a modified client stamps Alice\'s chest blue', target='chest', team='blue'),
    H('checkPiece', expect='red', label='the server puts back the builder\'s team', target='chest'),
    H('isOnPieceTeam', expect=False, label='Bob has no team access afterwards', target='chest'),
])

T('T-06', 'pieces from before the mod get their builder\'s team', [
    *alice_builds(),
    H('clearStamp', expect='', label='remove the stamp (as on a pre-mod chest)', target='chest'),
    H('checkPiece', expect='red', label='the server stamps it with Alice\'s team', target='chest'),
])

T('T-07', 'the full sweep fixes forged stamps', [
    *alice_builds(),
    H('forgeStamp', expect='blue', target='chest', team='blue'),
    H('sweepNow', expect=True, label='start a sweep of the whole world'),
    wait(5),
    H('sweeping', expect=False, label='the sweep finished'),
    H('pieceTeam', expect='red', label='the sweep restored the chest', target='chest'),
])

T('T-08', 'a faked published team does not last', [
    *on_team('Alice', 'red'),
    H('fakePublishedTeam', expect='blue', label='a modified client publishes blue', team='blue'),
    H('guardNow', expect='red', label='the server puts red back'),
])

T('T-09', 'wards and doors are stamped too', [
    H('usePieceTeams', expect=True),
    *on_team('Alice', 'red'),
    {'do': 'place', 'prefab': 'wood_door', 'dz': 2.5, 'as': 'door'},
    {'do': 'place', 'prefab': 'guard_stone', 'dx': 3, 'dz': 2.5, 'as': 'ward'},
    wait(0.6),
    H('pieceTeam', expect='red', label='the door is red', target='door'),
    H('pieceTeam', expect='red', label='the ward is red', target='ward'),
])

# ---- several players: Alice is the local player (play-as), Bjornita / Bob / Amy join as virtual players ------------

RED_SPAWN = (100, 35, 100)
BLUE_SPAWN = (-100, 35, -100)
GREEN_SPAWN = (100, 35, -100)


def xyz(p):
    return f'{p[0]},{p[1]},{p[2]}'


def fresh_teams():
    """Known starting point: no stored members, no spawn points, no third team left over from another test."""
    return [H('setDeathRules', enabled=False), H('removeTeam', team='green'), H('clearSpawn', team='red'), H('clearSpawn', team='blue'), H('clearMembers'),
            C({'1 Assignment/AssignmentMode': 'AdminOnly', '1 Assignment/PlayersCanSwitchTeams': 'false', '1 Assignment/MoveToSpawnOnFirstTeam': 'false'})]


def four_players():
    """Alice (local, play-as) plus three virtual players at different spots; everyone is in the world afterwards."""
    return [play('Alice'),
            {'do': 'peerJoin', 'name': 'Bjornita', 'dx': 3, 'dz': 0},
            {'do': 'peerJoin', 'name': 'Bob', 'dx': -3, 'dz': 0},
            {'do': 'peerJoin', 'name': 'Amy', 'dx': 0, 'dz': 4}]


def assign(who, team):
    return H('assignTo', expect=team, label=f'{who} -> {team}', player=who, team=team)


def spawn_is(who, spawn, label=None):
    return H('spawnOf', expect=spawn if spawn == 'none' else xyz(spawn), label=label or f'{who} spawns at {spawn}', player=who)


def set_spawn(team, p):
    return H('setSpawn', expect=xyz(p), label=f'{team} spawn point {p}', team=team, x=p[0], y=p[1], z=p[2])


T('T-10', 'four players, two teams: all placed, all told apart', [
    *fresh_teams(), *four_players(),
    assign('Alice', 'red'), assign('Bjornita', 'red'), assign('Bob', 'blue'), assign('Amy', 'blue'),
    H('teamOf', expect='red', label='Alice is red', player='Alice'),
    H('teamOf', expect='red', label='Bjornita is red', player='Bjornita'),
    H('teamOf', expect='blue', label='Bob is blue', player='Bob'),
    H('teamOf', expect='blue', label='Amy is blue', player='Amy'),
    H('sameTeam', expect=True, label='Alice and Bjornita are teammates', a='Alice', b='Bjornita'),
    H('sameTeam', expect=True, label='Bob and Amy are teammates', a='Bob', b='Amy'),
    H('sameTeam', expect=False, label='Alice and Bob are opponents', a='Alice', b='Bob'),
    H('sameTeam', expect=False, label='Bjornita and Amy are opponents', a='Bjornita', b='Amy'),
    H('memberCount', expect=2, label='two on red', team='red'),
    H('memberCount', expect=2, label='two on blue', team='blue'),
    H('guardNow', expect='red', label='the server publishes Alice as red'),
    H('publishedTeamOf', expect='red', label='Bjornita\'s character shows red to other clients', player='Bjornita'),
    H('publishedTeamOf', expect='blue', label='Bob\'s character shows blue', player='Bob'),
    H('publishedTeamOf', expect='blue', label='Amy\'s character shows blue', player='Amy'),
], 'Alice is the local player; the three others are virtual players that joined through the game\'s own connection code.')

T('T-11', 'moving a virtual player changes only that player', [
    *fresh_teams(), *four_players(),
    assign('Alice', 'red'), assign('Bjornita', 'red'), assign('Bob', 'blue'), assign('Amy', 'blue'),
    assign('Bjornita', 'blue'),
    H('teamOf', expect='blue', label='Bjornita moved to blue', player='Bjornita'),
    H('teamOf', expect='red', label='Alice is still red', player='Alice'),
    H('teamOf', expect='blue', label='Bob is still blue', player='Bob'),
    H('myTeam', expect='red', label='the local client\'s own team did not follow Bjornita\'s state'),
    H('memberCount', expect=1, label='one left on red', team='red'),
    H('memberCount', expect=3, label='three on blue', team='blue'),
])

T('T-12', 'each team spawns at its own point', [
    *fresh_teams(), *four_players(),
    set_spawn('red', RED_SPAWN), set_spawn('blue', BLUE_SPAWN),
    assign('Alice', 'red'), assign('Bjornita', 'red'), assign('Bob', 'blue'), assign('Amy', 'blue'),
    spawn_is('Alice', RED_SPAWN), spawn_is('Bjornita', RED_SPAWN),
    spawn_is('Bob', BLUE_SPAWN), spawn_is('Amy', BLUE_SPAWN),
    H('sharedSpawns', expect=0, label='no two teams share a spawn point'),
    assign('Bjornita', 'blue'),
    spawn_is('Bjornita', BLUE_SPAWN, 'Bjornita moved to blue and now spawns at blue\'s point'),
    spawn_is('Alice', RED_SPAWN, 'Alice\'s spawn did not change'),
])

T('T-13', 'a team without a spawn point, and a player without a team, use the default', [
    *fresh_teams(), *four_players(),
    set_spawn('red', RED_SPAWN),
    assign('Alice', 'red'), assign('Bob', 'blue'),
    spawn_is('Alice', RED_SPAWN),
    spawn_is('Bob', 'none', 'blue has no spawn point: Bob gets the game\'s default'),
    spawn_is('Amy', 'none', 'Amy has no team: the game\'s default'),
])

T('T-14', 'two teams sharing a spawn point are flagged', [
    *fresh_teams(),
    set_spawn('red', RED_SPAWN), set_spawn('blue', RED_SPAWN),
    H('sharedSpawns', expect=1, label='red and blue share a point'),
    set_spawn('blue', BLUE_SPAWN),
    H('sharedSpawns', expect=0, label='giving blue its own point clears it'),
])

T('T-15', 'a third team: four players, three teams, three spawn points', [
    *fresh_teams(), *four_players(),
    H('defineTeam', expect=3, label='green joins red and blue', team='green', title='Green', color='#27AE60',
      x=GREEN_SPAWN[0], y=GREEN_SPAWN[1], z=GREEN_SPAWN[2]),
    set_spawn('red', RED_SPAWN), set_spawn('blue', BLUE_SPAWN),
    assign('Alice', 'red'), assign('Bjornita', 'blue'), assign('Bob', 'green'), assign('Amy', 'green'),
    spawn_is('Alice', RED_SPAWN), spawn_is('Bjornita', BLUE_SPAWN),
    spawn_is('Bob', GREEN_SPAWN), spawn_is('Amy', GREEN_SPAWN),
    H('sharedSpawns', expect=0, label='three teams, three different points'),
    H('sameTeam', expect=True, label='Bob and Amy are teammates', a='Bob', b='Amy'),
    H('sameTeam', expect=False, label='Alice and Bjornita are not', a='Alice', b='Bjornita'),
    H('removeTeam', expect=True, label='remove green again', team='green'),
    H('teamOf', expect='', label='Bob is unassigned once green is gone', player='Bob'),
    spawn_is('Bob', 'none', 'and spawns at the default'),
])

T('T-16', 'auto-balance puts four players 2 and 2', [
    *fresh_teams(), *four_players(),
    C({'1 Assignment/AssignmentMode': 'AutoBalance'}),
    H('autoAssign', expect='red', label='Alice (first) -> red', player='Alice'),
    H('autoAssign', expect='blue', label='Bjornita -> blue', player='Bjornita'),
    H('autoAssign', expect='red', label='Bob -> red', player='Bob'),
    H('autoAssign', expect='blue', label='Amy -> blue', player='Amy'),
    H('memberCount', expect=2, label='two on red', team='red'),
    H('memberCount', expect=2, label='two on blue', team='blue'),
])

T('T-17', 'auto-balance with three teams fills each before doubling up', [
    *fresh_teams(), *four_players(),
    H('defineTeam', expect=3, team='green', title='Green', color='#27AE60'),
    C({'1 Assignment/AssignmentMode': 'AutoBalance'}),
    H('autoAssign', expect='red', player='Alice'),
    H('autoAssign', expect='blue', player='Bjornita'),
    H('autoAssign', expect='green', player='Bob'),
    H('autoAssign', expect='red', label='the fourth doubles up on the first team', player='Amy'),
    H('memberCount', expect=2, team='red'),
    H('memberCount', expect=1, team='blue'),
    H('memberCount', expect=1, team='green'),
    H('removeTeam', expect=True, team='green'),
])

T('T-18', 'the local client spawns at its own team\'s point (real spawn lookup)', [
    *fresh_teams(), *four_players(),
    set_spawn('red', RED_SPAWN), set_spawn('blue', BLUE_SPAWN),
    assign('Bjornita', 'blue'),
    assign('Alice', 'red'),
    wait(1.0),
    H('clientSpawn', expect=xyz(RED_SPAWN), label='the client was told red\'s point'),
    H('findSpawn', expect=xyz(RED_SPAWN), label='Game.FindSpawnPoint returns red\'s point for Alice'),
    assign('Alice', 'blue'),
    wait(1.0),
    H('findSpawn', expect=xyz(BLUE_SPAWN), label='after moving to blue it returns blue\'s point'),
], 'Bjornita is blue the whole time: her state must not leak into Alice\'s client.')

T('T-19', 'admins set and clear a team spawn point where they stand', [
    *fresh_teams(),
    {'do': 'teleport', 'x': 40, 'z': 55},
    H('adminCommand', contains='now spawns at', label='teams setspawn red', line='setspawn red'),
    play('Alice'),
    H('assignTo', expect='red', player='Alice', team='red'),
    H('spawnOf', contains='40,', label='red spawns where the admin stood', player='Alice'),
    H('adminCommand', contains='Unknown team', label='an unknown team is refused', line='setspawn purple'),
    H('adminCommand', contains='no longer', label='teams clearspawn red', line='clearspawn red'),
    spawn_is('Alice', 'none', 'red has no spawn point again'),
])

T('T-20', 'a first team moves the player to the team spawn point, a later change does not', [
    *fresh_teams(), *four_players(),
    C({'1 Assignment/MoveToSpawnOnFirstTeam': 'true'}),
    set_spawn('red', RED_SPAWN), set_spawn('blue', BLUE_SPAWN),
    H('clearTeleports'),
    assign('Bob', 'red'),
    H('teleportOrdered', expect=xyz(RED_SPAWN), label='Bob (new) is sent to the red point', player='Bob'),
    H('clearTeleports'),
    assign('Bob', 'blue'),
    H('teleportOrdered', expect='none', label='moving Bob to blue later does not move him', player='Bob'),
    assign('Alice', 'blue'),
    wait(4),
    H('myPosition', contains='-100,', label='Alice (new) actually arrived at the blue point'),
])

T('T-21', 'MoveToSpawnOnFirstTeam off: the team spawn only applies on respawn', [
    *fresh_teams(), *four_players(),
    C({'1 Assignment/MoveToSpawnOnFirstTeam': 'false'}),
    set_spawn('red', RED_SPAWN),
    H('clearTeleports'),
    assign('Bob', 'red'),
    H('teleportOrdered', expect='none', label='no move ordered', player='Bob'),
    spawn_is('Bob', RED_SPAWN, 'but red is still where Bob respawns'),
])

RED_HEX = '#C0392B'   # the starter teams.json colors
BLUE_HEX = '#2980B9'

T('T-22', 'players glow in their team color', [
    *fresh_teams(), *four_players(),
    C({'4 Visuals/TeamGlow': 'true'}),
    assign('Alice', 'red'), assign('Bjornita', 'blue'),
    wait(1.2),
    H('glowOf', expect=RED_HEX, label='Alice glows red', player='Alice'),
    H('glowOf', expect=BLUE_HEX, label='Bjornita glows blue', player='Bjornita'),
    H('glowOf', expect='none', label='Amy has no team: no glow', player='Amy'),
    C({'4 Visuals/TeamGlow': 'false'}),
    wait(1.2),
    H('glowOf', expect='none', label='the server switched glow off', player='Bjornita'),
    C({'4 Visuals/TeamGlow': 'true'}),
])

T('T-23', 'nameplates show team color and icon', [
    *fresh_teams(), *four_players(),
    C({'4 Visuals/TeamNameColor': 'true', '4 Visuals/TeamIcon': 'true'}),
    assign('Bjornita', 'blue'),
    {'do': 'lookAt', 'target': 'Bjornita'},
    wait(1.5),
    H('nameplateOf', contains=BLUE_HEX + '|icon ' + BLUE_HEX, label='the plate of Bjornita: blue name, blue icon', player='Bjornita'),
    C({'4 Visuals/TeamIcon': 'false'}),
    wait(0.5),
    H('nameplateOf', expect=BLUE_HEX + '|no icon', label='icon off: name stays blue', player='Bjornita'),
    C({'4 Visuals/TeamNameColor': 'false', '4 Visuals/TeamIcon': 'true'}),
    {'do': 'hook', 'name': 'teams.assignTo', 'args': {'player': 'Bjornita', 'team': 'red'}, 'expect': 'red'},
    wait(0.5),
    H('nameplateOf', contains='|icon ' + RED_HEX, label='moved to red: the icon follows', player='Bjornita'),
    C({'4 Visuals/TeamNameColor': 'true'}),
])

T('T-24', 'graves carry the team of the player who died', [
    *fresh_teams(), *four_players(),
    C({'4 Visuals/TeamGraveMarker': 'true'}),
    H('removeGraves'),
    wait(0.5),
    assign('Bjornita', 'blue'),
    H('makeGrave', expect='blue', label='her grave is written blue', player='Bjornita'),
    wait(1.2),
    H('graveInfo', expect='blue|' + BLUE_HEX + '|' + BLUE_HEX + '|icon|' + BLUE_HEX, label='name text, light, icon and the base band in blue'),
    H('removeGraves'),
    wait(0.5),
    C({'4 Visuals/TeamGraveMarker': 'false'}),
    H('makeGrave', expect='blue', label='marker off, new blue grave', player='Bjornita'),
    wait(1.0),
    H('graveInfo', contains='|none|none', label='marker off: no light, no icon'),
    C({'4 Visuals/TeamGraveMarker': 'true'}),
    H('removeGraves'),
    wait(0.5),
    H('makeGrave', expect='', label='Amy has no team: a plain grave', player='Amy'),
    wait(1.0),
    H('graveInfo', contains='|none|none', label='no light, no icon'),
    H('removeGraves'),
])

T('T-25', 'admins change a team icon', [
    *fresh_teams(),
    H('adminCommand', contains='icon: star', label='teams seticon red star', line='seticon red star'),
    H('setIcon', expect='shield', label='an unknown shape falls back to the default', team='red', icon='banana'),
    H('setIcon', expect='cross', team='red', icon='cross'),
    H('adminCommand', contains='Unknown team', line='seticon purple star'),
    H('setIcon', expect='shield', team='red', icon='shield'),
])

# ---- death rules: Alice (red) dies with a bag of items; the killer decides what happens --------------------------

def bag():
    """Coins 500, Wood 50, Flint 20 and an equipped bronze axe, with nothing else on the ground or in graves."""
    return [{'do': 'clearInventory'}, H('clearGround'),
            {'do': 'give', 'prefab': 'Coins', 'count': 500},
            {'do': 'give', 'prefab': 'Wood', 'count': 50},
            {'do': 'give', 'prefab': 'Flint', 'count': 20},
            {'do': 'give', 'prefab': 'AxeBronze', 'equip': True},
            wait(0.5)]


def count(hook, item, n, label=None):
    return H(hook, expect=n, label=label or f'{hook} {item} = {n}', item=item)


def rules(**kw):
    return H('setDeathRules', **kw)


def die(killer, outcome=None):
    # the player walks away like a respawn would: standing on the drop picks the coins straight back up
    return [H('simulateDeath', expect=outcome, label=f'killed by {killer}', killer=killer), {'do': 'teleport', 'dx': 30}, wait(0.3)]


T('T-26', 'death rules: killed by the enemy team keeps gear and drops coins for the killer', [
    *fresh_teams(), play('Alice'), H('assignTo', team='red'),
    rules(enabled=True, appliesTo='EnemyTeamKill', keepEquipped=True, lootable='Coins', percent=100),
    *bag(),
    *die('blue', 'keep=1 drop=1 grave=2'),
    count('invCount', 'AxeBronze', 1, 'the equipped axe stays with Alice'),
    H('invEquipped', expect=True, label='and is marked worn again', item='AxeBronze'),
    count('invCount', 'Coins', 0, 'her coins left her'),
    count('invCount', 'Wood', 0, 'her wood left her'),
    count('groundCount', 'Coins', 500, 'all 500 coins lie on the ground for the killer'),
    count('graveCount', 'Wood', 50, 'wood is in the grave'),
    count('graveCount', 'Flint', 20, 'flint is in the grave'),
    count('graveCount', 'AxeBronze', 0, 'the axe is not in the grave'),
    H('clearGround'),
])

T('T-27', 'death rules: a teammate\'s kill, or a monster, changes nothing', [
    *fresh_teams(), play('Alice'), H('assignTo', team='red'),
    rules(enabled=True, appliesTo='EnemyTeamKill', keepEquipped=True, lootable='Coins', percent=100),
    *bag(),
    *die('red'),
    count('invCount', 'AxeBronze', 0, 'killed by a teammate: normal grave, the axe went too'),
    count('graveCount', 'Coins', 500, 'coins are in the grave'),
    count('groundCount', 'Coins', 0, 'nothing on the ground'),
    *bag(),
    *die('monster'),
    count('invCount', 'AxeBronze', 0, 'killed by a monster: normal grave'),
    count('graveCount', 'Coins', 500),
    H('clearGround'),
])

T('T-28', 'death rules: AppliesTo AnyPlayerKill and Always', [
    *fresh_teams(), play('Alice'), H('assignTo', team='red'),
    rules(enabled=True, appliesTo='AnyPlayerKill', keepEquipped=True, lootable='Coins', percent=100),
    *bag(), *die('red'),
    count('invCount', 'AxeBronze', 1, 'AnyPlayerKill: even a teammate\'s kill applies the rules'),
    *bag(), *die('monster'),
    count('invCount', 'AxeBronze', 0, 'AnyPlayerKill: a monster does not'),
    rules(enabled=True, appliesTo='Always', keepEquipped=True, lootable='Coins', percent=100),
    *bag(), *die('monster'),
    count('invCount', 'AxeBronze', 1, 'Always: a monster\'s kill applies the rules too'),
    H('clearGround'),
])

T('T-29', 'death rules: only a share of the loot is dropped', [
    *fresh_teams(), play('Alice'), H('assignTo', team='red'),
    rules(enabled=True, appliesTo='EnemyTeamKill', keepEquipped=True, lootable='Coins', percent=50),
    *bag(), *die('blue'),
    count('groundCount', 'Coins', 250, '50 percent of the coins are on the ground'),
    count('graveCount', 'Coins', 250, 'the other half is in the grave'),
    H('clearGround'),
])

T('T-30', 'death rules: Keep list by name and by type', [
    *fresh_teams(), play('Alice'), H('assignTo', team='red'),
    rules(enabled=True, appliesTo='EnemyTeamKill', keepEquipped=False, keep='Flint,type:OneHandedWeapon', lootable='Coins', percent=100),
    *bag(), *die('blue', 'keep=2 drop=1 grave=1'),
    count('invCount', 'Flint', 20, 'Flint stays (by name)'),
    count('invCount', 'AxeBronze', 1, 'the axe stays (by type OneHandedWeapon) even though KeepEquipped is off'),
    count('invCount', 'Wood', 0, 'Wood is not on the list'),
    count('graveCount', 'Wood', 50),
    count('groundCount', 'Coins', 500),
    H('clearGround'),
])

T('T-31', 'death rules: off by default changes nothing', [
    *fresh_teams(), play('Alice'), H('assignTo', team='red'),
    rules(enabled=False, appliesTo='Always', keepEquipped=True, lootable='Coins'),
    *bag(), *die('blue'),
    count('invCount', 'AxeBronze', 0, 'switched off: everything goes to the grave'),
    count('graveCount', 'Coins', 500),
    count('graveCount', 'AxeBronze', 1),
    H('clearGround'),
])

T('T-32', 'death rules: admins switch them on and off', [
    *fresh_teams(),
    H('adminCommand', contains='Death rules: off', label='shows the rules (off)', line='deathrules'),
    H('adminCommand', contains='Death rules: on', label='teams deathrules on', line='deathrules on'),
    H('adminCommand', contains='Death rules: off', label='teams deathrules off', line='deathrules off'),
    H('adminCommand', contains='Usage', label='a bad word is refused', line='deathrules maybe'),
])

T('T-33', 'a team can have its own glow color (falls back to the team color)', [
    *fresh_teams(), *four_players(),
    assign('Alice', 'red'), assign('Bjornita', 'blue'),
    wait(1.0),
    H('glowOf', expect=RED_HEX, label='no GlowColor: the glow is the team color', player='Alice'),
    H('setGlow', expect='#00FF00', label='red gets a green glow', team='red', color='#00FF00'),
    wait(1.0),
    H('glowOf', expect='#00FF00', label='Alice now glows green', player='Alice'),
    H('glowOf', expect=BLUE_HEX, label='blue is unchanged', player='Bjornita'),
    H('adminCommand', contains='glow: #FF8800', label='teams setglow red #FF8800', line='setglow red #FF8800'),
    H('adminCommand', contains='Use a color', label='a bad color is refused', line='setglow red banana'),
    H('adminCommand', contains='follows the team color', label='teams setglow red none', line='setglow red none'),
    wait(1.0),
    H('glowOf', expect=RED_HEX, label='back to the team color', player='Alice'),
])

T('T-34', 'the grave light uses the glow color, the name and icon keep the team color', [
    *fresh_teams(), *four_players(),
    H('removeGraves'), wait(0.5),
    H('setGlow', team='blue', color='#00FF00'),
    assign('Bjornita', 'blue'),
    H('makeGrave', expect='blue', player='Bjornita'),
    wait(1.2),
    H('graveInfo', expect='blue|' + BLUE_HEX + '|#00FF00|icon|#00FF00', label='green light and band, blue name text'),
    H('setGlow', team='blue', color=''),
    H('removeGraves'),
])

T('T-35', 'colors by name or hex, icons by shape or by file', [
    *fresh_teams(), *four_players(),
    H('parseColor', expect='#F1C40F', label='a color by name', text='gold'),
    H('parseColor', expect='#FF8800', label='short hex', text='#f80'),
    H('parseColor', expect='#2980B9', label='full hex without the #', text='2980b9'),
    H('parseColor', expect='invalid', label='nonsense is refused', text='banana'),
    H('iconName', expect='star', label='built-in, any case', icon='STAR'),
    H('iconName', expect='banner', label='a file name (the .png is optional)', icon='Banner.png'),
    H('iconName', expect='shield', label='an unsafe name falls back to the default', icon='../secret'),
    H('iconKind', expect='missing', label='no such file yet', icon='sample-crest'),
    H('writeSampleIcon', expect='sample-crest.png', label='drop a PNG into the icons folder', icon='sample-crest'),
    H('iconKind', expect='file', label='now it is found', icon='sample-crest'),
    H('iconKind', expect='builtin', icon='diamond'),
    H('adminCommand', contains='color: #F1C40F', label='teams setcolor red gold', line='setcolor red gold'),
    H('adminCommand', contains='Use a color', label='a bad color is refused', line='setcolor red banana'),
    H('adminCommand', contains='color: #C0392B', label='put red back', line='setcolor red #C0392B'),
    H('adminCommand', contains='icon: sample-crest', label='teams seticon blue sample-crest', line='seticon blue sample-crest'),
    assign('Bjornita', 'blue'),
    {'do': 'lookAt', 'target': 'Bjornita'},
    wait(1.5),
    H('nameplateOf', contains='|icon #FFFFFF', label='a custom icon keeps its own colors (not tinted)', player='Bjornita'),
    H('adminCommand', contains='icon: shield', label='back to the shield', line='seticon blue shield'),
    H('deleteSampleIcon', expect=True, icon='sample-crest'),
])

T('T-36', 'a team can have its own copy of the start stones (opt-in)', [
    *fresh_teams(),
    H('hasStartLocation', expect=True, label='the game has a StartTemple location to copy'),
    H('adminCommand', contains='no spawn point yet', label='stones need a spawn point first', line='spawnstones red on'),
    {'do': 'teleport', 'dz': 60},
    wait(2.5),
    H('adminCommand', contains='now spawns at', label='stand on open ground and set the spawn of red', line='setspawn red'),
    H('stonesNear', expect=False, label='no stones there yet', team='red'),
    H('adminCommand', contains='start stones placed', label='teams spawnstones red on', line='spawnstones red on'),
    wait(1.0),
    H('teamSpawn', contains='', label='asked for', team='red'),
    H('stonesPlacedAt', contains='', label='the game put them at'),
    H('stonesNear', expect=True, label='a start location now stands at the spawn point of red', team='red'),
    H('ensureStones', expect='exists', label='asking again places nothing more', team='red'),
    H('adminCommand', contains='no new start stones', label='teams spawnstones red off', line='spawnstones red off'),
    H('stonesNear', expect=True, label='stones already placed stay in the world', team='red'),
    H('adminCommand', contains='Usage', label='a bad word is refused', line='spawnstones red maybe'),
    H('adminCommand', contains='no longer has a spawn point', label='clean up the spawn point', line='clearspawn red'),
])

def ward_setup():
    """Alice (red, admin) puts down a Team Ward; Bjornita (blue), Bob (red) and Amy (no team) are in the world."""
    return [*fresh_teams(), *four_players(),
            H('setAdmin', on=True), H('setWardDefault', what='build', on=True), H('setWardDefault', what='chests', on=True), H('setWardDefault', what='doors', on=False),
            assign('Alice', 'red'), assign('Bjornita', 'blue'), assign('Bob', 'red'),
            {'do': 'place', 'prefab': 'TeamWard', 'dz': 3, 'as': 'ward'}, wait(1.0)]


T('T-40', 'the Team Ward is in the Hammer menu and an admin can place it', [
    H('hasWardPiece', expect=True, label='TeamWard is a Hammer piece'),
    *ward_setup(),
    H('pieceTeam', expect='red', label='the server stamped it with the builder team', target='ward'),
])

T('T-41', 'team members get through, others are stopped, per kind', [
    *ward_setup(),
    H('wardPermits', expect=True, label='Alice (builder) may build', ward='ward', player='Alice', kind='build'),
    H('wardPermits', expect=True, label='Bob (same team) may build', ward='ward', player='Bob', kind='build'),
    H('wardPermits', expect=False, label='Bjornita (blue) may not build', ward='ward', player='Bjornita', kind='build'),
    H('wardPermits', expect=False, label='Amy (no team) may not build', ward='ward', player='Amy', kind='build'),
    H('wardPermits', expect=False, label='Bjornita may not open chests', ward='ward', player='Bjornita', kind='chest'),
    H('wardPermits', expect=True, label='doors are open to her by default', ward='ward', player='Bjornita', kind='door'),
    H('wardPermits', expect=True, label='Bob opens doors', ward='ward', player='Bob', kind='door'),
    H('wardFlags', expect='on/on/off', label='build/chests/doors defaults', ward='ward'),
    H('setWardDefault', expect='ProtectDoors=True', label='server turns door protection on', what='doors', on=True),
    H('wardPermits', expect=False, label='now Bjornita cannot open doors', ward='ward', player='Bjornita', kind='door'),
    H('setWardDefault', what='doors', on=False),
])

T('T-42', 'per ward overrides beat the server default', [
    *ward_setup(),
    H('adminCommand', contains='Team Ward of Red', label='teams ward describes it', line='ward'),
    H('adminCommand', contains='doors: protected', label='this ward protects doors', line='ward doors on'),
    H('wardPermits', expect=False, label='Bjornita cannot open its doors', ward='ward', player='Bjornita', kind='door'),
    H('adminCommand', contains='chests: open to everyone', label='this ward leaves chests open', line='ward chests off'),
    H('wardPermits', expect=True, label='Bjornita can open its chests', ward='ward', player='Bjornita', kind='chest'),
    H('adminCommand', contains='build: open to everyone', label='this ward lets anyone build', line='ward build off'),
    H('wardPermits', expect=True, label='Bjornita can build', ward='ward', player='Bjornita', kind='build'),
    H('adminCommand', contains='server default', label='back to the default', line='ward build inherit'),
    H('wardPermits', expect=False, label='blocked again', ward='ward', player='Bjornita', kind='build'),
    H('adminCommand', contains='Use on, off or inherit', label='a bad word is refused', line='ward doors maybe'),
])

T('T-43', 'an admin changes the ward team; its look follows', [
    *ward_setup(),
    wait(1.0),
    H('wardLook', contains='Red|#C0392B|icon|#C0392B', label='red label, glow, icon and light', ward='ward'),
    H('adminCommand', contains='Team Ward team: Blue', label='teams ward team blue', line='ward team blue'),
    H('pieceTeam', expect='blue', label='the stamp changed', target='ward'),
    wait(1.0),
    H('wardLook', contains='Blue|#2980B9|icon', label='blue now', ward='ward'),
    H('wardPermits', expect=True, label='Bjornita is now on the ward team', ward='ward', player='Bjornita', kind='build'),
    H('wardPermits', expect=False, label='Bob is not', ward='ward', player='Bob', kind='build'),
    H('adminCommand', contains='Unknown team', line='ward team purple'),
    H('adminCommand', contains='team: none', label='teams ward team none', line='ward team none'),
])

T('T-44', 'only admins can place a Team Ward', [
    *fresh_teams(), play('Alice'),
    H('setAdmin', on=False),
    {'do': 'place', 'prefab': 'TeamWard', 'dz': 3, 'as': 'refused', 'expect': False},
    H('setWardDefault', what='admin', on=False),
    {'do': 'place', 'prefab': 'TeamWard', 'dz': 3, 'as': 'allowed'},
    H('setWardDefault', what='admin', on=True),
    H('setAdmin', on='auto'),
])

T('T-45', 'console: assign me / all, forget me', [
    *fresh_teams(), *four_players(),
    H('adminCommand', contains='now on Red', label='teams assign me red', line='assign me red'),
    H('teamOf', expect='red', label='that was Alice', player='Alice'),
    H('adminCommand', contains='now on Blue', label='teams assign Bjornita blue', line='assign Bjornita blue'),
    H('adminCommand', contains='player(s) are now on Blue', label='teams assign all blue', line='assign all blue'),
    H('teamOf', expect='blue', label='Bob followed', player='Bob'),
    H('teamOf', expect='blue', label='and Alice', player='Alice'),
    H('adminCommand', contains='Deleted the stored entry', label='teams forget me', line='forget me'),
    H('teamOf', expect='', label='Alice has no team', player='Alice'),
    H('adminCommand', contains='Unknown team', label='an unknown team is refused', line='assign all purple'),
    H('labTabRegistered', expect='yes', label='the Teams tab is in the DrakesTestLab toolbox'),
])

T('T-46', 'Shift+E opens the Team Ward settings for admins only', [
    *ward_setup(),
    H('wardInteract', expect=False, label='plain E does not open the window', ward='ward', alt=False),
    H('wardInteract', expect=True, label='an admin pressing Shift+E opens it', ward='ward', alt=True),
    H('closeWardPanel', expect=False, label='closing works'),
    H('setAdmin', on=False),
    H('wardInteract', expect=False, label='a player who is not an admin cannot', ward='ward', alt=True),
    H('setAdmin', on=True),
    H('wardHover', contains='Shift+E', label='admins see the hint on the ward', ward='ward'),
    assign('Alice', 'blue'),
    H('setAdmin', on=False),
    H('wardHover', contains='protected', label='others just see it is protected', ward='ward'),
    H('setAdmin', on=True),
])

T('T-47', 'the serverteams-spawns case: me on Red, Bjornita on Blue, two spawn points', [
    {'do': 'case', 'name': 'serverteams-spawns'},
    wait(3.0),
    H('teamOf', expect='red', label='I am on Red', player='me'),
    H('teamOf', expect='blue', label='Bjornita joined and is on Blue', player='Bjornita'),
    H('teamSpawn', contains='-25,', label='Red spawn x', team='red'),
    H('teamSpawn', contains=',20', label='Red spawn z', team='red'),
    H('teamSpawn', contains='25,', label='Blue spawn x', team='blue'),
    H('spawnOf', contains='-25,', label='my spawn is the Red point', player='me'),
    H('spawnOf', contains='25,', label='Bjornita spawns at the Blue point', player='Bjornita'),
    H('clearSpawn', team='red'), H('clearSpawn', team='blue'),
])

T('T-48', 'first-join team picker: offered once, the server remembers, admins can offer again', [
    *fresh_teams(), *four_players(),
    H('setMode', expect='PlayerChoice', mode='PlayerChoice'),
    H('helloOffer', expect=True, label='Bob (new, no team) is offered the picker', player='Bob'),
    H('helloOffer', expect=False, label='the server wrote the flag: not offered again', player='Bob'),
    H('assignTo', expect='red', label='Bob picks a team (the picker button asks the server for this)', player='Bob', team='red'),
    H('adminCommand', contains='picker shown to 1', label='teams offer Bob: team cleared, picker shown again', line='offer Bob'),
    H('teamOf', expect='', label='Bob is back to no team', player='Bob'),
    H('setMode', expect='AutoBalance', mode='AutoBalance'),
    H('helloOffer', expect=False, label='AutoBalance never shows the picker', player='Amy'),
    H('teamOf', expect='red', label='Amy was auto-assigned the smallest team', player='Amy'),
    H('setMode', expect='AdminOnly', mode='AdminOnly'),
])

suite = {
    'name': 'DrakesServerTeams',
    'description': 'Automated regression for DrakesServerTeams: assignment and switching rules, server-signed piece teams on wards, '
                   'chests and doors (stamp on build, keep on team change, backfill, forged stamps removed) and the server '
                   'correcting faked published teams. Players are simulated with play-as (Alice, Bob, Carol); the multi-player tests (T-10..T-18) add virtual players (Bjornita, Bob, Amy): four players on two or three teams, unique spawn points per team, auto-balance.',
    'requireMods': [{'guid': GUID, 'minVersion': '0.0.1'}],
    'configOverrides': [{'guid': GUID, 'section': k.split('/')[0], 'key': k.split('/')[1], 'value': v} for k, v in {
        '1 Assignment/AssignmentMode': 'AdminOnly', '1 Assignment/PlayersCanSwitchTeams': 'false',
    }.items()],
    'setup': {'timeOfDay': 0.5, 'freezeTime': 1, 'god': 1, 'noBuild': 'off', 'hideUnlockPopups': 1},
    'launchConfig': [{'guid': 'com.drakemods.testlab', 'section': 'Case', 'key': 'OnLoad', 'value': ''}],
    'resetBetweenTests': True,
}

if os.path.isdir(OUT):
    shutil.rmtree(OUT)
os.makedirs(OUT)
with open(os.path.join(OUT, 'suite.json'), 'w', encoding='utf-8') as f:
    json.dump(suite, f, indent=2, ensure_ascii=False)
for tid, t in tests.items():
    with open(os.path.join(OUT, f'{tid}.json'), 'w', encoding='utf-8') as f:
        json.dump(t, f, indent=2, ensure_ascii=False)
print(len(tests), 'DrakesServerTeams tests')
