"""Visit native AI positions on both peers; verify each live AI gets a client model."""
import json
from pathlib import Path

from driver import command, wait_for

HOST, CLIENT = 'publichost', 'publicclient'
OUT = Path(__file__).resolve().parents[2] / 'artifacts/mode-selection'
seen, tested, failures = set(), [], []


def physical():
    audit = command(CLIENT, 'aiAudit')
    ids = {entry['id'] for entry in audit['replicas'] if entry['activeInHierarchy']}
    seen.update(ids)
    return ids


def live(entry):
    return not entry['vehicle'] and entry['status'] not in ['Dead', 'Despawned']


for sweep in range(3):
    targets = [entry for entry in command(HOST, 'aiAudit')['entries'] if live(entry) and entry['id'] not in seen]
    if not targets:
        break
    print('sweep', sweep, 'targets', len(targets), flush=True)
    for entry in targets:
        physical()
        if entry['id'] in seen:
            continue
        current = next((row for row in command(HOST, 'aiAudit')['entries'] if row['id'] == entry['id']), None)
        if not current or not live(current):
            continue
        point = current['anchor']
        position = dict(x=point['x'] + 2, y=point['y'], z=point['z'])
        try:
            command(HOST, 'safeMove', **position)
            command(CLIENT, 'safeMove', **position)
        except RuntimeError:
            failures.append(dict(id=entry['id'], preset=entry['preset'], reason='no supported ground'))
            continue
        awake = wait_for(lambda: any(row['id'] == entry['id'] and (row['activated'] or not live(row))
                                     for row in command(HOST, 'aiAudit')['entries']), 2)
        command(CLIENT, 'aiAudit', refresh=True)
        appeared = wait_for(lambda: entry['id'] in physical(), 4) if awake else False
        row = dict(id=entry['id'], preset=entry['preset'], awake=awake, appeared=appeared)
        tested.append(row)
        if not appeared:
            failures.append(row)
        if len(tested) % 10 == 0:
            print('visits', len(tested), 'models seen', len(seen), 'failures', len(failures), flush=True)

physical()
host, client = command(HOST, 'aiAudit'), command(CLIENT, 'aiAudit')
remaining = [entry for entry in host['entries'] if live(entry) and entry['id'] not in seen]
result = dict(hostCount=len(host['entries']), seen=sorted(seen), tested=tested, failures=failures, remaining=remaining,
              unregistered=host['unregistered'], invalidGuns=client['invalidGuns'],
              bosses=[entry for entry in host['entries'] if 'boss' in (entry['preset'] or '').lower()])
(OUT / 'ai-final-coverage.json').write_text(json.dumps(result), encoding='utf-8')
print('COMPLETE', result['hostCount'], 'models', len(seen), 'remaining', len(remaining), 'unregistered', len(host['unregistered']), flush=True)
assert not remaining and not host['unregistered'], 'See ai-final-coverage.json'
