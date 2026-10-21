"""Actual player deaths, native party extraction and automatic corpse recovery next raid."""
import json
import time
from driver import ROOT,command,state,wait_for
from matrix import profile,record
import maps
import multiplayer as group

ROLES=group.ALL
maps.ROLES=ROLES
group.ROLES=ROLES

def boxes(role):return command(role,'corpseInventory')
def fingerprint(box):
    return sorted((s['slot'],s['item']['type'],s['item']['count'],round(s['item']['durability'],4),
        tuple((c['type'],c['count']) for c in s['item']['children'])) for s in box['slots'])

def remainder(role,ident):
    return next((b for b in command(role,'savedCorpses') or [] if b['id']==ident),None)

def spawn_identical(expected):
    return [b for b in boxes('host') if fingerprint(b)==expected]

def enter():
    result=maps.enter('Level_GroundZero_Main','guest4')
    if not result['loaded']:raise RuntimeError('Corpse raid entry failed')
    command('host','pauseAI',enabled=True)
    group.parallel('clearBuffs');group.parallel('invincible',enabled=True)
    return result

def guest4_gun_retest():
    from multiplayer_combat import setup,hp_ready
    selected=setup();aid=selected['id'];q=selected['character']['position']
    command('host','invoke',ai=aid,method='SwitchToFirstAvailableWeapon')
    command('host','invoke',ai=aid,method='TryToReload',args=[None])
    loaded=wait_for(lambda:(command('host','combatInfo',ai=aid).get('bullets') or 0)>0,15)
    attempts=[]
    for x,z in [(2,4),(4,2),(-2,4),(-4,2),(2,-4),(-2,-4)]:
        command('guest4','move',x=q[0]+x,y=q[1],z=q[2]+z)
        time.sleep(.5)
        probe=command('host','aiFire',ai=aid,remote=True,player=state('guest4')['playerId'],probeOnly=True)
        attempts.append(probe)
        if probe['hits'] and probe['hits'][0].get('character')==probe['target']:break
    # Both native player and host-side replica protection last fifteen seconds after spawning.
    time.sleep(17)
    command('guest4','clearBuffs');command('guest4','invincible',enabled=False);command('guest4','heal',hp=100)
    wait_for(lambda:hp_ready('guest4'),15)
    fired=command('host','aiFire',ai=aid,remote=True,player=state('guest4')['playerId']) if loaded else None
    incoming=loaded and wait_for(lambda:state('guest4')['local']['hp']<99.9,5)
    hp=state('guest4')['local']['hp'];synced=wait_for(lambda:hp_ready('guest4',hp),15)
    record({'category':'guest4_unobstructed_ai_gun_vs_player','pass':incoming and synced,'fired':fired,'hp':hp,'line_checks':attempts})
    command('guest4','invincible',enabled=True);command('guest4','clearBuffs')

