"""A rejoining client restores new drops and their original additive-scene ownership."""
import time
from driver import command,state,wait_for
from matrix import profile,record
import maps
import multiplayer as group
from world_persistence import transfer,worlds

ROLES=group.ALL
maps.ROLES=group.ROLES=ROLES

def check(expected,active):
    snapshots=worlds()
    valid=all(all(any(d['id']==ident and (d['item'],d['count'])==value[:2] for d in w['drops']) and
        len([d for d in w['physicalDrops'] if d['id']==ident and (d['item'],d['count'])==value[:2] and
            d['parent']==value[2] and d['active']==(value[2]==active)])==1
        for ident,value in expected.items()) for w in snapshots.values())
    return valid,snapshots

def main():
    profile('baseline',0,0,0)
    if not maps.enter('Level_Farm_Main','client')['loaded']:raise RuntimeError('Drop-rejoin Farm entry failed')
    command('host','pauseAI',enabled=True)
    before={d['id'] for d in worlds()['host']['drops']}
    for index,r in enumerate(ROLES):command(r,'dropItem',id=140,count=41+index)
    if not wait_for(lambda:len([d for d in worlds()['host']['drops'] if d['id'] not in before])==4,20):raise RuntimeError('Farm drop creation failed')
    farm=state('host')['scene']
    expected={d['id']:(d['item'],d['count'],farm) for d in worlds()['host']['drops'] if d['id'] not in before}
    transfer('guest3','Level_Farm_JLab_Facility')
    command('guest4','stop')
    if not wait_for(lambda:state('host').get('peers')==2,15):raise RuntimeError('Test peer did not disconnect')
    before={d['id'] for d in worlds()['host']['drops']}
    command('host','dropItem',id=140,count=47)
    if not wait_for(lambda:any(d['id'] not in before for d in worlds()['host']['drops']),15):raise RuntimeError('Offline-period drop missing on host')
    lab=state('host')['scene']
    expected.update({d['id']:(d['item'],d['count'],lab) for d in worlds()['host']['drops'] if d['id'] not in before})
    command('guest4','join',transport='Direct',port=19053)
    joined=wait_for(group.peers_ready,45)
    restored=joined and wait_for(lambda:check(expected,lab)[0],25)
    valid,snapshots=check(expected,lab)
    record({'category':'four_player_drop_rejoin_scene_ownership','pass':restored and valid,'expected':expected,'worlds':snapshots})
    if not restored or not valid:raise RuntimeError('Drop rejoin lost items, scene ownership or unique physical agents')
    transfer('guest4',farm)
    returned=wait_for(lambda:check(expected,farm)[0],20)
    valid,snapshots=check(expected,farm)
    record({'category':'four_player_drop_rejoin_return','pass':returned and valid,'worlds':snapshots})
    if not returned or not valid:raise RuntimeError('Restored drops did not reactivate in their own scene')
    ident=next(ident for ident,value in expected.items() if value[2]==farm)
    command('guest4','pickupDrop',id=ident)
    picked=wait_for(lambda:all(not any(d['id']==ident for d in w['drops']) for w in worlds().values()),20)
    record({'category':'four_player_drop_rejoin_pickup','pass':picked,'id':ident})
    if not picked:raise RuntimeError('Rejoined client pickup did not remove every mirror')
    outcome=maps.leave('client')
    record({'category':'four_player_drop_rejoin_exit','pass':outcome['pass'],'result':outcome})
    if not outcome['pass']:raise RuntimeError('Drop-rejoin native exit failed')

if __name__=='__main__':main()
