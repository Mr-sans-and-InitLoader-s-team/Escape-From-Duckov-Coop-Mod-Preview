"""Exercise production AI queues, lifecycle and native sampling inside two isolated games."""
import json
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from driver import command, state, wait_for

roles = ['publichost', 'publicclient']
phase = sys.argv[1]
out = Path(__file__).resolve().parents[2] / 'artifacts/mode-selection'

try:
    def load(role):
        command(role, 'loadMod')
        command(role, 'loadBase', timeout=90)
    list(ThreadPoolExecutor().map(load, roles))
    command(roles[0], 'sharedHome', enabled=False)
    command(roles[0], 'loadScene', scene='Level_Farm_Main', timeout=90)
    assert wait_for(lambda: state(roles[0]).get('scene') == 'Level_Farm_01', 60)
    command(roles[0], 'invincible', enabled=True)
    command(roles[0], 'host', transport='Direct', port=19050)
    command(roles[1], 'join', transport='Direct', port=19050)
    assert wait_for(lambda: state(roles[1]).get('scene') == 'Level_Farm_01' and state(roles[1]).get('remotes'), 90)
    command(roles[1], 'invincible', enabled=True)
    time.sleep(2)
    results = {role: command(role, 'aiStreamChecks') for role in roles}
    (out / f'ai-stream-checks-{phase}.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
    for role, rows in results.items():
        for row in rows:
            print(role, row['name'], row['passed'], row['evidence'], flush=True)
    if phase != 'before':
        assert all(row['passed'] for rows in results.values() for row in rows)
finally:
    for role in roles:
        command(role, 'stop')
        command(role, 'quit')
