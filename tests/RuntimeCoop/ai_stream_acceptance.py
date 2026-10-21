"""Farm Town AI continuity and queue profiles, including client-only travel and UDP loss."""
import json
import math
import os
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from driver import command, state, wait_for

H, C = 'publichost', 'publicclient'
OUT = Path(__file__).resolve().parents[2] / 'artifacts/mode-selection'
PROFILE = OUT.parent / 'runtime-tests/network-profile.json'
checks = []


def emit(name, passed, **data):
    row = dict(name=name, passed=bool(passed), **data)
    checks.append(row)
    (OUT / 'ai-stream-acceptance.json').write_text(json.dumps(checks, ensure_ascii=False, indent=2), encoding='utf-8')
    print(name, bool(passed), json.dumps(data, ensure_ascii=False), flush=True)
    assert passed, name


def audit(role=C):
    return command(role, 'aiAudit')


def physical(a):
    return {e['id']: e['instance'] for e in a['replicas'] if e['activeInHierarchy']}


def missing():
    a = audit()
    ids = physical(a)
    position = state(C)['local']['position']
    authoritative = [e for e in audit(H)['entries'] if e['activated'] and e['status'] not in ['Dead', 'Despawned'] and not e['vehicle'] and
                     math.dist(position, [e['anchor'][axis] for axis in ['x', 'y', 'z']]) < a['activation'] - 2]
    return [e['id'] for e in authoritative if e['id'] not in ids]


def move(role, point):
    result = command(role, 'safeMove', x=point['x'] + 2, y=point['y'], z=point['z'])
    position = result['position']
    assert wait_for(lambda: state(role).get('local') and math.hypot(state(role)['local']['position'][0] - position[0],
                    state(role)['local']['position'][2] - position[2]) < .5, 8)
    time.sleep(.8)


def models_ready(timeout=25):
    deadline, clear_since = time.monotonic() + timeout, None
    while time.monotonic() < deadline:
        if missing():
            clear_since = None
        elif clear_since is None:
            clear_since = time.monotonic()
        elif time.monotonic() - clear_since >= 1:
            return True
        time.sleep(.2)
    return False


def network(name, delay=0, jitter=0, loss=0):
    PROFILE.write_text(json.dumps(dict(name=name, delay_ms=delay, jitter_ms=jitter, loss_percent=loss)), encoding='utf-8')


def joined(previous_character):
    current = state(C)
    return current.get('initialized') and current.get('local') and current['local']['id'] != previous_character and \
        current.get('remotes') and not audit()['joining']


def sample(name, seconds=15, cap=90):
    command(C, 'aiProfile', begin=True, frameCap=cap)
    start = time.monotonic()
    while time.monotonic() - start < seconds:
        time.sleep(.3)
    result = command(C, 'aiProfile', end=True)
    emit(name, result['maxSpawning'] <= 1 and result['maxStateQueue'] <= len(audit()['entries']), profile=result)
    return result


def setup():
    def load(role):
        command(role, 'loadMod')
        command(role, 'loadBase', timeout=90)
    list(ThreadPoolExecutor().map(load, [H, C]))
    command(H, 'sharedHome', enabled=False)
    command(H, 'loadScene', scene='Level_Farm_Main', timeout=90)
    assert wait_for(lambda: state(H).get('scene') == 'Level_Farm_01', 60)
    command(H, 'invincible', enabled=True)
    if os.environ.get('COOP_AI_SAME_MAP') == '1':
        command(C, 'loadScene', scene='Level_Farm_Main', timeout=90)
        assert wait_for(lambda: state(C).get('scene') == 'Level_Farm_01', 60)
        time.sleep(3)
        native_before = sum(e['hasCharacter'] for e in audit()['unregistered'])
        print('SAME_MAP_NATIVE_BEFORE', native_before, flush=True)
    command(H, 'host', transport='Direct', port=19050)
    previous_character = state(C)['local']['id']
    command(C, 'join', transport='Direct', port=19051)
    assert wait_for(lambda: joined(previous_character) and state(C).get('scene') == 'Level_Farm_01', 90)
    command(C, 'invincible', enabled=True)
    assert state(C)['local']['hp'] > 0
    assert wait_for(lambda: len(audit(H)['entries']) > 100, 40)
    assert wait_for(lambda: not audit()['joining'], 30)
    if os.environ.get('COOP_AI_SAME_MAP') == '1':
        native_after = sum(e['hasCharacter'] for e in audit()['unregistered'])
        emit('same_map_initial_join_replaces_local_world', native_before > 10 and native_after <= 1,
             before=native_before, after=native_after)


