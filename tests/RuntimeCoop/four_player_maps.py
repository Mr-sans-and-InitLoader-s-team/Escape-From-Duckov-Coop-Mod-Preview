"""Built map families; record conditional exits honestly instead of forcing their callbacks."""
import time
from driver import ROOT,command,state,wait_for
from matrix import profile,record
import maps
import multiplayer as group

ROLES=group.ALL
maps.ROLES=ROLES
group.ROLES=ROLES

def rescue(initiator):
    command(initiator,'loadScene',scene='Base',timeout=180)
    voted=maps.ready_vote()
    loaded=wait_for(lambda:all(not state(r).get('raid') and state(r).get('initialized') and not state(r).get('blackScreen') for r in ROLES),180)
    record({'category':'conditional_map_test_recovery','initiator':initiator,'voted':voted,'returned':loaded,
        'scope':'Return to base to continue the suite; does not validate the map evacuation gate'})
    return loaded

def main(scenes=None):
    profile('baseline',0,0,0)
    catalog=command('host','sceneCatalog')
    if scenes is None:
        scenes=[e['id'] for e in catalog if e['index']>=0 and e['id'].startswith('Level_') and e['id'].endswith('_Main')
            and e['id'] not in {'Level_GroundZero_Main','Level_Farm_Main'}]
    for index,scene in enumerate(scenes):
        initiator=ROLES[1+index%3]
        started=time.monotonic()
        entry=maps.enter(scene,initiator)
        if not entry['loaded']:
            record({'category':'four_player_map_evacuation','scene':scene,'initiator':initiator,'pass':False,'outcome':'entry_timeout','entry':entry})
            return
        command('host','pauseAI',enabled=True)
        result=maps.leave(initiator)
        record({'category':'four_player_map_evacuation','scene':scene,'initiator':initiator,'entry':entry,'exit':result,
            'pass':entry['voted'] and result['pass'],'seconds':round(time.monotonic()-started,1)})
        if not result.get('returned'):
            if result.get('outcome')=='no_active_countdown_to_base':
                if not rescue(initiator):return
            else:return
    (ROOT/'four-player-maps-ready').write_text('4')

if __name__=='__main__':main()
