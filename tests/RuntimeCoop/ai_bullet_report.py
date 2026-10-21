"""Summarize saved measurements; never treat private batch runs as GPU acceptance."""
import hashlib
import json
from pathlib import Path

repo=Path(__file__).resolve().parents[2]
out=repo/'artifacts/ai-bullet-storm'
def rows(name):
    return [json.loads(line) for line in (out/(name+'.jsonl')).read_text(encoding='utf-8').splitlines()]
def at(name,count=16):
    return next(r for r in rows(name) if r['count']==count)
def method(row,name):
    return next(m for m in row['profiles']['publicclient']['methods'] if m['method']==name)
def describe(name):
    r=at(name);p=r['profiles']['publicclient']
    return (f"{name}: {sum(r['firing']['shots'])} trigger pulls, host projectiles={r['profiles']['publichost']['spawned']}, "
        f"client visuals={p['spawned']}, frames={p['frames']}, meanFPS={p['frames']/p['seconds']:.1f}, "
        f"P95={p['p95Ms']:.2f}ms, P99={p['p99Ms']:.2f}ms, peakAudioObjects={p['peakEmitters']}, "
        f"fastFallback={p.get('fastFallback','not recorded')}")

accepted=json.loads((out/'acceptance.json').read_text(encoding='utf-8'))
sha=hashlib.sha256((repo/'EscapeFromDuckovCoopMod/bin/Release/netstandard2.1/EscapeFromDuckovCoopMod.dll').read_bytes()).hexdigest().upper()
data=[
'多 AI 弹幕性能修复与本地测试报告 / 1.4.0',
f'Final public DLL SHA256: {sha}',
'',
'Changes:',
'1. Preserve the native FMOD gunshot event and spatial position without creating a temporary 3-second CoopAudioEmitter for every one-shot gun sound.',
'2. Evaluate bullet flybys per local listener; suppress forwarding and replay of this listener-specific sound. Avoid the native unused SoundSource allocation during co-op flybys.',
'3. Emit muzzle/shell/recoil effects once per native trigger pull rather than once per shotgun pellet. Keep projectile and damage processing.',
'4. Send verified one-shot gun sounds unreliably using the existing AUDIO_EVENT payload/opcode. Preserve reliable delivery for other and parameterized audio. No gameplay RPC delivery changes.',
'5. Preserve AI identity in hearing cues so the native SoundVisualization reuses one indicator per enemy. Coalesce per-pellet cues within one native trigger pull and send transient hearing cues unreliably. Host AI hearing decisions are unchanged. Append an optional int source ID while retaining the legacy payload prefix.',
'',
'Method:',
'Two isolated public-game processes, hidden Direct3D11 batch mode, native Farm Town scene, native AI presets, native guns/ammo/TransToFire/projectile pool. Input disabled; cursor None. No desktop input automation.',
'AI brains paused for a controlled firing fixture. Native fire cadence bypassed deliberately for the 40-trigger-pulls/s extreme load. Magazine refill is test-only. Actors placed on native supported ground.',
'Same fixed firing area for matched-before/final-rifle; on one shared computer, weather and scheduling vary. Harmony profiling adds overhead. These are CPU/network comparisons, not visible-render/GPU/FPS guarantees.',
'GC.GetAllocatedBytesForCurrentThread returns zero in this Mono runtime, so allocation bytes are unavailable, not zero. Collection counts remain recorded.',
'Existing remote cosmetic budgets remain (72 broadcasts/frame and 28 fake visuals/frame). Cosmetic counts can differ with batching/loss; host native projectiles/damage are retained. Do not interpret cosmetic counts as shots dealing damage.',
'',
'Matched measurements:',
describe('hearing-before'),describe('release-rifle'),
f"Native hearing indicators (enabled during both runs): {at('hearing-before')['profiles']['publicclient']['hearing']['peak']} -> {at('release-rifle')['profiles']['publicclient']['hearing']['peak']}",
describe('mp155-before'),describe('release-mp155'),describe('release-weak'),
f"MP155 client muzzle-effect calls: {method(at('mp155-before'),'FxManager.PlayMuzzleFxAndShell')['calls']} -> {method(at('release-mp155'),'FxManager.PlayMuzzleFxAndShell')['calls']}",
'',
'Acceptance:',
f"Native game-process checks: {sum(len(c) for c in accepted['nativeChecks'].values())}, all passed.",
f"Actual AI bullet damage: {accepted['damage']}",
f"Visual-only explosion guard: {accepted['visualExplosion']}",
'141 health/buff/spectator/corpse/RPC compatibility regression assertions passed, including extended/legacy AI hearing packet compatibility.',
'Public Release build: 0 errors / 25 existing warnings. Preserved beta reference build: 0 errors / 74 existing warnings. Public rebuild performed last.',
'Weak profile: configured one-way 400ms, +/-100ms jitter, 10% loss; private localhost UDP proxy only.',
'Final weak delivery/recovery and 16-AI replica checks: see release-recovery.json. Packet counts: release-weak.jsonl; proxy counts: network-statistics.json.',
'',
'Evidence exclusions:',
'audio-before.jsonl contains an early distant-listener diagnostic and is not the matched firing comparison.',
'shotgun-before.jsonl used an invalid early gun/ammo fixture and is excluded. MP155 comparisons use native weapon 250 + ammo 146 with 5 pellets per trigger pull.',
'matched-after/mp155-after/mp155-weak/rifle-miss/weak-recovery.json are intermediate E514976E evidence; final-* files are intermediate EB488E2F evidence. Only release-* files and the final acceptance.json use the final DLL above. hearing-before uses the original 5756F591 baseline with the same new profiler.',
'baseline-hit contains the initial realistic 0/1/4/8/16 AI x 10 shots/s survey. Persistent startIndex messages were left unchanged as requested.',
'',
'Manual acceptance still needed:',
'Real rendered gameplay/GPU/particle overdraw with full effects; independently running AI/Boss combat and simultaneous movement; separate-account Steam P2P / four players; the same encounter on the user hardware.',
'The extreme-fire profile still has measurable native projectile collision cost. No claim that unlimited AI fire is now frame-drop-free.',
'No Git commit, push, or PR. Test hooks are private and are not installed in the normal game.',
]
(out/'AI-BULLET-STORM-REPORT.txt').write_text('\n'.join(data)+'\n',encoding='utf-8')
print('report saved',out/'AI-BULLET-STORM-REPORT.txt')
