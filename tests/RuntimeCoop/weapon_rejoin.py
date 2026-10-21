"""Keep native held state across reconnect after gun/melee cache changes."""
import time
from driver import command,state,wait_for
from matrix import profile,record
import multiplayer as group

def main():
    group.ROLES=group.ALL
    group.parallel('invincible',enabled=True)
    group.parallel('prepareGun')
    profile('rtt800_loss10',400,100,10)
    for index in [-1,0]:
        group.parallel('invoke',method='SwitchToWeapon',args=[index])
        if not wait_for(lambda:all(group.matched(r,lambda c:c['heldItem']==state(r)['local']['heldItem']) for r in group.ALL),25):
            raise RuntimeError('Native gun/melee setup has not converged')
    for expected in [240,1]:
        if expected==1:group.parallel('invoke',method='SwitchToWeapon',args=[-1])
        if not wait_for(lambda:all(group.matched(r,lambda c:c['heldItem']==expected) for r in group.ALL),25):
            raise RuntimeError('Held weapon baseline absent')
        command('guest4','stop')
        if not wait_for(lambda:state('host').get('peers')==2,15):raise RuntimeError('Weapon rejoin peer did not leave')
        command('guest4','join',transport='Direct',port=19053)
        joined=wait_for(group.peers_ready,45)
        matched=joined and wait_for(lambda:all(group.matched(r,lambda c:c['heldItem']==expected) for r in group.ALL),30)
        time.sleep(3)
        stable=matched and all(group.matched(r,lambda c:c['heldItem']==expected) for r in group.ALL)
        record({'category':'four_player_weapon_rejoin','profile':'rtt800_loss10','expected':expected,'pass':stable,
            'states':{r:state(r) for r in group.ALL}})
        if not stable:raise RuntimeError('Held state did not survive reconnect')
    profile('baseline',0,0,0)

if __name__=='__main__':main()
