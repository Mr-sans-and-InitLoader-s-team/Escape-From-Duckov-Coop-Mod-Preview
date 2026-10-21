"""Checks the actual menu cards in an already running private graphics test game."""
import json
import os
import time
from pathlib import Path

from driver import command, state, wait_for

ROLE = os.environ.get('COOP_UI_ROLE', 'publichost')
OUT = Path(__file__).resolve().parents[2] / 'artifacts/mode-selection'
results = []


def check(name, condition):
    results.append({'name': name, 'passed': bool(condition)})
    print(('PASS ' if condition else 'FAIL ') + name, flush=True)


def snap(**kwargs):
    return command(ROLE, 'modeSelection', **kwargs)


def resting(value):
    return len(value['ducks']) == 4 and all(d['speed'] < .01 and d['activePose'] == ['MagicBlend_Gun'] for d in value['ducks'])


idle = snap(hover='none')
assert wait_for(lambda: resting(snap()), 5)
idle = snap(screenshot='checked-idle.png')
check('Automatic main-menu modal with two ready portraits', idle['open'] and idle['previews'] == [True, True])
check('Four visual-only ducks, no gameplay players', len(idle['ducks']) == 4 and idle['characters'] == 0)
check('Old shared-mode toggle removed', not idle['oldToggle'])
check('Two cameras and isolated portrait light masks', len(idle['cameras']) == 2 and len({c['cullingMask'] for c in idle['cameras']}) == 2)
check('Native idle gun poses on every duck', resting(idle))
check('Every gun attached to native hand socket without offset', all(
    len(d['guns']) == 1 and all(g['attached'] and g['position']['sqrMagnitude'] < .00001 and g['rotation']['sqrMagnitude'] < .00001 for g in d['guns'])
    for d in idle['ducks']))
check('Number, title and info share header alignment', all(len({h['y'] for h in c['headers']}) == 1 for c in idle['cards']))
check('Bottom gradient clipped by rounded card mask', all(c['roundedMask'] and c['scrim']['bottom'] == 0 for c in idle['cards']))
check('Chinese labels stay inside their text regions', not any(t['overflow'] for t in idle['labels']))

for card in ['IndependentCoopCard', 'SharedCoopCard']:
    hovered = snap(hover=card, screenshot=f'checked-{card}.png')
    check(card + ': one runner and one walker on the hovered card', sum(d['speed'] > 1.5 and d['activePose'] == ['MagicBlend_Gun Run'] for d in hovered['ducks']) == 1
          and sum(.6 < d['speed'] <= 1.01 and d['activePose'] == ['MagicBlend_Gun'] for d in hovered['ducks']) == 1
          and sum(d['speed'] < .01 for d in hovered['ducks']) == 2)
    check(card + ': gradient reaches bottom and expands on hover', all(
        c['scrim']['bottom'] == 0 and abs(c['scrim']['height'] - (450 if c['name'] == card else 240)) < 1 for c in hovered['cards']))
    snap(hover='none')
    check(card + ': mouse exit restores all native holding poses', wait_for(lambda: resting(snap()), 5))

small = snap(width=1280, height=720, screenshot='checked-1280x720.png')
check('1280x720 runtime viewport and text layout', small['width'] == 1280 and small['height'] == 720 and not any(t['overflow'] for t in small['labels']))
snap(width=1920, height=1080)

selected = snap(select='IndependentCoopCard')
check('Independent choice updates mode and restores native lights', selected['shared'] is False and selected['lightsRestored'] is True)
time.sleep(.3)
closed = snap()
check('Choosing mode removes modal, preview cameras, lights and ducks', not closed['open'] and not closed['cameras'] and not closed['ducks'] and not closed['lights'])

command(ROLE, 'loadBase', timeout=90)
command(ROLE, 'sharedEntry', menu=True)
assert wait_for(lambda: snap()['open'] and len(snap()['ducks']) == 4 and all(snap()['previews']), 40)
check('Returning from actual base scene reopens mode selection', state(ROLE)['scene'] == 'MainMenu' and snap()['open'])
selected = snap(select='SharedCoopCard')
check('Shared choice updates mode and restores native lights', selected['shared'] is True and selected['lightsRestored'] is True)
time.sleep(.3)
closed = snap()
check('Shared choice also releases every preview resource', not closed['open'] and not closed['cameras'] and not closed['ducks'] and not closed['lights'])

(OUT / 'ui-checks.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
assert all(r['passed'] for r in results), 'See ui-checks.json'
print(f'{len(results)} live UI checks passed.', flush=True)
