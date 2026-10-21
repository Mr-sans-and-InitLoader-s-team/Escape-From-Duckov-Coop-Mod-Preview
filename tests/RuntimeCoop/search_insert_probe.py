import argparse
import json
import time
from driver import command, state, wait_for, ROOT

parser = argparse.ArgumentParser()
parser.add_argument('--phase', default='before')
args = parser.parse_args()
out = ROOT.parent / 'search-render-audit'
role = 'publicclient'
command(role, 'invincible', enabled=True)
boxes = command(role, 'lootInspection')
box = next(b for b in boxes if b['active'] and b['items'] and b['NeedInspection'] and not b['hasBeenInspectedInLootBox'] and all(i['type'] != 140 for i in b['items']))
x, y, z = box['position']
command(role, 'safeMove', x=x+1, y=y, z=z)
opened = command(role, 'nativeInteraction', instance=box['box'])
time.sleep(.1)
held = command(role, 'lootPut', instance=box['box'], holdSearch=True)
put = command(role, 'lootPut', instance=box['box'], type=140)
snap = [b for b in command(role, 'lootInspection') if b['box'] == box['box']]
row = dict(phase=args.phase, box=box, opened=opened, held=held, put=put, after=snap)
(out / ('search-insert-' + args.phase + '.json')).write_text(json.dumps(row, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(dict(opened=opened, uid=box['uid'], items=snap[0]['items']), ensure_ascii=True), flush=True)
