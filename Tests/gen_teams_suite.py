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
            C({'1 Assignment/AssignmentMode': 'AdminOnly', '1 Assignment/PlayersCanSwitchTeams': 'false', '1 Assignment/MoveToSpawnOnFirstTeam': 'false', '7 Experimental/SpawnStonesExperimental': 'false'})]


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
    H('setIcon', expect='shield', label='an invalid name falls back to the default (a plain name is a custom icon file)', team='red', icon='not a shape!'),
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

T('T-49', 'with RenameIt and DrakeModsLibs installed, Shift+E on a Team Ward is ours, not the rename window', [
    *ward_setup(),
    H('libsClaim', expect='DrakesServerTeams', label='Libs reports the Team Ward as claimed', ward='ward'),
    H('closeWardPanel', expect=False),
    {'do': 'lookAt', 'target': 'ward'},
    {'do': 'interact', 'target': 'ward', 'asPlayer': True, 'alt': True},
    wait(0.5),
    H('panelTab', expect='Ward', label='the Team Ward settings opened (Shift+E, through the real interact path)'),
    H('renameWindowOpen', expect='no', label='the RenameIt rename window did not'),
    H('closeWardPanel', expect=False),
])

T('T-50', 'start stones are off by default (experimental), the panel has tabs, and a panel blocks the game input', [
    *fresh_teams(), *four_players(),
    # start stones: experimental, off
    {'do': 'teleport', 'dz': 60}, wait(2.0),
    H('adminCommand', contains='now spawns at', label='set the red spawn by coordinates', line='setspawn red 20 20'),
    H('adminCommand', contains='experimental', label='teams spawnstones refuses while the setting is off', line='spawnstones red on'),
    H('ensureStones', contains='disabled', label='and nothing is placed', team='red'),
    H('stonesNear', expect=False, label='no start location appeared', team='red'),
    H('adminCommand', contains='no longer has a spawn point', line='clearspawn red'),
    # panel tabs
    H('panelTab', expect='closed', label='the panel starts closed'),
    H('openPanel', expect=True, open=True),
    H('panelTab', expect='Players', label='it opens on the Players tab'),
    H('openPanel', expect=False, open=False),
    # input: a click on a panel must not also be an attack
    H('gameTakesInput', expect='yes', label='the game reads attack and look input normally'),
    H('openPanel', expect=True, open=True),
    H('gameTakesInput', expect='no', label='with the panel open it does not (no punching, no camera)'),
    H('openPanel', expect=False, open=False),
    H('gameTakesInput', expect='yes', label='and it comes back when the panel closes'),
    # Shift+E opens the same panel on the Ward tab
    *ward_setup(),
    H('wardInteract', expect=True, label='Shift+E opens the admin panel', ward='ward', alt=True),
    H('panelTab', expect='Ward', label='on the Ward tab'),
    H('gameTakesInput', expect='no', label='and it also blocks the game input'),
    H('closeWardPanel', expect=False),
])

BASE = 'shield:#C0392B:#FFFFFF:0.06'
STAR = '|star:#FFFFFF:0:0:0.5:0:0'

