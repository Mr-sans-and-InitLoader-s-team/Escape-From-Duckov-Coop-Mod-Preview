"""Host-owned per-player equipment, buffer ownership and repeated join tests."""
import json,time
from collections import Counter
from driver import command,state,wait_for
import multiplayer as g
from shared_home_acceptance import snapshots,emit,OUT,personal_files
from matrix import profile

g.ROLES=g.ALL

def tally(items):
    c=Counter()
    for i in items:c[i['type']]+=i['count']
    return dict(c)

def audit():return {v['role']:tally(v['inventory']) for v in command('host','sharedHome',loadoutAudit=True)}

def joined():return g.peers_ready() and all(v['loadoutReady'] for r,v in snapshots().items() if r!='host')

def main():
    if not g.peers_ready():
        for r in g.ALL[1:]:command(r,'join',transport='Direct',port=g.PORTS[r])
    emit('loadout_all_players_ready',wait_for(joined,30))
    base=snapshots()
    emit('server_management_switch_location',all(v['ui'][0]['path']=='ModeCard' for v in base.values()))
    command('client','give',id=451,count=7)
    command('guest3','give',id=13,count=1)
    expected={r:tally(v['inventory']) for r,v in snapshots().items() if r!='host'}
    g.parallel('sharedHome',roles=g.ALL[1:],flushLoadout=True)
    emit('host_retains_separate_player_backpacks',wait_for(lambda:audit()==expected,15),expected=expected,audit=audit())
    fixture=json.loads((OUT/'personal-fixture.json').read_text(encoding='utf-8'))
    capacity=base['host']['characterCapacity']
    for attempt in range(4):
        for r in g.ALL[1:]:command(r,'stop')
        emit('repeat_'+str(attempt)+'_personal_capacity_restored',wait_for(lambda:all(command(r,'sharedHome')['characterCapacity']==fixture['states'][r]['characterCapacity'] and tally(command(r,'sharedHome')['inventory'])==tally(fixture['states'][r]['inventory']) for r in g.ALL[1:]),20))
        for r in g.ALL[1:]:command(r,'join',transport='Direct',port=g.PORTS[r])
        emit('repeat_'+str(attempt)+'_host_backpack_restored',wait_for(lambda:joined() and all(tally(v['inventory'])==expected[r] and v['characterCapacity']==capacity and v['inventoryCapacity']>=0 for r,v in snapshots().items() if r!='host'),30))
    profile('rtt800_loss10',400,100,10)
    before_buffer=len(snapshots()['host']['buffer'])
    command('client','give',id=451,count=5)
    command('client','sharedHome',bufferPush=451)
    emit('guest_buffer_deposit_reaches_host',wait_for(lambda:len(snapshots()['host']['buffer'])==before_buffer+1 and all(v['buffer']==snapshots()['host']['buffer'] for v in snapshots().values()),30),states=snapshots())
    count=len(snapshots()['host']['buffer'])
    g.parallel('sharedHome',roles=['client','guest3'],bufferTake=0)
    emit('concurrent_buffer_claim_no_shifted_second_item',wait_for(lambda:all(len(v['buffer'])==count-1 for v in snapshots().values()),30),before=count,states=snapshots())
    profile('baseline',0,0,0)
    perk=command('host','sharedHome',perkCandidate=True)
    emit('native_stat_perk_fixture_found',perk is not None,perk=perk)
    if perk:
        for enabled in [False,True,False,True]:
            command('host','sharedHome',perkAdjust=perk['perk'],tree=perk['tree'],unlocked=enabled)
            emit('perk_transition_'+str(enabled)+'_balanced',wait_for(lambda:all(v['characterCapacity']==snapshots()['host']['characterCapacity'] and v['characterCapacity']>0 for v in snapshots().values()),15),states={r:v['characterCapacity'] for r,v in snapshots().items()})
    command('host','sharedHome',save=True)
    g.parallel('sharedHome',roles=g.ALL[1:],flushLoadout=True)
    time.sleep(2)
    expected={r:tally(v['inventory']) for r,v in snapshots().items() if r!='host'}
    (OUT/'restart-expected.json').write_text(json.dumps({'inventory':expected,'unlocks':snapshots()['host']['unlocks'],'xp':snapshots()['host']['xp']}),encoding='utf-8')
    emit('guest_original_files_unchanged_after_repeated_joins',all(personal_files(r)==fixture['hashes'][r] for r in g.ALL[1:]))
    print('Loadout suite complete',flush=True)

if __name__=='__main__':main()
