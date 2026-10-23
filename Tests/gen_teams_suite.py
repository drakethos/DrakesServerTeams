"""Writes the DrakesTestLab suite for DrakesServerTeams into Tests/suites/DrakesServerTeams (run: python gen_teams_suite.py).
The build copies it into the test profiles; Tests/ is never packaged."""
import json, os, shutil

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'suites', 'DrakesServerTeams')
GUID = 'com.drakemods.serverteams'
NOERR = {'check': 'noErrors', 'source': 'DrakesServerTeams'}


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
    tests[tid] = {'name': f'{tid} {title}', 'description': desc, 'steps': steps + [play('me')], 'asserts': [NOERR]}


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

suite = {
    'name': 'DrakesServerTeams',
    'description': 'Automated regression for DrakesServerTeams: assignment and switching rules, server-signed piece teams on wards, '
                   'chests and doors (stamp on build, keep on team change, backfill, forged stamps removed) and the server '
                   'correcting faked published teams. Players are simulated with play-as (Alice, Bob, Carol).',
    'requireMods': [{'guid': GUID, 'minVersion': '0.1.0'}],
    'configOverrides': [{'guid': GUID, 'section': k.split('/')[0], 'key': k.split('/')[1], 'value': v} for k, v in {
        '1 Assignment/AssignmentMode': 'AdminOnly', '1 Assignment/PlayersCanSwitchTeams': 'false',
    }.items()],
    'setup': {'timeOfDay': 0.5, 'freezeTime': 1, 'god': 1, 'noBuild': 'off', 'hideUnlockPopups': 1},
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
