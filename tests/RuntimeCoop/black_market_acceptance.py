"""Native black-market state, UI, recovery and player isolation in four games."""
import json
import time
from pathlib import Path
from driver import command
import multiplayer as group
from matrix import profile

OUT=Path(__file__).resolve().parents[2]/'artifacts/black-market-fix'
group.ROLES=group.ALL
group.maps.ROLES=group.ALL

def snapshot():
    return group.parallel('blackMarket')

def market(value):
    return next(m for m in value['markets'] if m['instance']==value['singleton'])

def emit(name,passed,**details):
    row={'case':name,'pass':passed,**details}
    with (OUT/'acceptance.jsonl').open('a',encoding='utf-8') as f:f.write(json.dumps(row)+'\n')
    print(name,passed,flush=True)
    if not passed:raise AssertionError(name)

def main():
    for name,d,j,l in [('baseline',0,0,0),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        group.parallel('blackMarket',close=True)
        for role in group.ALL:
            ready=command(role,'blackMarket')
            command(role,'blackMarket',chance=market(ready)['max'],elapsed=0)
        for owner in group.ALL:
            before=snapshot()
            command(owner,'blackMarket',open=True)
            command(owner,'blackMarket',refresh=True)
            time.sleep(2)
            after=snapshot()
            own=market(after[owner])
            independent=all(market(after[r])['chance']==market(before[r])['chance'] and
                market(after[r])['supplies']==market(before[r])['supplies'] and
                market(after[r])['demands']==market(before[r])['demands'] for r in group.ALL if r!=owner)
            emit(name+'_'+owner+'_refresh',independent and own['chance']==market(before[owner])['chance']-1,
                 before=before,after=after)
            time.sleep(3)
            ticking=command(owner,'blackMarket')
            emit(name+'_'+owner+'_countdown',market(ticking)['remaining']<own['remaining']-2 and
                 ticking['view']['eta']!=after[owner]['view']['eta'],before=after[owner],after=ticking)
            interval=own['interval']
            command(owner,'blackMarket',elapsed=interval-1)
            time.sleep(2)
            recovered=command(owner,'blackMarket')
            emit(name+'_'+owner+'_recovery',market(recovered)['chance']==own['max'] and
                 recovered['view']['chance']==str(own['max'])+'/'+str(own['max']),state=recovered)
            command(owner,'blackMarket',close=True)
    profile('baseline',0,0,0)

if __name__=='__main__':main()
