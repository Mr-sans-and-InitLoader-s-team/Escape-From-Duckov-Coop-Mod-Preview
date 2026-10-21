"""Observe reported loot/door symptoms before changing the production build."""
import json,time
from driver import ROOT,command,state,wait_for
from matrix import record
import multiplayer as group

group.ROLES=group.ALL
OUT=ROOT.parent/'loot-door-fixes'
def loot(role,uid):return next((b for b in command(role,'lootInspection',uid=uid) if b['uid']==uid),None)
def door(role,key):return next(d for d in command(role,'worldCatalog')['doors'] if d['key']==key)
def main():
    group.parallel('invincible',enabled=True)
    catalog=command('client','interactionCatalog')
    target=next(i for i in catalog if 'SingleDirectionDoor/Built/DoorInteract' in i['path'])
    key=next(d['key'] for d in command('client','worldCatalog')['doors'] if abs(d['position'][0]-332.57)<2 and abs(d['position'][2]-112.02)<2)
    q=target['position']
    for proximity in ['near','far']:
        command('host','safeMove',x=q[0]+(1 if proximity=='near' else 110),y=q[1],z=q[2])
        command('client','safeMove',x=q[0]+1,y=q[1],z=q[2])
        command('host','worldAction',kind='door',key=key,open=False)
        command('client','nativeInteraction',instance=target['instance'])
        time.sleep(4)
        record({'category':'boarded_door_before','hostProximity':proximity,'states':{r:door(r,key) for r in group.ALL},'target':target})
    snapshots=command('host','aiSnapshot')
    ai=next(e for e in snapshots if e.get('character') and not e['character']['dead'])
    uids={b['uid'] for b in command('host','corpseInventory')}
    command('host','damage',ai=ai['id'],amount=99999)
    if not wait_for(lambda:any(b['uid'] not in uids for b in command('host','corpseInventory')),15):raise RuntimeError('Native AI corpse absent')
    uid=next(b['uid'] for b in command('host','corpseInventory') if b['uid'] not in uids)
    wait_for(lambda:loot('client',uid) is not None,15)
    command('client','openCorpse',uid=uid)
    wait_for(lambda:bool(loot('client',uid)['items']),15)
    record({'category':'enemy_corpse_inspection_before','states':{r:loot(r,uid) for r in group.ALL}})
    before={b['uid'] for b in command('host','corpseInventory')}
    command('guest3','give',id=140,count=37)
    command('guest3','invincible',enabled=False)
    time.sleep(17)
    command('guest3','damage',amount=99999)
    if not wait_for(lambda:any(b['uid'] not in before for b in command('host','corpseInventory')),20):raise RuntimeError('Native client corpse absent')
    uid=next(b['uid'] for b in command('host','corpseInventory') if b['uid'] not in before)
    wait_for(lambda:loot('guest4',uid) is not None,15)
    command('guest4','openCorpse',uid=uid)
    wait_for(lambda:bool(loot('guest4',uid)['items']),15)
    record({'category':'client_to_client_corpse_before','states':{r:loot(r,uid) for r in group.ALL}})
    command('guest4','nativeInteraction',uid=uid)
    time.sleep(12)
    record({'category':'client_to_client_native_search_before','states':{r:loot(r,uid) for r in group.ALL}})
    command('guest4','nativeInteraction',pickAll=True)
    time.sleep(4)
    record({'category':'client_to_client_native_take_before','states':{r:loot(r,uid) for r in group.ALL}})
    (OUT/'baseline-probe-ready').write_text('ready')

if __name__=='__main__':main()
