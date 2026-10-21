import json
import time
from driver import ROOT,command,state,wait_for,buff_ids
from matrix import profile,record
from ai_matrix import ai_state
import maps
import multiplayer as group

ROLES=group.ALL
group.ROLES=ROLES
maps.ROLES=ROLES

def hp_ready(role,hp=100):
    return group.matched(role,lambda c:abs(c['hp']-hp)<.02)

def ai_matched(aid,hp):
    values=[ai_state(r,aid) for r in ROLES]
    return all(c and abs(c['hp']-hp)<.02 for c in values)

def setup():
    profile('baseline',0,0,0)
    if not all(state(r).get('raid') and state(r).get('scene')=='Level_GroundZero_1' for r in ROLES):
        entered=maps.enter('Level_GroundZero_Main','guest4')
        record({'category':'four_player_raid_entry','pass':entered['loaded'] and entered['voted'],'result':entered})
        if not entered['loaded']:raise RuntimeError('Four-player raid entry failed')
    command('host','pauseAI',enabled=True)
    start=state('host')['local']['position']
    candidates=sorted([a for a in command('host','aiSnapshot') if a.get('character') and a['preset']=='Cname_Scav'],
        key=lambda a:sum((x-y)**2 for x,y in zip(start,a['character']['position'])))
    selected=next((a for a in candidates if command('host','combatInfo',ai=a['id'])['weapons']),candidates[0])
    aid=selected['id'];q=selected['character']['position']
    command('host','invincible',ai=aid,enabled=True)
    (ROOT/'ai-target.json').write_text(json.dumps(selected),encoding='utf-8')
    for r,(x,z) in zip(ROLES,[(4,4),(-4,4),(-4,-4),(4,-4)]):
        command(r,'invincible',enabled=True);command(r,'clearBuffs')
        command(r,'safeMove',x=q[0]+x,y=q[1],z=q[2]+z)
    if not wait_for(lambda:all(ai_state(r,aid) for r in ROLES),15):raise RuntimeError('Four-player AI visibility failed')
    return selected

def guns(selected):
    aid=selected['id'];maximum=selected['max']
    for r in ROLES:
        info=command(r,'combatInfo')
        if info.get('gun')!=240 or not info.get('bullets'):command(r,'prepareGun')
    command('host','invoke',ai=aid,method='SwitchToFirstAvailableWeapon')
    command('host','invoke',ai=aid,method='TryToReload',args=[None])
    loaded=wait_for(lambda:(command('host','combatInfo',ai=aid).get('bullets') or 0)>0,15)
    record({'category':'native_ai_gun_ammo','players':4,'loaded':loaded,'ai':command('host','combatInfo',ai=aid)})
    for r in ROLES:
        command('host','invincible',ai=aid,enabled=False);command('host','heal',ai=aid,hp=maximum)
        if not wait_for(lambda:ai_matched(aid,maximum),15):raise RuntimeError('AI heal did not reach every peer')
        fired=command(r,'fire',ai=aid)
        changed=wait_for(lambda:ai_state('host',aid)['hp']<maximum-.02,5)
        hp=ai_state('host',aid)['hp']
        synced=wait_for(lambda:ai_matched(aid,hp),15)
        record({'category':'four_player_native_gun_vs_ai','role':r,'fired':fired,'hp':hp,
            'pass':fired['before']>fired['after'] and changed and synced})
    command('host','invincible',ai=aid,enabled=True)
    if not loaded:return
    for r in ROLES:
        command(r,'clearBuffs');command(r,'invincible',enabled=False);command(r,'heal',hp=100)
        if not wait_for(lambda:hp_ready(r),15):raise RuntimeError('Victim heal failed')
        fired=command('host','aiFire',ai=aid,remote=r!='host',player=None if r=='host' else state(r)['playerId'])
        hit=wait_for(lambda:state(r)['local']['hp']<99.9,5)
        expected=state(r)['local']['hp']
        synced=wait_for(lambda:hp_ready(r,expected),15)
        record({'category':'four_player_native_ai_gun_vs_player','role':r,'fired':fired,'hp':expected,
            'pass':fired['before']>fired['after'] and hit and synced})
        command(r,'invincible',enabled=True);command(r,'clearBuffs')

