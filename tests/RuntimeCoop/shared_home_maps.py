"""Shared mode on four native players: real AI credit, additive travel and evacuation."""
import time,json
from driver import command,state,wait_for
from shared_home_acceptance import snapshots,emit,OUT
import multiplayer as g
import maps
import additive_stress as additive
from matrix import profile

g.ROLES=maps.ROLES=additive.ROLES=g.ALL
profile('baseline',0,0,0)
entry=maps.enter('Level_Farm_Main','guest4') if not globals().get('resume') else {'loaded':True,'resumed':True}
emit('shared_mode_four_player_raid_entry',entry['loaded'],entry=entry)
command('host','pauseAI',enabled=True)
fixture=command('host','sharedHome',killFixture=True)
def count():
    result={}
    for role,value in snapshots().items():
        quest=next((q for q in value['quests'] if q['id']==fixture['quest']),None)
        result[role]=None if quest is None else next(t['data'] for t in quest['tasks'] if t['id']==fixture['task'])
    return result
emit('native_kill_task_replicated',wait_for(lambda:all(v==0 for v in count().values()),20),fixture=fixture)
used=set()
for friendly in [False,True]:
    command('host','friendlyFire',enabled=friendly)
    choices=[]
    def target_ready():
        global choices
        # AOI intentionally creates different AI sets for distant players. Only
        # the firing client needs the replica; quest/EXP must still reach all four.
        shared_ids=set.intersection(*(set(a['id'] for a in command(r,'aiSnapshot') if a.get('character') and not a['character']['dead'] and a['character']['hp']>0) for r in ['host','client']))
        host=command('host','aiSnapshot')
        choices=[a for a in host if a['id'] in shared_ids and a['id'] not in used and 'scav' in (a.get('preset') or '').lower() and 'pet' not in (a.get('preset') or '').lower()]
        return bool(choices)
    emit('client_visible_native_ai_ready_'+str(friendly),wait_for(target_ready,30))
    target=choices[0];used.add(target['id'])
    before=snapshots()['host']['xp'];kills=count()['host']
    command('client','damage',ai=target['id'],amount=1000000,receiver=True)
    emit('client_ai_kill_shared_exactly_once_friendly_'+str(friendly),wait_for(lambda:all(v==kills+1 for v in count().values()) and all(v['xp']==snapshots()['host']['xp'] and v['xp']>before for v in snapshots().values()),30),target=target,counts=count(),xp={r:v['xp'] for r,v in snapshots().items()},before=before)
command('host','friendlyFire',enabled=False)
indices={v['id']:v['index'] for v in command('host','sceneCatalog')}
profile('rtt800_loss10',400,100,10)
for trip in range(2):
    for leg,destination in [('enter','Level_Farm_JLab_Facility'),('return','Level_Farm_01')]:
        source=g.ALL[(trip+(leg=='return'))%4]
        portal=next(p for p in command(source,'teleporterCatalog') if p['target']==destination)
        outcome=additive.transfer(source,portal,indices[destination])
        emit('shared_additive_'+str(trip)+'_'+leg,outcome['pass'],outcome=outcome)
        emit('shared_state_survives_additive_'+str(trip)+'_'+leg,wait_for(lambda:all(v['xp']==snapshots()['host']['xp'] and v['characterCapacity']>0 for v in snapshots().values()),15))
profile('baseline',0,0,0)
outcome=maps.leave('client')
emit('shared_mode_native_evacuation_to_base',outcome['pass'],outcome=outcome)
expected=json.loads((OUT/'restart-expected.json').read_text())
from shared_home_loadouts import tally
expected['inventory']={r:tally(v['inventory']) for r,v in snapshots().items() if r!='host'}
expected['xp']=snapshots()['host']['xp'];expected['unlocks']=snapshots()['host']['unlocks']
(OUT/'restart-expected.json').write_text(json.dumps(expected),encoding='utf-8')
print('Map suite complete',flush=True)
