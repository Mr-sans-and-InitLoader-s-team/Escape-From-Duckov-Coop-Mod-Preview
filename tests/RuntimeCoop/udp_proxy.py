"""Local UDP impairment proxy; transport retransmission remains active on both peers."""
import argparse
import heapq
import json
import random
import select
import socket
import time
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--port', type=int, default=19051)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2] / 'artifacts/runtime-tests'
config = root / 'network-profile.json'
stats_path = root / ('network-statistics.json' if args.port == 19051 else f'network-statistics-{args.port}.json')
rng = random.Random(140019 + args.port - 19051)
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind(('127.0.0.1', args.port))
sock.setblocking(False)
server = ('127.0.0.1', 19050)
client = None
queue = []
sequence = 0
last_write = 0
profile = {'name': 'baseline', 'delay_ms': 0, 'jitter_ms': 0, 'loss_percent': 0}
try:
    stats = json.loads(stats_path.read_text(encoding='utf-8-sig')).get('profiles', {})
except (OSError,ValueError):
    stats = {}
print(f'UDP proxy listening on {args.port} -> 19050', flush=True)
while not (root / 'stop-proxy').exists():
    try:
        profile = json.loads(config.read_text(encoding='utf-8-sig'))
    except (OSError, ValueError):
        pass
    now = time.monotonic()
    while queue and queue[0][0] <= now:
        _, _, data, target = heapq.heappop(queue)
        try:
            sock.sendto(data, target)
        except OSError:
            pass  # A previous game instance may have closed its UDP endpoint.
    if select.select([sock], [], [], .005)[0]:
        try:
            data, address = sock.recvfrom(65535)
        except ConnectionResetError:
            continue
        if address != server:
            client = address
        target = client if address == server else server
        if target:
            item = stats.setdefault(profile['name'], {'received': 0, 'dropped': 0, 'bytes': 0, 'profile': profile})
            item['received'] += 1
            item['bytes'] += len(data)
            if rng.random() * 100 < profile.get('loss_percent', 0):
                item['dropped'] += 1
            else:
                delay = max(0, profile.get('delay_ms', 0) + rng.uniform(-profile.get('jitter_ms', 0), profile.get('jitter_ms', 0))) / 1000
                sequence += 1
                heapq.heappush(queue, (now + delay, sequence, data, target))
    if now - last_write > 1:
        last_write = now
        temp = stats_path.with_suffix('.tmp')
        try:
            temp.write_text(json.dumps({'profiles': stats, 'queued': len(queue)}, indent=2), encoding='utf-8')
            temp.replace(stats_path)
        except OSError:
            pass  # Statistics readers must not interrupt forwarding or disconnect a test peer.
