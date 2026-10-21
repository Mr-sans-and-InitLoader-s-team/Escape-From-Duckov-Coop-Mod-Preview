"""Reported doors, per-player inspection and real client corpse recovery."""
import json,time,sys
from driver import ROOT,command,state,wait_for
from matrix import profile,record
from loot_door_baseline import loot,door
import multiplayer as group,maps

group.ROLES=maps.ROLES=group.ALL
OUT=ROOT.parent/'loot-door-fixes'

def check(category,valid,**evidence):
    record({'category':category,'pass':bool(valid),**evidence})
    if not valid:raise RuntimeError(category)

def close():
    group.parallel('nativeInteraction',close=True)

def position(role,x,y,z):
    # Wake the native distance-culling region before checking its ground colliders.
    command(role,'move',x=x,y=y+1,z=z);time.sleep(.3)
    return command(role,'safeMove',x=x,y=y,z=z)

def enter(scene):
    close()
    if state('host').get('raid'):
        result=maps.leave('client');check('loot_door_return',result['pass'])
    result=maps.enter(scene,'guest4');check('loot_door_entry',result['loaded'],scene=scene)
    command('host','pauseAI',enabled=True)

def doors():
    for scene in ['Level_GroundZero_Main'] if '--zero-only' in sys.argv else ['Level_Farm_Main','Level_GroundZero_Main']:
        if scene=='Level_GroundZero_Main' and not state('host')['scene'].startswith('Level_GroundZero'):enter(scene)
        for name,delay,jitter,loss in [('baseline',0,0,0),('rtt800_loss10',400,100,10)]:
            profile(name,delay,jitter,loss)
            for role in group.ALL[1:] if name=='baseline' else ['guest4']:
                target=next(i for i in command(role,'interactionCatalog') if 'SingleDirectionDoor/Built/DoorInteract' in i['path'])
                q=target['position'];native=min(command(role,'worldCatalog')['doors'],key=lambda d:(d['position'][0]-q[0])**2+(d['position'][2]-q[2])**2)
                key=native['key']
                for near in [True,False]:
                    offset=110 if scene=='Level_GroundZero_Main' else -110
                    position('host',x=q[0]+(1 if near else offset),y=q[1],z=q[2])
                    position(role,x=q[0]+1,y=q[1],z=q[2]);command('host','pauseAI',enabled=True)
                    command('host','worldAction',kind='door',key=key,open=False)
                    wait_for(lambda:all(not door(r,key)['open'] for r in group.ALL),20);time.sleep(2)
                    command(role,'nativeInteraction',instance=target['instance'])
                    opened=wait_for(lambda:all(door(r,key)['open'] for r in group.ALL),25)
                    time.sleep(2);command(role,'nativeInteraction',instance=target['instance'])
                    closed=wait_for(lambda:all(not door(r,key)['open'] for r in group.ALL),25)
                    check('boarded_door_native_open_close',opened and closed,scene=scene,profile=name,role=role,hostNear=near,opened=opened,closed=closed)
        profile('baseline',0,0,0)

def new_enemy():
    before={b['uid'] for b in command('host','corpseInventory')}
    ai=next(a for a in command('host','aiSnapshot') if 'Scav' in a.get('preset','') and a.get('character') and not a['character']['dead'])
    command('host','damage',ai=ai['id'],amount=99999)
    q=ai['character']['position']
    def candidate():
        return next((b for b in command('host','lootInspection') if b['uid'] not in before and b['uid']>=0 and 'Enemy' in b['path'] and
            sum((b['position'][i]-q[i])**2 for i in [0,2])<4),None)
    if not wait_for(lambda:candidate() is not None,15):raise RuntimeError('Targeted native AI corpse absent')
    box=candidate();uid=box['uid']
    wait_for(lambda:all(loot(r,uid) is not None for r in group.ALL),15)
    return box

def known(box):return [i['Inspected'] for i in box['items']]
def search(role,box):
    q=box['position'];position(role,x=q[0]+1,y=q[1],z=q[2]);command('host','pauseAI',enabled=True)
    command(role,'nativeInteraction',uid=box['uid'])

