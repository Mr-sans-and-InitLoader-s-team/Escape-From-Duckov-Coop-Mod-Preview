"""Deposit through native AddItem/AddAt/AddAndMerge while a world box is being searched."""
import json
import os
import time
from driver import command, wait_for, ROOT

OUT=ROOT.parent/'search-render-audit'
phase=os.environ.get('SEARCH_PHASE','normal')


def emit(name,passed,**details):
    with (OUT/'search-acceptance.jsonl').open('a',encoding='utf-8') as f:
        f.write(json.dumps(dict(phase=phase,case=name,passed=bool(passed),**details),ensure_ascii=False)+'\n')
    print(name,bool(passed),flush=True)
    assert passed,name


def get(role,instance):
    return next(b for b in command(role,'lootInspection') if b['box']==instance)


for role in ['publichost','publicclient']:
    command(role,'invincible',enabled=True)
    for method in ['add','at','merge']:
        command(role,'nativeInteraction',close=True)
        canonical={b['key']:b for b in command('publichost','lootInspection')}
        box=next(b for b in command(role,'lootInspection') if b['active'] and b['items'] and b['NeedInspection'] and
                 not b['hasBeenInspectedInLootBox'] and len(b['items'])<b['Capacity'] and all(i['type']!=140 for i in b['items']) and
                 b['key'] in canonical and 3<=len(canonical[b['key']]['items'])<canonical[b['key']]['Capacity'] and
                 canonical[b['key']]['NeedInspection'] and not canonical[b['key']]['hasBeenInspectedInLootBox'] and
                 all(i['type']!=140 for i in canonical[b['key']]['items']))
        x,y,z=box['position']
        command(role,'safeMove',x=x+1,y=y,z=z)
        command(role,'nativeInteraction',instance=box['box'])
        time.sleep(1.5 if phase=='weak' else .3)
        command(role,'lootPut',instance=box['box'],holdSearch=True)
        result=command(role,'lootPut',instance=box['box'],type=140,method=method)
        def correct():
            current=get(role,box['box'])
            inserted=[i for i in current['items'] if i['type']==140]
            return len(inserted)==1 and inserted[0]['count']==1 and inserted[0]['Inspected'] and not inserted[0]['Inspecting']
        emit(role+'_'+method+'_known_deposit_once',wait_for(correct,20),calls=result['calls'],after=get(role,box['box']))
        emit(role+'_'+method+'_unknown_contents_stay_hidden',any(not i['Inspected'] for i in get(role,box['box'])['items'] if i['type']!=140))
        command(role,'nativeInteraction',close=True)
        command(role,'nativeInteraction',instance=box['box'])
        time.sleep(2 if phase=='weak' else .5)
        emit(role+'_'+method+'_reopen_preserves_known_item',wait_for(correct,20))
        command(role,'nativeInteraction',close=True)
