import json
import time
from driver import ROOT,command,state,wait_for,buff_ids,connect
from matrix import profile,record

def same_buffs():
    for owner,observer in [('host','client'),('client','host')]:
        mirrors=state(observer).get('remotes') or []
        if not mirrors or any(buff_ids(e.get('character'))!=buff_ids(state(owner).get('local')) for e in mirrors):return False
    return True

for name,d,j,l in [('baseline',0,0,0),('rtt800_loss10',400,100,10)]:
    profile(name,d,j,l)
    if state('client').get('network'):
        command('client','stop')
        wait_for(lambda:state('host').get('peers')==0,12)
    for r in ['host','client']:
        command(r,'clearBuffs');command(r,'invincible',enabled=True)
        for _ in range(3):command(r,'addBuff',id=1002)
    if not state('host').get('network'):command('host','host',transport='Direct',port=19050)
    command('client','join',transport='Direct',port=19051)
    joined=wait_for(lambda:same_buffs() and all(buff_ids(state(r).get('local')).get(1002)==3 for r in ['host','client']),10)
    before={r:state(r) for r in ['host','client']}
    for r in ['host','client']:command(r,'removeBuff',id=1002,one=True)
    treated=wait_for(lambda:same_buffs() and all(buff_ids(state(r).get('local')).get(1002)==2 for r in ['host','client']),8)
    for r in ['host','client']:command(r,'clearBuffs')
    cleared=wait_for(lambda:same_buffs() and all(not buff_ids(state(r).get('local')) for r in ['host','client']),8)
    record({'category':'buff_rejoin_snapshot','harness_revision':2,'profile':name,'preexisting_layers_synced':joined,'partial_treatment':treated,
        'no_template_buffs':cleared,'pass':joined and treated and cleared,'before':before,'after':{r:state(r) for r in ['host','client']}})
profile('baseline',0,0,0)
