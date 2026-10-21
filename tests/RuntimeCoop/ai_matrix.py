import json
import time
from driver import ROOT,command,state,wait_for,buff_ids
from matrix import profile,record

def target(): return json.loads((ROOT/'ai-target.json').read_text())['id']
def ai_state(role,ident):
    return next((e.get('character') for e in command(role,'aiSnapshot') if e['id']==ident),None)
def layers(role,ident,buff): return buff_ids(ai_state(role,ident)).get(buff,0)

def layer_matrix():
    ident=target()
    command('host','invincible',ai=ident,enabled=True)
    for name,d,j,l in [('baseline',0,0,0),('rtt100_loss1',50,15,1),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
        profile(name,d,j,l)
        for source in ['host','client']:
            command('host','removeBuff',ai=ident,id=1002)
            wait_for(lambda:layers('client',ident,1002)==0,8)
            for _ in range(3):command(source,'addBuff',ai=ident,id=1002)
            three=wait_for(lambda:layers('host',ident,1002)==3 and layers('client',ident,1002)==3,8)
            command('host','removeBuff',ai=ident,id=1002,one=True)
            two=wait_for(lambda:layers('host',ident,1002)==2 and layers('client',ident,1002)==2,8)
            command('host','removeBuff',ai=ident,id=1002)
            zero=wait_for(lambda:layers('client',ident,1002)==0,8)
            record({'category':'ai_buff_layers','profile':name,'source':source,'three':three,'two':two,'zero':zero,'pass':three and two and zero})
    profile('baseline',0,0,0)

def buffs():
    ident=target()
    command('host','invincible',ai=ident,enabled=True)
    catalog=command('host','buffCatalog')
    for source in ['host','client']:
        for buff in catalog:
            bid=buff['id']
            command('host','removeBuff',ai=ident,id=bid)
            wait_for(lambda:layers('client',ident,bid)==0,5)
            added=command(source,'addBuff',ai=ident,id=bid)
            native_added=bid in buff_ids(added)
            synced=wait_for(lambda:layers('host',ident,bid)>0 and layers('client',ident,bid)>0,3) if native_added else False
            owner=ai_state('host',ident)
            command('host','removeBuff',ai=ident,id=bid)
            cleared=wait_for(lambda:layers('client',ident,bid)==0,5)
            record({'category':'ai_buff_catalog','source':source,**buff,'native_added':native_added,'mirror_added':synced,
                    'host_active_before_clear':bid in buff_ids(owner),'terminal_clear':cleared,'pass':cleared})

if __name__=='__main__':
    import sys
    {'layers':layer_matrix,'buffs':buffs}[sys.argv[1]]()
