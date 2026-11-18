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

def spot(prefab, tag, team=None, dz=4.0, extra=None, shots=()):
    """A fresh patch of ground 14 m to the side, the piece 4 m in front of the camera, then the pictures."""
    steps = [{'do': 'teleport', 'dx': 14, 'dz': 0}, wait(0.8)]
    if team:
        steps.append(H('assignTo', expect=team, player='Alice', team=team))
    steps += [{'do': 'place', 'prefab': prefab, 'dz': dz, 'as': tag}, wait(1.2)]
    steps += extra or []
    steps += [{'do': 'face', 'target': tag}, {'do': 'teleport', 'dz': -3.5}, {'do': 'face', 'target': tag}, wait(0.8)]
    for name in shots:
        steps.append(shot(name))
    return steps


T('V-09', 'the carried team banner in hand', [
    *setup_teams(), H('setTime', time=DAY), H('setAdmin', on=True),
    H('adminCommand', contains='new logo', label='Ember gets a logo', line='setlogo ember shield:#E67E22:#FFFFFF:0.06|star:#FFFFFF:0:0.05:0.5:0:0'),
    *to_clearing(),
    {'do': 'playAs', 'who': 'Alice'},
    H('assignTo', expect='ember', player='Alice', team='ember'),
    {'do': 'give', 'prefab': 'TeamBannerCarry', 'equip': True}, wait(1.5),
    shot('teams-carried-day'),
    {'do': 'teleport', 'dz': 0}, wait(0.5), shot('teams-carried-day-2'),
])

T('V-08', 'the admin window in the DrakeMods look: every tab', [
    *setup_teams(), H('setTime', time=DAY), H('setAdmin', on=True),
    H('adminCommand', contains='new logo', label='Ember gets a logo', line='setlogo ember shield:#E67E22:#FFFFFF:0.06|star:#FFFFFF:0:0.05:0.5:0:0'),
    *to_clearing(),
    {'do': 'playAs', 'who': 'Alice'},
    H('assignTo', expect='ember', player='Alice', team='ember'),
    {'do': 'place', 'prefab': 'TeamWard', 'dz': 5, 'as': 'wardP'}, wait(1.0),
    H('openPanel', open=True, tab='Players', contains=''), wait(1.5), {'do': 'screenshot', 'name': 'teams-window-players'},
    H('openPanel', open=True, tab='Teams', contains=''), wait(1.0), {'do': 'screenshot', 'name': 'teams-window-teams'},
    H('openPanel', open=True, tab='Logo', contains=''), wait(1.0), {'do': 'screenshot', 'name': 'teams-window-logo'},
    H('openPanel', open=True, tab='Ward', contains=''), wait(1.0), {'do': 'screenshot', 'name': 'teams-window-ward'},
    H('openPanel', open=True, tab='Server', contains=''), wait(1.0), {'do': 'screenshot', 'name': 'teams-window-server'},
    H('openPanel', open=False, contains=''),
])

T('V-07', 'diagnostic: which banner material shows magenta', [
    *setup_teams(), H('setTime', time=DAY), H('setAdmin', on=True),
    *to_clearing(),
    {'do': 'playAs', 'who': 'Alice'},
    *spot('TeamBannerStanding', 'plain', 'ember', dz=3.0, shots=('teams-diag-plain',)),
    *spot('TeamBannerStanding', 'ironAsWood', 'ember', dz=3.0, extra=[H('bannerSwap', contains='swapped', target='ironAsWood', slot=1, **{'with': 0})], shots=('teams-diag-iron-as-wood',)),
    *spot('TeamBannerStanding', 'woodAsIron', 'ember', dz=3.0, extra=[H('bannerSwap', contains='swapped', target='woodAsIron', slot=0, **{'with': 1})], shots=('teams-diag-wood-as-iron',)),
])

