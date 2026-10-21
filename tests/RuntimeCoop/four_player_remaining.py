"""Only network/Buff combinations not already covered by the baseline four-player run."""
import json
import time
from driver import ROOT,command,state,wait_for,buff_ids
from matrix import profile,record
import multiplayer as group

ROLES=group.ALL
group.ROLES=ROLES

def expiration():
    catalog=[b for b in command('host','buffCatalog') if b['timed']]
    group.parallel('invincible',enabled=True);group.parallel('clearBuffs')
    for name,d,j,l in [('baseline',0,0,0),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        for b in catalog:
            bid=b['id']
            added=group.parallel('addBuff',id=bid)
            native={r:bid in buff_ids(added[r]) for r in ROLES}
            # Require observation or a fresh owner expiry before accelerating native timers.
            before={r:state(r).get('time',0) for r in ROLES}
            wait_for(lambda:all(state(r).get('time',0)>before[r]+.25 for r in ROLES),3)
            seen=wait_for(lambda:all(not native[r] or group.matched(r,lambda c:bid in buff_ids(c)) or bid not in buff_ids(state(r).get('local')) for r in ROLES),12)
            expired=group.parallel('expireBuff',id=bid)
            cleared=wait_for(lambda:all(group.matched(r,lambda c:bid not in buff_ids(c)) for r in ROLES),15)
            record({'category':'four_player_native_expiry','profile':name,'id':bid,'native_added':native,
                'observed_or_native_expired':seen,'native_expiry_commands':expired,'pass':cleared})

def warmth():
    profile('baseline',0,0,0)
    group.parallel('clearBuffs');group.parallel('invincible',enabled=False)
    for _ in range(4):group.parallel('addBuff',id=2101)
    group.parallel('addBuff',id=2102);group.parallel('addBuff',id=2201)
    before={r:state(r) for r in ROLES}
    after=group.parallel('coldStep',level=0,ticks=20)
    cooled=wait_for(lambda:all(group.matched(r,lambda c:2101 not in buff_ids(c) and 2102 not in buff_ids(c)) for r in ROLES),15)
    frostbite={r:2201 in buff_ids(after[r]) for r in ROLES}
    record({'category':'four_player_native_warmth_ticks','pass':cooled,'frostbite_persists':frostbite,
        'scope':'Native warm-level updates; physical fire/oil-barrel/indoor trigger volumes require gameplay validation',
        'before':before,'after':after})
    group.parallel('clearBuffs');group.parallel('invincible',enabled=True)

def animation():
    offsets={r:(ROOT/r/'exceptions.jsonl').stat().st_size if (ROOT/r/'exceptions.jsonl').exists() else 0 for r in ROLES}
    result=group.parallel('animationProbe')
    time.sleep(1)
    errors={r:(ROOT/r/'exceptions.jsonl').read_bytes()[offsets[r]:].decode('utf-8-sig') if (ROOT/r/'exceptions.jsonl').exists() else '' for r in ROLES}
    passed=all(v['queued'] and v['ready'] and v['replayed'] and not v['pending'] and v['mainAlive'] for v in result.values())
    record({'category':'four_player_animation_initialization','pass':passed and not any(errors.values()),'result':result,'new_errors':errors})

def main():
    group.hp_layers([('rtt100_loss1',50,15,1)])
    group.parallel('clearBuffs');group.parallel('invincible',enabled=True)
    for name,d,j,l in [('rtt100_loss1',50,15,1),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l);group.buffs(name)
    expiration();warmth();animation()
    profile('baseline',0,0,0)
    (ROOT/'four-player-remaining-ready').write_text('4')

if __name__=='__main__':main()
