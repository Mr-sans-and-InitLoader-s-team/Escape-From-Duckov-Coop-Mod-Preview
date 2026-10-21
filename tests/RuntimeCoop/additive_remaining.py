"""Resume the pending weak-network vote, retaining completed portal coverage."""
import time
from driver import ROOT,command,state,wait_for
from matrix import profile,record
import maps
import additive_stress as stress

ROLES=['host','client','guest3','guest4']
maps.ROLES=stress.ROLES=ROLES

def main():
    indices={e['id']:e['index'] for e in command('host','sceneCatalog')}
    voted=maps.ready_vote(60)
    loaded=wait_for(lambda:all(state(r).get('scene')=='Level_Farm_01' and not state(r).get('blackScreen') for r in ROLES),120)
    time.sleep(5)
    world=stress.inspect_world()
    record({'category':'additive_pending_vote_resume','profile':'rtt300_loss5','voted':voted,'loaded':loaded,
        'pass':voted and loaded and world['pass'],'world':world,
        'scope':'Original 15-second vote propagation deadline expired; all four later received the pending vote'})
    if not(voted and loaded and world['pass']):return
    for name,d,j,l,trips in [('rtt300_loss5',150,50,5,range(1,3)),('rtt800_loss10',400,100,10,range(3))]:
        profile(name,d,j,l)
        for trip in trips:
            for leg,destination in [('enter','Level_Farm_JLab_Facility'),('return','Level_Farm_01')]:
                source=ROLES[(trip+(leg=='return'))%len(ROLES)]
                portal=next(p for p in command(source,'teleporterCatalog') if p['target']==destination)
                outcome=stress.transfer(source,portal,indices[destination])
                record({'category':'additive_scene_stress','profile':name,'trip':trip,'leg':leg,'initiator':source,'target':destination,**outcome})
                if not outcome['pass']:return
    profile('baseline',0,0,0)
    outcome=maps.leave('client')
    record({'category':'additive_stress_exit','result':outcome,'pass':outcome['pass']})
    if outcome['pass']:(ROOT/'four-player-world-ready').write_text('4')

if __name__=='__main__':main()
