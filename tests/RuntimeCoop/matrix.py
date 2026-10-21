"""Live game assertions. Each result records the actual owner and replica state."""
import json
import statistics
import time
from concurrent.futures import ThreadPoolExecutor
from threading import Lock
from driver import ROOT, command, state, wait_for, buff_ids

_result_lock=Lock()

def record(row):
    with _result_lock:
        with (ROOT/'live-matrix.jsonl').open('a',encoding='utf-8') as stream:
            stream.write(json.dumps(row,ensure_ascii=False)+'\n')
        print(json.dumps(row,ensure_ascii=True),flush=True)

def matched(owner,observer,tolerance=.05):
    local=state(owner).get('local')
    remotes=state(observer).get('remotes') or []
    return bool(local and remotes and all(abs(e['character']['hp']-local['hp'])<=tolerance and abs(e['character']['max']-local['max'])<=tolerance for e in remotes))

def mirror_cleared(role,ident):
    remotes=state(role).get('remotes') or []
    return bool(remotes) and all(ident not in buff_ids(e.get('character')) for e in remotes)

def owner_expired(role,ident):
    local=state(role).get('local')
    return bool(local) and ident not in buff_ids(local)

def profile(name,delay,jitter,loss):
    path=ROOT/'network-profile.json'
    temp=path.with_suffix('.tmp')
    temp.write_text(json.dumps({'name':name,'delay_ms':delay,'jitter_ms':jitter,'loss_percent':loss}),encoding='utf-8')
    for attempt in range(20):
        try:temp.replace(path);break
        except PermissionError:
            if attempt==19:raise
            time.sleep(.05)
    time.sleep(2)

