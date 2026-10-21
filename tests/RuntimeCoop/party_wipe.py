"""Simultaneous four-player death, delayed settlement and one stable base return."""
import time
from driver import command,state,wait_for
from matrix import profile,record
import maps
import multiplayer as group

ROLES=group.ALL
maps.ROLES=group.ROLES=ROLES

def main():
    profile('baseline',0,0,0)
    entry=maps.enter('Level_GroundZero_Main','guest3')
    if not entry['loaded']:raise RuntimeError('Party wipe entry failed')
    command('host','pauseAI',enabled=True)
    before={r:{b['id'] for b in command(r,'nativeCorpses')} for r in ROLES}
    time.sleep(17)
    group.parallel('clearBuffs');group.parallel('invincible',enabled=False)
    group.parallel('damage',amount=99999)
    records=wait_for(lambda:all(any(b['id'] not in before[r] for b in command(r,'nativeCorpses')) for r in ROLES),20)
    closure=wait_for(lambda:all((state(r).get('closure') or {}).get('canContinue') for r in ROLES),45)
    if closure:
        time.sleep(15);group.parallel('continue')
    returned=wait_for(lambda:all(state(r).get('initialized') and not state(r).get('raid') and
        not state(r).get('loading') and not state(r).get('blackScreen') for r in ROLES),180)
    samples=[]
    replicas_ready=returned and wait_for(group.peers_ready,30)
    if replicas_ready:
        for _ in range(12):samples.append({r:state(r) for r in ROLES});time.sleep(.5)
    stable=bool(samples) and all(not s[r].get('raid') and not s[r].get('loading') and not s[r].get('blackScreen') and
        len(s[r].get('remotes',[]))==3 and s[r].get('peers')==(3 if r=='host' else 1) for s in samples for r in ROLES)
    voted=all(not command(r,'sceneVote')['active'] for r in ROLES) if returned else False
    record({'category':'four_player_party_wipe','pass':records and closure and returned and stable and voted,
        'native_death_records':records,'closure':closure,'returned':returned,'replicas_ready':replicas_ready,
        'stable':stable,'votes_cleared':voted,'stability_samples':samples,
        'states':{r:state(r) for r in ROLES}})
    if not(records and closure and returned and stable and voted):raise RuntimeError('Party wipe did not complete a single stable base return')
    group.parallel('invincible',enabled=True)

if __name__=='__main__':main()
