"""Change native AI health and buffs while its snapshot is deliberately deferred."""
import json
import os
import time
from driver import command, wait_for, ROOT

phase=os.environ.get('BACKLOG_PHASE','normal')
out=ROOT.parent/'region-sync/backlog-acceptance.jsonl'
target=next(e for e in command('publichost','aiSnapshot') if e['preset']=='Cname_Boss_Red' and e['character'] and e['status']=='Active')
ident=target['id']
had_regeneration=any(b['id']==1307 for b in target['character']['buffs'])
command('publichost','pauseAI',enabled=True)
for role in ['publichost','publicclient']:
    command(role,'invincible',enabled=True)
    p=target['character']['position']
    command(role,'safeMove',x=p[0],y=p[1],z=p[2])
assert wait_for(lambda:any(e['id']==ident and e['character'] for e in command('publicclient','aiSnapshot')),30)
# This numeric fixture needs a stable health baseline; the native red Boss buff regenerates health.
for buff in [1307,1011]:command('publichost','removeBuff',ai=ident,id=buff)
time.sleep(1)

def ai(role):
    return next(e for e in command(role,'aiSnapshot') if e['id']==ident)

def has_buff(row):
    return any(b['id']==1011 for b in (row.get('character') or {}).get('buffs',[]))

def emit(case,passed,**details):
    row=dict(phase=phase,case=case,passed=bool(passed),**details)
    with out.open('a',encoding='utf-8') as stream:stream.write(json.dumps(row)+'\n')
    print(case,bool(passed),flush=True)
    assert passed,case

def hold(check_priority=False):
    assert wait_for(lambda:command('publichost','snapshotBacklog')['peers']==0,45)
    command('publichost','snapshotBacklog',holdSeconds=45)
    command('publicclient','aiAudit',refresh=True)
    assert wait_for(lambda:ident in command('publichost','snapshotBacklog')['ids'],15)
    if check_priority:
        emit('native_area_metadata_precedes_ordinary_ai',ident in command('publichost','snapshotBacklog')['ids'][:4])

def drain():
    command('publichost','snapshotBacklog',holdSeconds=0)
    assert wait_for(lambda:command('publichost','snapshotBacklog')['chunks']==0,45)
    time.sleep(2)

try:
    hold(check_priority=True)
    command('publichost','invincible',ai=ident,enabled=False)
    hurt=command('publichost','damage',ai=ident,amount=200,ignoreArmor=True,ignoreDifficulty=True)
    command('publichost','invincible',ai=ident,enabled=True)
    emit('native_damage_applies_while_snapshot_is_held',hurt['after']<hurt['before'],damage=hurt)
    command('publichost','addBuff',ai=ident,id=1011)
    assert wait_for(lambda:ai('publicclient')['hp']<hurt['before']-50 and has_buff(ai('publicclient')),20)
    drain()
    samples=[]
    until=time.monotonic()+6
    while time.monotonic()<until:
        samples.append((ai('publichost')['hp'],ai('publicclient')))
        time.sleep(.15)
    emit('deferred_snapshot_does_not_restore_old_health',all(hurt['after']-.01<=e['hp']<=owner+.01 and
         owner<hurt['before']-50 and abs(e['character']['hp']-e['hp'])<.01 for owner,e in samples),
         health=[dict(host=owner,client=e['hp'],body=e['character']['hp']) for owner,e in samples])
    emit('deferred_snapshot_keeps_new_native_buff',all(has_buff(e) for _,e in samples))
    hold()
    command('publichost','removeBuff',ai=ident,id=1011)
    assert wait_for(lambda:not has_buff(ai('publicclient')),20)
    drain()
    samples=[]
    until=time.monotonic()+6
    while time.monotonic()<until:
        samples.append(ai('publicclient'))
        time.sleep(.15)
    emit('deferred_snapshot_does_not_resurrect_removed_buff',all(not has_buff(e) for e in samples))
finally:
    command('publichost','snapshotBacklog',holdSeconds=0)
    if had_regeneration:command('publichost','addBuff',ai=ident,id=1307)