def damage_matrix():
    for name,delay,jitter,loss in [('baseline',0,0,0),('rtt100_loss1',50,15,1),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
        profile(name,delay,jitter,loss)
        for role in ['host','client']:
            command(role,'invincible',enabled=False)
            command(role,'removeBuff',id=1051)
        cases=[('physical',{}),('real',{'type':2}),('explosion_flag',{'explosion':True})]+[(f'element_{e}',{'element':e}) for e in range(1,7)]
        for label,args in cases:
            outcomes={}
            for role,other in [('host','client'),('client','host')]:
                command(role,'heal',hp=100)
                wait_for(lambda:state(role).get('local',{}).get('hp')==100 and matched(role,other),8)
                hit=command(role,'damage',amount=10,**args)
                converged=wait_for(lambda:abs(state(role).get('local',{}).get('hp',-999)-hit['after'])<.05 and matched(role,other),10)
                samples=[]
                for _ in range(8):
                    samples.append({'owner':state(role).get('local',{}).get('hp'),'mirror':(state(other).get('remotes') or [{}])[0].get('character',{}).get('hp')})
                    time.sleep(.1)
                rollback=any(s['mirror'] is not None and s['mirror']>hit['after']+.05 and s['owner']<=hit['after']+.05 for s in samples) if converged else None
                outcomes[role]={'native':hit,'converged':converged,'rollback_after_convergence':rollback,'samples':samples}
            equal=abs(outcomes['host']['native']['after']-outcomes['client']['native']['after'])<.05
            record({'category':'player_damage','harness_revision':2,'profile':name,'case':label,'same_native_damage':equal,
                    'pass':equal and all(v['converged'] and not v['rollback_after_convergence'] for v in outcomes.values()),'outcomes':outcomes})
        for role,other in [('host','client'),('client','host')]:
            command(role,'heal',hp=100)
            command(role,'damage',amount=99999)
            protected=wait_for(lambda:state(role).get('local',{}).get('hp')==1 and matched(role,other),10)
            record({'category':'shelter_death_protection','profile':name,'role':role,'pass':protected})
            command(role,'heal',hp=100)
        for role in ['host','client']:
            command(role,'heal',hp=100)
            result=command(role,'explosion',amount=10,radius=3)
            other='client' if role=='host' else 'host'
            converged=wait_for(lambda:abs(state(role).get('local',{}).get('hp',-999)-result['after'])<.05 and matched(role,other),10)
            record({'category':'native_explosion_aoe','profile':name,'role':role,'result':result,'converged':converged,
                    'pass':result['after']<result['before'] and converged})
    profile('baseline',0,0,0)

def expiry_matrix():
    catalog=json.loads((ROOT/'buff-catalog.json').read_text(encoding='utf-8'))
    for name,delay,jitter,loss in [('baseline',0,0,0),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
        profile(name,delay,jitter,loss)
        for role,other in [('host','client'),('client','host')]:
            command(role,'invincible',enabled=True)
            for buff in catalog:
                if not buff['timed']:continue
                ident=buff['id']
                before=state(role).get('time',0)
                source=command(role,'addBuff',id=ident)
                wait_for(lambda:state(role).get('time',0)>before+.25,3)
                if ident not in buff_ids(source):
                    record({'category':'buff_expiry','profile':name,'role':role,'id':ident,'outcome':'native_not_added'})
                    continue
                try:
                    expired=command(role,'expireBuff',id=ident)
                    if expired and not expired['expired']:
                        record({'category':'buff_expiry','profile':name,'role':role,'id':ident,'outcome':expired['reason']})
                        continue
                except RuntimeError:
                    record({'category':'buff_expiry','profile':name,'role':role,'id':ident,'outcome':'native_already_removed'})
                    continue
                time.sleep(max(.35,delay*2/1000+.3))
                passed=wait_for(lambda:owner_expired(role,ident) and mirror_cleared(other,ident),10)
                record({'category':'buff_expiry','profile':name,'role':role,'id':ident,'pass':passed})
            command(role,'invincible',enabled=False)
    profile('baseline',0,0,0)

def weak_buffs():
    catalog=json.loads((ROOT/'buff-catalog.json').read_text(encoding='utf-8'))
    for name,delay,jitter,loss in [('baseline',0,0,0),('rtt100_loss1',50,15,1),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
        profile(name,delay,jitter,loss)
        def run_role(role):
            other='client' if role=='host' else 'host'
            command(role,'invincible',enabled=True)
            for buff in catalog:
                ident=buff['id']
                before=state(role).get('time',0)
                source=command(role,'addBuff',id=ident)
                added=ident in buff_ids(source)
                wait_for(lambda:state(role).get('time',0)>before+.25,3)
                seen=wait_for(lambda:any(ident in buff_ids(e.get('character')) for e in state(other).get('remotes',[])) or owner_expired(role,ident),8) if added else False
                mirror_added=any(ident in buff_ids(e.get('character')) for e in state(other).get('remotes',[]))
                before_remove=state(role).get('local')
                removed=command(role,'removeBuff',id=ident)
                owner_removed=ident not in buff_ids(removed)
                if not mirror_added:time.sleep(max(.35,delay*2/1000+.3))
                cleared=wait_for(lambda:mirror_cleared(other,ident),12) if owner_removed else False
                record({'category':'buff_weak_network','harness_revision':3,'profile':name,'role':role,'id':ident,'owner_added':added,'mirror_added':mirror_added,
                        'observed_or_native_expired':seen,'owner_removed':owner_removed,'mirror_removed':cleared,
                        'pass':cleared if owner_removed else None})
            command(role,'invincible',enabled=False)
        with ThreadPoolExecutor(max_workers=2) as pool:list(pool.map(run_role,['host','client']))
    profile('baseline',0,0,0)

def bleed_performance():
    profile('baseline',0,0,0)
    for role,other in [('host','client'),('client','host')]:
        command(role,'removeBuff',id=1051)
        command(role,'removeBuff',id=1002)
        command(role,'heal',hp=100)
        command(role,'invincible',enabled=False)
        phases={}
        for phase,seconds in [('before',10),('bleeding',45),('after_treatment',15)]:
            if phase=='bleeding':command(role,'addBuff',id=1002)
            if phase=='after_treatment':command(role,'removeBuff',id=1002)
            samples=[]
            deadline=time.monotonic()+seconds
            while time.monotonic()<deadline:
                samples.append({r:state(r) for r in ['host','client']})
                time.sleep(.2)
            phases[phase]=samples
        (ROOT/(role+'-bleed-performance.json')).write_text(json.dumps(phases,ensure_ascii=False),encoding='utf-8')
        summary={phase:{r:round(statistics.median(s[r]['fps'] for s in samples if 'fps' in s[r]),1) for r in ['host','client']} for phase,samples in phases.items()}
        record({'category':'bleed_performance','owner':role,'median_fps':summary,'hp_start':phases['bleeding'][0][role]['local']['hp'],
                'hp_end':phases['bleeding'][-1][role]['local']['hp']})

def outage():
    profile('baseline',0,0,0)
    for role in ['host','client']:
        command(role,'invincible',enabled=False)
        command(role,'clearBuffs')
        command(role,'heal',hp=100)
    time.sleep(2)
    profile('outage100',150,50,100)
    hits={}
    for role in ['host','client']:
        hits[role]=command(role,'damage',amount=10)
        for _ in range(3):command(role,'addBuff',id=1002)
        command(role,'removeBuff',id=1002,one=True)
        command(role,'removeBuff',id=1002)
    time.sleep(5)
    profile('baseline',0,0,0)
    converged=wait_for(lambda:matched('host','client') and matched('client','host') and all(
        1002 not in buff_ids(e.get('character')) for role in ['host','client'] for e in state(role).get('remotes',[])),20)
    record({'category':'temporary_total_loss','seconds':5,'hits':hits,'pass':converged,'final':{r:state(r) for r in ['host','client']}})

if __name__=='__main__':
    import sys
    {'damage':damage_matrix,'expiry':expiry_matrix,'weak-buffs':weak_buffs,'bleed':bleed_performance,'outage':outage}[sys.argv[1]]()
