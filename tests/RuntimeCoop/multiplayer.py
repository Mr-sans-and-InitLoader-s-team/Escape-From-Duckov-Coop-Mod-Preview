"""Three/four native instances: every assertion identifies the player by its network ID."""
import json
import time
import sys
from concurrent.futures import ThreadPoolExecutor
from driver import ROOT,command,state,wait_for,buff_ids
from matrix import profile,record
import maps

ALL=['host','client','guest3','guest4']
PORTS={'client':19051,'guest3':19052,'guest4':19053}
ROLES=ALL[:3]

def parallel(action,roles=None,**kwargs):
    with ThreadPoolExecutor(max_workers=4) as pool:
        return dict(zip(roles or ROLES,pool.map(lambda r:command(r,action,**kwargs),roles or ROLES)))

def remote(owner,observer):
    pid=state(owner).get('playerId')
    return next((e['character'] for e in state(observer).get('remotes',[]) if e['key']==pid),None)

def peers_ready():
    return all(state(r).get('peers')==(len(ROLES)-1 if r=='host' else 1) and len(state(r).get('remotes',[]))==len(ROLES)-1 for r in ROLES)

def matched(owner,test):
    local=state(owner).get('local')
    return bool(local and test(local) and all(remote(owner,o) and test(remote(owner,o)) for o in ROLES if o!=owner))

def group_record(category,**kwargs):
    record({'category':category,'players':len(ROLES),**kwargs})

def setup():
    for r in ALL:
        if not wait_for(lambda:state(r).get('time',99999)<180,120):raise RuntimeError('New process failed to start: '+r)
    parallel('loadMod',ALL)
    parallel('loadBase',ALL,timeout=180)
    if not wait_for(lambda:all(state(r).get('initialized') and state(r).get('local') for r in ALL),180):raise RuntimeError('Base loading failed')
    command('host','host',transport='Direct',port=19050)
    for r in ROLES[1:]:command(r,'join',transport='Direct',port=PORTS[r])
    joined=wait_for(peers_ready,45)
    group_record('multiplayer_join',**{'pass':joined},states={r:state(r) for r in ROLES})
    if not joined:raise RuntimeError('Three-player replicas failed')

def buffs(profile_name='baseline'):
    parallel('invincible',enabled=True);parallel('clearBuffs')
    for b in command('host','buffCatalog'):
        bid=b['id']
        added=parallel('addBuff',id=bid)
        expected={r:buff_ids(added[r]).get(bid,0) for r in ROLES}
        converged=wait_for(lambda:all(not expected[r] or matched(r,lambda c:buff_ids(c).get(bid,0)==expected[r]) for r in ROLES),12)
        before={r:state(r) for r in ROLES}
        parallel('removeBuff',id=bid)
        cleared=wait_for(lambda:all(matched(r,lambda c:bid not in buff_ids(c)) for r in ROLES),8)
        still_active={r:bid in buff_ids(before[r].get('local')) for r in ROLES}
        group_record('multiplayer_buff',profile=profile_name,id=bid,native_added=expected,mirror_added=converged,owner_still_active=still_active,
            terminal_clear=cleared,**{'pass':cleared and (converged or not any(still_active.values()))})

def hp_layers(profiles=None):
    for name,d,j,l in profiles or [('baseline',0,0,0),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        parallel('clearBuffs');parallel('invincible',enabled=True)
        for _ in range(3):parallel('addBuff',id=1002)
        three=wait_for(lambda:all(matched(r,lambda c:buff_ids(c).get(1002)==3) for r in ROLES),15)
        parallel('removeBuff',id=1002,one=True)
        two=wait_for(lambda:all(matched(r,lambda c:buff_ids(c).get(1002)==2) for r in ROLES),15)
        parallel('removeBuff',id=1002)
        zero=wait_for(lambda:all(matched(r,lambda c:1002 not in buff_ids(c)) for r in ROLES),15)
        group_record('multiplayer_layers',profile=name,three=three,two=two,zero=zero,**{'pass':three and two and zero})
        parallel('invincible',enabled=False);parallel('heal',hp=100)
        if not wait_for(lambda:all(matched(r,lambda c:abs(c['hp']-100)<.02) for r in ROLES),15):raise RuntimeError('Group heal failed')
        hits=parallel('damage',amount=10)
        converged=wait_for(lambda:all(matched(r,lambda c:abs(c['hp']-hits[r]['after'])<.02) for r in ROLES),15)
        expected={r:hits[r]['after'] for r in ROLES}
        samples=[]
        for _ in range(15):
            samples.append(all(matched(r,lambda c:abs(c['hp']-expected[r])<.02) for r in ROLES));time.sleep(.2)
        group_record('multiplayer_simultaneous_damage',profile=name,hits=hits,stable=all(samples),**{'pass':converged and all(samples)})
        parallel('invincible',enabled=True)
    profile('baseline',0,0,0)

def rejoin():
    departing=ROLES[-1]
    command(departing,'stop')
    absent=wait_for(lambda:state('host').get('peers')==len(ROLES)-2 and all(len(state(r).get('remotes',[]))==len(ROLES)-2 for r in ROLES if r!=departing),15)
    command(departing,'join',transport='Direct',port=PORTS[departing])
    joined=wait_for(peers_ready,40)
    full=wait_for(lambda:all(matched(r,lambda c:buff_ids(c)==buff_ids(state(r)['local']) and abs(c['hp']-state(r)['local']['hp'])<.02) for r in ROLES),15)
    group_record('multiplayer_rejoin',role=departing,absent=absent,rejoined=joined,full_state=full,**{'pass':absent and joined and full})

def main():
    global ROLES
    profile('baseline',0,0,0)
    if '--resume' not in sys.argv:setup()
    for n in [3,4]:
        if n==4:
            ROLES=ALL[:4]
            command('guest4','join',transport='Direct',port=19053)
            if not wait_for(peers_ready,45):raise RuntimeError('Four-player replicas failed')
            group_record('multiplayer_join',**{'pass':True},states={r:state(r) for r in ROLES})
        maps.ROLES=ROLES
        hp_layers()
        rejoin()
        if n!=3 or '--skip-three-catalog' not in sys.argv:buffs()
    (ROOT/'multiplayer-ready').write_text('4')

if __name__=='__main__':main()
