import json
import time
from driver import command,wait_for,ROOT

host={b['key']:b for b in command('publichost','lootInspection')}
box=next(b for b in command('publicclient','lootInspection') if b['active'] and b['key'] in host and
    3<=len(host[b['key']]['items'])<host[b['key']]['Capacity'] and all(i['type']!=140 for i in host[b['key']]['items']))
role='publicclient'
command(role,'nativeInteraction',close=True)
x,y,z=box['position']
command(role,'safeMove',x=x+1,y=y,z=z)
command(role,'nativeInteraction',instance=box['box'])
time.sleep(.5)
command(role,'lootPut',instance=box['box'],holdSearch=True)
for step in range(2):
    command(role,'lootPut',instance=box['box'],type=140,method='add')
    time.sleep(1)
    row=dict(step=step+1)
    for peer in ['publichost','publicclient']:
        current=next(b for b in command(peer,'lootInspection') if b['key']==box['key'])
        row[peer]=[i for i in current['items'] if i['type']==140]
    print(row,flush=True)
    with (ROOT.parent/'search-render-audit/stack-probe.jsonl').open('a',encoding='utf-8') as f:f.write(json.dumps(row)+'\n')
command(role,'nativeInteraction',close=True)