T('T-51', 'logo designer: specs parse, render, and imported icons recolor or stay static', [
    *fresh_teams(),
    # parsing: round trip and refusals
    H('logoRoundTrip', expect='shield:#C0392B:#FFFFFF:0.06|star:#FFFFFF:0:0:0.5:0:0', label='a spec round-trips (colors upper-cased)', spec='shield:#c0392b:#ffffff:0.06|star:#ffffff:0:0:0.5:0:0'),
    H('logoRoundTrip', contains='invalid', label='a spec with a missing part is refused', spec='shield:#C0392B'),
    H('logoRoundTrip', contains='invalid', label='five symbols are too many', spec=BASE + STAR * 5),
    H('logoRoundTrip', contains='invalid', label='a path in a symbol name is refused', spec=BASE + '|../secret:#FFFFFF:0:0:0.5:0:0'),
    H('logoRoundTrip', contains='invalid', label='a bad color is refused', spec='shield:red:#FFFFFF:0.06'),
    H('logoRoundTrip', expect='shield:#C0392B:#FFFFFF:0.06|star:#FFFFFF:1.5:-1.5:2:360:1', label='numbers are clamped to sane ranges', spec=BASE + '|star:#FFFFFF:9:-9:9:999:1'),
    # rendering
    H('logoPixel', expect='#C0392B', label='the center of a plain logo is the base color', spec=BASE, x=0, y=0),
    H('logoPixel', expect='clear', label='outside the base is transparent', spec='circle:#C0392B:#FFFFFF:0.06', x=0.9, y=0.9),
    H('logoPixel', expect='#FFFFFF', label='the border is drawn at the rim', spec='circle:#C0392B:#FFFFFF:0.2', x=0.85, y=0),
    H('logoPixel', expect='#FFFFFF', label='a white star sits on the center', spec=BASE + STAR, x=0, y=0),
    H('logoPixel', expect='#C0392B', label='and not beyond its size', spec=BASE + STAR, x=0.6, y=0.1),
    H('logoPixel', expect='#2980B9', label='a later symbol draws over an earlier one', spec=BASE + STAR + '|circle:#2980B9:0:0:0.2:0:0', x=0, y=0),
    H('logoPixel', expect='#C0392B', label='a symbol that is not on this machine is skipped, the logo stays', spec=BASE + '|not-an-icon:#FFFFFF:0:0:0.5:0:0', x=0, y=0),
    # imported icons: recolor vs static
    H('writeLogoIcons', expect='written', label='drop three sample icons into the icons folder'),
    H('iconMode', expect='recolor', label='a plain name.png is a recolor mask', icon='tlogo-mask'),
    H('iconMode', expect='static', label='name.static.png is static', icon='tlogo-flat'),
    H('iconMode', expect='static', label='a sidecar name.icon.json can say static', icon='tlogo-side'),
    H('logoPixel', expect='#00FF00', label='a recolor icon takes the layer color', spec=BASE + '|tlogo-mask:#00FF00:0:0:0.8:0:0', x=0, y=0),
    H('logoPixel', expect='#0000FF', label='a static icon keeps its own colors and ignores the layer color', spec=BASE + '|tlogo-flat:#FF0000:0:0:0.8:0:0', x=0, y=0),
    H('logoPixel', expect='#FF8000', label='static by sidecar keeps its own orange', spec=BASE + '|tlogo-side:#FF0000:0:0:0.8:0:0', x=0, y=0),
    H('logoPixel', expect='#C0392B', label='outside the small imported square the base shows', spec=BASE + '|tlogo-mask:#00FF00:0:0:0.8:0:0', x=0.6, y=0),
    H('deleteLogoIcons', expect=4, label='clean up the sample icons'),
    # the server side
    H('adminCommand', contains='new logo', label='teams setlogo stores a valid logo', line='setlogo red ' + BASE + STAR),
    H('teamLogo', expect='shield:#C0392B:#FFFFFF:0.06|star:#FFFFFF:0:0:0.5:0:0', label='and it is saved on the team', team='red'),
    H('adminCommand', contains='not valid', label='an invalid logo is refused', line='setlogo red shield:red'),
    H('teamLogo', expect='shield:#C0392B:#FFFFFF:0.06|star:#FFFFFF:0:0:0.5:0:0', label='the stored logo did not change', team='red'),
    H('adminCommand', contains='plain icon', label='teams clearlogo', line='clearlogo red'),
    H('teamLogo', expect='', label='the logo is gone', team='red'),
])

T('T-52', 'team banners and flags: pieces, models, stamp and cloth picture', [
    *fresh_teams(),
    H('bannerPieces', expect=3, label='hanging banner, standing banner and flag are Hammer pieces'),
    H('bannerPixel', expect='256x512|#8C8C8C', label='no team: a grey tall cloth', team='none', shape='tall', x=0.5, y=0.2),
    H('bannerPixel', expect='384x256|#8C8C8C', label='no team: a grey wide cloth', team='none', shape='wide', x=0.5, y=0.05),
    H('adminCommand', contains='new logo', label='red gets a white star logo', line='setlogo red ' + BASE + STAR),
    H('bannerPixel', expect='256x512|#FFFFFF', label='the logo sits in the middle of the tall cloth', team='red', shape='tall', x=0.5, y=0.66),
    H('bannerPixel', expect='384x256|#FFFFFF', label='and in the middle of the wide cloth', team='red', shape='wide', x=0.46, y=0.5),
    H('usePieceTeams', expect=True),
    *on_team('Alice', 'red'),
    {'do': 'place', 'prefab': 'TeamBannerHanging', 'dz': 3, 'dx': -4, 'as': 'hang'},
    {'do': 'place', 'prefab': 'TeamBannerStanding', 'dz': 3, 'as': 'stand'},
    {'do': 'place', 'prefab': 'TeamFlag', 'dz': 3, 'dx': 4, 'as': 'flag'},
    wait(1.2),
    H('pieceTeam', expect='red', label='the server stamped the hanging banner', target='hang'),
    H('pieceTeam', expect='red', label='the standing banner', target='stand'),
    H('pieceTeam', expect='red', label='and the flag', target='flag'),
    H('bannerLook', contains='red|1|', label='the hanging banner shows red with its cloth painted', target='hang'),
    H('bannerLook', contains='red|1|', label='the standing banner too', target='stand'),
    H('bannerLook', contains='red|1|', label='and the flag', target='flag'),
    *on_team('Bob', 'blue'),
    {'do': 'place', 'prefab': 'TeamBannerStanding', 'dz': 6, 'as': 'other'},
    wait(1.0),
    H('pieceTeam', expect='blue', label='a banner Bob builds is blue', target='other'),
    H('bannerLook', contains='blue|1|', label='and looks blue', target='other'),
    H('bannerRenderers', contains='model', label='renderers on a placed standing banner (listed in the log)', target='stand'),
    H('bannerShaders', contains='TeamCloth', label='every banner material has a supported shader (the list is in the log)', target='stand'),
    # the Team Ward wears the Blender model, facing the way the vanilla stone does
    play('me'), H('setAdmin', on=True),
    {'do': 'place', 'prefab': 'TeamWard', 'dz': 9, 'as': 'wardm'}, wait(1.0),
    H('wardModel', expect='model|shards 4|halos 6|runes 12', label='the Team Ward wears the Blender model: 4 crystals, 6 diamonds, 12 rune segments', ward='wardm'),
    H('setAdmin', on='auto'),
])

