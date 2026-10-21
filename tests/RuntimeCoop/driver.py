import argparse
import json
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'artifacts/runtime-tests'

def state(role):
    for _ in range(4):
        try:
            return json.loads((ROOT / role / 'state.json').read_text(encoding='utf-8-sig'))
        except (OSError, ValueError):
            time.sleep(.005)
    return {}

def command(role, action, timeout=60, _attempt=0, **kwargs):
    key = str(time.time_ns()) + '-' + action
    path = ROOT / role / 'commands' / (key + '.json')
    result = ROOT / role / 'results' / (key + '.json')
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps({'action': action, **kwargs}), encoding='utf-8')
    temp.replace(path)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            value = json.loads(result.read_text(encoding='utf-8-sig'))
            with (ROOT / 'command-results.jsonl').open('a', encoding='utf-8') as stream:
                stream.write(json.dumps({'role': role, 'key': key, **value}, ensure_ascii=False) + '\n')
            if not value['ok']:
                error=value.get('error','')
                if _attempt<3 and 'System.IO.IOException: Sharing violation on path' in error and str(path) in error and 'File.ReadAllText' in error:
                    time.sleep(.1)
                    return command(role,action,timeout=timeout,_attempt=_attempt+1,**kwargs)
                raise RuntimeError(value.get('error'))
            return value.get('result')
        except (FileNotFoundError,PermissionError):
            time.sleep(.05)
    raise TimeoutError(f'{role} {action} timed out')

def wait_for(predicate, timeout=10):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return True
        time.sleep(.1)
    return False

def connect():
    for role in ['host', 'client']:
        if not wait_for(lambda: bool(state(role).get('local')), 180):
            raise RuntimeError(f'{role} failed to finish loading: {state(role)}')
    command('host', 'host', transport='Direct', port=19050)
    command('client', 'join', transport='Direct', port=19051)
    if not wait_for(lambda: all(state(r).get('remotes') for r in ['host', 'client']), 40):
        raise RuntimeError(f'No replicas: host={state("host")} client={state("client")}')
    print('Connected two live game instances through UDP impairment proxy.', flush=True)

def buff_ids(character):
    return {b['id']: b['layers'] for b in (character or {}).get('buffs', [])}

def buff_suite():
    catalog = command('host', 'buffCatalog')
    (ROOT / 'buff-catalog.json').write_text(json.dumps(catalog, ensure_ascii=False, indent=2), encoding='utf-8')
    results = []
    for role, other in [('host', 'client'), ('client', 'host')]:
        command(role, 'invincible', enabled=True)
        for buff in catalog:
            ident = buff['id']
            start = time.monotonic()
            source = command(role, 'addBuff', id=ident)
            added = ident in buff_ids(source)
            synced = wait_for(lambda: any(ident in buff_ids(e.get('character')) for e in state(other).get('remotes', [])), 4) if added else False
            removed = command(role, 'removeBuff', id=ident)
            owner_removed = ident not in buff_ids(removed)
            cleared = wait_for(lambda: all(ident not in buff_ids(e.get('character')) for e in state(other).get('remotes', [])), 5) if owner_removed else False
            row = {'role': role, **buff, 'owner_added': added, 'mirror_added': synced,
                   'owner_removed': owner_removed, 'mirror_removed': cleared, 'seconds': round(time.monotonic() - start, 2)}
            results.append(row)
            with (ROOT / 'buff-results.jsonl').open('a', encoding='utf-8') as stream:
                stream.write(json.dumps(row, ensure_ascii=False) + '\n')
            print(f'Buff {role} {ident}: owner={added}/{owner_removed} mirror={synced}/{cleared}', flush=True)
        command(role, 'invincible', enabled=False)
    return results

def layer_probe():
    if not all(state(r).get('peers') == 1 and state(r).get('remotes') for r in ['host','client']):
        raise RuntimeError('Layer probe requires two connected replicas')
    results = []
    for role, other in [('host','client'),('client','host')]:
        command(role,'invincible',enabled=True)
        command(role,'removeBuff',id=1002)
        for _ in range(3): command(role,'addBuff',id=1002)
        wait_for(lambda:any(buff_ids(e.get('character')).get(1002)==3 for e in state(other).get('remotes',[])),4)
        before = {r:state(r) for r in [role,other]}
        result = command(role,'removeBuff',id=1002,one=True)
        matched = wait_for(lambda:any(buff_ids(e.get('character')).get(1002)==2 for e in state(other).get('remotes',[])),4)
        row={'role':role,'owner':buff_ids(result).get(1002),'mirror': [buff_ids(e.get('character')).get(1002) for e in state(other).get('remotes',[])], 'matched':matched}
        results.append(row)
        print('Layer probe',row,flush=True)
        command(role,'removeBuff',id=1002)
        command(role,'invincible',enabled=False)
    (ROOT/'layer-probe.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
    return results

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('suite', choices=['connect', 'buffs', 'layers'])
    args = parser.parse_args()
    if args.suite == 'connect': connect()
    if args.suite == 'buffs': buff_suite()
    if args.suite == 'layers': layer_probe()
