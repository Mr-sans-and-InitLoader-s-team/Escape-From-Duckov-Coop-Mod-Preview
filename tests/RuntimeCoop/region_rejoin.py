import json
import os
import time
from driver import command,state,wait_for,ROOT

def areas(role):
    return [m for m in command(role,'regions')['markers'] if m['IsArea']]

def emit(name,passed):
    row=dict(phase=os.environ.get('REGION_REJOIN_PHASE','late-join'),case=name,passed=bool(passed),elapsed=round(time.monotonic()-started,2))
    with (ROOT.parent/'region-sync/acceptance.jsonl').open('a',encoding='utf-8') as f:
        f.write(json.dumps(row)+'\n')
    print(name,bool(passed),flush=True)
    assert passed,name

started=time.monotonic()
old=state('publicclient')['local']['id']
command('publicclient','stop')
assert wait_for(lambda:state('publichost').get('peers')==0,20)
command('publicclient','join',transport='Direct',port=19051)
emit('late_join_reloads_native_raid_without_black_screen',wait_for(lambda:state('publicclient').get('initialized') and
     not state('publicclient').get('blackScreen') and (state('publicclient').get('local') or {}).get('id')!=old and
     len(state('publicclient').get('remotes',[]))==1,120))
emit('late_join_restores_all_native_area_markers',wait_for(lambda:sorted((m['color'],m['AreaRadius'],m['mapScene']) for m in areas('publicclient'))==
     sorted((m['color'],m['AreaRadius'],m['mapScene']) for m in areas('publichost')),30))
emit('late_join_registers_each_area_once',all(e['count']==1 for e in command('publicclient','regions')['areaRegistrations']))
(ROOT/'network-profile.json').write_text(json.dumps(dict(name=os.environ.get('REGION_PROFILE','region_rtt800_loss10'),delay_ms=400,jitter_ms=100,loss_percent=10)),encoding='utf-8')
print('Enabled 400 ms one-way latency, 100 ms jitter and 10 percent loss',flush=True)
