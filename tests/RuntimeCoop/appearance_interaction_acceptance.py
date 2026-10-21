"""Native appearance interactions, walking, three viewpoints and host persistence."""
import json
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from driver import command, state, wait_for
from shared_face_switch import appearance

ROLES = ['publichost', 'publicclient', 'faceobserver']
HOST, CLIENT, OBSERVER = ROLES
OUT = Path(__file__).resolve().parents[2] / 'artifacts/appearance-interaction'
results = []


def parallel(action, **kwargs):
    with ThreadPoolExecutor(len(ROLES)) as pool:
        return dict(zip(ROLES, pool.map(lambda r: command(r, action, **kwargs), ROLES)))


def check(name, passed, **details):
    results.append(dict(case=name, passed=bool(passed), **details))
    OUT.joinpath('acceptance.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    print(('PASS ' if passed else 'FAIL ') + name, flush=True)
    assert passed, details


def at_base(role):
    s = state(role)
    return s.get('initialized') and not s.get('loading', True) and s.get('mainScene') == 'Base'


def editor_open(role):
    return (not state(role).get('loading', True) and state(role).get('initialized')
            and command(role, 'appearanceInteraction')['editor'])


def faces_match():
    values = parallel('faceState')
    expected = {v['id']: appearance(v['saved']) for v in values.values()}
    return all(appearance(v['own']) == expected[v['id']] and len(v['remotes']) == 2
               and all(appearance(r['face']) == expected.get(r['id']) for r in v['remotes'])
               for v in values.values())


parallel('loadMod')
parallel('loadBase', timeout=120)
parallel('sharedHome')
command(HOST, 'sharedHome', enabled=True)
command(HOST, 'host', transport='Direct', port=19050)
for role in ROLES[1:]:
    command(role, 'join', transport='Direct', port=19050)
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        if at_base(role) and command(role, 'sharedEntry')['ready']:
            break
        if editor_open(role):
            command(role, 'sharedCharacter', confirm=True)
        time.sleep(.2)
    check(role + '_joined', at_base(role) and command(role, 'sharedEntry')['ready'])
check('three_initial_viewpoints', wait_for(faces_match, 15))

for role, cancel in [(CLIENT, False), (CLIENT, True), (HOST, False), (OBSERVER, False)]:
    label = role + ('_cancel' if cancel else '_save')
    unchanged = {r: state(r)['sceneIndex'] for r in ROLES if r != role}
    original_face = appearance(command(role, 'faceState')['saved'])
    command(role, 'appearanceInteraction', interact=True)
    check(label + '_personal_scene', wait_for(lambda: state(role).get('initialized')
          and not state(role).get('loading', True) and state(role).get('mainScene') == 'Prologue_Main', 60), state=state(role))
    if not editor_open(role):
        command(role, 'appearanceInteraction', openEditor=True)
    check(label + '_native_editor', wait_for(lambda: editor_open(role), 12))
    if cancel:
        command(role, 'appearanceInteraction', previewColor=True, cancel=True)
    else:
        new_red = .17 if original_face['headSetting']['mainColor']['r'] > .5 else .83
        command(role, 'sharedCharacter', confirm=True, r=new_red,
                g=.53 if role == CLIENT else .37, b=.81, hairSteps=1)
    check(label + '_editor_closed', wait_for(lambda: not editor_open(role), 10))
    time.sleep(2)
    command(role, 'appearanceInteraction', returnBase=True)
    check(label + '_returned', wait_for(lambda: at_base(role), 60), state=state(role))
    movement = command(role, 'appearanceInteraction', drive=True)
    check(label + '_can_walk', movement['after']['canMove'] and movement['after']['inputActive']
          and not movement['after']['actionRunning'] and movement['distance'] > .2, movement=movement)
    check(label + '_others_stayed', all(state(r)['sceneIndex'] == scene for r, scene in unchanged.items()))
    check(label + '_all_viewpoints', wait_for(faces_match, 15), faces=parallel('faceState'))
    current_face = appearance(command(role, 'faceState')['saved'])
    check(label + '_appearance_result', current_face == original_face if cancel else current_face != original_face)
    if role != HOST:
        stored = command(HOST, 'sharedCharacter', identity='private-test-' + role)['storedFace']
        check(label + '_host_saved_face', wait_for(lambda: appearance(command(HOST, 'sharedCharacter',
              identity='private-test-' + role)['storedFace']) == current_face, 6), stored=stored)

saved = appearance(command(CLIENT, 'faceState')['saved'])
command(CLIENT, 'stop')
check('client_disconnect_menu', wait_for(lambda: state(CLIENT).get('scene') == 'MainMenu'
      and not state(CLIENT).get('loading', True), 45))
command(CLIENT, 'loadBase', timeout=90)
command(CLIENT, 'join', transport='Direct', port=19050)
check('client_rejoined', wait_for(lambda: at_base(CLIENT) and command(CLIENT, 'sharedEntry')['ready'], 60))
check('client_face_persisted', appearance(command(CLIENT, 'faceState')['saved']) == saved)
check('three_rejoin_viewpoints', wait_for(faces_match, 15))
