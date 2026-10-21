from types import SimpleNamespace
import shared_storage_operations as storage
from region_survey import maps
from driver import command

storage.ARGS=SimpleNamespace(port=19051)
storage.setup()
for role in storage.ROLES:command(role,'nativeInteraction',close=True)
result=maps.enter('Level_Farm_Main','publichost')
print('shared farm entry',result['voted'],result['loaded'],flush=True)
assert result['loaded']