def damage(selected,profiles=None,skip=None):
    aid=selected['id'];maximum=selected['max']
    cases=[('armor',{'ignoreArmor':False}),('unarmored',{'ignoreArmor':True}),('critical',{'critRate':1,'critFactor':2})]+[(f'element_{e}',{'element':e}) for e in range(1,7)]
    for name,d,j,l in profiles or [('baseline',0,0,0),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        for label,args in cases:
            if (name,label) in (skip or set()):continue
            if not group.peers_ready():raise RuntimeError('Test transport disconnected before AI case')
            outgoing={}
            for r in ROLES:
                command('host','invincible',ai=aid,enabled=False)
                for bid in [1001,1002]:command('host','removeBuff',ai=aid,id=bid)
                command('host','heal',ai=aid,hp=maximum)
                if not wait_for(lambda:ai_matched(aid,maximum),20):raise RuntimeError('AI heal baseline absent')
                command(r,'damage',ai=aid,amount=10,**args)
                hit=wait_for(lambda:ai_state('host',aid)['hp']<maximum-.02,10)
                hp=ai_state('host',aid)['hp']
                synced=wait_for(lambda:ai_matched(aid,hp),20)
                outgoing[r]={'hp':hp,'pass':hit and synced}
            record({'category':'four_player_ai_outgoing','profile':name,'case':label,'outcomes':outgoing,
                'pass':all(v['pass'] for v in outgoing.values()) and max(v['hp'] for v in outgoing.values())-min(v['hp'] for v in outgoing.values())<.02})
            command('host','invincible',ai=aid,enabled=True)
            incoming={}
            for r in ROLES:
                command(r,'clearBuffs');command(r,'invincible',enabled=False);command(r,'heal',hp=100)
                if not wait_for(lambda:hp_ready(r),20):raise RuntimeError('Victim heal baseline absent')
                hit=command('host','aiAttack',ai=aid,remote=r!='host',player=None if r=='host' else state(r)['playerId'],amount=10,**args)
                changed=wait_for(lambda:state(r)['local']['hp']<99.9,10)
                hp=state(r)['local']['hp']
                synced=wait_for(lambda:hp_ready(r,hp),20)
                incoming[r]={'hit':hit,'hp':hp,'pass':changed and synced}
                command(r,'invincible',enabled=True)
            record({'category':'four_player_ai_incoming','profile':name,'case':label,'outcomes':incoming,
                'pass':all(v['pass'] for v in incoming.values()) and max(v['hp'] for v in incoming.values())-min(v['hp'] for v in incoming.values())<.02})
        for r in ROLES:
            command(r,'clearBuffs');command(r,'invincible',enabled=False);command(r,'heal',hp=100)
            if not wait_for(lambda:hp_ready(r),20):raise RuntimeError('Explosion victim heal absent')
            blast=command('host','aiExplosion',ai=aid,remote=r!='host',player=None if r=='host' else state(r)['playerId'])
            changed=wait_for(lambda:state(r)['local']['hp']<99.9,10)
            hp=state(r)['local']['hp'];synced=wait_for(lambda:hp_ready(r,hp),20)
            record({'category':'four_player_ai_explosion','profile':name,'role':r,'blast':blast,'hp':hp,'pass':changed and synced})
            command(r,'invincible',enabled=True)
    profile('baseline',0,0,0)

def main():
    selected=setup()
    guns(selected)
    damage(selected)
    returned=maps.leave('guest3',15)
    record({'category':'four_player_evacuation','result':returned,'pass':returned['pass']})
    if returned['pass']:(ROOT/'four-player-combat-ready').write_text('4')

if __name__=='__main__':main()