T('T-53', 'Team Ward on and off, and the capture sweep on the rune ring', [
    *fresh_teams(), H('setAdmin', on=True),
    *on_team('Alice', 'red'),
    {'do': 'place', 'prefab': 'TeamWard', 'dz': 6, 'as': 'w'}, wait(1.2),
    H('wardState', expect='on|open 1.00|halos 6|captured 0/12|capture 0.00', label='a new ward is on: crystals out, six diamonds, no capture', ward='w'),
    H('wardSet', contains='power off', label='switch it off', ward='w', power=False),
    wait(2.4),
    H('wardState', contains='off|open 0.00|halos 0|', label='off: the crystals have folded in and the diamonds are gone', ward='w'),
    H('wardSet', contains='power on', label='switch it on again', ward='w', power=True),
    wait(2.4),
    H('wardState', contains='on|open 1.00|halos 6|', label='on again: crystals out, diamonds back', ward='w'),
    H('wardSet', contains='capture 0.50|captor blue', label='blue is half way through capturing it', ward='w', capture=0.5, captor='blue'),
    wait(3.2),
    H('wardState', contains='captured 6/12|capture 0.50', label='half the rune ring has changed color', ward='w'),
    H('wardSet', contains='capture 1.00', label='the capture completes', ward='w', capture=1.0, captor='blue'),
    wait(3.2),
    H('wardState', contains='captured 12/12', label='the whole ring is blue', ward='w'),
    H('adminCommand', contains='cleared', label='teams ward capture off clears it', line='ward capture off'),
    wait(5.0),
    H('wardState', contains='captured 0/12|capture 0.00', label='the ring is back to the team color', ward='w'),
    H('setAdmin', on='auto'),
])

T('T-54', 'the carried team banner: an item that shows the carrying player team', [
    *fresh_teams(),
    H('adminCommand', contains='new logo', label='red gets a logo', line='setlogo red ' + BASE + STAR),
    *on_team('Alice', 'red'),
    {'do': 'give', 'prefab': 'TeamBannerCarry', 'equip': True}, wait(1.5),
    H('carryInfo', expect='item|carried red', label='the banner is in the hand and painted for red'),
    *on_team('Bob', 'blue'),
    wait(1.2),
    H('carryInfo', expect='item|carried blue', label='the same banner follows whoever carries it (now blue)'),
])

T('T-55', 'diagnostic: how vanilla items sit in the hand (listed in the log)', [
    H('itemAxes', contains='attach=', label='Torch', prefab='Torch'),
    H('itemAxes', contains='attach=', label='Club', prefab='Club'),
    H('itemAxes', contains='attach=', label='SpearFlint', prefab='SpearFlint'),
    H('itemAxes', contains='attach=', label='our carried banner', prefab='TeamBannerCarry'),
])

def click(text, nth=0):
    step = {'do': 'click', 'button': text}
    if nth:
        step['nth'] = nth
    return [step, wait(0.4)]