def inspections():
    for host_search in [True,False]:
        box=new_enemy();uid=box['uid']
        if host_search:
            search('host',box)
            check('host_native_search_complete',wait_for(lambda:loot('host',uid)['items'] and all(known(loot('host',uid))),25),uid=uid)
            command('host','nativeInteraction',close=True)
        command('client','openCorpse',uid=uid)
        check('enemy_first_snapshot_hidden',wait_for(lambda:bool(loot('client',uid)['items']) and not any(known(loot('client',uid))),15),hostSearched=host_search,uid=uid)
        search('client',box)
        partial=wait_for(lambda:0<sum(known(loot('client',uid)))<len(known(loot('client',uid))),20)
        command('client','nativeInteraction',close=True)
        saved=known(loot('client',uid));command('client','openCorpse',uid=uid);time.sleep(2)
        actual=loot('client',uid)
        check('partial_search_survives_snapshot',partial and known(actual)==saved and actual['NeedInspection'],hostSearched=host_search,expected=saved,actual=actual)
        command('guest4','openCorpse',uid=uid)
        check('search_progress_is_per_player',wait_for(lambda:bool(loot('guest4',uid)['items']) and not any(known(loot('guest4',uid))),15),uid=uid)
        close()

def corpse_journey(owner,looter):
    close();before={b['uid'] for b in command('host','corpseInventory')}
    command(owner,'give',id=140,count=37);command(owner,'clearBuffs');command(owner,'invincible',enabled=False)
    time.sleep(17);command(owner,'damage',amount=99999)
    check('client_native_death',wait_for(lambda:state(owner)['local']['dead'],10),owner=owner)
    def body():return next((b for b in command('host','lootInspection') if b['uid'] not in before and b['uid']>=0 and 'Tomb' in b['path']),None)
    if not wait_for(lambda:body() is not None,20):raise RuntimeError('Player corpse absent')
    box=body();uid=box['uid'];wait_for(lambda:loot(looter,uid) is not None,15)
    search(looter,box)
    check('other_client_can_search_corpse',wait_for(lambda:bool(loot(looter,uid)['items']) and any(known(loot(looter,uid))),25),owner=owner,looter=looter)
    command(looter,'nativeInteraction',close=True)
    command(looter,'openCorpse',uid=uid);time.sleep(1)
    physical=next(b for b in command(looter,'corpseInventory') if b['uid']==uid)
    ammo=next(s for s in physical['slots'] if s['item']['type']==140 and s['item']['count']>7)
    count=ammo['item']['count'];command(looter,'splitCorpse',uid=uid,slot=ammo['slot'],count=7)
    check('other_client_partial_loot_authority',wait_for(lambda:any(i['type']==140 and i['count']==count-7 for i in loot('host',uid)['items']),15),owner=owner,looter=looter)
    expected=sorted((i['type'],i['count']) for i in loot('host',uid)['items'])
    close();result=maps.leave(looter);check('corpse_native_evacuation',result['pass'],owner=owner)
    result=maps.enter('Level_GroundZero_Main','client');check('corpse_next_raid_entry',result['loaded'],owner=owner)
    command('host','pauseAI',enabled=True)
    def restored():return next((b for b in command('host','lootInspection') if b['uid']>=0 and 'Tomb' in b['path'] and sorted((i['type'],i['count']) for i in b['items'])==expected),None)
    check('corpse_exact_remainder_restored',wait_for(lambda:restored() is not None,30),owner=owner)
    body=restored();uid=body['uid'];wait_for(lambda:loot(owner,uid) is not None,15)
    command(owner,'openCorpse',uid=uid)
    check('own_recovered_corpse_needs_no_search',wait_for(lambda:(b:=loot(owner,uid)) and bool(b['items']) and all(known(b)) and not b['NeedInspection'] and not b['boxNeedInspect'],15),owner=owner)
    check('recovery_does_not_kill_current_owner',all(not state(r)['local']['dead'] and len(state(r)['remotes'])==3 for r in group.ALL),owner=owner)

def main():
    profile('baseline',0,0,0)
    if '--loot-only' not in sys.argv:doors()
    inspections()
    corpse_journey('guest3','guest4')
    corpse_journey('guest4','client')
    close();result=maps.leave('client');check('loot_door_final_return',result['pass'])
    (OUT/'acceptance-ready').write_text('ready')

if __name__=='__main__':main()
