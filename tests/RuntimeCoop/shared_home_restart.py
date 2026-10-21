"""Host-owned progress and per-player gear survive a real game-process restart."""
import json
import multiplayer as g
from driver import command,wait_for
from shared_home_acceptance import snapshots,emit,OUT
from shared_home_loadouts import tally,joined
from matrix import profile

g.ROLES=g.ALL
profile('baseline',0,0,0)
g.parallel('loadMod',g.ALL)
g.parallel('loadBase',g.ALL,timeout=180)
g.parallel('sharedHome',g.ALL,compatibilityNative=True)
print('native_manifests',json.dumps(g.parallel('sharedHome',g.ALL,compatibilityState=True),ensure_ascii=False),flush=True)
command('host','sharedHome',enabled=True)
command('host','host',transport='Direct',port=19050)
for r in g.ALL[1:]:command(r,'join',transport='Direct',port=g.PORTS[r])
emit('r11_validated_four_players_after_process_restart',wait_for(joined,60))
expected=json.loads((OUT/'restart-expected.json').read_text(encoding='utf-8-sig'))
values=snapshots()
emit('r11_host_progress_persists_after_process_restart',all(v['xp']==expected['xp'] and v['unlocks']==expected['unlocks'] for v in values.values()))
emit('r11_each_players_gear_persists_after_process_restart',all(tally(values[r]['inventory'])=={int(k):v for k,v in items.items()} for r,items in expected['inventory'].items()))
for r in g.ALL[1:]:command(r,'stop')
command('host','stop')
print('Persistence suite complete',flush=True)
