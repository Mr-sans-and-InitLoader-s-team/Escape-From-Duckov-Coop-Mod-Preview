"""A new shared character joins the host's existing tutorial scene and progress."""
import time
from driver import command, state, wait_for
import shared_character_creation as t

t.RUN = 'tutorial-final'
HOST, CLIENT = t.HOST, t.CLIENT

for role in [CLIENT, HOST]:
    command(role, 'stop')
time.sleep(2)
original = t.probe(CLIENT)
t.probe(HOST, forget=True, tutorialComplete=False)
command(HOST, 'loadScene', scene='Level_Guide_Main', timeout=180)
assert wait_for(lambda: state(HOST).get('initialized') and not state(HOST).get('loading'), 60)
command(HOST, 'invincible', enabled=True)
command(HOST, 'host', transport='Direct', port=19050)
t.join()
t.emit('tutorial_host_first_visit_still_requires_native_face', wait_for(lambda: t.probe(CLIENT)['editor'], 60))
t.emit('character_setup_does_not_mark_host_tutorial_complete', not t.probe(HOST)['tutorialComplete'] and state(HOST)['mainScene'] == 'Level_Guide_Main')
t.probe(CLIENT, confirm=True)
t.emit('new_character_joins_host_current_tutorial_scene', wait_for(lambda: state(CLIENT).get('mainScene') == state(HOST).get('mainScene') and state(CLIENT).get('initialized') and not state(CLIENT).get('loading'), 90))
t.emit('host_tutorial_is_not_skipped', not t.probe(HOST)['tutorialComplete'] and not t.probe(CLIENT)['tutorialComplete'])
t.emit('tutorial_character_face_and_record_persist_on_host', t.probe(HOST)['recorded'] and t.probe(HOST)['storedFace'] == t.probe(CLIENT)['face'])
host_position = state(HOST)['local']['position']
client_position = state(CLIENT)['local']['position']
t.emit('joins_at_host_tutorial_position', sum((a - b) ** 2 for a, b in zip(host_position, client_position)) < 1, host=host_position, client=client_position)


def same_progress():
    values = {r: command(r, 'sharedHome') for r in [HOST, CLIENT]}
    return values[HOST]['xp'] == values[CLIENT]['xp'] and values[HOST]['quests'] == values[CLIENT]['quests']


t.emit('tutorial_quest_and_exp_progress_match_host', wait_for(same_progress, 20))
t.stop_guest()
t.emit('tutorial_leave_restores_personal_face_and_gear', t.probe(CLIENT)['face'] == original['face'] and t.probe(CLIENT)['inventory'] == original['inventory'])
command(HOST, 'stop')
