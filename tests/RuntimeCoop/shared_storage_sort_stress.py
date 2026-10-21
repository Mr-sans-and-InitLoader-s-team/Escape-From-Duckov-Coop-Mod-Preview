"""Continue an initialized pair through weak-network sorting and native transfers."""
import json
import time
import shared_storage_sort as test
from driver import command, wait_for, ROOT

test.ARGS = type('Args', (), {'phase': 'stress-v4'})()
HOST, CLIENT = test.ROLES


def profile(name, delay=0, jitter=0, loss=0):
    (ROOT / 'network-profile.json').write_text(json.dumps(dict(name=name, delay_ms=delay, jitter_ms=jitter, loss_percent=loss)), encoding='utf-8')


def items(value):
    return [{k: v for k, v in item.items() if k != 'locked'} for item in value['items']]


def converged():
    values = test.snapshot()
    return items(values[HOST]) == items(values[CLIENT]) and all(not v['pending'] and not v['escrow'] and not v['loading'] for v in values.values())


def assert_conservation(name, before):
    assert wait_for(converged, 45), name + ' convergence'
    after = test.snapshot()
    test.emit(name, all(test.totals(v['items']) == test.totals(before[HOST]['items']) and v['backpack'] == before[r]['backpack'] for r, v in after.items()), before=before, after=after)


profile('sort_baseline')
for role in [CLIENT, HOST]:
    command(role, 'stop')
time.sleep(2)
original = test.snapshot()[CLIENT]
command(HOST, 'sharedStorage', clear=True)
command(HOST, 'sharedHome', storageSeed=2, count=1)
command(HOST, 'sharedHome', storageSeed=13, count=1)
for _ in range(2):
    command(HOST, 'sharedHome', storageSeed=2, count=1)
command(HOST, 'sharedEntry', enabled=True)
command(HOST, 'host', transport='Direct', port=19050)
command(CLIENT, 'join', transport='Direct', port=19051)
assert wait_for(test.ready, 60)
assert wait_for(converged, 30)
test.parallel('sharedHome', openStorage=True)
# The retained test loadout is full. Deposit one native backpack stack first.
before_put = test.snapshot()
deposit_type = next(i['type'] for i in before_put[CLIENT]['backpack'] if i['count'] >= 120)
command(CLIENT, 'sharedStorage', put=deposit_type)
assert wait_for(lambda: converged() and test.totals(test.snapshot()[HOST]['items'])[deposit_type] > 0, 30)
after_put = test.snapshot()
test.emit('native_deposit_frees_full_backpack_slot', test.totals(after_put[CLIENT]['backpack'] + after_put[CLIENT]['items']) == test.totals(before_put[CLIENT]['backpack'] + before_put[HOST]['items']), after=after_put)
for count in [10, 20]:
    command(HOST, 'sharedHome', storageSeed=140, count=count)
assert wait_for(converged, 30)
before = test.snapshot()
command(CLIENT, 'sharedStorage', sort=True)
time.sleep(2)
assert_conservation('native_sort_merges_stacks_without_transfers', before)
after = test.snapshot()
test.emit('ammo_stacks_merge_on_host', len([i for i in after[HOST]['items'] if i['type'] == 140]) == 1 and test.totals(after[HOST]['items'])[140] == 30)

slot = next((i['slot'] for i in after[HOST]['items'] if i['locked']), after[HOST]['items'][-1]['slot'])
command(HOST, 'sharedStorage', **{'lock': slot})
locked = next(i for i in test.snapshot()[HOST]['items'] if i['slot'] == slot)
for name, delay, jitter, loss in [('sort_baseline', 0, 0, 0), ('sort_rtt800_loss10', 400, 100, 10)]:
    profile(name, delay, jitter, loss)
    before = test.snapshot()
    for _ in range(15):
        test.parallel('sharedStorage', sort=True)
    time.sleep(5)
    assert_conservation(name + '_thirty_simultaneous_sorts', before)
    test.emit(name + '_host_locked_slot_preserved', locked in test.snapshot()[HOST]['items'])
    before = test.snapshot()
    slot = next(i['slot'] for i in before[CLIENT]['items'] if i['type'] == 140)
    command(CLIENT, 'sharedHome', takeStorage=slot)
    assert wait_for(lambda: converged() and test.totals(test.snapshot()[HOST]['items'])[140] == 0, 45)
    after = test.snapshot()
    test.emit(name + '_native_take_exactly_once', test.totals(after[CLIENT]['backpack'])[140] - test.totals(before[CLIENT]['backpack'])[140] == 30 and after[HOST]['backpack'] == before[HOST]['backpack'], after=after)
    command(CLIENT, 'sharedStorage', put=140)
    assert wait_for(converged, 45)
    after = test.snapshot()
    test.emit(name + '_native_put_conserves_combined_stock', all(test.totals(v['items'] + v['backpack']) == test.totals(before[HOST]['items'] + before[r]['backpack']) for r, v in after.items()), after=after)

profile('sort_baseline')
command(CLIENT, 'stop')
test.emit('stress_disconnect_restores_guest_warehouse_and_gear', wait_for(lambda: test.snapshot()[CLIENT]['items'] == original['items'] and test.snapshot()[CLIENT]['backpack'] == original['backpack'], 30))
command(HOST, 'stop')
