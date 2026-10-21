import time
from driver import command,state,wait_for
from matrix import profile,record
import multiplayer as group

ROLES=group.ALL
group.ROLES=ROLES

def main():
    group.parallel('invincible',enabled=True);group.parallel('prepareGun')
    for name,d,j,l in [('baseline',0,0,0),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        for _ in range(8):
            group.parallel('invoke',method='SwitchWeapon',args=[1]);time.sleep(.1)
        initial={r:state(r)['local']['heldItem'] for r in ROLES}
        latest=None;stable_since=None
        def settled():
            nonlocal latest,stable_since
            expected={r:state(r)['local']['heldItem'] for r in ROLES}
            matched=all(expected[r] and group.matched(r,lambda c:c.get('heldItem')==expected[r]) for r in ROLES)
            if expected!=latest or not matched:stable_since=None
            latest=expected
            if matched and stable_since is None:stable_since=time.monotonic()
            return matched and time.monotonic()-stable_since>=3
        synced=wait_for(settled,30)
        record({'category':'four_player_rapid_weapon_switch','profile':name,'initial_sample':initial,'expected':latest,'pass':synced,
            'states':{r:state(r) for r in ROLES}})
        if not synced:raise RuntimeError('Held weapon did not converge after rapid native switching')
    profile('baseline',0,0,0)

if __name__=='__main__':main()
