"""Private two-process shared join and return visit; no desktop input."""
import json
import time
from pathlib import Path
from driver import command, state, wait_for

HOST, CLIENT = 'publichost', 'publicclient'
OUT = Path(__file__).resolve().parents[2] / 'artifacts/join-feedback'
results = []


def ready():
    s = state(CLIENT)
    return (s.get('initialized') and not s.get('loading', True) and s.get('mainScene') == 'Base'
            and s.get('peers') == 1 and len(s.get('remotes', [])) == 1
            and command(CLIENT, 'sharedEntry')['ready'])


command(CLIENT, 'loadMod')
command(CLIENT, 'loadBase', timeout=90)
for role in (HOST, CLIENT):
    command(role, 'sharedHome')
command(HOST, 'sharedHome', enabled=True)
command(HOST, 'host', transport='Direct', port=19050)

for visit in range(2):
    if visit:
        command(CLIENT, 'loadBase', timeout=90)
    for role in (HOST, CLIENT):
        command(role, 'joinFeedback', monitor=True)
    command(CLIENT, 'join', transport='Direct', port=19050)
    deadline = time.monotonic() + 75
    while time.monotonic() < deadline:
        if ready():
            break
        if state(CLIENT).get('initialized') and not state(CLIENT).get('loading', True):
            if command(CLIENT, 'sharedCharacter')['editor']:
                command(CLIENT, 'sharedCharacter', confirm=True)
        time.sleep(.2)
    assert ready(), state(CLIENT)
    time.sleep(2)
    notices = {r: command(r, 'joinFeedback')['notifications'] for r in (HOST, CLIENT)}
    row = dict(visit=visit, notifications=notices, client=state(CLIENT), host=state(HOST))
    results.append(row)
    OUT.joinpath('shared-join.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    assert not any('解锁了商品' in text or 'unlocked' in text.lower()
                   for texts in notices.values() for text in texts), notices
    print('PASS shared join', visit, 'both peers ready; no replay unlock notifications', flush=True)
    command(CLIENT, 'stop')
    assert wait_for(lambda: state(CLIENT).get('scene') == 'MainMenu' and not state(CLIENT).get('loading', True), 45)

command(HOST, 'stop')
