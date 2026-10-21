"""Validate fresh AI HP/Buff recovery after leaving the filtered snapshot radius."""
import time
from driver import command,state,wait_for,buff_ids
from matrix import profile,record
from ai_matrix import ai_state
import maps
import multiplayer as group

ROLES=group.ALL
maps.ROLES=group.ROLES=ROLES

def main():
    profile('baseline',0,0,0)
    if not maps.enter('Level_Farm_Main','guest4')['loaded']:raise RuntimeError('AI range Farm entry failed')
    command('host','pauseAI',enabled=True)
    start=state('host')['local']['position']
    candidates=[a for a in command('host','aiSnapshot') if a.get('character') and a['preset']=='Cname_Scav' and a['status']!='Dead']
    selected=max(candidates,key=lambda a:sum((x-y)**2 for x,y in zip(start,a['character']['position'])))
    aid=selected['id'];maximum=selected['max'];q=selected['character']['position']
    if sum((a-b)**2 for a,b in zip(start,q))**.5<=350:raise RuntimeError('Native Farm spawn is not outside the default 312m snapshot radius')
    command('host','invincible',ai=aid,enabled=True)
    near_sites={}
    for r in ROLES:
        command(r,'invincible',enabled=True)
        for dx,dz in [(-4,-4),(4,-4),(-6,-4),(6,-4),(-4,-6),(4,-6)]:
            site=command(r,'safeMove',x=q[0]+dx,y=q[1],z=q[2]+dz)
            time.sleep(2)
            if abs(state(r)['local']['position'][1]-site['position'][1])<1:
                near_sites[r]=site['position'];break
        if r not in near_sites:raise RuntimeError('No stable native character ground near the selected AI')
    if not wait_for(lambda:all(ai_state(r,aid) for r in ROLES),25):raise RuntimeError('Four-player AI visibility failed')
    command('host','removeBuff',ai=aid,id=1002)
    profile('rtt800_loss10',400,100,10)
    for index,owner in enumerate(ROLES[1:]):
        command(owner,'invincible',enabled=True)
        far=command(owner,'safeMove',x=start[0],y=start[1],z=start[2])
        distance=sum((a-b)**2 for a,b in zip(far['position'],q))**.5
        inactive=wait_for(lambda:not any(a['id']==aid and a['active'] for a in command(owner,'worldSnapshot')['aiReplicas']),20)
        record({'category':'four_player_ai_outside_snapshot_radius','owner':owner,'distance':distance,'replica_inactive':inactive,'pass':distance>350 and inactive})
        if distance<=350 or not inactive:raise RuntimeError('Selected AI was not deactivated outside the snapshot radius')
        command('host','removeBuff',ai=aid,id=1002)
        command('host','heal',ai=aid,hp=maximum-10-index)
        for _ in range(3):command('host','addBuff',ai=aid,id=1002)
        expected=ai_state('host',aid)['hp']
        near=near_sites[owner]
        command(owner,'safeMove',x=near[0],y=near[1],z=near[2])
        restored=wait_for(lambda:all((c:=ai_state(r,aid)) and abs(c['hp']-expected)<.02 and buff_ids(c).get(1002)==3 for r in ROLES),25)
        command('host','removeBuff',ai=aid,id=1002,one=True)
        treated=wait_for(lambda:all(buff_ids(ai_state(r,aid)).get(1002)==2 for r in ROLES),20)
        command('host','removeBuff',ai=aid,id=1002)
        cleared=wait_for(lambda:all(1002 not in buff_ids(ai_state(r,aid)) for r in ROLES),20)
        record({'category':'four_player_ai_snapshot_radius_reentry','owner':owner,'profile':'rtt800_loss10',
            'far_ground':far,'far_distance':distance,'inactive_while_far':inactive,
            'hp':expected,'restored':restored,'treated':treated,'cleared':cleared,'pass':restored and treated and cleared,
            'ai':{r:ai_state(r,aid) for r in ROLES},'queues':{r:command(r,'transportDiagnostics') for r in ROLES}})
        if not(restored and treated and cleared):raise RuntimeError('AI radius reentry state failed')
    profile('baseline',0,0,0)
    result=maps.leave('guest4')
    record({'category':'four_player_ai_radius_exit','pass':result['pass'],'result':result})
    if not result['pass']:raise RuntimeError('AI radius native exit failed')

if __name__=='__main__':main()