T('V-06', 'team banners and flags with team logos, and the Team Ward with its moving parts', [
    *setup_teams(), H('setTime', time=DAY), H('setAdmin', on=True),
    H('adminCommand', contains='new logo', label='Ember gets a logo', line='setlogo ember shield:#E67E22:#FFFFFF:0.06|star:#FFFFFF:0:0.05:0.5:0:0'),
    H('adminCommand', contains='new logo', label='Tide gets a logo', line='setlogo tide circle:#2980B9:#FFFFFF:0.08|moon:#FFFFFF:0:0:0.55:0:0'),
    *to_clearing(),
    {'do': 'playAs', 'who': 'Alice'},
    *spot('TeamBannerStanding', 'standA', 'ember', dz=3.0, extra=[{'do': 'place', 'prefab': 'piece_chest_wood', 'dz': 3.0, 'dx': 1.8, 'as': 'chestRef'}, wait(1.0)], shots=('teams-banner-standing-ember',)),
    *spot('TeamFlag', 'flagB', 'tide', dz=3.0, shots=('teams-flag-tide',)),
    *spot('TeamBannerHanging', 'hangA', 'ember', dz=3.2, shots=('teams-banner-hanging-ember',)),
    *spot('piece_chest_wood', 'vanilla', 'ember', shots=('teams-vanilla-chest-compare',)),
    *spot('TeamWard', 'wardOn', 'ember', dz=6.0, shots=('teams-ward-on-day',)),
    H('setTime', time=NIGHT), wait(1.0), shot('teams-ward-on-night'),
    *spot('TeamWard', 'wardOff', 'tide', dz=6.0, extra=[H('wardSet', contains='power off', ward='wardOff', power=False), wait(2.6)], shots=('teams-ward-off-night',)),
    {'do': 'teleport', 'dx': 14, 'dz': 0}, wait(0.8), H('assignTo', expect='ember', player='Alice', team='ember'),
    {'do': 'give', 'prefab': 'TeamBannerCarry', 'equip': True}, wait(1.5), shot('teams-banner-carried'),
    *spot('TeamWard', 'wardCap', 'ember', dz=6.0,
          extra=[H('wardSet', contains='capture 0.50', ward='wardCap', capture=0.5, captor='tide'), wait(3.5)], shots=('teams-ward-capture-half-night',)),
], 'Banners (standing, hanging) and flags for two teams carry their team logos; a vanilla chest beside them for comparison; the Team Ward on, off, and half-way through a capture by the other team (the rune ring changes color clockwise).')

T('V-12', 'the Team Ward with its figure: wolf, dragon, stag, stone, Freya and Thor, day and night', [
    *setup_teams(), H('setTime', time=DAY), H('setAdmin', on=True),
    *to_clearing(),
    {'do': 'playAs', 'who': 'Alice'},
    *[step for team, tag in (('ember', 'wolf'), ('tide', 'dragon'), ('moss', 'stag'), ('dusk', 'stone'), ('ember', 'freya'), ('tide', 'thor')) for step in (
        H('setFigure', expect=tag, team=team, figure=tag),
        *spot('TeamWard', 'w_' + tag, team, dz=5.0, shots=(f'teams-figure-{tag}-day',)),
        H('setTime', time=NIGHT), wait(1.0), shot(f'teams-figure-{tag}-night'), H('setTime', time=DAY))],
])
T('V-13', 'figure wards on cleared ground at night, next to the original: do the flow channels glow and pulse?', [
    *setup_teams(), H('setTime', time=NIGHT), H('setAdmin', on=True),
    *to_clearing(),
    {'do': 'playAs', 'who': 'Alice'},
    {'do': 'teleport', 'dx': 30, 'dz': 30}, wait(1.0),
    H('clearArea', contains='', label='cleared ground: trees, rocks and stones switched off here', radius=22), wait(0.5),
    *[step for team, tag in (('dusk', 'stone'), ('ember', 'wolf'), ('tide', 'dragon'), ('moss', 'stag')) for step in (
        H('setFigure', expect=tag, team=team, figure=tag),
        H('assignTo', expect=team, player='Alice', team=team),
        {'do': 'place', 'prefab': 'TeamWard', 'dz': 3.0, 'dx': {'dusk': -6, 'ember': -2, 'tide': 2, 'moss': 6}[team], 'as': 'c_' + tag}, wait(1.5))],
    H('wardShot', contains='yaw', label='the four wards from the front', ward='c_wolf', file='teams-close-all-a', back=9.0, height=2.2, yaw=0), 
    wait(0.9), H('wardShot', contains='yaw', label='again, a moment later', ward='c_wolf', file='teams-close-all-b', back=9.0, height=2.2, yaw=0),
    *[step for tag in ('stone', 'wolf', 'dragon', 'stag') for step in (
        H('wardShot', contains='yaw', label=tag + ' close', ward='c_' + tag, file=f'teams-close-{tag}', back=3.4, height=1.5, yaw=0),)],
])
T('V-14', 'diagnostic: the light effects on a stone ward and a wolf ward', [
    *setup_teams(), H('setTime', time=NIGHT), H('setAdmin', on=True),
    *to_clearing(),
    {'do': 'playAs', 'who': 'Alice'},
    H('setFigure', expect='stone', team='dusk', figure='stone'), H('setFigure', expect='wolf', team='ember', figure='wolf'),
    H('assignTo', expect='dusk', player='Alice', team='dusk'),
    {'do': 'place', 'prefab': 'TeamWard', 'dz': 3.0, 'dx': -3, 'as': 'ds'}, wait(0.5),
    H('assignTo', expect='ember', player='Alice', team='ember'),
    {'do': 'place', 'prefab': 'TeamWard', 'dz': 3.0, 'dx': 3, 'as': 'wf'}, wait(2.5),
    H('lightInfo', contains='', label='stone ward', ward='ds'),
    H('lightInfo', contains='', label='wolf ward', ward='wf'),
])
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
