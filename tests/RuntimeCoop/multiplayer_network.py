import json
import statistics
import time
from driver import ROOT,command,state,wait_for,buff_ids
from matrix import profile,record
import multiplayer as group

ROLES=group.ALL
group.ROLES=ROLES

def set_profile(name,loss):
    path=ROOT/'network-profile.json';temp=path.with_suffix('.tmp')
    temp.write_text(json.dumps({'name':name,'delay_ms':0,'jitter_ms':0,'loss_percent':loss}),encoding='utf-8')
    for attempt in range(20):
        try:temp.replace(path);break
        except PermissionError:
            if attempt==19:raise
            time.sleep(.05)

def outage(seconds):
    profile('baseline',0,0,0)
    group.parallel('clearBuffs');group.parallel('invincible',enabled=False);group.parallel('heal',hp=100)
    if not wait_for(lambda:all(group.matched(r,lambda c:abs(c['hp']-100)<.02) for r in ROLES),15):raise RuntimeError('Outage heal did not converge')
    started=time.monotonic();set_profile(f'four_player_outage_{seconds}',100)
    hits=group.parallel('damage',amount=10)
    group.parallel('invincible',enabled=True)
    for _ in range(3):group.parallel('addBuff',id=1002)
    group.parallel('removeBuff',id=1002,one=True)
    time.sleep(max(0,seconds-(time.monotonic()-started)))
    set_profile('baseline',0)
    elapsed=time.monotonic()-started
    disconnected={r:state(r).get('peers')==0 for r in ROLES[1:]}
    if any(disconnected.values()):
        for r in ROLES[1:]:command(r,'join',transport='Direct',port=group.PORTS[r])
    connected=wait_for(group.peers_ready,45)
    synced=wait_for(lambda:all(group.matched(r,lambda c:abs(c['hp']-state(r)['local']['hp'])<.02 and buff_ids(c).get(1002)==2) for r in ROLES),20)
    record({'category':'four_player_total_loss','seconds':round(elapsed,3),'explicit_rejoin':disconnected,'recovered':connected and synced,
        'pass':connected and synced,'states':{r:state(r) for r in ROLES}})
    group.parallel('clearBuffs')

def bleeding():
    profile('baseline',0,0,0)
    group.parallel('clearBuffs');group.parallel('invincible',enabled=False);group.parallel('heal',hp=100)
    phases={}
    for name,duration in [('before',10),('bleeding',45),('after_treatment',15)]:
        if name=='bleeding':group.parallel('addBuff',id=1002)
        if name=='after_treatment':group.parallel('removeBuff',id=1002)
        samples=[];deadline=time.monotonic()+duration
        while time.monotonic()<deadline:
            samples.append({r:state(r) for r in ROLES});time.sleep(.2)
        phases[name]=samples
    (ROOT/'four-player-bleed-samples.json').write_text(json.dumps(phases),encoding='utf-8')
    fps={p:{r:round(statistics.median(s[r]['fps'] for s in samples),2) for r in ROLES} for p,samples in phases.items()}
    actual={r:phases['bleeding'][0][r]['local']['hp']-phases['bleeding'][-1][r]['local']['hp'] for r in ROLES}
    cleared=wait_for(lambda:all(group.matched(r,lambda c:1002 not in buff_ids(c)) for r in ROLES),10)
    record({'category':'four_player_bleed_performance','median_fps':fps,'native_hp_loss':actual,'cleared':cleared,
        'pass':all(v>0 for v in actual.values()) and cleared and all(fps['after_treatment'][r]>.8*fps['before'][r] for r in ROLES),
        'scope':'D3D11 batch mode; foreground frame rate requires manual validation'})
    group.parallel('invincible',enabled=True)

def main():
    import party_wipe
    party_wipe.main()
    outage(2.2);outage(7.2);bleeding()
    (ROOT/'four-player-network-ready').write_text('4')

if __name__=='__main__':main()
