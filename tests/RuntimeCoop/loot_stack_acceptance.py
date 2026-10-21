import json
import os
import time
from driver import command,wait_for,ROOT

phase=os.environ.get('STACK_PHASE','normal')
out=ROOT.parent/'search-render-audit/stack-acceptance.jsonl'
canonical={b['key']:b for b in command('publichost','lootInspection')}
box=next(b for b in command('publicclient','lootInspection') if b['active'] and b['key'] in canonical and
    3<=len(canonical[b['key']]['items'])<=canonical[b['key']]['Capacity']-3 and all(i['type']!=140 for i in canonical[b['key']]['items']))

def items(role):
    return [i for b in command(role,'lootInspection') if b['key']==box['key'] for i in b['items'] if i['type']==140]

def emit(name,passed):
    row=dict(phase=phase,case=name,passed=bool(passed),host=items('publichost'),client=items('publicclient'))
    with out.open('a',encoding='utf-8') as f:f.write(json.dumps(row)+'\n')
    print(name,bool(passed),flush=True)
    assert passed,name

command('publicclient','nativeInteraction',close=True)
x,y,z=box['position']
command('publicclient','safeMove',x=x+1,y=y,z=z)
command('publicclient','nativeInteraction',instance=box['box'])
assert wait_for(lambda:len(next(b for b in command('publicclient','lootInspection') if b['box']==box['box'])['items'])==len(canonical[box['key']]['items']),20)
command('publicclient','lootPut',instance=box['box'],holdSearch=True)
for target in [1,2]:
    command('publicclient','lootPut',instance=box['box'],type=140,count=1,method='add')
    assert wait_for(lambda:sum(i['count'] for i in items('publichost'))==target,20)
# Each deposit has an explicit host acknowledgement; allow delayed deltas to settle.
emit('automatic_merge_has_no_client_optimistic_ghost',wait_for(lambda:len(items('publichost'))==1 and len(items('publicclient'))==1 and
     items('publichost')[0]['count']==items('publicclient')[0]['count']==2,30))
maximum=items('publichost')[0]['max']
assert maximum>2
command('publicclient','lootPut',instance=box['box'],type=140,count=-1,method='add',gap=True)
emit('partial_merge_and_version_gap_conserve_both_stacks',wait_for(lambda:all(len(items(r))==2 and
     sum(i['count'] for i in items(r))==maximum+1 for r in ['publichost','publicclient']),30))
emit('all_partial_merge_receipts_keep_known_items_visible',wait_for(lambda:all(i['Inspected'] and not i['Inspecting'] for i in items('publicclient')),20))
command('publicclient','nativeInteraction',close=True)
command('publicclient','nativeInteraction',instance=box['box'])
emit('partial_merge_reopen_keeps_authoritative_quantity_and_visibility',wait_for(lambda:len(items('publicclient'))==2 and
     sum(i['count'] for i in items('publicclient'))==maximum+1 and all(i['Inspected'] for i in items('publicclient')),30))
command('publicclient','nativeInteraction',close=True)
