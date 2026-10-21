"""Three viewpoints, independent host save slots, fresh faces and return visits."""
import json
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from driver import command, state, wait_for, ROOT
from shared_face_switch import appearance

HOST, CLIENT, OBSERVER = 'publichost', 'publicclient', 'faceobserver'
ROLES = [HOST, CLIENT, OBSERVER]
OUT = ROOT.parent / 'shared-face-switch'
RUN = sys.argv[1] if len(sys.argv) > 1 else 'three-final'


def parallel(action, **args):
    with ThreadPoolExecutor(3) as pool:
        return dict(zip(ROLES, pool.map(lambda r: command(r, action, **args), ROLES)))


def emit(case, passed, **details):
    print(case, bool(passed), flush=True)
    with (OUT / (RUN + '.jsonl')).open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(dict(case=case, passed=bool(passed), **details)) + '\n')
    if not passed:
        raise AssertionError(case)


def faces():
    return parallel('faceState')


def stable():
    return all(state(r).get('mainScene') == 'Base' and state(r).get('initialized') and not state(r).get('loading', True) for r in ROLES)


def all_views_match(values):
    expected = {v['id']: appearance(v['saved']) for v in values.values()}
    return all(len(v['remotes']) == 2 and all(appearance(remote['face']) == expected.get(remote['id']) for remote in v['remotes']) for v in values.values())


def editor_ready(role):
    return not state(role).get('loading', True) and command(role, 'sharedCharacter')['editor']


def joined():
    return stable() and all(command(r, 'sharedEntry')['ready'] for r in [CLIENT, OBSERVER])


def enter(slot, fresh=False, client_slot=None):
    for r in [OBSERVER, CLIENT, HOST]:
        if state(r).get('network'):
            command(r, 'stop')
    time.sleep(2)
    for r in ROLES:
        if state(r).get('scene') != 'MainMenu':
            command(r, 'sharedEntry', menu=True)
    assert wait_for(lambda: all(state(r).get('scene') == 'MainMenu' and not state(r).get('loading', True) for r in ROLES), 90)
    for r in ROLES:
        command(r, 'sharedCharacter', slot=slot if r == HOST or client_slot is None else client_slot)
    if fresh:
        for identity in ['private-test-' + CLIENT, 'private-test-' + OBSERVER]:
            command(HOST, 'sharedCharacter', forget=True, tutorialComplete=True, identity=identity)
    parallel('loadBase', timeout=180)
    assert wait_for(stable, 90)
    command(HOST, 'sharedEntry', enabled=True)
    command(HOST, 'host', transport='Direct', port=19050)
    for r in [CLIENT, OBSERVER]:
        command(r, 'join', transport='Direct', port=19050)


def check(label):
    emit(label + '_all_three_viewpoints_converge', wait_for(lambda: joined() and all_views_match(faces()), 30), values=faces())
    values = faces()
    emit(label + '_local_models_match_their_save', all(appearance(v['own']) == appearance(v['saved']) for v in values.values()))
    emit(label + '_wire_matches_each_current_face', all(appearance(v['wire']) == appearance(v['saved']) for v in values.values()))
    return {r: appearance(v['saved']) for r, v in values.items()}


def main():
    parallel('loadMod')
    parallel('sharedEntry')
    enter(1, fresh=True)
    emit('slot_a_two_new_visitors_get_native_editor', wait_for(lambda: all(editor_ready(r) for r in [CLIENT, OBSERVER]), 90))
    command(CLIENT, 'sharedCharacter', confirm=True, r=.8, g=.2, b=.1, hairSteps=1)
    command(OBSERVER, 'sharedCharacter', confirm=True, r=.2, g=.3, b=.9, hairSteps=3)
    assert wait_for(joined, 90)
    face_a = check('slot_a')
    enter(2, fresh=True)
    emit('slot_b_two_new_visitors_get_native_editor', wait_for(lambda: all(editor_ready(r) for r in [CLIENT, OBSERVER]), 90))
    command(CLIENT, 'sharedCharacter', confirm=True, r=.1, g=.7, b=.2, hairSteps=2)
    command(OBSERVER, 'sharedCharacter', confirm=True, r=.7, g=.2, b=.8, hairSteps=4)
    assert wait_for(joined, 90)
    face_b = check('slot_b')
    emit('both_visitors_have_separate_faces_per_host_save', all(face_a[r] != face_b[r] for r in [CLIENT, OBSERVER]))
    enter(1)
    emit('return_to_a_does_not_reopen_creation', wait_for(joined, 90) and all(not editor_ready(r) for r in [CLIENT, OBSERVER]))
    emit('return_to_a_restores_each_original_face', check('slot_a_return') == face_a)
    enter(2, client_slot=1)
    emit('host_b_with_guests_personal_slot_a_restores_host_b_faces', wait_for(joined, 90) and check('host_b_guest_a') == face_b)
    before = faces()
    time.sleep(2)
    after = faces()
    emit('repeated_status_keeps_all_remote_face_part_instances', all({e['id']: e['face']['parts'] for e in before[r]['remotes']} == {e['id']: e['face']['parts'] for e in after[r]['remotes']} for r in ROLES))
    for r in [OBSERVER, CLIENT, HOST]:
        command(r, 'stop')


if __name__ == '__main__':
    main()