T('T-56', 'logo tab: click through layers, colors, apply, revert and drafts', [
    *fresh_teams(), H('setAdmin', on=True),
    H('adminCommand', contains='plain icon', label='red starts without a logo', line='clearlogo red'),
    H('openPanel', open=True, tab='Logo', contains=''), wait(0.8),
    *click('Crimson'),
    H('logoState', contains='red|sel -1|layers 1 star|dirty|shield:', label='base selected; a team without a logo starts from a starter draft that is not applied yet'),
    # base shape and colors
    *click('circle'),
    H('logoState', contains='|dirty|circle:', label='picking a base shape changes the draft'),
    H('teamLogo', expect='', label='and nothing reached the team', team='red'),
    # a second symbol becomes its own layer
    *click('+ Symbol'),
    H('logoState', contains='sel 1|layers 2 star,star|', label='+ Symbol adds a layer and selects it'),
    *click('moon'),
    H('logoState', contains='sel 1|layers 2 star,moon|', label='the symbol buttons change the selected layer only'),
    {'do': 'click', 'button': 'quick-#2980B9'}, wait(0.4),
    H('logoState', contains='|moon:#2980B9:', label='a quick color paints the selected symbol'),
    # back to the base: border color
    *click('Base: circle'),
    H('logoState', contains='sel -1|layers 2 star,moon|', label='clicking the base row selects the base'),
    {'do': 'click', 'button': 'tile-Border'}, wait(0.4),
    {'do': 'click', 'button': 'quick-#27AE60'}, wait(0.4),
    H('logoState', contains=':#27AE60:', label='with Border chosen, a quick color paints the border'),
    H('teamLogo', expect='', label='still nothing applied to the team', team='red'),
    # apply
    *click('Apply to team'), wait(0.6),
    H('teamLogo', contains='circle:', label='Apply to team sends the logo', team='red'),
    H('logoState', contains='|clean|', label='and the draft is clean'),
    # reorder and remove
    *click('+ Symbol'),
    H('logoState', contains='sel 2|layers 3 star,moon,star|', label='a third layer'),
    *click('Symbol 2: moon'),
    H('logoState', contains='sel 1|', label='clicking a layer row selects it'),
    *click('▲', nth=1),
    H('logoState', contains='sel 2|layers 3 star,star,moon|', label='the up arrow of that row moves the moon above the star'),
    *click('✕', nth=1),   # number 0 is the window's own close button
    H('logoState', contains='layers 2 star,star|', label='the top row remove button removes the top symbol'),
    # revert
    *click('Revert'),
    H('logoState', contains='layers 2 star,moon|clean|', label='Revert brings back the applied logo'),
    # drafts survive switching team
    *click('square'),
    H('logoState', contains='|dirty|square:', label='a new unsaved change'),
    *click('Saphire'),
    H('logoState', contains='blue|', label='switched to the other team'),
    *click('Crimson'),
    H('logoState', contains='red|sel -1|layers 2 star,moon|dirty|square:', label='the unsaved draft was still there'),
    *click('Apply to team'), wait(0.6),
    H('teamLogo', contains='square:', label='and applies', team='red'),
    H('openPanel', open=False, contains=''),
    H('setAdmin', on='auto'),
])

T('T-57', 'ward figures: a team picks the figure in the middle of its Team Ward', [
    *fresh_teams(), H('setAdmin', on=True),
    *on_team('Alice', 'red'),
    {'do': 'place', 'prefab': 'TeamWard', 'dz': 6, 'as': 'w'}, wait(1.2),
    H('wardFigure', contains='stone|stone_runes:1 slots', label='a team starts with the original guard stone in the middle', ward='w'),
    H('setFigure', expect='wolf', label='red picks the wolf', team='red', figure='wolf'), wait(1.2),
    H('wardFigure', contains='figure_wolf:2 slots', label='the wolf stands on the ward: one body mesh with stone and glow materials', ward='w'),
    H('wardFigure', contains='|channels 10', label='the wolf has its ten glowing flow channels', ward='w'),
    H('setFigure', expect='dragon', label='then the dragon', team='red', figure='dragon'), wait(1.2),
    H('wardFigure', contains='figure_dragon:2 slots', label='the dragon replaced the wolf', ward='w'),
    H('wardFigure', contains='|channels 12', label='the dragon has twelve', ward='w'),
    H('setFigure', expect='stag', label='then the stag', team='red', figure='stag'), wait(1.2),
    H('wardFigure', contains='figure_stag:2 slots', label='the stag replaced the dragon', ward='w'),
    H('wardFigure', contains='|channels 11', label='the stag has eleven', ward='w'),
    H('setFigure', expect='freya', label='then Freya, borrowed from the game statue', team='red', figure='freya'), wait(1.2),
    H('wardFigure', contains='freya|default:', label='Freya stands on the ward with her own mesh', ward='w'),
    H('setFigure', expect='thor', label='then Thor', team='red', figure='thor'), wait(1.2),
    H('wardFigure', contains='thor|', label='Thor replaced Freya (and has at least one part)', ward='w'),    H('setFigure', expect='stone', label='an unknown figure falls back to the original stone', team='red', figure='unicorn'), wait(1.2),
    H('wardFigure', contains='stone|stone_runes:1 slots', label='and the original stone is back', ward='w'),
    H('adminCommand', contains='ward figure: wolf', label='the console command sets it too', line='setfigure red wolf'), wait(1.2),
    H('wardFigure', contains='wolf|figure_wolf:', label='and the ward follows', ward='w'),
    H('adminCommand', contains='Usage: teams setfigure', label='a missing figure shows the usage', line='setfigure red'),
    H('setFigure', expect='stone', label='tidy up', team='red', figure='stone'),
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
