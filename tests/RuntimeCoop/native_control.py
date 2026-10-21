"""Four isolated native players without the co-op assembly: transition control."""
import time
from driver import ROOT,command,state,wait_for
from matrix import record
import multiplayer as group

ROLES=group.ALL
group.ROLES=ROLES

def main():
    if not wait_for(lambda:all(state(r).get('time',99999)<180 for r in ROLES),120):raise RuntimeError('Native control did not start')
    group.parallel('loadBase',timeout=180)
    if not wait_for(lambda:all(state(r).get('initialized') and state(r).get('local') for r in ROLES),180):raise RuntimeError('Native control base load failed')
    unloaded=all(not state(r).get('coopAssemblyLoaded',True) for r in ROLES)
    record({'category':'four_player_native_control_no_mod','pass':unloaded,'states':{r:state(r) for r in ROLES}})
    if not unloaded:raise RuntimeError('Co-op assembly unexpectedly present in native control')
    for scene in ['Level_Farm_Main','Level_SnowMilitaryBase_Main']:
        started=time.monotonic();group.parallel('loadScene',scene=scene,timeout=180)
        entered=wait_for(lambda:all(state(r).get('initialized') and state(r).get('raid') and not state(r).get('blackScreen') for r in ROLES),180)
        group.parallel('invincible',enabled=True)
        areas={r:next(a for a in command(r,'exitCatalog') if a['active'] and a['enabled'] and any(p['target']=='Base' for p in a['proxies'])) for r in ROLES}
        for r,a in areas.items():command(r,'enterExit',id=a['id'])
        closure=wait_for(lambda:all((state(r).get('closure') or {}).get('canContinue') for r in ROLES),45)
        if closure:group.parallel('continue')
        returned=wait_for(lambda:all(state(r).get('initialized') and not state(r).get('raid') and not state(r).get('blackScreen') and not state(r).get('loading') for r in ROLES),180)
        record({'category':'four_native_no_mod_evacuation','scene':scene,'entry':entered,'closure':closure,'returned':returned,
            'seconds':round(time.monotonic()-started,1),'pass':entered and closure and returned,'states':{r:state(r) for r in ROLES}})
        if not returned:return
    (ROOT/'native-control-ready').write_text('4')

if __name__=='__main__':main()
