"""Four native instances: host save authority, common stock and personal restoration."""
import hashlib
import json
import time
from pathlib import Path
from driver import command,state,wait_for,ROOT
import multiplayer as group
from matrix import profile

OUT=ROOT.parent/'shared-home'
group.ROLES=group.ALL
group.maps.ROLES=group.ALL

def snapshots(**kwargs):return group.parallel('sharedHome',**kwargs)

def emit(name,passed,**details):
    row={'case':name,'pass':bool(passed),**details}
    with (OUT/'acceptance.jsonl').open('a',encoding='utf-8') as f:f.write(json.dumps(row)+'\n')
    print(name,bool(passed),flush=True)
    if not passed:raise AssertionError(name)

def personal_files(role):
    folder=ROOT/role/'profile'/'Saves'
    return {str(p.relative_to(folder)):hashlib.sha256(p.read_bytes()).hexdigest() for p in folder.glob('*') if p.is_file()}

def boot():
    profile('baseline',0,0,0)
    group.parallel('loadMod',group.ALL)
    group.parallel('loadBase',group.ALL,timeout=180)
    if not wait_for(lambda:all(state(r).get('initialized') for r in group.ALL),180):raise RuntimeError('Loading failed')
    command('host','sharedHome',resetQuest=611,removeNegativeQuests=True,visitedFalse='Level_Farm_Main',removeFixtureBuilding='Showcase_FakeDuck_01')
    command('host','sharedHome',removeFixtureBuilding='Showcase_FakeDuck_02')
    command('guest3','sharedHome',personalLayout=True,visited='Level_Farm_Main')
    # Private copies only: save different personal EXP values before joining.
    for i,r in enumerate(group.ALL[1:]):command(r,'sharedHome',xp=111+i,save=True)
    before=snapshots(probeScene='Level_Farm_Main')
    hashes={r:personal_files(r) for r in group.ALL[1:]}
    (OUT/'personal-fixture.json').write_text(json.dumps({'states':before,'hashes':hashes}),encoding='utf-8')
    command('host','sharedHome',enabled=True)
    command('host','host',transport='Direct',port=19050)
    for r in group.ALL[1:]:command(r,'join',transport='Direct',port=group.PORTS[r])
    emit('four_players_join',wait_for(group.peers_ready,45))
    matched=wait_for(lambda:all(v['xp']==before['host']['xp'] and v['buildings']==before['host']['buildings'] for v in snapshots().values()),20)
    emit('host_progress_and_buildings_replace_guest_runtime',matched,before=before,after=snapshots())
    emit('guest_extra_map_unlock_is_cleared',wait_for(lambda:all(not v['unlocks']['visited'] for v in snapshots(probeScene='Level_Farm_Main').values()),15))
    emit('guest_loadouts_ready',wait_for(lambda:all(v['loadoutReady'] for r,v in snapshots().items() if r!='host'),30))
    emit('initial_capacity_matches_host',all(v['characterCapacity']==snapshots()['host']['characterCapacity'] and v['inventoryCapacity']>=0 for v in snapshots().values()))
    emit('guest_overlay_no_disk_writes',all(personal_files(r)==hashes[r] for r in hashes))

def main():
    boot()
    for name,d,j,l in [('baseline',0,0,0),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        before=snapshots()['host']['xp']
        snapshots(xp=13)
        converged=wait_for(lambda:all(v['xp']==before+52 for v in snapshots().values()),20)
        emit(name+'_simultaneous_exp_exactly_once',converged,before=before,after={r:v['xp'] for r,v in snapshots().items()})
    profile('baseline',0,0,0)
    command('client','sharedHome',quest=611,task=8,delta=1)
    done=wait_for(lambda:all(any(q['id']==611 and q['tasks'][0]['finished'] for q in v['quests']) for v in snapshots().values()),15)
    emit('client_task_progress_authoritative',done)
    command('guest3','sharedHome',complete=611)
    emit('client_completion_authoritative',wait_for(lambda:all(611 in v['history'] for v in snapshots().values()),15))
    snapshots(claim=True,quest=611)
    time.sleep(3)
    first=snapshots(quest=611)
    snapshots(claim=True,quest=611)
    time.sleep(2)
    second=snapshots(quest=611)
    emit('quest_reward_not_duplicated_by_four_claimers',all(v['xp']==first['host']['xp'] and v['money']==first['host']['money'] and
         all(r['claimed'] for r in v['rewards']) for v in second.values()),first=first,second=second)
    before_seed=snapshots()
    before_slots={i['slot'] for i in before_seed['host']['storage']['items']}
    command('host','sharedHome',storageSeed=13,count=1)
    emit('warehouse_replicas',wait_for(lambda:all(v['storage']==snapshots()['host']['storage'] for v in snapshots().values()),15))
    current=snapshots()
    newslot=next(i['slot'] for i in current['host']['storage']['items'] if i['slot'] not in before_slots)
    def carried(values):return sum(i['count'] for v in values.values() for i in v['inventory'] if i['type']==13)
    carried_before=carried(current)
    group.parallel('sharedHome',openStorage=True)
    group.parallel('sharedHome',roles=['client','guest3'],takeStorage=newslot)
    time.sleep(3)
    after_take=snapshots()
    emit('concurrent_shared_warehouse_take',all(not any(i['slot']==newslot for i in v['storage']['items']) for v in after_take.values()) and
         carried(after_take)==carried_before+1,states=after_take,carried_before=carried_before,carried_after=carried(after_take))
    fixture=json.loads((OUT/'personal-fixture.json').read_text(encoding='utf-8'))
    snapshots(save=True)
    emit('guest_saves_still_unchanged',all(personal_files(r)==fixture['hashes'][r] for r in group.ALL[1:]))
    for r in group.ALL[1:]:command(r,'stop')
    restored=wait_for(lambda:all(command(r,'sharedHome')['xp']==fixture['states'][r]['xp'] and
        command(r,'sharedHome')['buildings']==fixture['states'][r]['buildings'] for r in group.ALL[1:]),30)
    emit('disconnect_restores_personal_progress_and_layout',restored,states={r:command(r,'sharedHome') for r in group.ALL[1:]})
    emit('disconnect_restores_character_capacity',all(command(r,'sharedHome')['characterCapacity']==fixture['states'][r]['characterCapacity'] for r in group.ALL[1:]))
    emit('disconnect_restores_personal_warehouse',all(command(r,'sharedHome')['storage']==fixture['states'][r]['storage'] for r in group.ALL[1:]))
    emit('disconnect_preserves_personal_save_files',all(personal_files(r)==fixture['hashes'][r] for r in group.ALL[1:]))

if __name__=='__main__':main()
