"""Keep the original eight-second failure and measure bounded recovery on a fresh repeat."""
import time
from driver import command,state,wait_for,buff_ids
from matrix import profile,record
import multiplayer as group

ROLES=group.ALL
group.ROLES=ROLES

def samples():return {r:state(r) for r in ROLES}
def cleared():return all(group.matched(r,lambda c:1206 not in buff_ids(c)) for r in ROLES)

def main():
    profile('rtt800_loss10',400,100,10)
    group.parallel('invincible',enabled=True)
    for attempt in range(3):
        group.parallel('removeBuff',id=1206)
        if not wait_for(cleared,20):raise RuntimeError('Tagilla baseline is not clear')
        group.parallel('addBuff',id=1206)
        added=wait_for(lambda:all(group.matched(r,lambda c:buff_ids(c).get(1206)==1) for r in ROLES),20)
        before=samples();started=time.monotonic();removed=group.parallel('removeBuff',id=1206)
        eight_second_state=None
        def observe():
            nonlocal eight_second_state
            if eight_second_state is None and time.monotonic()-started>=8:eight_second_state=samples()
            return cleared()
        converged=wait_for(observe,20);seconds=round(time.monotonic()-started,3)
        stable=[]
        if converged:
            for _ in range(15):stable.append(cleared());time.sleep(.2)
        valid=added and converged and bool(stable) and all(stable)
        record({'category':'four_player_buff_1206_bounded_recheck','profile':'rtt800_loss10','attempt':attempt,
            'pass':valid,'added':added,'removed':removed,'seconds_to_clear':seconds,'converged_within_8s':converged and seconds<=8,
            'eight_second_state':eight_second_state,'stable':all(stable) if stable else False,
            'before':before,'after':samples(),'queues':{r:command(r,'transportDiagnostics') for r in ROLES}})
        if not valid:raise RuntimeError('Buff 1206 did not reach a stable clear state within 20 seconds')
    profile('baseline',0,0,0)

if __name__=='__main__':main()
