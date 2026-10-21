import json
import time
from driver import ROOT,command,state,wait_for,buff_ids
from maps import enter
from matrix import profile,record
from ai_matrix import ai_state,layers

def prepare():
    if not enter('Level_GroundZero_Main','client')['loaded']:raise RuntimeError('Raid entry failed')
    command('host','pauseAI',enabled=True)
    p=state('host')['local']['position']
    ais=command('host','aiSnapshot')
    candidates=sorted([a for a in ais if a.get('character') and a['preset']=='Cname_Scav'],key=lambda a:sum((x-y)**2 for x,y in zip(p,a['character']['position'])))
    selected=candidates[0]
    for candidate in candidates:
        info=command('host','combatInfo',ai=candidate['id'])
        if info['weapons']:
            selected=candidate;break
    aid=selected['id'];q=selected['character']['position']
    (ROOT/'ai-target.json').write_text(json.dumps(selected))
    for r in ['host','client']:
        command(r,'clearBuffs');command(r,'invincible',enabled=True)
        command(r,'move',x=q[0]+(2 if r=='host' else -2),y=q[1],z=q[2]+2)
    if not wait_for(lambda:any(a['id']==aid and a.get('character') for a in command('client','aiSnapshot')),10):raise RuntimeError('AI replica absent')
    return selected

def native_damage(selected):
    aid=selected['id'];maximum=selected['max']
    cases=[('armor',{'ignoreArmor':False}),('unarmored',{'ignoreArmor':True}),('critical',{'critRate':1,'critFactor':2})]+[(f'element_{e}',{'element':e}) for e in range(1,7)]
    for name,d,j,l in [('baseline',0,0,0),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        for label,args in cases:
            values={}
            for r in ['host','client']:
                command('host','invincible',ai=aid,enabled=False)
                for bid in [1001,1002]:command('host','removeBuff',ai=aid,id=bid)
                command('host','heal',ai=aid,hp=maximum)
                if not wait_for(lambda:abs(ai_state('client',aid)['hp']-maximum)<.01,8):raise RuntimeError('AI heal did not converge')
                command(r,'damage',ai=aid,amount=10,**args)
                changed=wait_for(lambda:ai_state('host',aid)['hp']<maximum-.01,8)
                synced=wait_for(lambda:abs(ai_state('client',aid)['hp']-ai_state('host',aid)['hp'])<.01,15)
                values[r]={'host':ai_state('host',aid),'client':ai_state('client',aid),'changed':changed,'synced':synced}
            equal=abs(values['host']['host']['hp']-values['client']['host']['hp'])<.01
            record({'category':'ai_outgoing_damage','profile':name,'case':label,'pass':equal and all(v['changed'] and v['synced'] for v in values.values()),'outcomes':values})
        command('host','invincible',ai=aid,enabled=True)
        for label,args in cases:
            values={}
            for r,other in [('host','client'),('client','host')]:
                command(r,'clearBuffs');command(r,'invincible',enabled=False);command(r,'heal',hp=100)
                if not wait_for(lambda:abs(state(r)['local']['hp']-100)<.01 and any(abs(e['character']['hp']-100)<.01 for e in state(other).get('remotes',[])),15):raise RuntimeError('Player heal did not converge before AI hit')
                hit=command('host','aiAttack',ai=aid,remote=r=='client',amount=10,**args)
                changed=wait_for(lambda:state(r)['local']['hp']<99.9,8)
                time.sleep(.3)
                after=state(r)['local']
                synced=wait_for(lambda:any(abs(e['character']['hp']-after['hp'])<.01 for e in state(other).get('remotes',[])),8)
                values[r]={'hit':hit,'after':after,'changed':changed,'synced':synced}
                command(r,'invincible',enabled=True)
            equal=abs(values['host']['after']['hp']-values['client']['after']['hp'])<.01
            record({'category':'ai_incoming_damage','profile':name,'case':label,'pass':equal and all(v['changed'] and v['synced'] for v in values.values()),'outcomes':values})
    profile('baseline',0,0,0)

def guns(selected):
    aid=selected['id'];maximum=selected['max']
    for r in ['host','client']:
        command(r,'prepareGun')
    command('host','invoke',ai=aid,method='SwitchToFirstAvailableWeapon')
    time.sleep(1)
    record({'category':'native_combat_setup','ai':command('host','combatInfo',ai=aid),'players':{r:command(r,'combatInfo') for r in ['host','client']}})
    for r in ['host','client']:
        for shot in range(3):
            command('host','invincible',ai=aid,enabled=False);command('host','heal',ai=aid,hp=maximum)
            for bid in [1001,1002]:command('host','removeBuff',ai=aid,id=bid)
            wait_for(lambda:abs(ai_state('client',aid)['hp']-maximum)<.01,5)
            fired=command(r,'fire',ai=aid)
            changed=wait_for(lambda:ai_state('host',aid)['hp']<maximum-.01,3)
            synced=wait_for(lambda:abs(ai_state('client',aid)['hp']-ai_state('host',aid)['hp'])<.01,5)
            record({'category':'native_gun_vs_ai','role':r,'shot':shot,'fired':fired,'changed':changed,'converged':synced,'host':ai_state('host',aid),'client':ai_state('client',aid),'pass':changed and synced})
    command('host','invincible',ai=aid,enabled=True)
    for r,other in [('host','client'),('client','host')]:
        command(r,'invincible',enabled=False);command(r,'heal',hp=100)
        fired=command('host','aiFire',ai=aid,remote=r=='client')
        changed=wait_for(lambda:state(r)['local']['hp']<99.9,5)
        time.sleep(.5)
        record({'category':'native_ai_gun_vs_player','role':r,'fired':fired,'pass':changed,'victim':state(r)['local'],'observer':state(other)['remotes']})
        command(r,'invincible',enabled=True)
        command(r,'clearBuffs')

def kill_credit(selected):
    aid=selected['id']
    before={r:state(r) for r in ['host','client']}
    command('host','invincible',ai=aid,enabled=False)
    command('client','damage',ai=aid,amount=99999)
    dead=wait_for(lambda:any(a['id']==aid and a['status']=='Dead' for a in command('host','aiSnapshot')),10)
    credited=wait_for(lambda:state('client').get('localKillEvents',0)>before['client'].get('localKillEvents',0),8)
    time.sleep(1)
    after={r:state(r) for r in ['host','client']}
    # Late/duplicate hits on the same dead AI must not award the kill again.
    try:command('client','damage',ai=aid,amount=99999)
    except RuntimeError:pass
    time.sleep(2)
    final={r:state(r) for r in ['host','client']}
    xp=after['client']['experience']-before['client']['experience']
    host_xp=after['host']['experience']-before['host']['experience']
    once=final['client']['localKillEvents']-before['client']['localKillEvents']==1 and final['client']['experience']==after['client']['experience']
    record({'category':'client_ai_kill_credit','dead':dead,'credited':credited,'client_xp':xp,'host_xp':host_xp,'once':once,
        'pass':dead and credited and xp>0 and host_xp==0 and once,'before':before,'after':after,'final':final})

if __name__=='__main__':
    selected=prepare()
    guns(selected)
    native_damage(selected)
    kill_credit(selected)
