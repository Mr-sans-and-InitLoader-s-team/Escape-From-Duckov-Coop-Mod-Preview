"""Native UI lifecycle, weak-network hits, actual trigger fire and kill feedback."""
import json,time,sys
from driver import ROOT,command,state,wait_for
from matrix import profile,record
from ai_matrix import ai_state
import maps
import multiplayer as group

group.ROLES=maps.ROLES=group.ALL

def lifecycle():
    result=command('client','hitMarkerLifecycle')
    valid=all(result[k] for k in ['registered','forgotten','restored','stable'])
    record({'category':'hit_marker_native_lifecycle','scene':state('client')['scene'],'pass':valid,'result':result})
    if not valid:raise RuntimeError('Native hit marker lifecycle failed')

def converged(aid):
    hp=ai_state('host',aid)['hp']
    return all((v:=ai_state(r,aid)) and abs(v['hp']-hp)<.02 for r in group.ALL)

def main():
    aid=json.loads((ROOT/'ai-target.json').read_text())['id']
    lifecycle()
    actor=group.remote('client','host')['id'];target=ai_state('host',aid)['id']
    if '--skip-weak' not in sys.argv:
        profile('rtt800_loss10',400,100,10)
        before=ai_state('host',aid)['hp']
        group.parallel('combatProfileBegin')
        hit=command('client','combatHitStream',ai=aid,seconds=8,rate=120)
        # Keep the ledger active until reliable requests and replies drain.
        drained=wait_for(lambda:ai_state('host',aid)['hp']<=before-hit['hits']*5+.02 and converged(aid),90)
        time.sleep(3)
        profiles=group.parallel('combatProfileEnd')
        ledger=next((e for e in profiles['host']['appliedDamage'] if e['actor']==actor and e['target']==target),{'hits':0,'damage':0})
        valid=drained and ledger['hits']==hit['hits'] and abs(ledger['damage']-hit['hits']*5)<.02 and profiles['client']['hitMarkers']==hit['hits'] and converged(aid)
        record({'category':'continuous_client_ai_hits_weak','profile':'rtt800_loss10','pass':valid,'drained':drained,'hit':hit,'ledger':ledger,'profiles':profiles})
        if not valid:raise RuntimeError('Weak-network hit ledger or native UI mismatch')
    profile('baseline',0,0,0)
    command('client','prepareGun')
    q=ai_state('host',aid)['position']
    for dx,dz in [(-4,0),(0,-4),(4,0),(0,4)]:
        command('client','safeMove',x=q[0]+dx,y=q[1],z=q[2]+dz);time.sleep(.3)
        probe=command('client','fire',ai=aid,probeOnly=True)
        if probe['hits'] and probe['hits'][0]['character']==probe['target']:break
    else:raise RuntimeError('Native gun fixture has no unobstructed firing line')
    # Keep one observation window across bursts/reloads: network requests can
    # arrive after a burst's local hit feedback has already finished.
    time.sleep(10)
    group.parallel('combatProfileBegin')
    bursts=[]
    for burst in range(3):
        if command('client','combatInfo')['bullets']==0:
            command('client','invoke',method='TryToReload',args=[None])
            if not wait_for(lambda:command('client','combatInfo')['bullets']>0,15):raise RuntimeError('Native reload failed')
        command('client','fire',ai=aid,probeOnly=True)
        before=command('client','combatInfo')['bullets']
        firing=command('client','combatGunStream',ai=aid,seconds=4)
        after=command('client','combatInfo')['bullets'];time.sleep(3)
        bursts.append({'burst':burst,'beforeAmmo':before,'afterAmmo':after,'firing':firing})
    time.sleep(15)
    synced=wait_for(lambda:converged(aid),30)
    profiles=group.parallel('combatProfileEnd')
    # Native spread can hit a neighbouring AI as well as the aimed target.
    rows=[e for e in profiles['host']['appliedDamage'] if e['actor']==actor]
    hits=sum(e['hits'] for e in rows);damage=sum(e['damage'] for e in rows)
    # Native OnHealthHitEvent excludes isFromBuffOrEffect and <=1.01 damage.
    # Bleed ticks retain the shooter as their source but should not add markers.
    native_hits=sum(m['calls'] for m in profiles['host']['methods'] if m['method']=='HitMarker.OnHit')
    isolated=rows==profiles['host']['appliedDamage']
    valid=all(b['firing']['consumed']>1 for b in bursts) and isolated and hits>=native_hits>0 and damage>0 and profiles['client']['hitMarkers']==native_hits and synced
    record({'category':'native_continuous_gun_vs_ai','pass':valid,'bursts':bursts,'hitsIncludingBuffTicks':hits,
        'nativeEligibleHitCallbacks':native_hits,'damage':damage,'profiles':profiles})
    if not valid:raise RuntimeError('Native trigger fire or hit feedback failed')
    command('host','heal',ai=aid,hp=10)
    if not wait_for(lambda:all(abs(ai_state(r,aid)['hp']-10)<.02 for r in group.ALL),15):raise RuntimeError('Kill fixture baseline absent')
    kills=state('client')['localKillEvents']
    group.parallel('combatProfileBegin')
    command('client','damage',ai=aid,amount=100,receiver=True)
    dead=wait_for(lambda:state('client')['localKillEvents']==kills+1,15)
    time.sleep(3);profiles=group.parallel('combatProfileEnd')
    valid=dead and state('client')['localKillEvents']==kills+1 and profiles['client']['killMarkers']==1
    record({'category':'native_ai_kill_feedback_once','pass':valid,'profiles':profiles})
    if not valid:raise RuntimeError('Native kill feedback or kill credit mismatch')
    result=maps.leave('guest4')
    record({'category':'hit_feedback_native_evacuation','pass':result['pass'],'result':result})
    if not result['pass']:raise RuntimeError('Native return after hit stress failed')
    lifecycle()
    (ROOT/'hit-feedback-acceptance-ready').write_text('4')

if __name__=='__main__':main()
