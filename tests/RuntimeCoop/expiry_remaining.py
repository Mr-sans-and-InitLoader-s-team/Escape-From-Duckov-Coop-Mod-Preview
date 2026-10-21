import json
import time
from driver import ROOT,command,state,wait_for,buff_ids
from matrix import profile,record,owner_expired,mirror_cleared

rows=[json.loads(line) for line in (ROOT/'live-matrix.jsonl').read_text(encoding='utf-8').splitlines() if line.strip()]
done={(r['profile'],r['role'],r['id']) for r in rows if r.get('category')=='buff_expiry'}
catalog=json.loads((ROOT/'buff-catalog.json').read_text(encoding='utf-8'))
for name,d,j,l in [('baseline',0,0,0),('rtt300_loss5',150,50,5),('rtt800_loss10',400,100,10)]:
    profile(name,d,j,l)
    for role,other in [('host','client'),('client','host')]:
        command(role,'invincible',enabled=True)
        for b in catalog:
            bid=b['id']
            if not b['timed'] or (name,role,bid) in done:continue
            source=command(role,'addBuff',id=bid)
            if bid not in buff_ids(source):
                record({'category':'buff_expiry','profile':name,'role':role,'id':bid,'outcome':'native_not_added'});continue
            result=command(role,'expireBuff',id=bid)
            if not result['expired']:
                record({'category':'buff_expiry','profile':name,'role':role,'id':bid,'outcome':result['reason']});continue
            passed=wait_for(lambda:owner_expired(role,bid) and mirror_cleared(other,bid),12)
            record({'category':'buff_expiry','profile':name,'role':role,'id':bid,'pass':passed})
profile('baseline',0,0,0)
