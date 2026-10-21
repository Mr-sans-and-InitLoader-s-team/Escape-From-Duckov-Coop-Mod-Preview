"""Late join, corpse replay, native AI coverage and reconnect through a real UDP proxy."""
import json
import math
import time
from pathlib import Path

from driver import command, state, wait_for

HOST, CLIENT = 'publichost', 'publicclient'
OUT = Path(__file__).resolve().parents[2] / 'artifacts/mode-selection'
PROFILE = OUT.parent / 'runtime-tests/network-profile.json'


def emit(name, ok, **data):
    with (OUT / 'recovery-checks.jsonl').open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(dict(name=name, passed=bool(ok), **data), ensure_ascii=False) + '\n')
    print(name, bool(ok), flush=True)
    assert ok, name


def near():
    host, client = state(HOST), state(CLIENT)
    return host.get('local') and client.get('local') and host['mainScene'] == client['mainScene'] and \
        math.dist(host['local']['position'], client['local']['position']) < 2 and client.get('remotes')


def missing_models():
    audit = command(CLIENT, 'aiAudit')
    physical = {entry['id'] for entry in audit['replicas']}
    return [entry['id'] for entry in audit['entries'] if entry['expectedReplica'] and entry['id'] not in physical]


def profile(name, delay=0, jitter=0, loss=0):
    PROFILE.write_text(json.dumps(dict(name=name, delay_ms=delay, jitter_ms=jitter, loss_percent=loss)), encoding='utf-8')


def main():
    command(HOST, 'sharedHome', enabled=False)
    command(HOST, 'loadScene', scene='Level_Farm_Main', timeout=90)
    assert wait_for(lambda: state(HOST).get('initialized'), 60)
    command(HOST, 'invincible', enabled=True)
    command(HOST, 'host', transport='Direct', port=19050)
    time.sleep(2)
    audit = command(HOST, 'aiAudit')
    (OUT / 'host-ai-initial.json').write_text(json.dumps(audit), encoding='utf-8')
    emit('farm_host_all_native_ai_registered', not audit['unregistered'], entries=len(audit['entries']), unregistered=audit['unregistered'])
    command(HOST, 'dropItem', id=13, count=3)
    before = {entry['uid'] for entry in command(HOST, 'worldInventories')}
    enemies = command(HOST, 'aiSnapshot')
    victim = next(entry for entry in enemies if entry.get('character') and not entry['character']['dead'] and 'pet' not in entry['preset'].lower())
    command(HOST, 'damage', ai=victim['id'], amount=100000, receiver=True)
    time.sleep(1)
    corpses = [entry['uid'] for entry in command(HOST, 'worldInventories') if entry['uid'] not in before]
    emit('native_ai_corpse_fixture_created', bool(corpses), uids=corpses)
    command(CLIENT, 'join', transport='Direct', port=19051)
    emit('farm_independent_late_join_at_host', wait_for(near, 90))
    command(CLIENT, 'invincible', enabled=True)
    emit('preexisting_corpse_replayed', wait_for(lambda: all(any(entry['uid'] == uid for entry in command(CLIENT, 'worldInventories')) for uid in corpses), 20))
    for _ in range(3):
        command(CLIENT, 'sessionUI', replayWorld=True)
    inventories = [entry['uid'] for entry in command(CLIENT, 'worldInventories')]
    emit('repeated_world_snapshot_does_not_duplicate_corpses', all(inventories.count(uid) == 1 for uid in corpses))
    emit('farm_client_expected_ai_all_have_models', wait_for(lambda: not missing_models(), 20), firstMissing=missing_models())
    audit = command(CLIENT, 'aiAudit')
    emit('farm_client_no_duplicate_ai_models', len(audit['replicas']) == len({entry['id'] for entry in audit['replicas']}))
    (OUT / 'client-ai-initial.json').write_text(json.dumps(audit), encoding='utf-8')
    profile('recovery-rtt800-loss10', 400, 100, 10)
    command(CLIENT, 'stop')
    command(CLIENT, 'loadBase', timeout=90)
    command(CLIENT, 'join', transport='Direct', port=19051)
    emit('weak_network_late_join', wait_for(near, 100))
    command(CLIENT, 'invincible', enabled=True)
    command(HOST, 'stop')
    emit('weak_network_disconnect_detected', wait_for(lambda: command(CLIENT, 'sessionUI')['reconnecting'], 20))
    command(HOST, 'host', transport='Direct', port=19050)
    emit('weak_network_auto_reconnect', wait_for(near, 40))
    command(HOST, 'stop')
    assert wait_for(lambda: command(CLIENT, 'sessionUI')['reconnecting'], 20)
    command(CLIENT, 'sessionUI', cancelReconnect=True)
    time.sleep(6)
    emit('weak_network_cancel_stays_disconnected', not command(CLIENT, 'sessionUI')['reconnecting'] and not state(CLIENT)['network'])
    profile('recovery-baseline')


if __name__ == '__main__':
    main()
