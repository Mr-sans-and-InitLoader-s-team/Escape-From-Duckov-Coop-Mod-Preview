"""Continue a connected private pair through conflicting warehouse operations."""
from concurrent.futures import ThreadPoolExecutor
import json
import time
import shared_storage_operations as t
from driver import command, wait_for, ROOT

t.ARGS = type('Args', (), {'phase': 'stress'})()
HOST, CLIENT = t.ROLES


def profile(name, delay=0, jitter=0, loss=0):
    (ROOT / 'network-profile.json').write_text(json.dumps(dict(name=name, delay_ms=delay,
        jitter_ms=jitter, loss_percent=loss)), encoding='utf-8')


for name, delay, jitter, loss in [('storage_baseline', 0, 0, 0), ('storage_rtt800_loss10', 400, 100, 10)]:
    profile(name, delay, jitter, loss)
    command(HOST, 'sharedStorage', clear=True)
    assert wait_for(t.settled, 30)
    slot = t.seed(140, 10)
    command(HOST, 'give', id=140, count=3)
    command(CLIENT, 'give', id=140, count=5)
    before = t.snapshots()
    with ThreadPoolExecutor(2) as pool:
        futures = [pool.submit(command, HOST, 'sharedStorage', put=140),
                   pool.submit(command, CLIENT, 'sharedStorage', put=140)]
        for f in futures:
            f.result()
    time.sleep(5)
    after = t.snapshots()
    t.emit(name + '_concurrent_deposit_conserves_items', t.settled() and
           t.total(after[HOST]['items'] + after[HOST]['backpack'] + after[CLIENT]['backpack'], 140) ==
           t.total(before[HOST]['items'] + before[HOST]['backpack'] + before[CLIENT]['backpack'], 140), before=before, after=after)
    before = t.snapshots()
    slot = next(i['slot'] for i in before[HOST]['items'] if i['type'] == 140)
    with ThreadPoolExecutor(2) as pool:
        futures = [pool.submit(command, HOST, 'sharedStorage', take=slot),
                   pool.submit(command, CLIENT, 'sharedStorage', drop=slot, repeat=20)]
        for f in futures:
            f.result()
    time.sleep(5)
    after = t.snapshots()
    t.emit(name + '_competing_take_and_discard_conserve_items', t.settled() and
           t.total(after[HOST]['items'] + after[HOST]['backpack'] + after[CLIENT]['backpack'] + after[HOST]['drops'], 140) ==
           t.total(before[HOST]['items'] + before[HOST]['backpack'] + before[CLIENT]['backpack'] + before[HOST]['drops'], 140), before=before, after=after)
profile('storage_baseline')
