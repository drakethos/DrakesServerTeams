"""Writes the DrakesTestLab screenshot suite for DrakesServerTeams (team colors, icons, glow and graves with several players)
into Tests/suites/DrakesServerTeams-Visuals (run: python gen_visuals_suite.py). Tests/ is never packaged.
Pictures land in the run's results folder (config/DrakesTestLab/results/run-*/), named teams-*.png."""
import json, os, shutil

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'suites', 'DrakesServerTeams-Visuals')
GUID = 'com.drakemods.serverteams'
NOERR = {'check': 'noErrors', 'source': 'DrakesServerTeams'}
NOERR_UNITY = {'check': 'noErrors', 'source': 'Unity Log'}

# four teams that exist only for the pictures, so the shipped teams.json is untouched
TEAMS = [
    ('ember', 'Ember', '#E67E22', 'shield', ''),
    ('tide', 'Tide', '#2980B9', 'star', ''),
    ('moss', 'Moss', '#27AE60', 'sample-banner', ''),
    ('dusk', 'Dusk', '#8E44AD', 'diamond', '#F1C40F'),
]
# who stands where, and on which team (Alice is the local player and the camera)
PEERS = [('Bjornita', 'tide', -6, 8), ('Bob', 'moss', -2, 8), ('Amy', 'dusk', 2, 8), ('Carol', 'ember', 6, 8)]


def H(name, expect=None, label=None, contains=None, **args):
    s = {'do': 'hook', 'name': 'teams.' + name}
    if args:
        s['args'] = args
    for k, v in (('expect', expect), ('contains', contains), ('label', label)):
        if v is not None:
            s[k] = v
    return s


def wait(s=0.3):
    return {'do': 'wait', 'seconds': s}


def shot(name):
    return {'do': 'screenshot', 'name': name, 'clean': True}


def cleanup():
    return [H('removeTeam', team=t[0]) for t in TEAMS] + [H('removeGraves'), H('deleteSampleIcon', icon='sample-banner')]


def setup_teams():
    steps = [*cleanup(), H('clearMembers'), H('setDeathRules', enabled=False), H('writeSampleIcon', expect='sample-banner.png', label='a custom icon file in the icons folder', icon='sample-banner')]
    for tid, name, color, icon, glow in TEAMS:
        steps.append(H('defineTeam', label=f'team {name}: {color}, icon {icon}', team=tid, title=name, color=color, icon=icon, glow=glow))
    return steps


tests = {}


def T(tid, title, steps, desc=''):
    tests[tid] = {'name': f'{tid} {title}', 'description': desc,
                  'steps': steps + [{'do': 'playAs', 'who': 'me'}, *cleanup()], 'asserts': [NOERR, NOERR_UNITY]}


def to_clearing():
    """The spawn is a ring of standing stones; 45 m north is flat open ground (found by scouting the test world)."""
    return [H('goClearing', contains=''), wait(2.5)]


def join_all(positions):
    steps = [{'do': 'playAs', 'who': 'Alice'}]
    for who, team, dx, dz in positions:
        steps.append({'do': 'peerJoin', 'name': who, 'dx': dx, 'dz': dz})
    steps.append(H('assignTo', expect='ember', player='Alice', team='ember'))
    for who, team, dx, dz in positions:
        steps.append(H('assignTo', expect=team, label=f'{who} is on {team}', player=who, team=team))
    return steps


DAY, NIGHT = 0.5, 0.12


def at(who, dx, dz):
    return {'do': 'peerMove', 'peer': who, 'dx': dx, 'dz': dz}


T('V-01', 'four players on four teams: name colors, icons and glow', [
    *setup_teams(), H('setTime', time=DAY),
    *to_clearing(),
    *join_all(PEERS),
    wait(1.5),
    {'do': 'face', 'target': 'Bob'}, wait(0.6),
    shot('teams-players-day'),
    H('setTime', time=NIGHT), wait(1.0),
    shot('teams-players-night'),
    # close-ups: bring one player to 3.5 m in front, then put them back
    at('Bjornita', 0, 2.6), wait(0.6), {'do': 'face', 'target': 'Bjornita'}, wait(0.6), shot('teams-closeup-tide-star'),
    at('Bjornita', -6, 8), at('Bob', 0, 2.6), wait(0.6), {'do': 'face', 'target': 'Bob'}, wait(0.6), shot('teams-closeup-moss-custom-icon'),
    at('Bob', -2, 8), at('Amy', 0, 2.6), wait(0.6), {'do': 'face', 'target': 'Amy'}, wait(0.6), shot('teams-closeup-dusk-gold-glow'),
], 'Alice (camera) and Carol are Ember; Bjornita Tide (star); Bob Moss (a custom PNG icon); Amy Dusk (diamond, gold glow). Day and night.')

