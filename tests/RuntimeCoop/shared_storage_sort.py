"""Native warehouse Sort button must never transfer items to a backpack."""
import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
import json
import time
from driver import command, state, wait_for, ROOT

ROLES = ['publichost', 'publicclient']
OUT = ROOT.parent / 'shared-storage-sort'


def parallel(action, **args):
    with ThreadPoolExecutor(2) as pool:
        return dict(zip(ROLES, pool.map(lambda r: command(r, action, **args), ROLES)))


def snapshot():
    return parallel('sharedStorage')


def totals(items):
    result = Counter()
    for item in items:
        result[item['type']] += item['count']
    return result


def emit(name, passed, **details):
    row = dict(case=name, passed=bool(passed), **details)
    with (OUT / (ARGS.phase + '.jsonl')).open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(row) + '\n')
    print(name, bool(passed), flush=True)
    if not passed and ARGS.phase != 'baseline':
        raise AssertionError(name)


def ready():
    return command(ROLES[0], 'sharedEntry')['active'] and command(ROLES[1], 'sharedEntry')['ready']


def main():
    if not all(state(r).get('network') for r in ROLES):
        parallel('loadMod')
        parallel('sharedEntry')
        parallel('loadBase', timeout=180)
    assert wait_for(lambda: all(state(r).get('initialized') for r in ROLES), 120)
    original = snapshot()
    if not state(ROLES[0]).get('network'):
        command(ROLES[0], 'sharedStorage', clear=True)
        command(ROLES[0], 'sharedEntry', enabled=True)
        command(ROLES[0], 'host', transport='Direct', port=19050)
        command(ROLES[1], 'join', transport='Direct', port=ARGS.port)
    assert wait_for(ready, 60), 'Shared loadout not ready'
    assert wait_for(lambda: snapshot()[ROLES[0]]['items'] == snapshot()[ROLES[1]]['items'], 30)
    for type_id, count in [(13, 3), (2, 1), (13, 4)]:
        command(ROLES[0], 'sharedHome', storageSeed=type_id, count=count)
    assert wait_for(lambda: snapshot()[ROLES[0]]['items'] == snapshot()[ROLES[1]]['items'], 30)
    parallel('sharedHome', openStorage=True)
    before = snapshot()
    command(ROLES[1], 'sharedStorage', sort=True)
    time.sleep(8)
    after = snapshot()
    emit('native_client_sort_preserves_warehouse_totals', all(totals(v['items']) == totals(before[ROLES[0]]['items']) for v in after.values()), before=before, after=after)
    emit('native_client_sort_does_not_fill_backpacks', all(v['backpack'] == before[r]['backpack'] for r, v in after.items()))
    emit('native_sort_converges_and_drains_transactions', after[ROLES[0]]['items'] == after[ROLES[1]]['items'] and all(not v['loading'] and not v['pending'] and not v['escrow'] for v in after.values()))
    if ARGS.phase != 'baseline':
        for i in range(30):
            command(ROLES[i % 2], 'sharedStorage', sort=True)
        time.sleep(8)
        final = snapshot()
        emit('thirty_alternating_sort_clicks_conserve_items', all(totals(v['items']) == totals(before[ROLES[0]]['items']) and v['backpack'] == before[r]['backpack'] for r, v in final.items()), after=final)
        emit('repeated_sort_converges_without_transfers', final[ROLES[0]]['items'] == final[ROLES[1]]['items'] and all(not v['loading'] and not v['pending'] and not v['escrow'] for v in final.values()))
    command(ROLES[1], 'stop')
    emit('disconnect_restores_personal_warehouse_and_gear', wait_for(lambda: command(ROLES[1], 'sharedStorage')['items'] == original[ROLES[1]]['items'] and command(ROLES[1], 'sharedStorage')['backpack'] == original[ROLES[1]]['backpack'], 20))
    command(ROLES[0], 'stop')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--phase', default='baseline')
    parser.add_argument('--port', type=int, default=19050)
    ARGS = parser.parse_args()
    main()
