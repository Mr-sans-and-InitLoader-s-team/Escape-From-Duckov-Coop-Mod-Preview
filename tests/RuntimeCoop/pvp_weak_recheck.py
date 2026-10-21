"""Recheck the single weak-network guest3-to-guest4 native projectile miss."""
import time
from driver import command,state,wait_for
from matrix import profile,record
import maps
import multiplayer as group

ROLES=group.ALL
maps.ROLES=group.ROLES=ROLES

def fire_native(shooter,victim):
    probe=command(shooter,'fire',player=state(victim)['playerId'],probeOnly=True)
    before=command(shooter,'combatInfo')['bullets']
    command(shooter,'invoke',method='Trigger',args=[True,True,False])
    try:fired=wait_for(lambda:command(shooter,'combatInfo')['bullets']<before,2)
    finally:command(shooter,'invoke',method='Trigger',args=[False,False,True])
    return {'fired':fired,'before':before,'after':command(shooter,'combatInfo')['bullets'],'probe':probe,
        'after_rotation':command(shooter,'fire',player=state(victim)['playerId'],probeOnly=True)}

def main():
    profile('baseline',0,0,0)
    if not maps.enter('Level_GroundZero_Main','guest4')['loaded']:raise RuntimeError('PvP recheck entry failed')
    command('host','pauseAI',enabled=True)
    group.parallel('invincible',enabled=True);group.parallel('clearBuffs');group.parallel('prepareGun')
    time.sleep(17)
    shooter='guest3';victim='guest4';anchor=state(shooter)['local']['position']
    for index,r in enumerate(ROLES):command(r,'safeMove',x=anchor[0]+(0 if r==shooter else 4 if r==victim else 8+index),y=anchor[1],z=anchor[2])
    profile('rtt800_loss10',400,100,10)
    lanes=[]
    for dx,dz in [(4,0),(0,4),(-4,0),(0,-4),(4,4)]:
        command(victim,'safeMove',x=anchor[0]+dx,y=anchor[1],z=anchor[2]+dz);time.sleep(2)
        probe=command(shooter,'fire',player=state(victim)['playerId'],probeOnly=True);lanes.append(probe)
        if probe['hits'] and probe['hits'][0].get('character')==probe['target']:break
    else:raise RuntimeError('PvP recheck fixture has no unobstructed ray')
    for enabled in [False,True]:
        command('host','friendlyFire',enabled=enabled)
        if not wait_for(lambda:all(command(r,'friendlyFire')==enabled for r in ROLES),30):raise RuntimeError('Friendly-fire flag has not converged')
        group.parallel('invincible',enabled=False);group.parallel('heal',hp=100)
        if not wait_for(lambda:group.matched(victim,lambda c:abs(c['hp']-100)<.02),20):raise RuntimeError('PvP heal absent')
        hits=[]
        for shot in range(3):
            command(victim,'clearBuffs');command(victim,'heal',hp=100)
            if not wait_for(lambda:group.matched(victim,lambda c:abs(c['hp']-100)<.02),20):raise RuntimeError('Shot baseline absent')
            before=state(victim)['local']['hp'];fired=fire_native(shooter,victim)
            changed=wait_for(lambda:state(victim)['local']['hp']<before-.02,8 if enabled else 2)
            hits.append({'shot':shot,'before':before,'after':state(victim)['local']['hp'],'changed':changed,'fired':fired})
        hp=state(victim)['local']['hp']
        synced=wait_for(lambda:group.matched(victim,lambda c:abs(c['hp']-hp)<.02),20)
        expected=synced and all(h['fired']['fired'] for h in hits) and (all(h['changed'] for h in hits) if enabled else all(not h['changed'] for h in hits))
        record({'category':'four_player_weak_pvp_projectile_recheck','enabled':enabled,'profile':'rtt800_loss10',
            'pass':expected,'hits':hits,'lanes':lanes,'hp':hp,'synced':synced})
        if not expected:raise RuntimeError('Weak-network projectile check failed with confirmed state')
        if not enabled:continue
        command(victim,'clearBuffs');command(victim,'heal',hp=100)
        if not wait_for(lambda:group.matched(victim,lambda c:abs(c['hp']-100)<.02),20):raise RuntimeError('PvP deterministic-hit baseline absent')
        direct=command(shooter,'damage',player=state(victim)['playerId'],amount=10,receiver=True)
        verified=wait_for(lambda:group.matched(victim,lambda c:abs(c['hp']-90)<.02),20)
        record({'category':'four_player_weak_pvp_health_forward','enabled':enabled,'profile':'rtt800_loss10','pass':verified,'native':direct,
            'states':{r:state(r) for r in ROLES}})
        if not verified:raise RuntimeError('Weak-network native health forwarding failed')
    command('host','friendlyFire',enabled=False);group.parallel('invincible',enabled=True)
    profile('baseline',0,0,0)
    result=maps.leave('client');record({'category':'four_player_pvp_recheck_exit','pass':result['pass'],'result':result})
    if not result['pass']:raise RuntimeError('PvP recheck exit failed')

if __name__=='__main__':main()
