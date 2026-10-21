"""Native shared warehouse operations in isolated host/guest game copies."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
import time
from driver import command, state, wait_for, ROOT

ROLES = ['publichost', 'publicclient']
OUT = ROOT.parent / 'shared-storage-operations'


def parallel(action, **kwargs):
    with ThreadPoolExecutor(2) as pool:
        return dict(zip(ROLES, pool.map(lambda role: command(role, action, **kwargs), ROLES)))


def snapshots():
    return parallel('sharedStorage')


def settled():
    values = snapshots()
    return values[ROLES[0]]['items'] == values[ROLES[1]]['items'] and all(
        not v['pending'] and not v['escrow'] for v in values.values())


def emit(name, passed, **details):
    row = dict(case=name, passed=bool(passed), **details)
    with (OUT / (ARGS.phase + '.jsonl')).open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(row) + '\n')
    print(name, bool(passed), flush=True)
    if not passed and ARGS.phase != 'before':
        raise AssertionError(name)


def seed(type_id, count):
    before = {i['slot'] for i in command(ROLES[0], 'sharedStorage')['items']}
    command(ROLES[0], 'sharedHome', storageSeed=type_id, count=count)
    assert wait_for(settled, 20)
    return next(i['slot'] for i in command(ROLES[0], 'sharedStorage')['items'] if i['slot'] not in before)


def total(items, type_id):
    return sum(i['count'] for i in items if i['type'] == type_id)


def setup():
    parallel('loadMod')
    parallel('sharedEntry')
    parallel('loadBase', timeout=180)
    assert wait_for(lambda: all(state(r).get('initialized') for r in ROLES), 120)
    command(ROLES[0], 'sharedStorage', clear=True)
    command(ROLES[0], 'sharedEntry', enabled=True)
    command(ROLES[0], 'host', transport='Direct', port=19050)
    command(ROLES[1], 'join', transport='Direct', port=ARGS.port)
    assert wait_for(lambda: command(ROLES[1], 'sharedEntry')['ready'], 90)
    assert wait_for(settled, 20)
    parallel('sharedHome', openStorage=True)


def main():
    OUT.mkdir(exist_ok=True)
    if not ARGS.existing:
        setup()
    command(ROLES[0], 'sharedStorage', clear=True)
    slot = seed(140, 7)
    before = snapshots()
    command(ROLES[1], 'sharedStorage', drop=slot, repeat=10)
    assert wait_for(settled, 45)
    after = snapshots()
    emit('guest_discard_debits_host_and_creates_one_stack',
         not after[ROLES[0]]['items'] and all(total(v['drops'], 140) - total(before[r]['drops'], 140) == 7
                                             for r, v in after.items()), before=before, after=after)
    command(ROLES[0], 'sharedStorage', clear=True)
    slot = seed(140, 5)
    before = snapshots()
    command(ROLES[1], 'sharedStorage', move=slot, to=20)
    assert wait_for(settled, 45)
    after = snapshots()
    emit('guest_rearrange_stays_in_warehouse',
         all(any(i['slot'] == 20 and i['type'] == 140 and i['count'] == 5 for i in v['items']) and
             v['backpack'] == before[r]['backpack'] for r, v in after.items()), before=before, after=after)
    command(ROLES[0], 'sharedStorage', clear=True)
    a, b = seed(140, 3), seed(140, 4)
    before = snapshots()
    command(ROLES[1], 'sharedStorage', combine=b, to=a)
    assert wait_for(settled, 45)
    after = snapshots()
    emit('guest_combine_conserves_warehouse_and_backpack',
         all(total(v['items'], 140) == 7 and v['backpack'] == before[r]['backpack'] for r, v in after.items())
         and settled(), before=before, after=after)
    if ARGS.phase == 'before':
        return
    slot = after[ROLES[0]]['items'][0]['slot']
    before = snapshots()
    command(ROLES[1], 'sharedStorage', split=slot, count=2)
    assert wait_for(settled, 20)
    after = snapshots()
    emit('guest_split_debits_exact_amount', all(total(v['items'], 140) == 5 for v in after.values()) and
         total(after[ROLES[1]]['backpack'], 140) == total(before[ROLES[1]]['backpack'], 140) + 2, after=after)
    before = snapshots()
    command(ROLES[1], 'sharedStorage', take=slot)
    assert wait_for(settled, 20)
    after = snapshots()
    emit('guest_quick_take_debits_exact_amount', not after[ROLES[0]]['items'] and
         total(after[ROLES[1]]['backpack'], 140) == total(before[ROLES[1]]['backpack'], 140) + 5, after=after)
    before = snapshots()
    command(ROLES[1], 'sharedStorage', put=140)
    expected = total(before[ROLES[0]]['items'] + before[ROLES[1]]['backpack'], 140)
    assert wait_for(lambda: settled() and total(snapshots()[ROLES[0]]['items'] + snapshots()[ROLES[1]]['backpack'], 140) == expected, 30)
    after = snapshots()
    emit('guest_deposit_conserves_items', total(after[ROLES[0]]['items'] + after[ROLES[1]]['backpack'], 140) ==
         total(before[ROLES[0]]['items'] + before[ROLES[1]]['backpack'], 140), after=after)
    command(ROLES[0], 'sharedStorage', clear=True)
    slot = seed(140, 6)
    command(ROLES[1], 'give', id=140, count=2)
    before = snapshots()
    command(ROLES[1], 'sharedStorage', mergeToBag=slot, type=140)
    assert wait_for(settled, 45)
    after = snapshots()
    emit('guest_drag_merge_to_backpack_conserves_items', settled() and
         total(after[ROLES[0]]['items'] + after[ROLES[1]]['backpack'], 140) ==
         total(before[ROLES[0]]['items'] + before[ROLES[1]]['backpack'], 140), before=before, after=after)
    command(ROLES[0], 'sharedStorage', clear=True)
    gun = seed(240, 1)
    command(ROLES[0], 'sharedStorage', gunAmmo=gun, count=9)
    assert wait_for(settled, 20)
    before = snapshots()
    command(ROLES[1], 'sharedStorage', unload=gun)
    assert wait_for(settled, 20)
    after = snapshots()
    emit('guest_unload_preserves_all_rounds', all(total(v['items'], 140) == 9 and
         not next(i for i in v['items'] if i['type'] == 240)['children'] for v in after.values()) and
         all(v['backpack'] == before[r]['backpack'] for r, v in after.items()), before=before, after=after)
    before = snapshots()
    ammo = next(i['slot'] for i in before[ROLES[0]]['items'] if i['type'] == 140)
    command(ROLES[1], 'sharedStorage', uiMove=gun, to=ammo)
    assert wait_for(settled, 20)
    after = snapshots()
    emit('native_ui_swap_preserves_both_items', all(next(i for i in v['items'] if i['type'] == 240)['slot'] == ammo and
         next(i for i in v['items'] if i['type'] == 140)['slot'] == gun for v in after.values()) and
         all(v['backpack'] == before[r]['backpack'] for r, v in after.items()), before=before, after=after)
    before = snapshots()
    command(ROLES[1], 'sharedStorage', uiMove=gun, to=before[ROLES[1]]['bagEmpty'], toBag=True)
    assert wait_for(settled, 20)
    after = snapshots()
    emit('native_ui_drag_to_bag_conserves_ammunition', total(after[ROLES[0]]['items'] + after[ROLES[1]]['backpack'], 140) ==
         total(before[ROLES[0]]['items'] + before[ROLES[1]]['backpack'], 140), after=after)
    for _ in range(15):
        parallel('sharedStorage', sort=True)
    assert wait_for(settled, 30)
    final = snapshots()
    emit('thirty_sort_requests_preserve_items_and_backpacks', all(
         sorted((i['type'], i['count'], json.dumps(i['children'])) for i in v['items']) ==
         sorted((i['type'], i['count'], json.dumps(i['children'])) for i in after[r]['items']) and
         v['backpack'] == after[r]['backpack'] for r, v in final.items()), before=after, after=final)
    command(ROLES[1], 'sharedStorage', fillBackpack=True)
    command(ROLES[0], 'sharedStorage', clear=True)
    slot = seed(140, 10)
    before = snapshots()
    command(ROLES[1], 'sharedStorage', take=slot)
    assert wait_for(settled, 20)
    after = snapshots()
    emit('full_backpack_partial_room_does_not_duplicate_rollback', after[ROLES[0]]['items'] == before[ROLES[0]]['items'] and
         after[ROLES[1]]['backpack'] == before[ROLES[1]]['backpack'], before=before, after=after)
    command(ROLES[1], 'sharedStorage', clearBackpack=True)
    command(ROLES[0], 'sharedStorage', clear=True)
    slot = seed(13, 1)
    command(ROLES[1], 'invoke', method='set_CurrentWater', args=[1])
    command(ROLES[1], 'invoke', method='set_CurrentEnergy', args=[1])
    before = snapshots()
    command(ROLES[1], 'sharedStorage', use=slot)
    used = wait_for(lambda: settled() and not snapshots()[ROLES[0]]['items'] and
        total(snapshots()[ROLES[1]]['backpack'], 13) == 0 and not snapshots()[ROLES[1]]['useRunning'], 25)
    emit('native_consumable_use_debits_host_once', used, before=before, after=snapshots())


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--phase', default='before')
    parser.add_argument('--port', type=int, default=19050)
    parser.add_argument('--existing', action='store_true')
    ARGS = parser.parse_args()
    main()