T('V-02', 'graves of four teams', [
    *setup_teams(), H('setTime', time=DAY),
    *to_clearing(),
    *join_all([('Bjornita', 'tide', -3, -8), ('Bob', 'moss', 3, -8), ('Amy', 'dusk', -6, -9), ('Carol', 'ember', 6, -9)]),
    wait(0.5),
    H('makeGrave', expect='tide', label='grave of Bjornita', player='Bjornita', dx=-6, dz=3),
    H('makeGrave', expect='moss', label='grave of Bob', player='Bob', dx=-2, dz=3),
    H('makeGrave', expect='dusk', label='grave of Amy', player='Amy', dx=2, dz=3),
    H('makeGrave', expect='ember', label='grave of Carol', player='Carol', dx=6, dz=3),
    wait(1.5),
    shot('teams-graves-day'),
    H('setTime', time=NIGHT), wait(1.0),
    shot('teams-graves-night'),
    H('removeGraves'), wait(0.5),
    H('makeGrave', expect='dusk', label='one grave up close', player='Amy', dx=1.4, dz=-1.2),
    wait(1.2),
    shot('teams-grave-closeup-dusk'),
], 'Four graves in a row, one per team: team-colored light, floating icon, colored name text. Day, night and a close-up.')

T('V-04', 'the admin panel and the DrakesTestLab Teams tab', [
    *setup_teams(), H('setTime', time=DAY),
    *to_clearing(),
    *join_all(PEERS),
    wait(1.0),
    {'do': 'hook', 'name': 'testlab.showTab', 'args': {'tab': 'Teams'}}, wait(1.0),
    {'do': 'screenshot', 'name': 'teams-lab-tab'},
    {'do': 'hook', 'name': 'testlab.closeToolbox'}, wait(0.3),
    H('openPanel', open=True, contains=''), wait(1.0),
    {'do': 'screenshot', 'name': 'teams-admin-panel'},
    H('openPanel', open=False, contains=''),
], 'The tab in the lab toolbox (F9) and the in-game admin panel (F6 / teams panel), with four players in the world.')

T('V-05', 'the Team Ward: obsidian look, team glow and label, and its Shift+E settings', [
    *setup_teams(), H('setTime', time=DAY), H('setAdmin', on=True),
    *to_clearing(),
    {'do': 'playAs', 'who': 'Alice'},
    H('assignTo', expect='ember', player='Alice', team='ember'),
    {'do': 'place', 'prefab': 'TeamWard', 'dz': 9, 'dx': -3, 'as': 'wardA'},
    wait(0.6),
    H('assignTo', expect='tide', player='Alice', team='tide'),
    {'do': 'place', 'prefab': 'TeamWard', 'dz': 9, 'dx': 4, 'as': 'wardB'},
    wait(1.5),
    {'do': 'face', 'target': 'wardA'}, wait(0.6),
    shot('teams-ward-day'),
    H('setTime', time=NIGHT), wait(1.0),
    shot('teams-ward-night'),
    {'do': 'teleport', 'dz': 5}, {'do': 'face', 'target': 'wardA'}, wait(1.0),
    shot('teams-ward-closeup'),
    H('wardInteract', contains='', label='Shift+E as an admin', ward='wardA', alt=True), wait(1.0),
    {'do': 'screenshot', 'name': 'teams-ward-settings'},
    H('closeWardPanel', contains=''),
], 'Two Team Wards (Ember and Tide) next to each other: obsidian body, rune glow and light in the team color, icon and name above. The last picture is the Shift+E settings window.')

suite = {
    'name': 'DrakesServerTeams-Visuals',
    'description': 'Screenshots of the team visuals with several players: name colors, faction icons (built-in and a custom PNG), glow and graves. '
                   'Pictures are written to the run\'s results folder as teams-*.png.',
    'requireMods': [{'guid': GUID, 'minVersion': '0.0.1'}],
    'configOverrides': [{'guid': GUID, 'section': k.split('/')[0], 'key': k.split('/')[1], 'value': v} for k, v in {
        '1 Assignment/AssignmentMode': 'AdminOnly', '1 Assignment/MoveToSpawnOnFirstTeam': 'false',
        '4 Visuals/TeamNameColor': 'true', '4 Visuals/TeamIcon': 'true', '4 Visuals/TeamGlow': 'true', '4 Visuals/TeamGraveMarker': 'true',
    }.items()],
    'setup': {'timeOfDay': 0.05, 'freezeTime': 1, 'god': 1, 'noBuild': 'off', 'hideUnlockPopups': 1},
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
print(len(tests), 'DrakesServerTeams-Visuals tests')
