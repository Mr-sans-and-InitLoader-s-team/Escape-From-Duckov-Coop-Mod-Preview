import json
import time
from driver import ROOT,command,state,wait_for
from matrix import profile,record
import maps
import multiplayer as group

ROLES=group.ALL
maps.ROLES=ROLES
group.ROLES=ROLES

def pvp():
    anchor=state('host')['local']['position']
    for r in ROLES:command(r,'prepareGun')
    time.sleep(17)
    for name,d,j,l in [('baseline',0,0,0),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        for index,shooter in enumerate(ROLES):
            victim=ROLES[(index+1)%4]
            for r in ROLES:
                offset=0 if r==shooter else 3 if r==victim else 15+ROLES.index(r)*3
                command(r,'move',x=anchor[0]+offset,y=anchor[1],z=anchor[2]+(offset if offset<4 else -8))
            line_checks=[]
            for dx,dz in [(3,3),(2,0),(0,2),(-2,0),(0,-2)]:
                command(victim,'move',x=anchor[0]+dx,y=anchor[1],z=anchor[2]+dz)
                time.sleep(1.5 if d else .25)
                probe=command(shooter,'fire',player=state(victim)['playerId'],probeOnly=True)
                line_checks.append(probe)
                if probe['hits'] and probe['hits'][0].get('character')==probe['target']:break
            else:
                record({'category':'pvp_fixture_obstruction','shooter':shooter,'victim':victim,'profile':name,'line_checks':line_checks})
                raise RuntimeError('No unobstructed player firing lane in the native fixture')
            for enabled in [False,True]:
                command('host','friendlyFire',enabled=enabled)
                wait_for(lambda:all(command(r,'friendlyFire')==enabled for r in ROLES),15)
                group.parallel('clearBuffs');group.parallel('invincible',enabled=False);group.parallel('heal',hp=100)
                if not wait_for(lambda:all(group.matched(r,lambda c:abs(c['hp']-100)<.02) for r in ROLES),20):raise RuntimeError('PvP heal not converged')
                fired=command(shooter,'fire',player=state(victim)['playerId'])
                changed=wait_for(lambda:state(victim)['local']['hp']<99.9,5 if enabled else 2)
                command(victim,'clearBuffs')
                expected=state(victim)['local']['hp']
                synced=wait_for(lambda:group.matched(victim,lambda c:abs(c['hp']-expected)<.02),15)
                record({'category':'four_player_friendly_fire','profile':name,'enabled':enabled,'shooter':shooter,'victim':victim,
                    'hp':expected,'fired':fired,'pass':synced and (changed if enabled else not changed and abs(expected-100)<.02)})
    command('host','friendlyFire',enabled=False)
    group.parallel('invincible',enabled=True)

def credit():
    profile('baseline',0,0,0)
    for owner in ROLES[1:]:
        target=next(a for a in command('host','aiSnapshot') if a.get('character') and a['preset']=='Cname_Scav' and not a['character']['dead'])
        aid=target['id'];q=target['character']['position']
        command('host','invincible',ai=aid,enabled=True)
        for index,r in enumerate(ROLES):command(r,'move',x=q[0]+index*2,y=q[1],z=q[2]+4)
        if not wait_for(lambda:any(a['id']==aid and a.get('character') for a in command(owner,'aiSnapshot')),15):raise RuntimeError('Killer has no target replica')
        before={r:state(r) for r in ROLES}
        command('host','invincible',ai=aid,enabled=False);command(owner,'damage',ai=aid,amount=99999)
        credited=wait_for(lambda:state(owner).get('localKillEvents',0)==before[owner].get('localKillEvents',0)+1,15)
        time.sleep(1)
        after={r:state(r) for r in ROLES}
        xp={r:after[r]['experience']-before[r]['experience'] for r in ROLES}
        try:command(owner,'damage',ai=aid,amount=99999)
        except RuntimeError:pass
        time.sleep(1)
        once=state(owner)['experience']==after[owner]['experience'] and state(owner)['localKillEvents']==after[owner]['localKillEvents']
        record({'category':'four_player_client_kill_credit','owner':owner,'xp':xp,'once':once,
            'pass':credited and once and xp[owner]>0 and all(xp[r]==0 for r in ROLES if r!=owner)})

def fake_blast():
    group.parallel('clearBuffs');group.parallel('invincible',enabled=False);group.parallel('heal',hp=100)
    wait_for(lambda:all(group.matched(r,lambda c:abs(c['hp']-100)<.02) for r in ROLES),20)
    values=group.parallel('fakeExplosion')
    time.sleep(1)
    passed=all(v['before']==v['after']==100 and v['contextDamage']==v['contextExplosion']==v['contextBleed']==0 for v in values.values())
    record({'category':'four_player_visual_explosion_zero_damage','pass':passed,'result':values})
    group.parallel('invincible',enabled=True)

def main():
    if (ROOT/'require-staged-restart').exists():
        record({'category':'four_player_staged_restart_gate','scope':'Map coverage retained; waiting for the targeted persistence check on the newly staged build'})
        if not wait_for(lambda:(ROOT/'staged-restart-ready').exists(),1800):raise RuntimeError('Staged build restart not completed')
    profile('baseline',0,0,0)
    entered=maps.enter('Level_GroundZero_Main','client')
    if not entered['loaded']:raise RuntimeError('Extra combat entry failed')
    command('host','pauseAI',enabled=True);group.parallel('invincible',enabled=True)
    pvp();credit();fake_blast()
    profile('baseline',0,0,0)
    exited=maps.leave('guest4')
    record({'category':'four_player_extra_combat_exit','pass':exited['pass'],'result':exited})
    if exited['pass']:(ROOT/'four-player-extra-combat-ready').write_text('4')

if __name__=='__main__':main()
