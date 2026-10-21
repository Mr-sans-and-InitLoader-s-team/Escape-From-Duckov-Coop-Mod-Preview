"""Partial deposits, authoritative world drops and shared visitor reconnects."""
import json
import time
import shared_storage_operations as t
from driver import command, state, wait_for, ROOT

t.ARGS = type('Args', (), {'phase': 'persistence'})()
HOST, CLIENT = t.ROLES
(ROOT / 'network-profile.json').write_text(json.dumps(dict(name='storage_persistence', delay_ms=0, jitter_ms=0, loss_percent=0)), encoding='utf-8')
command(CLIENT, 'sharedStorage', fillBackpack=True)
source = command(CLIENT, 'sharedStorage')
source_count = t.total(source['backpack'], 140)
command(HOST, 'sharedStorage', clear=True)
slot = t.seed(140, source_count - 1)
before = t.snapshots()
command(CLIENT, 'sharedStorage', mergeFromBag=140, to=slot)
done = wait_for(lambda: t.settled() and t.total(command(HOST, 'sharedStorage')['items'], 140) == source_count + 1 and
               t.total(command(CLIENT, 'sharedStorage')['backpack'], 140) == source_count - 2, 30)
t.emit('partial_deposit_moves_only_target_free_space', done, before=before, after=t.snapshots())
command(CLIENT, 'sharedStorage', clearBackpack=True)
command(HOST, 'sharedStorage', clear=True)
slot = t.seed(13, 1)
before = t.snapshots()
command(HOST, 'sharedStorage', drop=slot)
done = wait_for(lambda: t.settled() and not command(HOST, 'sharedStorage')['items'] and
               all(t.total(v['drops'], 13) == t.total(before[r]['drops'], 13) + 1 for r, v in t.snapshots().items()), 30)
after = t.snapshots()
t.emit('host_discard_debits_stock_and_syncs_one_world_drop', done, before=before, after=after)
old_ids = {d['id'] for d in before[HOST]['drops']}
drop = next(d for d in after[HOST]['drops'] if d['id'] not in old_ids)
command(CLIENT, 'pickupDrop', id=drop['id'])
done = wait_for(lambda: all(drop['id'] not in {d['id'] for d in v['drops']} for v in t.snapshots().values()), 30)
after_pickup = t.snapshots()
t.emit('guest_pickup_removes_drop_on_both_peers_once', done and t.total(after_pickup[CLIENT]['backpack'], 13) == 1,
       before=after, after=after_pickup)
command(CLIENT, 'give', id=140, count=11)
command(CLIENT, 'sharedStorage', put=140)
assert wait_for(lambda: t.settled() and t.total(command(HOST, 'sharedStorage')['items'], 140) == 11 and
                t.total(command(CLIENT, 'sharedStorage')['backpack'], 140) == 0, 30)
time.sleep(2)
before = t.snapshots()
command(CLIENT, 'stop')
assert wait_for(lambda: command(CLIENT, 'sharedEntry')['menu'], 45)
command(CLIENT, 'join', transport='Direct', port=19051)
assert wait_for(lambda: command(CLIENT, 'sharedEntry')['ready'] and state(CLIENT).get('initialized'), 90)
assert wait_for(t.settled, 30)
after = t.snapshots()
t.emit('reconnect_keeps_host_stock_and_visitor_inventory', after[HOST]['items'] == before[HOST]['items'] and
       after[CLIENT]['items'] == before[HOST]['items'] and after[CLIENT]['backpack'] == before[CLIENT]['backpack'], before=before, after=after)
