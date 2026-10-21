import json
import time
from driver import command, state, wait_for, ROOT
from region_survey import parallel, ROLES

out = ROOT.parent / 'ai-bullet-storm' / 'acceptance.json'
results = {}
results['nativeChecks'] = parallel('aiFireStress', verify=True)
assert all(all(checks.values()) for checks in results['nativeChecks'].values())
assert wait_for(lambda: command(ROLES[1], 'aiFireStress')['fakes'] == 0, 15)
parallel('combatProfileBegin')
results['target'] = command(ROLES[1], 'combatTargetHealth', hp=100000)
time.sleep(1)
before = state(ROLES[1])['local']['hp']
results['firing'] = command(ROLES[0], 'aiFireStress', seconds=4, count=16, rate=4)
time.sleep(1)
results['damageProfiles'] = parallel('combatProfileEnd')
after = state(ROLES[1])['local']['hp']
matched = wait_for(lambda: abs(state(ROLES[0])['remotes'][0]['character']['hp']-state(ROLES[1])['local']['hp'])<.1, 5)
results['damage'] = dict(before=before, after=after, applied=after<before, mirrorMatched=matched)
assert after<before and matched, results['damage']
assert wait_for(lambda: command(ROLES[1], 'aiFireStress')['fakes'] == 0, 15)
results['visualExplosion'] = command(ROLES[1], 'fakeExplosion')
v = results['visualExplosion']
assert v['before']==v['after'] and v['contextDamage']==v['contextExplosion']==v['contextBleed']==0, v
command(ROLES[1], 'combatTargetHealth', hp=107)
parallel('invincible', enabled=True)
results['transport'] = parallel('transportDiagnostics')
results['states'] = {r:state(r) for r in ROLES}
out.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
print('PASS native checks',sum(len(c) for c in results['nativeChecks'].values()),'damage',results['damage'],'visual explosion',v,flush=True)
