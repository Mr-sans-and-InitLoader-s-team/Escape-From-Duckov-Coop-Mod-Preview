"""Exercise native extraction and fresh warehouse initialization on base return."""
import maps
import shared_storage_operations as t
from shared_storage_loading import emit
from driver import command, state, wait_for

ROLES = t.ROLES
ALIASES = dict(host=ROLES[0], client=ROLES[1])
maps.ROLES = ROLES
maps.command = lambda role, *args, **kwargs: command(ALIASES.get(role, role), *args, **kwargs)
maps.state = lambda role: state(ALIASES.get(role, role))

for scene in ['Level_GroundZero_Main', 'Level_Farm_Main']:
    before = command(ROLES[0], 'sharedStorage')['items']
    entered = maps.enter(scene, ROLES[1])
    emit(scene + '_shared_native_entry', entered['voted'] and entered['loaded'], result=entered)
    returned = maps.leave(ROLES[1])
    emit(scene + '_native_extraction_returns_without_black_screen', returned['pass'], result=returned)
    emit(scene + '_warehouse_returns_without_item_transfers', wait_for(t.settled, 30) and
         all(command(r, 'sharedStorage')['items'] == before for r in ROLES), before=before, after=t.snapshots())
