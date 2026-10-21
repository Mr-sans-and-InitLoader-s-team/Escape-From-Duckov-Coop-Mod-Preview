"""Targeted persistence acceptance after the sub-scene drop-registry repair."""
import json,time
from driver import ROOT,command,state,wait_for
from matrix import profile,record
import maps
import multiplayer as group
import additive_stress as stress

ROLES=group.ALL
maps.ROLES=group.ROLES=stress.ROLES=ROLES

def worlds():return {r:command(r,'worldSnapshot') for r in ROLES}
def boxes(role):return command(role,'worldInventories')
def fingerprint(box):return json.dumps(box['slots'],sort_keys=True)

def transfer(source,destination):
    indices={e['id']:e['index'] for e in command(source,'sceneCatalog')}
    portal=next(p for p in command(source,'teleporterCatalog') if p['target']==destination)
    outcome=stress.transfer(source,portal,indices[destination])
    record({'category':'world_persistence_portal','target':destination,**outcome})
    if not outcome['pass']:raise RuntimeError('Persistence portal failed')

def main():
    profile('baseline',0,0,0)
    entered=maps.enter('Level_Farm_Main','guest3')
    if not entered['loaded']:raise RuntimeError('Persistence Farm entry failed')
    command('host','pauseAI',enabled=True)
    catalogs={r:command(r,'worldCatalog') for r in ROLES}
    door=next(d for d in catalogs['host']['doors'] if d['active'] and d['unlocked'] and
        all(any(e['key']==d['key'] for e in catalogs[r]['doors']) for r in ROLES))
    command('guest4','worldAction',kind='door',key=door['key'],open=True)
    target=next(d for d in catalogs['host']['destructibles'] if d['id'] and d['active'] and d['hp']>10 and
        all(any(e['id']==d['id'] for e in catalogs[r]['destructibles']) for r in ROLES))
    command('client','worldAction',kind='health',id=target['id'],amount=999999,explosion=True)
    before={d['id'] for d in worlds()['host']['drops']}
    for index,r in enumerate(ROLES):command(r,'dropItem',id=140,count=21+index)
    if not wait_for(lambda:len([d for d in worlds()['host']['drops'] if d['id'] not in before])==4,20):raise RuntimeError('Persistence drop creation failed')
    expected={d['id']:(d['item'],d['count']) for d in worlds()['host']['drops'] if d['id'] not in before}
    host_boxes=boxes('host')
    loot=next((b for b in host_boxes if b['uid']<0 and b['slots'] and all(any(e['key']==b['key'] for e in boxes(r)) for r in ROLES[1:])),None)
    remainder=None
    if loot:
        command('client','openCorpse',key=loot['key'])
        if not wait_for(lambda:any(b['key']==loot['key'] and fingerprint(b)==fingerprint(loot) for b in boxes('client')),15):raise RuntimeError('World loot initial snapshot mismatch')
        slot=next((s for s in loot['slots'] if s['item']['count']>1),loot['slots'][0])
        command('client','splitCorpse' if slot['item']['count']>1 else 'takeCorpse',key=loot['key'],slot=slot['slot'],**({'count':1} if slot['item']['count']>1 else {}))
        if not wait_for(lambda:any(b['key']==loot['key'] and fingerprint(b)!=fingerprint(loot) for b in boxes('host')),15):raise RuntimeError('World loot mutation did not reach host')
        remainder=next(b for b in boxes('host') if b['key']==loot['key'])
    transfer('guest4','Level_Farm_JLab_Facility')
    hidden=worlds()
    transfer('guest3','Level_Farm_01')
    returned=worlds();catalogs={r:command(r,'worldCatalog') for r in ROLES}
    drop_ok=all(all(any(d['id']==ident and (d['item'],d['count'])==value for d in returned[r]['drops']) for ident,value in expected.items()) for r in ROLES)
    physical_ok=all(all(len([d for d in returned[r]['physicalDrops'] if d['id']==ident and d['active'] and (d['item'],d['count'])==value])==1 for ident,value in expected.items()) for r in ROLES)
    door_ok=all(any(d['key']==door['key'] and d['open'] for d in catalogs[r]['doors']) for r in ROLES)
    broken_ok=all(not any(d['id']==target['id'] and d['active'] and d['hp']>0 for d in catalogs[r]['destructibles']) for r in ROLES)
    loot_ok=None
    if remainder:
        for r in ROLES:command(r,'openCorpse',key=loot['key'])
        loot_ok=wait_for(lambda:all(any(b['key']==loot['key'] and fingerprint(b)==fingerprint(remainder) for b in boxes(r)) for r in ROLES),20)
    record({'category':'four_player_world_persistence','pass':drop_ok and physical_ok and door_ok and broken_ok and loot_ok is not False,
        'drops':drop_ok,'physical_once':physical_ok,'door':door_ok,'broken':broken_ok,'partial_loot':loot_ok,
        'expected_drops':expected,'loot_before':loot,'loot_remaining':remainder,'hidden':hidden,'returned':returned})
    ident=next(iter(expected));command('client','pickupDrop',id=ident)
    picked=wait_for(lambda:all(not any(d['id']==ident for d in w['drops']) for w in worlds().values()),20)
    record({'category':'four_player_pickup_after_subscene_return','pass':picked,'id':ident})
    outcome=maps.leave('client');record({'category':'world_persistence_exit','pass':outcome['pass'],'result':outcome})
    if drop_ok and physical_ok and door_ok and broken_ok and loot_ok is not False and picked and outcome['pass']:
        (ROOT/'four-player-world-persistence-ready').write_text('4')

if __name__=='__main__':main()
