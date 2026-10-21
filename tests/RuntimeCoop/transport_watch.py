"""Record native reliable send queues without changing transport behavior."""
import json,time
from concurrent.futures import ThreadPoolExecutor
from driver import ROOT,command,state

ROLES=['host','client','guest3','guest4']

def sample(role):
    try:return {'state':{k:state(role).get(k) for k in ['time','scene','loading','blackScreen','peers']},
        'queues':command(role,'transportDiagnostics',timeout=15)}
    except Exception as exc:return {'error':str(exc)[:500]}

def main():
    while not (ROOT/'stop-transport-watch').exists():
        with ThreadPoolExecutor(max_workers=4) as pool:values=dict(zip(ROLES,pool.map(sample,ROLES)))
        try:profile=json.loads((ROOT/'network-profile.json').read_text(encoding='utf-8'))
        except (OSError,ValueError):profile={}
        with (ROOT/'transport-queues.jsonl').open('a',encoding='utf-8') as stream:
            stream.write(json.dumps({'utc':time.time(),'profile':profile,'roles':values})+'\n')
        time.sleep(2)

if __name__=='__main__':main()
