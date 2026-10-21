"""Native warehouse reloads must not become shared-player item transactions."""
import argparse
import json
from pathlib import Path
import shared_storage_operations as t
from driver import command, state, wait_for

OUT = Path('artifacts/shared-storage-loading')
ROLES = t.ROLES


def emit(name, passed, **details):
    row = dict(case=name, passed=bool(passed), **details)
    with (OUT / 'after.jsonl').open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(row) + '\n')
    print(name, bool(passed), flush=True)
    assert passed, name


def main():
    t.ARGS = type('Args', (), dict(port=19050))()
    t.setup()
    t.seed(140, 5)
    before_host = command(ROLES[0], 'sharedStorage')['items']
    for name, args in [('native_warehouse_reload', {}),
                       ('native_reload_before_character_exists', dict(missingCharacter=True)),
                       ('native_inventory_data_reload', dict(inventoryOnly=True))]:
        result = command(ROLES[1], 'sharedStorageLoading', **args)
        emit(name, not result['error'] and not result['loadingStuck'] and not result['inventoryLoadingStuck'] and
             result['sent'] == 0 and result['before'] == result['after'], result=result)
        emit(name + '_host_stock_unchanged', wait_for(t.settled, 20) and command(ROLES[0], 'sharedStorage')['items'] == before_host)
    emit('reloads_leave_both_games_initialized', all(state(r).get('initialized') and not state(r).get('blackScreen') for r in ROLES))


if __name__ == '__main__':
    main()
