"""Force the host warehouse to become ready after all guests return to Base."""
import multiplayer as g
import maps
from driver import command,wait_for
from shared_home_acceptance import snapshots,emit
from shared_home_loadouts import joined
from matrix import profile

g.ROLES=maps.ROLES=g.ALL
profile('baseline',0,0,0)
g.parallel('loadMod',g.ALL)
g.parallel('loadBase',g.ALL,timeout=180)
g.parallel('sharedHome',g.ALL,compatibilityNative=True)
command('host','sharedHome',enabled=True)
command('host','host',transport='Direct',port=19050)
for r in g.ALL[1:]:command(r,'join',transport='Direct',port=g.PORTS[r])
emit('final_build_four_player_validated_join',wait_for(joined,60))
emit('initial_warehouse_automatic_snapshot',wait_for(lambda:all(v['storage']==snapshots()['host']['storage'] for v in snapshots().values()),20))
entry=maps.enter('Level_Farm_Main','client')
emit('warehouse_regression_native_raid_entry',entry['loaded'])
command('host','sharedHome',holdStorage=True)
result=maps.leave('guest4')
emit('warehouse_regression_four_player_native_evacuation',result['pass'],result=result)
command('host','sharedHome',storageSeed=13,count=1)
emit('test_fixture_host_storage_is_deliberately_late',any(v['storage']!=snapshots()['host']['storage'] for r,v in snapshots().items() if r!='host'))
command('host','sharedHome',holdStorage=False)
emit('late_host_warehouse_ready_pushes_to_all_guests_without_opening_ui',wait_for(lambda:all(v['storage']==snapshots()['host']['storage'] for v in snapshots().values()),25))
print('Late host warehouse return suite complete',flush=True)
