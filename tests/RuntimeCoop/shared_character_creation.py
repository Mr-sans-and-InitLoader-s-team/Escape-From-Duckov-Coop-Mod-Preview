"""First-visit native customization, host-owned faces and cancellation."""
from concurrent.futures import ThreadPoolExecutor
import json
import sys
import time
from driver import command, state, wait_for, ROOT

HOST, CLIENT = 'publichost', 'publicclient'
OUT = ROOT.parent / 'shared-character'
RUN = sys.argv[1] if len(sys.argv) > 1 else 'creation-run1'


def probe(role, **args):
    if not args and state(role).get('loading'):
        return dict(editor=False, ready=False, recorded=False)
    return command(role, 'sharedCharacter', **args)


def emit(name, passed, **details):
    with (OUT / (RUN + '.jsonl')).open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(dict(case=name, passed=bool(passed), **details)) + '\n')
    print(name, bool(passed), flush=True)
    if not passed:
        raise AssertionError(name)


def join():
    command(CLIENT, 'join', transport='Direct', port=19050)


def stop_guest():
    command(CLIENT, 'stop')
    time.sleep(2)


def main():
    for role in [CLIENT, HOST]:
        if state(role).get('network'):
            command(role, 'stop')
    time.sleep(2)
    for role in [HOST, CLIENT]:
        command(role, 'loadMod')
        command(role, 'sharedEntry')
    with ThreadPoolExecutor(2) as pool:
        list(pool.map(lambda r: command(r, 'loadBase', timeout=180), [HOST, CLIENT]))
    assert wait_for(lambda: all(state(r).get('initialized') for r in [HOST, CLIENT]), 120)
    original = probe(CLIENT)
    probe(HOST, forget=True, tutorialComplete=True)
    command(HOST, 'sharedEntry', enabled=True)
    command(HOST, 'host', transport='Direct', port=19050)
    join()
    emit('first_visit_opens_native_face_editor', wait_for(lambda: probe(CLIENT)['editor'], 45), client=probe(CLIENT))
    emit('incomplete_creation_not_saved_or_ready', not probe(HOST)['recorded'] and not probe(CLIENT)['ready'])
    probe(CLIENT, tryExit=True)
    emit('creation_exit_cannot_start_party_travel', not probe(HOST)['vote'] and not probe(CLIENT)['vote'] and state(HOST).get('mainScene') == 'Base')
    stop_guest()
    emit('cancel_restores_personal_face_and_gear', probe(CLIENT)['face'] == original['face'] and probe(CLIENT)['inventory'] == original['inventory'])
    emit('cancel_keeps_host_player_uncreated', not probe(HOST)['recorded'])
    join()
    emit('rejoin_after_cancel_reopens_native_editor', wait_for(lambda: probe(CLIENT)['editor'], 45))
    before = probe(CLIENT)
    emit('first_character_does_not_import_personal_gear', before['inventory'] != original['inventory'], inventory=before['inventory'])
    probe(CLIENT, confirm=True)
    emit('completed_host_skips_tutorial', wait_for(lambda: state(CLIENT).get('mainScene') == 'Base' and state(CLIENT).get('initialized') and not state(CLIENT).get('loading', True), 90) and probe(HOST)['tutorialComplete'])
    emit('native_confirm_saves_character_on_host', wait_for(lambda: probe(CLIENT)['ready'] and probe(HOST)['recorded'] and probe(HOST).get('storedFace') == probe(CLIENT).get('face'), 30), host=probe(HOST), client=probe(CLIENT))
    created = probe(CLIENT)
    stop_guest()
    emit('leave_restores_original_personal_face', probe(CLIENT)['face'] == original['face'])
    join()
    emit('returning_player_loads_without_creation', wait_for(lambda: probe(CLIENT)['ready'], 30) and not probe(CLIENT)['editor'])
    emit('returning_player_restores_host_owned_face_and_gear', probe(CLIENT)['face'] == created['face'] and probe(CLIENT)['inventory'] == created['inventory'])
    stop_guest()
    command(HOST, 'stop')


if __name__ == '__main__':
    main()
