import json
import time
from collections import Counter
from driver import ROOT,command,state,wait_for
from maps import enter,leave,ready_vote
from matrix import profile,record
ROLES=['host','client']

def inspect_world():
    worlds={r:command(r,'worldSnapshot') for r in ROLES}
    duplicates={r:[ident for ident,count in Counter(t['id'] for t in worlds[r]['aiReplicas'] if t['active']).items() if ident and count>1] for r in ROLES[1:]}
    # AI replicas also carry RemoteReplicaTag; exclude their known character IDs.
    player_counts={r:len([p for p in worlds[r]['players'] if p['active'] and p['character'] not in
        {a['character'] for a in worlds[r]['aiReplicas']}]) for r in ROLES}
    host_ai={a['id']:a for a in worlds['host']['ai']}
    hp_mismatch={r:[a['id'] for a in worlds[r]['ai'] if a['id'] in host_ai and host_ai[a['id']]['status']=='Active' and abs(a['hp']-host_ai[a['id']]['hp'])>.05] for r in ROLES[1:]}
    return {'pass':not any(duplicates.values()) and all(n==len(ROLES)-1 for n in player_counts.values()) and not any(hp_mismatch.values()),
        'duplicate_ai':duplicates,'players':player_counts,'hp_mismatch':hp_mismatch,'worlds':worlds}

def transfer(source,portal,target_index):
    started=time.monotonic()
    command(source,'teleport',id=portal['id'])
    voted=ready_vote(60)
    loaded=wait_for(lambda:all(state(r).get('sceneIndex')==target_index and state(r).get('initialized') and
        not state(r).get('blackScreen') and len(state(r).get('remotes',[]))==len(ROLES)-1 and state(r).get('peers')==(len(ROLES)-1 if r=='host' else 1) for r in ROLES),90)
    if not loaded:return {'pass':False,'voted':voted,'loaded':False,'states':{r:state(r) for r in ROLES}}
    time.sleep(2)
    world=inspect_world()
    # Allow bounded registry rescans/replica reconstruction before declaring a persistent failure.
    if not world['pass']:
        time.sleep(5);world=inspect_world()
    return {'pass':voted and loaded and world['pass'],'voted':voted,'loaded':loaded,'world':world,
        'seconds':round(time.monotonic()-started,2),'states':{r:state(r) for r in ROLES}}

def main():
    profile('baseline',0,0,0)
    result=enter('Level_Farm_Main','client')
    if not result['loaded']:raise RuntimeError('Farm entry failed')
    catalog=command('host','sceneCatalog');indices={e['id']:e['index'] for e in catalog}
    original=state('host')['scene']
    portals=command('client','teleporterCatalog')
    (ROOT/'farm-teleporters.json').write_text(json.dumps(portals,ensure_ascii=False,indent=2),encoding='utf-8')
    candidate=next((p for p in portals if p['target']!=original and p['target'] in indices),None)
    if not candidate:raise RuntimeError('No native additive teleporter in current scene')
    target=candidate['target']
    for name,d,j,l in [('baseline',0,0,0),('rtt100_loss1',50,15,1),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        for trip in range(3):
            for leg,destination in [('enter',target),('return',original)]:
                source=ROLES[(trip+(leg=='return'))%len(ROLES)]
                portal=next((p for p in command(source,'teleporterCatalog') if p['target']==destination),None)
                if not portal:
                    record({'category':'additive_scene_stress','profile':name,'trip':trip,'leg':leg,'pass':False,'outcome':'return_portal_missing','portals':command(source,'teleporterCatalog')});return False
                outcome=transfer(source,portal,indices[destination])
                record({'category':'additive_scene_stress','profile':name,'trip':trip,'leg':leg,'initiator':source,'target':destination,**outcome})
                if not outcome['pass']:return False
    profile('baseline',0,0,0)
    exit_result=leave('client')
    record({'category':'additive_stress_exit','result':exit_result,'pass':exit_result['pass']})
    return exit_result['pass']

if __name__=='__main__':main()
