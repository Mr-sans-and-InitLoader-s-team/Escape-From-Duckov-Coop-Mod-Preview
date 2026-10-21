"""Sample walkable navigation cells and eight native movement/aim directions on each map."""
import argparse
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor
import json
import math
import time
from driver import command, state, wait_for, ROOT
from region_survey import ROLES, capture, maps

OUT=ROOT.parent/'region-sync'


def write(row):
    with (OUT/'map-audit.jsonl').open('a',encoding='utf-8') as f:
        f.write(json.dumps(row,ensure_ascii=False)+'\n')


def survey(scene):
    command(ROLES[0],'pauseAI',enabled=False)
    for role in ROLES:
        command(role,'invincible',enabled=True)
        command(role,'nativeInteraction',close=True)
    catalog=command(ROLES[0],'mapSurvey',grid=80)
    cells=defaultdict(list)
    for p in catalog['anchors']:
        cells[(math.floor(p[0]/80),math.floor(p[2]/80))].append(p)
    walked=unsupported=0
    distance=0
    seen=set()
    for cell,points in sorted(cells.items()):
        for point in sorted(points,key=lambda p:p[1]):
            kwargs=dict(walk=True,x=point[0],y=point[1],z=point[2])
            try:
                with ThreadPoolExecutor(2) as pool:
                    result=dict(zip(ROLES,pool.map(lambda r:command(r,'mapSurvey',timeout=25,**kwargs),ROLES)))
            except RuntimeError as error:
                if 'No supported ground' not in str(error):raise
                continue
            if any(not value.get('supportedEnd',True) for value in result.values()):
                write(dict(kind='unsupported_walk',scene=scene,cell=cell,point=point,result=result))
                continue
            walked+=1
            distance+=sum(v['distance'] for v in result.values())
            seen.update(e['id'] for e in command(ROLES[1],'aiAudit')['replicas'] if e['activeInHierarchy'])
            write(dict(kind='walk_cell',scene=scene,cell=cell,point=point,result=result,
                       states={r:dict(initialized=state(r).get('initialized'),blackScreen=state(r).get('blackScreen'),cursor=state(r).get('cursor')) for r in ROLES}))
            break
        else:
            unsupported+=1
            write(dict(kind='unsupported_cell',scene=scene,cell=cell,candidates=points))
        if (walked+unsupported)%20==0:
            print(scene,'cells',walked+unsupported,'/',len(cells),'walked',walked,'unsupported',unsupported,flush=True)
    def missing_models():
        audit=command(ROLES[1],'aiAudit')
        actual={e['id'] for e in audit['replicas']}
        return [e for e in audit['entries'] if e['expectedReplica'] and e['id'] not in actual]
    initially_missing=missing_models()
    if initially_missing:wait_for(lambda:not missing_models(),10)
    snapshots={r:command(r,'aiAudit') for r in ROLES}
    expected=[e['id'] for e in snapshots[ROLES[1]]['entries'] if e['expectedReplica']]
    actual={e['id'] for e in snapshots[ROLES[1]]['replicas']}
    local=state(ROLES[1])['local']['position']
    server_expected=[e['id'] for e in snapshots[ROLES[0]]['entries'] if e['status']=='Active' and e['activated'] and
                     (e['model'] or e['preset']) and math.dist(local,[e['anchor'][v] for v in ['x','y','z']])<=
                     (140 if e['vehicle'] else snapshots[ROLES[1]]['activation'])]
    result=dict(kind='map_survey',scene=scene,navigationNodes=catalog['nodes'],grid=80,cells=len(cells),walked=walked,
                unsupported=unsupported,nativeWalkDistanceBothPeers=distance,missingCurrentModels=sorted(set(expected)-actual),
                seenAI=sorted(seen),
                initiallyMissingModels=initially_missing,
                unresolvedModels=missing_models(),
                serverModelsForMissing=[e for e in snapshots[ROLES[0]]['entries'] if e['id'] in set(expected)-actual],
                missingServerEligibleModels=sorted(set(server_expected)-actual),
                invalidGuns=snapshots[ROLES[1]]['invalidGuns'],hostAI=len(snapshots[ROLES[0]]['entries']),
                states={r:state(r) for r in ROLES})
    write(result)
    capture('map-'+scene)
    print('survey complete',scene,'walked',walked,'unsupported',unsupported,flush=True)


def transfer(scene,point,index):
    portal=next((p for p in command(ROLES[1],'teleporterCatalog') if p['target']==scene),None)
    if portal is None:
        write(dict(kind='additive_entry',scene=scene,outcome='no_current_native_teleporter'))
        return False
    command(ROLES[1],'teleport',id=portal['id'])
    voted=maps.ready_vote(30)
    loaded=wait_for(lambda:all(state(r).get('initialized') and state(r).get('sceneIndex')==index and
        not state(r).get('loading') and not state(r).get('blackScreen') and len(state(r).get('remotes',[]))==1 for r in ROLES),120)
    row=dict(kind='additive_entry',scene=scene,voted=voted,loaded=loaded,states={r:state(r) for r in ROLES})
    write(row)
    print('additive',scene,voted,loaded,flush=True)
    if not loaded:raise RuntimeError('Additive scene entry failed')
    return True


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--scenes',nargs='*')
    parser.add_argument('--current',action='store_true')
    parser.add_argument('--skip-first-survey',action='store_true')
    args=parser.parse_args()
    catalog=command(ROLES[0],'sceneCatalog')
    scenes=args.scenes or [e['id'] for e in catalog if e['index']>=0 and e['id'].startswith('Level_') and e['id'].endswith('_Main')]
    for sequence,scene in enumerate(scenes):
        if not args.current or state(ROLES[0]).get('mainScene')!=scene:
            entry=maps.enter(scene,ROLES[0])
            write(dict(kind='entry',scene=scene,result=entry))
            if not entry['loaded']:raise RuntimeError('Map entry failed: '+scene)
        if sequence!=0 or not args.skip_first_survey:survey(scene)
        original=state(ROLES[0])
        subscenes=command(ROLES[0],'subSceneCatalog')
        origin=next(e for e in subscenes if e['index']==original['sceneIndex'])
        route=[origin]
        visited={origin['id']}
        children={child['id']:child for child in subscenes if child['index']>=0}
        while route:
            child=next((children[p['target']] for p in command(ROLES[1],'teleporterCatalog')
                        if p['target'] in children and p['target'] not in visited),None)
            if child is not None:
                if not transfer(child['id'],None,child['index']):break
                visited.add(child['id'])
                route.append(child)
                survey(child['id'])
                continue
            route.pop()
            if route and not transfer(route[-1]['id'],None,route[-1]['index']):break
        for child in children.values():
            if child['id'] not in visited:
                write(dict(kind='additive_entry',scene=child['id'],outcome='no_reachable_active_native_teleporter'))
        exits=command(ROLES[0],'exitCatalog')
        if any(e['active'] and e['enabled'] and any(p['target']=='Base' for p in e['proxies']) for e in exits):
            returned=maps.leave(ROLES[1])
            write(dict(kind='extraction',scene=scene,result=returned))
            print('extraction',scene,returned['pass'],flush=True)
            if not returned['returned']:raise RuntimeError('Base return failed')
        else:
            write(dict(kind='extraction',scene=scene,outcome='no_active_native_countdown_exit',exits=exits))