def main():
    network('ai-stream-baseline')
    setup()
    regressions = {role: command(role, 'aiStreamChecks') for role in [H, C]}
    (OUT / 'ai-stream-checks-after.json').write_text(json.dumps(regressions, ensure_ascii=False, indent=2), encoding='utf-8')
    emit('production_queue_lifecycle_and_asset_regressions', all(row['passed'] for rows in regressions.values() for row in rows), results=regressions)
    lifetime = command(C, 'aiAgentLifetime')
    emit('replaced_ai_agents_unsubscribe_before_end_of_frame', lifetime['staleCallbacks'] == 0 and lifetime['activeListeners'] == 1 and
         lifetime['unplugReleasedActiveAgent'], result=lifetime)
    time.sleep(2)
    native = audit(H)
    entries = [e for e in native['entries'] if e['status'] == 'Active' and not e['vehicle'] and 'pet' not in e['preset'].lower()]
    def distance(a, b):
        return math.hypot(a['x'] - b['x'], a['z'] - b['z'])
    host_position = state(H)['local']['position']
    distant = [e for e in entries if math.dist(host_position, [e['anchor'][axis] for axis in ['x', 'y', 'z']]) > 120]
    dense = max(distant, key=lambda e: sum(distance(e['anchor'], other['anchor']) < 45 for other in entries))['anchor']
    command(C, 'aiProfile', begin=True, frameCap=90)
    move(C, dense)
    command(C, 'aiAudit', refresh=True)
    appeared = models_ready() and len(physical(audit())) >= 4
    time.sleep(10)
    first = command(C, 'aiProfile', end=True)
    client_position = state(C)['local']['position']
    emit('client_only_exploration_generates_ai_with_host_stationary', appeared and state(H)['local']['position'] == host_position and
         math.hypot(client_position[0] - dense['x'], client_position[2] - dense['z']) < 5 and math.dist(host_position, client_position) > 80,
         profile=first, hostPosition=host_position, clientPosition=state(C)['local']['position'])
    emit('client_uses_native_local_scene_culling', audit()['nativeCulling']['activeBeyondLocalRange'] == 0, culling=audit()['nativeCulling'])
    emit('expected_ai_models_converge', models_ready(), missing=missing())
    stable = sample('farm_stationary_profile')
    snapshot_requests = next((e['calls'] for e in stable['methods'] if e['method'] == 'Client_SendSnapshotRequest'), 0)
    emit('no_repeated_three_second_snapshot_recovery_after_success', snapshot_requests <= 4, requests=snapshot_requests)
    move(H, dense)
    sample('farm_client_20fps_profile', cap=20)
    emit('low_fps_models_converge', models_ready(), missing=missing())

    before = physical(audit())
    for _ in range(3):
        command(C, 'aiAudit', refresh=True)
    time.sleep(3)
    after = physical(audit())
    changed = [id for id in before.keys() & after.keys() if before[id] != after[id]]
    emit('full_snapshots_do_not_recreate_existing_models', not changed, replaced=changed)

    far = max(entries, key=lambda e: distance(e['anchor'], dense))['anchor']
    assert distance(far, dense) > 300
    for index in range(8):
        move(C, far)
        time.sleep(.8)
        move(C, dense)
        emit(f'client_range_round_trip_{index + 1}', models_ready(), missing=missing())

    network('ai-stream-rtt800-loss10', 400, 100, 10)
    weak = sample('farm_weak_network_20fps_profile', seconds=20, cap=20)
    emit('weak_network_models_converge', models_ready(), missing=missing())
    emit('weak_network_no_duplicate_models_or_invalid_guns',
         len(audit()['replicas']) == len({e['id'] for e in audit()['replicas']}) and not audit()['invalidGuns'])
    network('ai-stream-restored')
    command(C, 'aiProfile', frameCap=90)
    assert models_ready()

    combat_and_recovery(dense, far)


