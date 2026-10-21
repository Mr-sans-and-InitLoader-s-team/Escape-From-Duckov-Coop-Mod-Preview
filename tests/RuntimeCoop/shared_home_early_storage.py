"""A shared warehouse packet cannot touch a guest's warehouse before personal backup."""
import time
import multiplayer as g
from driver import command,wait_for
from shared_home_acceptance import snapshots,emit
from shared_home_loadouts import joined
from matrix import profile

g.ROLES=g.ALL
profile('baseline',0,0,0)
g.parallel('loadMod',g.ALL)
g.parallel('loadBase',g.ALL,timeout=180)
g.parallel('sharedHome',g.ALL,compatibilityNative=True)
original=snapshots()
command('host','sharedHome',enabled=True)
command('host','host',transport='Direct',port=19050)
command('host','sharedHome',delayCapture=30)
for r in g.ALL[1:]:command(r,'join',transport='Direct',port=g.PORTS[r])
emit('early_storage_all_guests_validated_before_overlay',wait_for(lambda:all(command(r,'sharedHome',compatibilityState=True)['ready'] for r in g.ALL[1:]),15))
emit('fixture_overlay_not_started',all(not v['overlay'] for r,v in snapshots().items() if r!='host'))
command('host','sharedHome',earlyStorageSnapshot=True)
time.sleep(2)
emit('early_warehouse_packet_preserves_personal_inventory',all(v['storage']==original[r]['storage'] for r,v in snapshots().items() if r!='host'))
command('host','sharedHome',delayCapture=0)
emit('overlay_then_shared_warehouse_become_ready',wait_for(joined,30) and wait_for(lambda:all(v['storage']==snapshots()['host']['storage'] for v in snapshots().values()),20))
for r in g.ALL[1:]:command(r,'stop')
emit('personal_warehouse_restored_after_early_packet_test',wait_for(lambda:all(command(r,'sharedHome')['storage']==original[r]['storage'] for r in g.ALL[1:]),20))
command('host','stop')
print('Early warehouse packet suite complete',flush=True)
