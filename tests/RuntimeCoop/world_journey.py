import json
import time
from driver import ROOT,command,state,wait_for
from matrix import profile,record
import maps
import multiplayer as group
import additive_stress as stress

ROLES=group.ALL
maps.ROLES=ROLES
group.ROLES=ROLES
stress.ROLES=ROLES

def catalog():return {r:command(r,'worldCatalog') for r in ROLES}
def door_state(key,opened):
    return all(any(d['key']==key and d['open']==opened for d in command(r,'worldCatalog')['doors']) for r in ROLES)
def health_state(ident):
    return {r:next((d for d in command(r,'worldCatalog')['destructibles'] if d['id']==ident),None) for r in ROLES}
def drops():return {r:command(r,'worldSnapshot')['drops'] for r in ROLES}

def departure_during_vote():
    command('guest4','loadScene',scene='Level_Farm_Main',timeout=180)
    if not wait_for(lambda:all(command(r,'sceneVote')['active'] for r in ROLES),15):raise RuntimeError('No four-player vote')
    for r in ROLES[:-1]:command(r,'ready')
    command('guest4','stop')
    departed=wait_for(lambda:state('host').get('peers')==2,15)
    cancelled=wait_for(lambda:all(not command(r,'sceneVote')['active'] for r in ROLES[:-1]),5)
    votes={r:command(r,'sceneVote') for r in ROLES[:-1]}
    record({'category':'four_player_departure_during_vote','departed':departed,'cancelled':cancelled,'votes':votes,'pass':departed and cancelled})
    if not cancelled:command('host','invoke',type='EscapeFromDuckovCoopMod.SceneNet',method='CancelVote')
    command('guest4','join',transport='Direct',port=group.PORTS['guest4'])
    if not wait_for(group.peers_ready,45):raise RuntimeError('Vote departure rejoin failed')
    if not cancelled:raise RuntimeError('Disconnected participant left the native scene vote pending')

def mutations():
    values=catalog();snapshots={r:command(r,'worldSnapshot') for r in ROLES}
    record({'category':'four_player_initial_world_registry','catalog':values,'registries':snapshots})
    door=next((d for d in values['host']['doors'] if d['active'] and d['unlocked'] and all(any(e['key']==d['key'] for e in values[r]['doors']) for r in ROLES)),None)
    opened=closed=False
    if door:
        command('guest4','worldAction',kind='door',key=door['key'],open=True)
        opened=wait_for(lambda:door_state(door['key'],True),15)
        command('host','worldAction',kind='door',key=door['key'],open=False)
        closed=wait_for(lambda:door_state(door['key'],False),15)
    record({'category':'four_player_native_door','door':door,'opened':opened,'closed':closed,'pass':opened and closed if door else None,'outcome':None if door else 'no_common_unlocked_door'})
    if door and not (opened and closed):raise RuntimeError('Native door state failed to converge')
    target=next((d for d in values['host']['destructibles'] if d['id'] and d['active'] and 10<d['hp']<999999 and
        all(any(e['id']==d['id'] for e in values[r]['destructibles']) for r in ROLES)),None)
    if target:
        ident=target['id']
        command('guest4','worldAction',kind='health',id=ident,amount=10)
        def damage_matched():
            current=health_state(ident);host=current['host']
            return bool(host and host['hp']<target['hp'] and all(d and abs(d['hp']-host['hp'])<.02 for d in current.values()))
        damaged=wait_for(damage_matched,15)
        after=health_state(ident)
        before_concurrent=after['host']['hp']
        group.parallel('worldAction',kind='health',id=ident,amount=2)
        expected=before_concurrent-8
        concurrent=wait_for(lambda:all(d and abs(d['hp']-expected)<.02 for d in health_state(ident).values()),15)
        record({'category':'four_player_concurrent_destructible_hits','id':ident,'expected':expected,'actual':health_state(ident),'pass':concurrent})
        command('host','worldAction',kind='health',id=ident,amount=999999,explosion=True)
        destroyed=wait_for(lambda:all(not d or not d['active'] or d['hp']<=0 for d in health_state(ident).values()),15)
        record({'category':'four_player_native_destructible','target':target,'damaged':damaged,'destroyed':destroyed,'after':after,'pass':damaged and destroyed})
        if not (damaged and destroyed and concurrent):raise RuntimeError('Native destructible state failed to converge')
    else:record({'category':'four_player_native_destructible','pass':None,'outcome':'no_common_destructible'})
    before={d['id'] for d in drops()['host']}
    for index,r in enumerate(ROLES):command(r,'dropItem',id=140,count=11+index)
    new=[]
    def spawned():
        nonlocal new
        all_drops=drops();new=[d for d in all_drops['host'] if d['id'] not in before]
        return len(new)==4 and all(all(any(d['id']==n['id'] and d['count']==n['count'] for d in all_drops[r]) for n in new) for r in ROLES)
    spawned_all=wait_for(spawned,20)
    record({'category':'four_player_native_item_drops','pass':spawned_all,'drops':drops()})
    if not spawned_all:raise RuntimeError('Native item drops failed to converge')
    if spawned_all:
        first=new[0]['id'];command('guest3','pickupDrop',id=first)
        picked=wait_for(lambda:all(not any(d['id']==first for d in ds) for ds in drops().values()),15)
        record({'category':'four_player_native_item_pickup','id':first,'pass':picked,'drops':drops()})
        if not picked:raise RuntimeError('Native item pickup failed to converge')
    return door,target

def main():
    profile('baseline',0,0,0)
    departure_during_vote()
    entered=maps.enter('Level_Farm_Main','guest4')
    if not entered['loaded']:raise RuntimeError('Farm world entry failed')
    command('host','pauseAI',enabled=True)
    mutations()
    # The following suite performs real portal votes, alternating participants, under all four profiles.
    original_enter=stress.enter
    stress.enter=lambda scene,initiator:{'loaded':True}
    try:passed=stress.main()
    finally:stress.enter=original_enter
    if not passed:raise RuntimeError('Native additive scene stress failed')
    (ROOT/'four-player-world-ready').write_text('4')

if __name__=='__main__':main()
