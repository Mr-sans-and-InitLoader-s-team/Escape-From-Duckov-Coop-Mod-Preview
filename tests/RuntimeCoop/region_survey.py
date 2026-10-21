"""Capture native map markers, volume assets and spawner events in isolated peers."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import maps
from driver import command, state, wait_for, ROOT

ROLES = ['publichost', 'publicclient']
OUT = ROOT.parent / 'region-sync'
OUT.mkdir(exist_ok=True)
ALIASES = dict(host=ROLES[0], client=ROLES[1])
maps.ROLES = ROLES
maps.command = lambda role, *a, **k: command(ALIASES.get(role, role), *a, **k)
maps.state = lambda role: state(ALIASES.get(role, role))


def parallel(action, **kwargs):
    with ThreadPoolExecutor(2) as pool:
        return dict(zip(ROLES, pool.map(lambda r: command(r, action, **kwargs), ROLES)))


def capture(label):
    result = parallel('regions')
    (OUT / (label + '.json')).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    for role, row in result.items():
        print(role, 'markers', len(row['markers']), 'volumes', len(row['volumes']),
              'roots', len(row['roots']), 'selectors', len(row['selectors']), flush=True)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--setup', action='store_true')
    parser.add_argument('--scene')
    parser.add_argument('--label', default='before')
    args = parser.parse_args()
    if args.setup:
        parallel('loadMod')
        parallel('sharedEntry', enabled=False)
        parallel('loadBase', timeout=180)
        assert wait_for(lambda: all(state(r).get('initialized') for r in ROLES), 120)
        command(ROLES[0], 'host', transport='Direct', port=19050)
        command(ROLES[1], 'join', transport='Direct', port=19050)
        assert wait_for(lambda: all(state(r).get('remotes') for r in ROLES), 60)
    if args.scene:
        result = maps.enter(args.scene, ROLES[0])
        print('entry', result['voted'], result['loaded'], flush=True)
        assert result['loaded']
    capture(args.label)
