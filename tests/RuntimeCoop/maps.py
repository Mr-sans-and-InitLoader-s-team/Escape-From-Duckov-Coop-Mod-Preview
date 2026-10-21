"""Native map entry, exit countdown, co-op vote, settlement and base-return checks."""
import json
import time
from driver import ROOT,command,state,wait_for
from matrix import profile,record

ROLES=['host','client']
BASE_SCENES={'Base_SceneV2','Base_SceneV2_2','Base_Scene','NewBase'}

def settlement_ready(timeout=45):
    from mono_confirm import query
    next_override=0
    def ready():
        nonlocal next_override
        if all((state(r).get('closure') or {}).get('canContinue') for r in ROLES):return True
        if time.monotonic()>=next_override:
            next_override=time.monotonic()+2
            for r in ROLES:
                if (state(r).get('closure') or {}).get('canContinue'):continue
                override=query(r,island=True)
                if override['shown']:
                    confirmed=query(r,confirm=True,island=True)
                    record({'category':'native_override_settlement_confirm','result':confirmed})
        return False
    return wait_for(ready,timeout)

def ready_vote(timeout=12):
    if not wait_for(lambda:command('host','sceneVote')['active'],timeout):return False
    if not wait_for(lambda:all(command(r,'sceneVote')['active'] for r in ROLES),timeout):return False
    for role in ROLES:command(role,'ready')
    return True

def enter(scene,initiator):
    previous={r:(state(r).get('local') or {}).get('id') for r in ROLES}
    command(initiator,'loadScene',scene=scene,timeout=180)
    voted=ready_vote()
    protected=set()
    def ready():
        snapshots={r:state(r) for r in ROLES}
        for r,snapshot in snapshots.items():
            if r not in protected and snapshot.get('initialized') and (snapshot.get('local') or {}).get('id')!=previous[r]:
                command(r,'invincible',enabled=True);protected.add(r)
        return all(s.get('raid') and s.get('initialized') and not s.get('loading') and not s.get('blackScreen') and
            s.get('remotes') and (s.get('local') or {}).get('id')!=previous[r] and not s['local']['dead'] for s in snapshots.values())
    loaded=wait_for(ready,180)
    return {'voted':voted,'loaded':loaded,'states':{r:state(r) for r in ROLES}}

def leave(initiator,settlement_wait=1):
    catalog=command(initiator,'exitCatalog')
    usable=[a for a in catalog if a['active'] and a['enabled'] and any(p['target']=='Base' for p in a['proxies'])]
    if not usable:return {'pass':False,'outcome':'no_active_countdown_to_base','catalog':catalog}
    area=usable[0]
    command(initiator,'enterExit',id=area['id'])
    voted=ready_vote(timeout=area['seconds']+15)
    closure=settlement_ready(45)
    if closure:
        time.sleep(settlement_wait)
        for r in ROLES:command(r,'continue')
    returned=wait_for(lambda:all(state(r).get('initialized') and not state(r).get('raid') and state(r).get('scene') in BASE_SCENES and
        not state(r).get('loading') and not state(r).get('blackScreen') for r in ROLES),180)
    samples=[]
    if returned:
        for _ in range(12):
            samples.append({r:state(r) for r in ROLES})
            time.sleep(.5)
    stable=bool(samples) and all(not s[r].get('raid') and s[r].get('initialized') and s[r].get('scene') in BASE_SCENES and s[r].get('peers')==(len(ROLES)-1 if r=='host' else 1) and not s[r].get('loading') and not s[r].get('blackScreen') for s in samples for r in ROLES)
    votes_cleared=returned and all(not command(r,'sceneVote')['active'] for r in ROLES)
    return {'pass':voted and closure and returned and stable and votes_cleared,'exit':area,'voted':voted,'closure':closure,'returned':returned,'stable':stable,'votes_cleared':votes_cleared,'states':{r:state(r) for r in ROLES}}

def main():
    profile('baseline',0,0,0)
    catalog=command('host','sceneCatalog')
    scenes=[e['id'] for e in catalog if e['index']>=0 and e['id'].startswith('Level_') and e['id'].endswith('_Main')]
    # Parentless built maps are reached through their native main scene where one exists.
    for index,scene in enumerate(scenes):
        for source in ['client','host']:
            start=time.monotonic()
            entry=enter(scene,source)
            result=leave(source,15 if index==0 and source=='client' else 1) if entry['loaded'] else {'pass':False,'outcome':'entry_timeout'}
            record({'category':'map_evacuation','scene':scene,'initiator':source,'entry':entry,'exit':result,'pass':entry['loaded'] and result['pass'],'seconds':round(time.monotonic()-start,1)})
            if not result.get('returned'):
                print('Map run stopped at a non-returned scene for diagnosis.',flush=True)
                return

if __name__=='__main__':main()
