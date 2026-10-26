"""Stage-1 measurements for the War spike (crumble + shield): how does a hit on a piece inside a ward behave today?
Writes Tests/suites/DrakesServerTeams-Spike. Throwaway; delete when the spike is done."""
import json, os, shutil

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'suites', 'DrakesServerTeams-Spike')


def H(name, label=None, contains='', **args):
    s = {'do': 'hook', 'name': name, 'contains': contains}
    if args:
        s['args'] = args
    if label:
        s['label'] = label
    return s


def wait(s=0.4):
    return {'do': 'wait', 'seconds': s}


def shot(name):
    return {'do': 'screenshot', 'name': name, 'clean': True}


steps = [
    {'do': 'playAs', 'who': 'Alice'},
    {'do': 'peerJoin', 'name': 'Bjornita', 'dx': -4, 'dz': -3},
    {'do': 'place', 'prefab': 'guard_stone', 'dz': 3, 'as': 'ward'},
    {'do': 'place', 'prefab': 'woodwall', 'dx': 2.5, 'dz': 3, 'as': 'wall'},
    {'do': 'place', 'prefab': 'woodwall', 'dx': 0, 'dz': 26, 'as': 'control'},
    wait(),
    H('spike.enableWard', 'ward on', ward='ward', on=True),
    H('spike.hp', 'wall hp', piece='wall'),
    H('spike.hp', 'control hp', piece='control'),
    H('spike.pieceParts', 'wall parts', piece='wall'),
    H('spike.hit', 'ENEMY hits the wall INSIDE the ward', piece='wall', attacker='Bjornita', dmg=50),
    H('spike.hit', 'ENEMY hits the CONTROL wall (no ward)', piece='control', attacker='Bjornita', dmg=50),
    H('spike.hit', 'ALICE (owner) hits her wall', piece='wall', attacker='Alice', dmg=50),
    H('spike.hit', 'ENEMY hits for 5000 inside the ward', piece='wall', attacker='Bjornita', dmg=5000),
    H('spike.hp', 'wall hp after', piece='wall'),
    {'do': 'lookAt', 'target': 'wall'},
    wait(0.4),
    H('spike.setHp', 'wall at 100%', piece='wall', hp=1000), H('spike.healthPercent', 'pct', piece='wall'), shot('spike-wall-new'),
    H('spike.setHp', 'wall at 50%', piece='wall', hp=50), H('spike.healthPercent', 'pct', piece='wall'), shot('spike-wall-worn'),
    H('spike.setHp', 'wall at 1', piece='wall', hp=1), H('spike.healthPercent', 'pct', piece='wall'), shot('spike-wall-broken'),
    H('spike.pieceParts', 'wall parts when broken', piece='wall'),
]
suite = {'name': 'DrakesServerTeams-Spike', 'description': 'Throwaway measurements for the crumble/shield spike.',
         'requireMods': [{'guid': 'com.drakemods.serverteams', 'minVersion': '0.0.1'}],
         'setup': {'timeOfDay': 0.5, 'freezeTime': 1, 'god': 1, 'noBuild': 'off', 'hideUnlockPopups': 1},
         'launchConfig': [{'guid': 'com.drakemods.testlab', 'section': 'Case', 'key': 'OnLoad', 'value': ''}],
    'resetBetweenTests': True}
if os.path.isdir(OUT):
    shutil.rmtree(OUT)
os.makedirs(OUT)
json.dump(suite, open(os.path.join(OUT, 'suite.json'), 'w', encoding='utf-8'), indent=2)
sp2 = [
    {'do': 'playAs', 'who': 'Alice'},
    H('spike.enable', 'prototype on', shield=True, crumble=True, shieldMax=200, down=30, cooldown=5, fraction=0.3),
    H('teams.usePieceTeams', 'piece teams on'),
    H('teams.assignTo', 'Alice red', player='Alice', team='red'),
    {'do': 'peerJoin', 'name': 'Bjornita', 'dx': -4, 'dz': -3},
    H('teams.assignTo', 'Bjornita blue', player='Bjornita', team='blue'),
    {'do': 'place', 'prefab': 'guard_stone', 'dz': 3, 'as': 'ward'},
    {'do': 'place', 'prefab': 'woodwall', 'dx': 2.5, 'dz': 3, 'as': 'wall'},
    wait(1.0),
    H('teams.checkPiece', 'ward stamped', target='ward'),
    H('spike.enableWard', 'ward on', ward='ward', on=True),
    H('spike.shield', 'shield at start', ward='ward'),
    H('spike.hit', 'ENEMY hit 1: shield takes it', piece='wall', attacker='Bjornita', dmg=40),
    H('spike.shield', 'shield', ward='ward'), H('spike.hp', 'wall hp (untouched)', piece='wall'),
    H('spike.hit', 'ENEMY hit 2: shield breaks', piece='wall', attacker='Bjornita', dmg=40),
    H('spike.shield', 'shield (down)', ward='ward'), H('spike.hp', 'wall hp (still untouched)', piece='wall'),
    H('spike.hit', 'ENEMY hit 3: siege, wall takes damage', piece='wall', attacker='Bjornita', dmg=40),
    H('spike.hp', 'wall hp after siege hit', piece='wall'),
    H('spike.hit', 'OWN TEAM hits the wall (friendly)', piece='wall', attacker='Alice', dmg=10),
    H('spike.hp', 'wall hp after friendly hit', piece='wall'),
    H('spike.hit', 'ENEMY smashes it: crumbles', piece='wall', attacker='Bjornita', dmg=3000),
    H('spike.isCrumbled', 'crumbled?', piece='wall'), H('spike.hp', 'hp', piece='wall'), H('spike.solid', 'solid?', piece='wall'),
    {'do': 'face', 'target': 'wall'}, wait(0.5), shot('spike-wall-crumbled'),
    H('spike.hit', 'ENEMY hits the crumbled wall again', piece='wall', attacker='Bjornita', dmg=500),
    H('spike.hp', 'hp (unchanged)', piece='wall'),
    {'do': 'playAs', 'who': 'Bjornita'},
    H('spike.repair', 'ENEMY tries to repair', piece='wall'),
    {'do': 'playAs', 'who': 'Alice'},
    H('spike.repair', 'ALICE repairs too early', piece='wall'),
    wait(5.5),
    H('spike.repair', 'ALICE repairs after the cooldown', piece='wall'),
    H('spike.isCrumbled', 'crumbled?', piece='wall'), H('spike.hp', 'hp (30 percent)', piece='wall'), H('spike.solid', 'solid again?', piece='wall'),
    wait(31),
    H('spike.shield', 'shield after the down time (back up)', ward='ward'),
]
json.dump({'name': 'SP-02 shield + crumble', 'steps': sp2 + [{'do': 'playAs', 'who': 'me'}], 'asserts': []},
          open(os.path.join(OUT, 'SP-02.json'), 'w', encoding='utf-8'), indent=2)
print('ok')
