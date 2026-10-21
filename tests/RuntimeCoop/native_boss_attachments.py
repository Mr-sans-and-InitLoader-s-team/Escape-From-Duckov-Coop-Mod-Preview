import json
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

game = Path(r'D:\soft\steam\steamapps\common\Escape from Duckov\Duckov_Data')
env = UnityPy.load(str(game / 'resources.assets'))
gen = TypeTreeGenerator(next(iter(env.files.values())).unity_version)
gen.load_local_game(str(game.parent))
env.typetree_generator = gen
objects = {o.path_id: o for o in env.objects}


def read(ident):
    return objects[ident].read_typetree()


def tree(go_id):
    go = read(go_id)
    components = []
    children = []
    for c in go['m_Component']:
        ident = c['component']['m_PathID']
        obj = objects[ident]
        data = read(ident)
        components.append(dict(id=ident, type=obj.type.name, data=data))
        if obj.type.name == 'Transform':
            children = [tree(read(p['m_PathID'])['m_GameObject']['m_PathID']) for p in data['m_Children']]
    return dict(id=go_id, name=go['m_Name'], components=components, children=children)


result = [tree(i) for i in [9055, 9059]]
out = Path(__file__).resolve().parents[2] / 'artifacts/region-sync/native-boss-attachments.json'
out.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
for root in result:
    def summary(node):
        print(node['name'], [(c['type'], list(c['data'])) for c in node['components']])
        for child in node['children']:
            summary(child)
    summary(root)
