import json
import time
from driver import ROOT, command, state, wait_for

AI = -1874606615

def incoming():
    rows=[]
    for role in ['host','client']:
        for label,extra in [('normal',{}),('unarmored',{'ignoreArmor':True}),('crit',{'critRate':1,'critFactor':2}),('fire',{'element':1}),('explosion',{'explosion':True})]:
            command(role,'invincible',enabled=False)
            command(role,'heal',hp=100)
            wait_for(lambda:abs(state(role)['local']['hp']-100)<.01,3)
            before=state(role)['local']
            hit=command('host','aiAttack',ai=AI,remote=role=='client',amount=10,**extra)
            changed=wait_for(lambda:state(role)['local']['hp']<99.9,5)
            time.sleep(.5)
            after=state(role)['local']
            other='client' if role=='host' else 'host'
            synced=wait_for(lambda:any(abs(e['character']['hp']-after['hp'])<.01 for e in state(other)['remotes']),5)
            row={'role':role,'case':label,'before':before,'hit':hit,'after':after,'changed':changed,'mirror_matched':synced}
            rows.append(row)
            print(json.dumps(row),flush=True)
            command(role,'invincible',enabled=True)
    (ROOT/'ai-incoming-verified.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')

if __name__=='__main__': incoming()