def journey(owner,looter,verify_gun=True):
    enter()
    if owner=='guest3' and verify_gun:guest4_gun_retest()
    anchor=state('host')['local']['position']
    for index,r in enumerate(ROLES):command(r,'move',x=anchor[0]+index*2,y=anchor[1],z=anchor[2])
    command(owner,'give',id=140,count=37)
    command(owner,'give',id=1352,durability=.42)
    command(owner,'prepareGun')
    time.sleep(17)
    before_records={b['id'] for b in command(owner,'nativeCorpses')}
    before_uids={b['uid'] for b in boxes('host')}
    position=state(owner)['local']['position']
    command(owner,'clearBuffs');command(owner,'invincible',enabled=False)
    command(owner,'damage',amount=99999)
    dead=wait_for(lambda:state(owner).get('local',{}).get('dead'),10)
    spectating=wait_for(lambda:command(owner,'spectator')['active'],10)
    if not dead or not spectating:raise RuntimeError('Native death/spectator not reached')
    if not wait_for(lambda:any(b['id'] not in before_records for b in command(owner,'nativeCorpses')),10):raise RuntimeError('Native death record missing')
    info=next(b for b in command(owner,'nativeCorpses') if b['id'] not in before_records)
    if not wait_for(lambda:any(b['uid'] not in before_uids for b in boxes('host')),15):raise RuntimeError('Authoritative death container absent')
    new=[b for b in boxes('host') if b['uid'] not in before_uids]
    if len(new)!=1:raise RuntimeError('Duplicate/missing current death bodies: '+str(len(new)))
    initial=new[0];uid=initial['uid']
    if not wait_for(lambda:any(b['uid']==uid for b in boxes(looter)),15):raise RuntimeError('Survivor has no corpse replica')
    command(looter,'openCorpse',uid=uid)
    if not wait_for(lambda:any(b['uid']==uid and fingerprint(b)==fingerprint(initial) for b in boxes(looter)),15):raise RuntimeError('Corpse open snapshot mismatch')
    ammo=next((s for s in initial['slots'] if s['item']['type']==140 and s['item']['count']>7),None)
    if not ammo:raise RuntimeError('Native corpse has no test ammunition')
    command(looter,'splitCorpse',uid=uid,slot=ammo['slot'],count=7)
    if not wait_for(lambda:any(b['uid']==uid and any(s['slot']==ammo['slot'] and s['item']['count']==ammo['item']['count']-7 for s in b['slots']) for b in boxes('host')),15):raise RuntimeError('Authoritative partial loot did not apply')
    expected=next(b for b in boxes('host') if b['uid']==uid)
    saved=wait_for(lambda:remainder(owner,info['id']) and any(e['slot']==ammo['slot'] and e['items'][0]['count']==ammo['item']['count']-7 for e in remainder(owner,info['id'])['entries']),15)
    camera=[]
    for _ in range(6):
        command(owner,'invoke',type='EscapeFromDuckovCoopMod.Spectator',method='SpectateNext')
        time.sleep(.15)
        camera.append(command(owner,'spectator'))
    stable_camera=all(c['active'] and c['camera'] and not c['camera']['dead'] and c['controlling']['id']==c['main']['id'] for c in camera)
    stable_camera=stable_camera and len({c['camera']['id'] for c in camera})==3
    record({'category':'real_death_partial_loot','players':4,'owner':owner,'looter':looter,'record':info,'initial':initial,'remaining':expected,
        'saved':saved,'spectator':camera,'pass':saved and stable_camera})
    returned=maps.leave(looter,15)
    record({'category':'death_party_settlement_return','owner':owner,'pass':returned['pass'],'result':returned})
    if not returned['pass']:raise RuntimeError('Death return failed')
    enter()
    for index,r in enumerate(ROLES):command(r,'move',x=position[0]+index*1.5,y=position[1],z=position[2]+2)
    restored=wait_for(lambda:len(spawn_identical(fingerprint(expected)))==1,30)
    actual=spawn_identical(fingerprint(expected))
    record({'category':'real_corpse_next_raid_remainder','owner':owner,'record':info['id'],'expected':expected,'actual':actual,
        'native':command(owner,'nativeCorpses'),'pass':restored})
    if not restored:raise RuntimeError('Next raid remainder missing or duplicated')
    box=actual[0];uid=box['uid']
    command('host','openCorpse',uid=uid)
    for slot in box['slots']:command('host','takeCorpse',uid=uid,slot=slot['slot'])
    empty_saved=wait_for(lambda:remainder(owner,info['id']) is not None and not remainder(owner,info['id'])['entries'],15)
    record({'category':'real_corpse_fully_looted','owner':owner,'pass':empty_saved,'saved':remainder(owner,info['id'])})
    returned=maps.leave('guest4')
    if not returned['pass']:raise RuntimeError('Empty corpse extraction failed')
    enter()
    time.sleep(4)
    no_duplicate=not spawn_identical(fingerprint(initial)) and not spawn_identical(fingerprint(expected))
    record({'category':'real_empty_corpse_next_raid','owner':owner,'pass':empty_saved and no_duplicate,
        'native':command(owner,'nativeCorpses'),'bodies':boxes('host')})
    returned=maps.leave('client')
    if not returned['pass']:raise RuntimeError('Final corpse test extraction failed')

def main():
    profile('baseline',0,0,0)
    journey('guest3','host')
    journey('host','client')
    (ROOT/'corpse-journey-ready').write_text('4')

if __name__=='__main__':main()
