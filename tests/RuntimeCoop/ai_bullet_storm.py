"""Two private, input-disabled game processes; native AI guns and projectile replay."""
import argparse
import json
import time
from driver import command, state, wait_for, ROOT
from region_survey import maps, parallel, ROLES

OUT = ROOT.parent / 'ai-bullet-storm'


def setup():
    assert wait_for(lambda: all(state(r).get('batchMode') and state(r).get('cursor') == 'None' for r in ROLES), 90)
    parallel('loadMod')
    parallel('sharedEntry', enabled=False)
    parallel('loadBase', timeout=180)
    assert wait_for(lambda: all(state(r).get('initialized') and not state(r).get('loading') for r in ROLES), 120)
    command(ROLES[0], 'host', transport='Direct', port=19050)
    command(ROLES[1], 'join', transport='Direct', port=19051)
    assert wait_for(lambda: all(state(r).get('remotes') for r in ROLES), 60)
    entry = maps.enter('Level_Farm_Main', ROLES[0])
    assert entry['loaded'], entry
    parallel('invincible', enabled=True)
    command(ROLES[0], 'pauseAI')
    parallel('nativeInteraction', close=True)
    command(ROLES[0], 'safeMove', x=300.18, y=.02, z=693.7)
    assert wait_for(lambda: abs(state(ROLES[0])['local']['position'][0]-300.18)<.1, 5)
    print('connected, native Farm Town loaded, player input disabled', flush=True)


def prepare(count):
    position = state(ROLES[0])['local']['position']
    command(ROLES[1], 'safeMove', x=position[0]+1.5, y=position[1], z=position[2])
    assert wait_for(lambda: abs(state(ROLES[1])['local']['position'][0]-position[0]-1.5)<.2, 5)
    fixture = command(ROLES[0], 'aiFireStress', prepare=count, timeout=180)
    (OUT / 'fixture.json').write_text(json.dumps(fixture, indent=2), encoding='utf-8')
    parallel('aiFireStress')
    time.sleep(6)
    command(ROLES[0], 'pauseAI')
    print('native AI fixture', len(fixture['shooters']), 'guns', [s['gun'] for s in fixture['shooters']], flush=True)


def run(label, seconds=12, counts=(0, 1, 4, 8, 16), rate=10, miss=0):
    rows=[]
    for count in counts:
        command(ROLES[0], 'pauseAI')
        time.sleep(2)
        host = state(ROLES[0])['local']['position']
        client = state(ROLES[1])['local']['position']
        assert sum((a-b)**2 for a,b in zip(host,client))<10, (host,client)
        parallel('packetProfile', reset=True)
        parallel('aiFireStress', begin=True)
        result=command(ROLES[0], 'aiFireStress', seconds=seconds, count=count, rate=rate, missHeight=miss, timeout=seconds+30)
        profiles=parallel('aiFireStress', end=True)
        packets=parallel('packetProfile')
        row=dict(label=label, count=count, rate=rate, missHeight=miss, firing=result, profiles=profiles, packets=packets,
                 states={r:state(r) for r in ROLES})
        rows.append(row)
        with (OUT / (label+'.jsonl')).open('a', encoding='utf-8') as stream:
            stream.write(json.dumps(row, ensure_ascii=False)+'\n')
        print(label, count, 'shots', sum(result['shots']),
              {r:{k:profiles[r][k] for k in ['frames','p95Ms','p99Ms','allocatedBytes','collections','spawned','peakFakes']} for r in ROLES}, flush=True)
    return rows


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--setup', action='store_true')
    parser.add_argument('--prepare', type=int)
    parser.add_argument('--label', default='baseline')
    parser.add_argument('--seconds', type=float, default=12)
    parser.add_argument('--counts', default='0,1,4,8,16')
    parser.add_argument('--rate', type=float, default=10)
    parser.add_argument('--miss', type=float, default=0)
    args=parser.parse_args()
    if args.setup: setup()
    if args.prepare: prepare(args.prepare)
    run(args.label,args.seconds,tuple(map(int,args.counts.split(','))),args.rate,args.miss)
