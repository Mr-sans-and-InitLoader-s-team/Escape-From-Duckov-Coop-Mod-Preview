"""Continue the four-player portal run after correcting shared player/AI tag classification."""
import json
from driver import ROOT,command,state
from matrix import profile,record
import maps
import additive_stress as stress

ROLES=['host','client','guest3','guest4']
maps.ROLES=stress.ROLES=ROLES

def main():
    verified=stress.inspect_world()
    record({'category':'additive_scene_shared_tag_validation','profile':'baseline','trip':0,'leg':'enter',
        'pass':verified['pass'],'world':verified,'scope':'AI replicas also use RemoteReplicaTag; identified AI characters are excluded from player count'})
    if not verified['pass']:raise RuntimeError('Facility registry failed after shared-tag correction')
    catalog=command('host','sceneCatalog');indices={e['id']:e['index'] for e in catalog}
    original='Level_Farm_01';target='Level_Farm_JLab_Facility'
    for name,d,j,l in [('baseline',0,0,0),('rtt100_loss1',50,15,1),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        for trip in range(3):
            for leg,destination in [('enter',target),('return',original)]:
                if name=='baseline' and trip==0 and leg=='enter':continue
                source=ROLES[(trip+(leg=='return'))%len(ROLES)]
                portal=next((p for p in command(source,'teleporterCatalog') if p['target']==destination),None)
                if not portal:raise RuntimeError('Native portal missing: '+destination)
                outcome=stress.transfer(source,portal,indices[destination])
                record({'category':'additive_scene_stress','profile':name,'trip':trip,'leg':leg,'initiator':source,'target':destination,**outcome})
                if not outcome['pass']:return
                if name=='baseline' and trip==0 and leg=='return':
                    world={r:command(r,'worldSnapshot') for r in ROLES}
                    record({'category':'four_player_world_after_portal_roundtrip','worlds':world})
    profile('baseline',0,0,0)
    exit_result=maps.leave('client')
    record({'category':'additive_stress_exit','result':exit_result,'pass':exit_result['pass']})
    if exit_result['pass']:(ROOT/'four-player-world-ready').write_text('4')

if __name__=='__main__':main()
