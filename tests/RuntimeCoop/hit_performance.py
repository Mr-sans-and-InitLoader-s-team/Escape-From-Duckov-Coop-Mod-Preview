"""Continuous native DamageReceiver hits with scoped CPU and allocation measurements."""
import json,time,sys
from driver import ROOT,command,state,wait_for
from matrix import profile,record
import multiplayer as group
import multiplayer_combat as combat
from ai_matrix import ai_state

def main():
    group.ROLES=group.ALL
    if '--resume' not in sys.argv:group.setup()
    selected=combat.setup();aid=selected['id']
    command('host','combatTargetHealth',ai=aid,hp=1000000)
    if not wait_for(lambda:all(ai_state(r,aid) and ai_state(r,aid)['max']>999999 for r in group.ALL),25):
        raise RuntimeError('High-HP native target did not converge')
    for rate in [0,30,120]:
        before=ai_state('host',aid)['hp']
        group.parallel('combatProfileBegin')
        hit=command('client','combatHitStream',ai=aid,seconds=12,rate=rate,timeout=90)
        time.sleep(3)
        profiles=group.parallel('combatProfileEnd')
        hp=ai_state('host',aid)['hp']
        converged=wait_for(lambda:all(ai_state(r,aid) and abs(ai_state(r,aid)['hp']-hp)<.02 for r in group.ALL),20)
        actor=group.remote('client','host')['id']
        ledger=next((e for e in profiles['host']['appliedDamage'] if e['actor']==actor and e['target']==selected['character']['id']),{'hits':0,'damage':0})
        damage_exact=ledger['hits']==hit['hits'] and abs(ledger['damage']-hit['hits']*5)<.02
        record({'category':'continuous_client_ai_hit_performance','rate':rate,'hit':hit,'profiles':profiles,
            'host_hp':hp,'host_before':before,'ledger':ledger,'damage_exact':damage_exact,'converged':converged,
            'pass':converged and damage_exact and profiles['client']['hitMarkers']==hit['hits'] and (hit['hits']>rate*10 if rate else True)})
    (ROOT/'hit-performance-ready').write_text('4')

if __name__=='__main__':main()
