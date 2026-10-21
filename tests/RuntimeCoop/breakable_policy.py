"""Check native saved/unsaved breakable behavior without imposing new game rules."""
from driver import ROOT,command,state,wait_for
from matrix import profile,record
import maps
import multiplayer as group
from world_persistence import transfer

ROLES=group.ALL
maps.ROLES=group.ROLES=ROLES

def main():
    profile('baseline',0,0,0)
    entered=maps.enter('Level_Farm_Main','guest3')
    if not entered['loaded']:raise RuntimeError('Breakable policy entry failed')
    catalogs={r:command(r,'worldCatalog') for r in ROLES}
    target=next(d for d in catalogs['host']['destructibles'] if d['id'] and d['active'] and d['hp']>10 and
        d['nativeSave'] is not None and all(any(e['id']==d['id'] for e in catalogs[r]['destructibles']) for r in ROLES))
    command('client','worldAction',kind='health',id=target['id'],amount=999999,explosion=True)
    transfer('guest4','Level_Farm_JLab_Facility');transfer('guest3','Level_Farm_01')
    returned={r:next((d for d in command(r,'worldCatalog')['destructibles'] if d['id']==target['id']),None) for r in ROLES}
    if target['nativeSave']:
        passed=all(not d or d['hp']<=0 or d['brokenVisual'] or d['mainCollider'] is False for d in returned.values())
    else:
        passed=all(d and abs(d['hp']-d['max'])<.02 and not d['brokenVisual'] and d['mainCollider'] is not False for d in returned.values())
    record({'category':'four_player_native_breakable_reload_policy','pass':passed,'target':target,'returned':returned,
        'scope':'Native Breakable.Awake restores only save=true objects; unsaved objects reset on sub-scene reload'})
    outcome=maps.leave('client');record({'category':'breakable_policy_exit','pass':outcome['pass'],'result':outcome})
    if not(passed and outcome['pass']):raise RuntimeError('Native breakable reload policy differs between peers')
    (ROOT/'staged-restart-ready').write_text('4')

if __name__=='__main__':main()