def combat_and_recovery(dense, far):
    position = state(C)['local']['position']
    ordinary = [e for e in command(H, 'aiSnapshot') if e['preset'] == 'Cname_Scav' and e['status'] == 'Active' and e.get('character') and not e['character']['dead']]
    selected = min(ordinary, key=lambda e: math.dist(e['character']['position'], position))
    target = selected['id']
    command(H, 'combatTargetHealth', ai=target, hp=100000)
    point = dict(zip(['x', 'y', 'z'], selected['character']['position']))
    move(H, point)
    move(C, point)
    assert models_ready()

    client_models = physical(audit())
    assert target in client_models
    command(H, 'combatTargetHealth', ai=target, hp=100000)
    def health(role):
        return next(e['hp'] for e in command(role, 'aiSnapshot') if e['id'] == target)
    assert wait_for(lambda: health(C) > 99999, 15)
    for _ in range(20):
        command(C, 'damage', ai=target, amount=5, receiver=True)
    emit('client_native_hits_remain_host_authoritative', wait_for(lambda: health(H) < 100000 and abs(health(H) - health(C)) < .01, 15),
         hostHealth=health(H), clientHealth=health(C))
    command(H, 'addBuff', ai=target, id=1002)
    def has_buff(role):
        return any(b['id'] == 1002 for e in command(role, 'aiSnapshot') if e['id'] == target for b in e['character']['buffs'])
    emit('ai_buff_survives_pose_updates', wait_for(lambda: has_buff(C), 15))
    command(H, 'removeBuff', ai=target, id=1002)
    emit('ai_buff_removal_still_syncs', wait_for(lambda: not has_buff(C), 15))
    native_baseline = sum(e['hasCharacter'] for e in audit()['unregistered'])
    command(C, 'give', id=13, count=2)
    inventory_before = command(C, 'inventory')
    for index in range(3):
        move(C, far)
        time.sleep(1)
        move(C, dense)
        previous_character = state(C)['local']['id']
        command(C, 'stop')
        time.sleep(1)
        command(C, 'join', transport='Direct', port=19051)
        emit(f'reconnect_during_ai_loading_{index + 1}', wait_for(lambda: joined(previous_character), 90) and models_ready())
        command(C, 'invincible', enabled=True)
        time.sleep(2)
        current = audit()
        emit(f'reconnect_has_no_duplicate_or_orphan_models_{index + 1}', not current['invalidGuns'] and
             len(current['replicas']) == len({e['id'] for e in current['replicas']}) and all(e['activeInHierarchy'] for e in current['replicas']))
        native_now = sum(e['hasCharacter'] for e in current['unregistered'])
        emit(f'reconnect_clears_locally_generated_ai_{index + 1}', native_now <= native_baseline, before=native_baseline, after=native_now)
        emit(f'reconnect_preserves_player_inventory_{index + 1}', command(C, 'inventory') == inventory_before)
    final = {role: audit(role) for role in [H, C]}
    (OUT / 'ai-stream-final-world.json').write_text(json.dumps(final), encoding='utf-8')
    emit('native_ai_all_registered', not final[H]['unregistered'], count=len(final[H]['entries']))


if __name__ == '__main__':
    if '--resume-combat' in sys.argv:
        previous = json.loads((OUT / 'ai-stream-acceptance.json').read_text(encoding='utf-8'))
        checks[:] = [row for row in previous if row['passed']]
        dense = dict(zip(['x', 'y', 'z'], state(H)['local']['position']))
        entries = audit(H)['entries']
        far = max((e for e in entries if e['status'] == 'Active' and not e['vehicle']),
                  key=lambda e: math.dist([e['anchor'][axis] for axis in ['x', 'y', 'z']], list(dense.values())))['anchor']
        combat_and_recovery(dense, far)
    else:
        main()
