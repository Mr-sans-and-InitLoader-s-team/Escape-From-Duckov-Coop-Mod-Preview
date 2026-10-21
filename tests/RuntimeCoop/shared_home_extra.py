"""Additional native unlock, building, split and UI acceptance checks."""
import json,time
from driver import command,state,wait_for
import multiplayer as g
from shared_home_acceptance import snapshots,emit,OUT
from matrix import profile

g.ROLES=g.ALL
for r in g.ALL[1:]:command(r,'join',transport='Direct',port=g.PORTS[r])
emit('four_player_rejoin_shared_mode',wait_for(g.peers_ready,45))
emit('joining_needs_no_guest_toggle',wait_for(lambda:all(v['active'] and v['enabled'] for v in snapshots().values()),20))
s=snapshots()
emit('default_off_and_room_setting_locked',all(not v['defaultMode'] and v['ui'] and not v['ui'][0]['interactable'] for v in s.values()))
tip=command('host','sharedHome',tooltip=True)
emit('hover_explanation',bool(tip['text']) and ('自动' in tip['text'] or 'automatically' in tip['text']),text=tip['text'])
cat=command('host','sharedHome',unlockCatalog=True)
initial=s['host']['unlocks']
formula=next(f for f in cat['formulas'] if f not in initial['formulas'])
note=next(n for n in cat['notes'] if n not in initial['notes'])
key=next(k for k in cat['keys'] if k not in initial['keys'])
scene='Level_Farm_Main'
profile('rtt800_loss10',400,100,10)
command('client','sharedHome',visited=scene,beacon=scene)
command('guest3','sharedHome',formula=formula,note=note)
command('guest4','sharedHome',key=key,worldBool='SharedHomeTest')
def unlocks_ready():
    values=snapshots(probeScene=scene)
    return all(v['unlocks']['visited'] and v['unlocks']['beacon'] and formula in v['unlocks']['formulas'] and note in v['unlocks']['notes'] and key in v['unlocks']['keys'] and v['unlocks']['worldBool'] for v in values.values())
emit('weak_network_maps_beacons_recipes_notes_keys_world_flags',wait_for(unlocks_ready,35),states=snapshots(probeScene=scene))
profile('baseline',0,0,0)
for building in ['Showcase_FakeDuck_01','Showcase_FakeDuck_02']:
    before=snapshots()['host']['buildings']
    command('host','sharedHome',grantToken=building)
    place=command('host','sharedHome',placement=True,building=building)
    emit(building+'_legal_placement_found',place is not None,placement=place)
    command('client','sharedHome',build=building,**place)
    def found():return all(any(b['id']==building for a in v['buildings'] for b in a['buildings']) for v in snapshots().values())
    emit(building+'_guest_build_authoritative',wait_for(found,20),states=snapshots())
    data=snapshots()['host']['buildings']
    area=next(a for a in data if any(b['id']==building for b in a['buildings']))
    new=next(b for b in area['buildings'] if b['id']==building)
    print('return debug',command('guest3','sharedHome',**{'return':new['guid'],'area':area['id']}),flush=True)
    print('host after return',snapshots()['host']['buildingReturnBusy'],flush=True)
    emit(building+'_guest_return_authoritative',wait_for(lambda:all(v['buildings']==before for v in snapshots().values()),20))
before=snapshots()
slots={i['slot'] for i in before['host']['storage']['items']}
command('host','sharedHome',storageSeed=451,count=10)
emit('split_stack_seed_replicated',wait_for(lambda:all(v['storage']==snapshots()['host']['storage'] for v in snapshots().values()),15))
values=snapshots()
slot=next(i['slot'] for i in values['host']['storage']['items'] if i['slot'] not in slots)
def total(vs):return sum(i['count'] for v in vs.values() for i in v['inventory'] if i['type']==451)+sum(i['count'] for i in vs['host']['storage']['items'] if i['type']==451)
expected=total(values)
command('client','sharedHome',splitStorage=slot,count=3)
emit('shared_warehouse_split_conserves_items',wait_for(lambda:all(any(i['slot']==slot and i['count']==7 for i in v['storage']['items']) for v in snapshots().values()) and total(snapshots())==expected,15),states=snapshots(),expected=expected)
profile('baseline',0,0,0)
print('Supplement complete',flush=True)
