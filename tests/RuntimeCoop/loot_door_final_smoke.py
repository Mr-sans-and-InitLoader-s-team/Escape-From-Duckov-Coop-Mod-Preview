"""Final packaged build: native wood removal, owner metadata and native pickup."""
import time
from driver import ROOT,command,state,wait_for
from matrix import profile
from loot_door_acceptance import check,position,known,new_enemy,search,close,loot,door
import multiplayer as group,maps

group.ROLES=maps.ROLES=group.ALL

def main():
    profile('baseline',0,0,0);group.setup()
    entered=maps.enter('Level_GroundZero_Main','client');check('final_build_ground_zero_entry',entered['loaded'])
    command('host','pauseAI',enabled=True)
    for r in group.ALL:command(r,'constructionProbe',reset=True)
    site=command('guest3','constructionProbe')[0]
    command('guest3','nativeInteraction',instance=site['costTaker'])
    check('final_build_ground_zero_native_removal',wait_for(lambda:all(command(r,'constructionProbe')[0]['built'] for r in group.ALL),25))
    target=next(i for i in command('guest3','interactionCatalog') if 'SingleDirectionDoor/Built/DoorInteract' in i['path'])
    q=target['position'];native=min(command('guest3','worldCatalog')['doors'],key=lambda d:(d['position'][0]-q[0])**2+(d['position'][2]-q[2])**2)
    command('host','worldAction',kind='door',key=native['key'],open=False)
    wait_for(lambda:all(not door(r,native['key'])['open'] for r in group.ALL),20)
    position('guest3',x=q[0]+1,y=q[1],z=q[2]);command('host','pauseAI',enabled=True)
    command('guest3','nativeInteraction',instance=target['instance'])
    check('final_build_native_door_after_removal',wait_for(lambda:all(door(r,native['key'])['open'] for r in group.ALL),20))
    enemy=new_enemy();uid=enemy['uid'];search('host',enemy)
    check('final_build_native_host_search',wait_for(lambda:bool(loot('host',uid)['items']) and all(known(loot('host',uid))),25))
    command('host','nativeInteraction',close=True);command('client','openCorpse',uid=uid)
    check('final_build_enemy_snapshot_hidden',wait_for(lambda:bool(loot('client',uid)['items']) and not any(known(loot('client',uid))),15))
    before={b['uid'] for b in command('host','corpseInventory')};p=state('guest3')['local']['position']
    command('guest3','corpseFixture',items=[{'type':140,'count':37},{'type':1352,'count':1}])
    def body():
        return next((b for b in command('host','lootInspection') if b['uid']>=0 and b['uid'] not in before and 'Tomb' in b['path'] and
            (b['position'][0]-p[0]-3)**2+(b['position'][2]-p[2])**2<.25),None)
    check('final_build_reported_body_spawn',wait_for(lambda:body() is not None,20))
    box=body();uid=box['uid'];wait_for(lambda:loot('guest3',uid) is not None and loot('guest4',uid) is not None,15)
    command('guest3','openCorpse',uid=uid)
    check('final_build_owner_known_items',wait_for(lambda:bool(loot('guest3',uid)['items']) and all(known(loot('guest3',uid))) and not loot('guest3',uid)['NeedInspection'],15))
    check('final_build_owner_stays_alive',all(not state(r)['local']['dead'] and len(state(r)['remotes'])==3 for r in group.ALL))
    command('guest4','openCorpse',uid=uid)
    check('final_build_other_client_needs_search',wait_for(lambda:bool(loot('guest4',uid)['items']) and not any(known(loot('guest4',uid))),15))
    search('guest4',box)
    check('final_build_other_client_native_search',wait_for(lambda:all(known(loot('guest4',uid))),45))
    command('guest4','nativeInteraction',pickAll=True)
    check('final_build_other_client_native_pick_all',wait_for(lambda:not loot('host',uid)['items'],20))
    close();returned=maps.leave('client');check('final_build_native_return',returned['pass'])
    (ROOT.parent/'loot-door-fixes/final-smoke-ready').write_text('ready')

if __name__=='__main__':main()
