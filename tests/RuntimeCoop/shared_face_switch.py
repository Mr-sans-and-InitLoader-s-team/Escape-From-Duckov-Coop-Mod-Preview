"""Switch native save slots without restarting and compare rendered remote faces."""
import json
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from driver import command, state, wait_for, ROOT

HOST, CLIENT = 'publichost', 'publicclient'
ROLES = [HOST, CLIENT]
PHASE = sys.argv[1] if len(sys.argv) > 1 else 'baseline'
OUT = ROOT.parent / 'shared-face-switch'


def parallel(action, **args):
    with ThreadPoolExecutor(2) as pool:
        return dict(zip(ROLES, pool.map(lambda r: command(r, action, **args), ROLES)))


def emit(name, passed, **details):
    row = dict(case=name, passed=bool(passed), **details)
    with (OUT / (PHASE + '.jsonl')).open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(row) + '\n')
    print(name, bool(passed), flush=True)
    if not passed and not PHASE.startswith('baseline'):
        raise AssertionError(name)


def appearance(value):
    if isinstance(value, dict):
        value = value['json']
    if not value:
        return None
    data = json.loads(value)
    data.pop('savedSetting', None)
    def normalized(item):
        if isinstance(item, dict):
            return {key: normalized(v) for key, v in item.items()}
        # Native DataToJson keeps three decimal places; wire JsonUtility keeps full floats.
        return round(item, 3) if isinstance(item, float) else item
    return normalized(data)


def snapshots():
    return parallel('faceState')


def enter_slot(slot, fresh=False):
    for role in [CLIENT, HOST]:
        if state(role).get('network'):
            command(role, 'stop')
    time.sleep(2)
    for role in ROLES:
        if state(role).get('scene') != 'MainMenu':
            command(role, 'sharedEntry', menu=True)
    assert wait_for(lambda: all(state(r).get('scene') == 'MainMenu' and not state(r).get('loading', True) for r in ROLES), 90)
    for role in ROLES:
        command(role, 'sharedCharacter', slot=slot)
    if fresh:
        command(HOST, 'sharedCharacter', forget=True, tutorialComplete=True)
    parallel('loadBase', timeout=180)
    assert wait_for(lambda: all(state(r).get('initialized') and not state(r).get('loading', True) for r in ROLES), 90)
    command(HOST, 'sharedEntry', enabled=True)
    command(HOST, 'host', transport='Direct', port=19050)
    command(CLIENT, 'join', transport='Direct', port=19050)


def wait_editor():
    return wait_for(lambda: not state(CLIENT).get('loading', True) and command(CLIENT, 'sharedCharacter')['editor'], 90)


def wait_joined():
    return wait_for(lambda: state(CLIENT).get('mainScene') == 'Base' and not state(CLIENT).get('loading', True) and command(CLIENT, 'sharedEntry')['ready'], 90)


def check_faces(label):
    values = snapshots()
    expected = appearance(values[CLIENT]['saved'])
    emit(label + '_own_model_matches_current_save', appearance(values[CLIENT]['own']) == expected, values=values)
    emit(label + '_wire_face_matches_current_save', appearance(values[CLIENT]['wire']) == expected)
    def host_matches():
        host = command(HOST, 'faceState')
        return len(host['remotes']) == 1 and appearance(host['remotes'][0]['face']) == expected
    emit(label + '_host_rendered_model_matches_current_face', wait_for(host_matches, 5), expected=expected, host=command(HOST, 'faceState'))
    return expected


def main():
    parallel('loadMod')
    parallel('sharedEntry')
    enter_slot(1, fresh=True)
    emit('slot_a_new_player_editor', wait_editor())
    command(CLIENT, 'sharedCharacter', confirm=True, r=.8, g=.2, b=.1, hairSteps=1)
    assert wait_joined()
    face_a = check_faces('slot_a')
    if PHASE.endswith('single'):
        for role in [CLIENT, HOST]: command(role, 'stop')
        return
    enter_slot(2, fresh=True)
    emit('slot_b_new_player_editor', wait_editor())
    command(CLIENT, 'sharedCharacter', confirm=True, r=.1, g=.7, b=.2, hairSteps=2)
    assert wait_joined()
    face_b = check_faces('slot_b')
    emit('separate_slot_appearance_is_distinct', face_a != face_b)
    enter_slot(1)
    emit('returning_slot_a_reuses_existing_character', wait_joined() and not command(CLIENT, 'sharedCharacter')['editor'])
    restored = check_faces('slot_a_return')
    emit('slot_a_saved_appearance_restored_without_slot_b_data', restored == face_a)
    before = snapshots()
    time.sleep(2)
    after = snapshots()
    emit('unchanged_status_does_not_rebuild_face_parts', all(before[r]['remotes'] and after[r]['remotes'] and before[r]['remotes'][0]['face']['parts'] == after[r]['remotes'][0]['face']['parts'] for r in ROLES))
    for role in [CLIENT, HOST]:
        command(role, 'stop')


if __name__ == '__main__':
    main()
