"""Recheck the failed 800ms portal route with native queue telemetry and isolated input."""
from driver import ROOT,command
from matrix import profile,record
import maps
import multiplayer as group
import additive_stress as stress

ROLES=group.ALL
maps.ROLES=group.ROLES=stress.ROLES=ROLES

def main():
    profile('baseline',0,0,0)
    entered=maps.enter('Level_Farm_Main','guest4')
    if not entered['loaded']:raise RuntimeError('Weak portal Farm entry failed')
    # Retain live drops through the repeated portal route and the final full unload.
    for index,r in enumerate(ROLES):command(r,'dropItem',id=140,count=31+index)
    indices={e['id']:e['index'] for e in command('host','sceneCatalog')}
    profile('rtt800_loss10',400,100,10)
    for trip in range(3):
        for leg,destination in [('enter','Level_Farm_JLab_Facility'),('return','Level_Farm_01')]:
            source=ROLES[(trip+(leg=='return'))%4]
            portal=next(p for p in command(source,'teleporterCatalog') if p['target']==destination)
            outcome=stress.transfer(source,portal,indices[destination])
            record({'category':'weak_portal_input_isolated_recheck','profile':'rtt800_loss10','trip':trip,'leg':leg,
                'target':destination,'initiator':source,**outcome,'queues':{r:command(r,'transportDiagnostics') for r in ROLES}})
            if not outcome['pass']:raise RuntimeError('Weak portal recheck failed; preserve live queues for diagnosis')
    profile('baseline',0,0,0)
    outcome=maps.leave('client');record({'category':'weak_portal_recheck_exit','pass':outcome['pass'],'result':outcome})
    if not outcome['pass']:raise RuntimeError('Weak portal exit failed')

if __name__=='__main__':main()
