"""Read original installed Unity asset metadata; never modify game assets."""
import json
import re
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

GAME = Path(r'D:\soft\steam\steamapps\common\Escape from Duckov\Duckov_Data')
OUT = Path(__file__).resolve().parents[2] / 'artifacts/region-sync'
PATTERN = re.compile(r'boss|question|myst|element|encounter|danger|super|random', re.I)
rows = []
generator = None
for path in [GAME / 'resources.assets', *sorted(GAME.glob('level*'))]:
    if not path.is_file() or path.suffix:
        if path.name != 'resources.assets':
            continue
    env = UnityPy.load(str(path))
    if generator is None:
        version = next(iter(env.files.values())).unity_version
        generator = TypeTreeGenerator(version)
        generator.load_local_game(str(GAME.parent))
    env.typetree_generator = generator
    count = 0
    for obj in env.objects:
        if obj.type.name not in {'MonoBehaviour', 'GameObject', 'MonoScript'}:
            continue
        try:
            data = obj.read_typetree()
        except Exception:
            continue
        name = data.get('m_Name', '')
        if PATTERN.search(name) or data.get('nameKey') == 'Character_Question' or data.get('isArea') == 1:
            rows.append(dict(file=path.name, id=obj.path_id, type=obj.type.name, data=data))
            count += 1
    print(path.name, count, flush=True)
    del env
OUT.mkdir(exist_ok=True)
(OUT / 'native-asset-candidates.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
print('candidates', len(rows), flush=True)
