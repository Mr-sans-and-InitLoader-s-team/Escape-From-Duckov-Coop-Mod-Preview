import json
from driver import command,wait_for,ROOT
from map_region_audit import transfer

def emit(case,passed):
    row=dict(phase='release-shared-portal',case=case,passed=bool(passed))
    with (ROOT.parent/'region-sync/acceptance.jsonl').open('a',encoding='utf-8') as f:f.write(json.dumps(row)+'\n')
    print(case,bool(passed),flush=True)
    assert passed,case

def red():
    return [p for p in command('publicclient','regions')['markers'] if 'POIBoss_Red' in p['path']]

assert red()
transfer('Level_Farm_JLab_Facility',None,12)
emit('native_area_hidden_with_original_subscene',wait_for(lambda:bool(red()) and all(not p['active'] for p in red()),15))
emit('hidden_native_area_unregistered',not any('POIBoss_Red' in p['path'] for p in command('publicclient','regions')['areaRegistrations']))
transfer('Level_Farm_01',None,10)
emit('native_area_restored_after_return',wait_for(lambda:bool(red()) and all(p['active'] for p in red()),15))
emit('restored_native_area_has_one_registration',all(p['count']==1 for p in command('publicclient','regions')['areaRegistrations']))
