import json
import time
from driver import ROOT,command,state,wait_for,buff_ids
from matrix import profile,record
import multiplayer as group

def buff(character,bid):
    return next((b for b in character.get('buffs',[]) if b['id']==bid),None)

def main():
    group.ROLES=group.ALL
    profile('rtt800_loss10',400,100,10)
    group.parallel('invincible',enabled=True);group.parallel('clearBuffs')
    for _ in range(3):group.parallel('addBuff',id=1001)
    initial=wait_for(lambda:all(group.matched(r,lambda c:buff_ids(c).get(1001)==3) for r in group.ALL),15)
    time.sleep(5)
    before={r:{o:buff(state(r)['local'] if r==o else group.remote(r,o),1001) for o in group.ALL} for r in group.ALL}
    group.parallel('removeBuff',id=1001,one=True)
    partial=wait_for(lambda:all(group.matched(r,lambda c:buff_ids(c).get(1001)==2) for r in group.ALL),15)
    after={r:{o:buff(state(r)['local'] if r==o else group.remote(r,o),1001) for o in group.ALL} for r in group.ALL}
    refreshed=[(r,o) for r in group.ALL for o in group.ALL if before[r][o] and after[r][o] and after[r][o]['remaining']>before[r][o]['remaining']+1]
    record({'category':'four_player_timed_partial_cure','profile':'rtt800_loss10','initial':initial,'partial':partial,
        'before':before,'after':after,'refreshed':refreshed,'pass':initial and partial and not refreshed})
    expired=wait_for(lambda:all(group.matched(r,lambda c:1001 not in buff_ids(c)) for r in group.ALL),35)
    record({'category':'four_player_natural_bleed_expiry','pass':expired,'states':{r:state(r) for r in group.ALL}})
    profile('baseline',0,0,0)

if __name__=='__main__':main()
