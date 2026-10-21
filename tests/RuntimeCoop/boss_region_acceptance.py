"""Native elemental boss attachments in real isolated host/client processes."""
import json
import math
import os
import time
import atexit
from driver import command, state, wait_for, ROOT
from region_survey import capture, ROLES

OUT = ROOT.parent / 'region-sync'


def emit(name, passed, **details):
    row = dict(phase=os.environ.get('REGION_PHASE','baseline'),case=name, passed=bool(passed), **details)
    with (OUT / 'acceptance.jsonl').open('a', encoding='utf-8') as f:
        f.write(json.dumps(row, ensure_ascii=False) + '\n')
    print(name, bool(passed), flush=True)
    if not passed:
        raise AssertionError(name)


def areas(role, color=None):
    return [m for m in command(role, 'regions')['markers'] if m['IsArea'] and
            (color is None or ('POIBoss_' + color) in m['path'])]


def move(role, position, dx=0):
    for ox,oz in dict.fromkeys([(dx,0),(-dx,0),(0,dx),(0,-dx),(0,0)]):
        try:return command(role,'safeMove',x=position['x']+ox,y=position['y'],z=position['z']+oz)
        except RuntimeError:pass
    raise RuntimeError('No supported nearby test point')


def dist(a, b):
    return math.sqrt(sum((a[k]-b[k])**2 for k in ['x','y','z']))


def stack(row):
    return {e['type']:e['fields'] for e in row['stack']}


for role in ROLES:
    command(role, 'regions', holdPlayer=True)
    command(role, 'invincible', enabled=True)
    command(role, 'nativeInteraction', close=True)
command('publichost', 'pauseAI', enabled=True)
atexit.register(lambda: [command(role, 'regions', holdPlayer=False) for role in ROLES])
for old in areas('publichost','Blue'):
    command('publichost','regions',nativeAI=old['nativeAI'],kill=True)
for old in areas('publichost','Red'):
    if old['position']['y'] < -100:
        command('publichost','regions',nativeAI=old['nativeAI'],kill=True)
assert wait_for(lambda:not areas('publicclient','Blue'),20)

if not areas('publichost','Red'):
    p=state('publichost')['local']['position']
    command('publichost','regions',spawn='EnemyPreset_Boss_Red',x=p[0],y=p[1],z=p[2])
red = areas('publichost', 'Red')[0]
emit('red_marker_matches_native_asset', wait_for(lambda: len(areas('publicclient','Red')) == len(areas('publichost','Red')),15)
     and all(m['AreaRadius']==50 and m['icon']==red['icon'] and m['color']==red['color'] for m in areas('publicclient','Red')))
move('publichost', red['position'], 3)
move('publicclient', red['position'], 3)
time.sleep(1)
near = capture('red-near-after')
emit('red_local_volume_matches_native_host', stack(near['publichost'])==stack(near['publicclient']),
     host=near['publichost']['stack'], client=near['publicclient']['stack'])

boxes=command('publichost','lootInspection')
for remote in sorted((b for b in boxes if b['active']), key=lambda b: sum((b['position'][i]-red['position'][k])**2 for i,k in enumerate(['x','y','z'])),reverse=True):
    far=dict(zip(['x','y','z'],remote['position']))
    try:
        move('publicclient',far)
        break
    except RuntimeError:
        continue
else:
    raise RuntimeError('No supported far location')
time.sleep(2)
far_view=capture('red-far-after')
emit('far_marker_survives_model_culling', len(areas('publicclient','Red'))==len(areas('publichost','Red')) and
     dist(far,red['position'])>300, distance=dist(far,red['position']))
placed=move('publichost',red['position'],4)['position']
command('publichost','regions',nativeAI=red['nativeAI'],x=placed[0],y=placed[1],z=placed[2])
emit('far_marker_tracks_authoritative_boss_position',wait_for(lambda: any(dist(m['position'],areas('publichost','Red')[0]['position'])<.25
     for m in areas('publicclient','Red')),20))
emit('local_entry_does_not_tint_remote_player', stack(far_view['publichost']) != stack(far_view['publicclient']))
move('publichost',far)
time.sleep(1)
outside=capture('red-outside-after')
emit('leaving_red_area_restores_native_stack', stack(outside['publichost'])==stack(outside['publicclient']))

p=state('publichost')['local']['position']
spawn=command('publichost','regions',spawn='EnemyPreset_Boss_Blue',x=p[0],y=p[1],z=p[2])
blue=areas('publichost','Blue')[0]
emit('blue_marker_uses_native_asset',wait_for(lambda: bool(areas('publicclient','Blue')),15) and
     areas('publicclient','Blue')[0]['color']==blue['color'] and areas('publicclient','Blue')[0]['AreaRadius']==blue['AreaRadius'])
for role in ROLES:move(role,blue['position'],3)
time.sleep(1)
blue_near=capture('blue-near-after')
emit('blue_local_volume_matches_native_host',stack(blue_near['publichost'])==stack(blue_near['publicclient']),
     host=blue_near['publichost']['stack'],client=blue_near['publicclient']['stack'])
command('publichost','regions',nativeAI=spawn['spawned'],kill=True)
emit('blue_death_removes_marker_and_area',wait_for(lambda:not areas('publicclient','Blue'),15))
capture('blue-dead-after')
emit('each_native_area_registered_once',all(e['count']==1 for e in command('publicclient','regions')['areaRegistrations']))
