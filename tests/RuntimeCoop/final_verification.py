"""Focused live regression after the final weapon replay correction."""
from driver import ROOT
from matrix import profile,record
import multiplayer as group
import weapon_switching
import ai_reentry
import pvp_weak_recheck
import drop_rejoin
import weapon_rejoin

def main():
    group.ROLES=group.ALL
    profile('baseline',0,0,0)
    group.setup()
    for attempt in range(3):
        record({'category':'weapon_replay_repeat','attempt':attempt})
        weapon_switching.main()
    ai_reentry.main()
    pvp_weak_recheck.main()
    drop_rejoin.main()
    weapon_rejoin.main()
    (ROOT/'four-player-focused-final-ready').write_text('4',encoding='utf-8')

if __name__=='__main__':main()
