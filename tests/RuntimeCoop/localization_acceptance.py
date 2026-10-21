"""Validate shipped translations and the actual menu UI in the private game."""
import json
import os
import re
import time
from pathlib import Path

from driver import command, state, wait_for

OUT = Path(os.environ.get('COOP_LOCALIZATION_OUTPUT', Path(__file__).resolve().parents[2] / 'artifacts/ui-localization'))
OUT.mkdir(parents=True, exist_ok=True)
ROLE = 'publichost'
LANGUAGES = {'zh-CN': 'ChineseSimplified', 'en-US': 'English', 'ja-JP': 'Japanese',
             'ko-KR': 'Korean', 'de-DE': 'German', 'ru-RU': 'Russian', 'pt-BR': 'Portuguese'}
checks = []
evidence = {}


def check(name, passed):
    checks.append({'name': name, 'passed': bool(passed)})
    print(('PASS ' if passed else 'FAIL ') + name, flush=True)


assert wait_for(lambda: bool(state(ROLE)), 60), 'Private game did not start'
command(ROLE, 'loadMod')
assert wait_for(lambda: command(ROLE, 'modeSelection')['open'], 50)
assert wait_for(lambda: command(ROLE, 'modeSelection')['previews'] == [True, True], 50)
source = Path(__file__).resolve().parents[2] / 'Localization'
english = {e['key']: e['value'] for e in json.loads((source / 'en-US.json').read_text(encoding='utf-8'))['translations']}

for code, language in LANGUAGES.items():
    entries = json.loads((source / (code + '.json')).read_text(encoding='utf-8'))['translations']
    values = {e['key']: e['value'] for e in entries}
    check(code + ' complete and unique keys', len(entries) == len(values) == len(english) and values.keys() == english.keys())
    check(code + ' placeholders and rich-text breaks', all(
        sorted(re.findall(r'\{\d+(?::[^}]*)?\}', v)) == sorted(re.findall(r'\{\d+(?::[^}]*)?\}', english[k])) and
        v.count('<br>') == english[k].count('<br>') for k, v in values.items()))
    if code not in ('en-US', 'zh-CN'):
        check(code + ' no English shared-mode placeholders', all(v != english[k] for k, v in values.items()
              if k.startswith(('ui.sharedHome', 'net.sharedHome.'))))
    result = command(ROLE, 'localization', language=language, screenshot='localized-' + code + '.png')
    evidence[code] = result
    check(code + ' game language detected', result['code'] == code)
    check(code + ' all shipped strings decoded exactly', result['count'] == len(english) and not result['mismatch'])
    check(code + ' error arguments and escaped JSON', not result['formatMismatch'] and result['escapedTextRoundTrip'])
    check(code + ' new UI English fallback complete', not result['fallbackMismatch'])
    check(code + ' mode labels refresh and fit', all(t['matches'] and not t['overflow'] for t in result['modeLabels']))
    check(code + ' mode badges translated and fit', all(
        b['labels'][0]['text'] == values['ui.steam.coopMode.' + ('shared' if b['shared'] else 'independent' if b['shared'] is False else 'unknown')]
        and not b['labels'][0]['overflow'] for b in result['badges']))
    check(code + ' connection error fits', all(not t['overflow'] for t in result['errorLabels']))
    check(code + ' disconnect notice fits', all(not t['overflow'] for t in result['noticeLabels']))
    check(code + ' switch button translated', result['switchLabel'] == values['ui.coopMode.switch'])
    check(code + ' glyphs available', all(not t['missingGlyphs'] for t in result['modeLabels'] + result['errorLabels'] + result['noticeLabels']))

command(ROLE, 'localization', language='ChineseSimplified')
OUT.joinpath('runtime-evidence.json').write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding='utf-8')
OUT.joinpath('checks.json').write_text(json.dumps(checks, ensure_ascii=False, indent=2), encoding='utf-8')
assert all(c['passed'] for c in checks), 'See checks.json and runtime-evidence.json'
print(str(len(checks)) + ' localization checks passed.', flush=True)
