import json
import time
from driver import command,state,wait_for,ROOT,buff_ids
from matrix import matched,record,profile

def set_profile(name,loss):
    path=ROOT/'network-profile.json';temp=path.with_suffix('.tmp')
    temp.write_text(json.dumps({'name':name,'delay_ms':0,'jitter_ms':0,'loss_percent':loss}))
    temp.replace(path)
    # The proxy polls configuration every 0.1 second.
    time.sleep(.2)

for duration in [2,7]:
    profile('baseline',0,0,0)
    for role in ['host','client']:
        command(role,'clearBuffs');command(role,'heal',hp=100);command(role,'invincible',enabled=False)
    wait_for(lambda:matched('host','client') and matched('client','host'),5)
    start=time.monotonic();set_profile('outage_%ds'%duration,100)
    for role in ['host','client']:
        command(role,'damage',amount=10)
        for _ in range(3):command(role,'addBuff',id=1002)
        command(role,'removeBuff',id=1002,one=True);command(role,'removeBuff',id=1002)
    time.sleep(max(0,duration-(time.monotonic()-start)))
    set_profile('baseline',0)
    actual=time.monotonic()-start
    disconnected=any(state(r).get('peers')!=1 for r in ['host','client'])
    recovered=wait_for(lambda:matched('host','client') and matched('client','host'),3)
    if not recovered and any(state(r).get('peers')!=1 for r in ['host','client']):
        command('client','join',transport='Direct',port=19051)
    recovered=wait_for(lambda:matched('host','client') and matched('client','host') and all(
        1002 not in buff_ids(e['character']) for r in ['host','client'] for e in state(r).get('remotes',[])),20)
    record({'category':'outage_recovery','requested_seconds':duration,'actual_seconds':round(actual,2),
            'disconnected':disconnected,'rejoined':disconnected,'pass':recovered,'states':{r:state(r) for r in ['host','client']}})
