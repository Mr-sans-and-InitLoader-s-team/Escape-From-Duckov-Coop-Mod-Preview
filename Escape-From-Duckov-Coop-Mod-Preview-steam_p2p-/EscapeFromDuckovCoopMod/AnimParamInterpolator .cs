// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

namespace EscapeFromDuckovCoopMod;

public struct AnimSample
{
    public double t;
    public float speed, dirX, dirY;
    public int hand;
    public bool gunReady, dashing;
    public bool attack; // 兼容你现在的 Attack(bool)
    public int stateHash; // 可选：状态同步
    public float normTime; // 可选：状态时间
}

public class AnimParamInterpolator : MonoBehaviour
{
    [Header("时间窗")] //不用注释了这都看不懂的话就 nim
    public float interpolationBackTime = 0.12f;

    public float maxExtrapolate = 0.08f;

    [Header("平滑")] public float paramSmoothTime = 0.07f;

    public float minHoldTime = 0.08f;

    [Header("状态过渡（可选）")] public float crossfadeDuration = 0.05f;

    public int crossfadeLayer;
    private readonly List<AnimSample> _buf = new(64);

    private Animator anim;

    private float curSpeed, curDirX, curDirY;
    private int hMoveSpeed, hDirX, hDirY, hHand, hGunReady, hDashing, hAttack;
    private bool lastGunReady, lastDashing, lastAttack;
    private int lastHand;
    private int lastStateHash = -1;
    private double tGun, tDash, tAtk;
    private double tHand;
    private double tState;
    private float vSpeed, vDirX, vDirY;

    //sans这个类你就不用看你了，你不会的
    private void Awake()
    {
        if (!anim) anim = GetComponentInChildren<Animator>(true);
        if (anim) anim.applyRootMotion = false;

        hMoveSpeed = Animator.StringToHash("MoveSpeed");
        hDirX = Animator.StringToHash("MoveDirX");
        hDirY = Animator.StringToHash("MoveDirY");
        hHand = Animator.StringToHash("HandState");
        hGunReady = Animator.StringToHash("GunReady");
        hDashing = Animator.StringToHash("Dashing");
        hAttack = Animator.StringToHash("Attack");
    }

    private void LateUpdate()
    {
        if (!anim || _buf.Count == 0) return;

        var renderT = Time.unscaledTimeAsDouble - interpolationBackTime;
        var i = 0;
        while (i < _buf.Count && _buf[i].t < renderT) i++;

        AnimSample a, b;
        var t01 = 0f;
        if (i == 0)
        {
            a = b = _buf[0];
        }
        else if (i < _buf.Count)
        {
            a = _buf[i - 1];
            b = _buf[i];
            t01 = (float)((renderT - a.t) / Math.Max(1e-6, b.t - a.t));
            if (i > 1) _buf.RemoveRange(0, i - 1);
        }
        else
        {
            a = b = _buf[_buf.Count - 1];
            var dt = Math.Min(maxExtrapolate, renderT - b.t);
            if (_buf.Count >= 2)
            {
                var p = _buf[_buf.Count - 2];
                var ds = (b.speed - p.speed) / (float)Math.Max(1e-6, b.t - p.t);
                var dx = (b.dirX - p.dirX) / (float)Math.Max(1e-6, b.t - p.t);
                var dy = (b.dirY - p.dirY) / (float)Math.Max(1e-6, b.t - p.t);
                b.speed += ds * (float)dt;
                b.dirX += dx * (float)dt;
                b.dirY += dy * (float)dt;
            }
        }

        var targetSpeed = Mathf.LerpUnclamped(a.speed, b.speed, t01);
        var targetDirX = Mathf.LerpUnclamped(a.dirX, b.dirX, t01);
        var targetDirY = Mathf.LerpUnclamped(a.dirY, b.dirY, t01);

        curSpeed = Mathf.SmoothDamp(curSpeed, targetSpeed, ref vSpeed, paramSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        curDirX = Mathf.SmoothDamp(curDirX, targetDirX, ref vDirX, paramSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        curDirY = Mathf.SmoothDamp(curDirY, targetDirY, ref vDirY, paramSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

        TrySetFloat(hMoveSpeed, curSpeed);
        TrySetFloat(hDirX, curDirX);
        TrySetFloat(hDirY, curDirY);

        var now = Time.unscaledTimeAsDouble;

        var desiredHand = t01 < 0.5f ? a.hand : b.hand;
        if (desiredHand != lastHand && now - tHand >= minHoldTime)
        {
            TrySetInt(hHand, desiredHand);
            lastHand = desiredHand;
            tHand = now;
        }

        var desiredGun = t01 < 0.5f ? a.gunReady : b.gunReady;
        if (desiredGun != lastGunReady && now - tGun >= minHoldTime)
        {
            TrySetBool(hGunReady, desiredGun);
            lastGunReady = desiredGun;
            tGun = now;
        }

        var desiredDash = t01 < 0.5f ? a.dashing : b.dashing;
        if (desiredDash != lastDashing && now - tDash >= minHoldTime)
        {
            TrySetBool(hDashing, desiredDash);
            lastDashing = desiredDash;
            tDash = now;
        }

        var desiredAtk = t01 < 0.5f ? a.attack : b.attack;
        if (desiredAtk != lastAttack && now - tAtk >= minHoldTime)
        {
            TrySetBool(hAttack, desiredAtk);
            lastAttack = desiredAtk;
            tAtk = now;
        }

        var desiredState = -1;
        var desiredNorm = 0f;
        if (a.stateHash >= 0 || b.stateHash >= 0)
        {
            desiredState = t01 < 0.5f ? a.stateHash : b.stateHash;
            desiredNorm = t01 < 0.5f ? a.normTime : b.normTime;
            if (desiredState >= 0 && (desiredState != lastStateHash || now - tState > 0.5))
            {
                anim.CrossFade(desiredState, crossfadeDuration, crossfadeLayer, Mathf.Clamp01(desiredNorm));
                lastStateHash = desiredState;
                tState = now;
            }
        }
    }

    public void Push(AnimSample s, double when = -1)
    {
        if (when < 0) when = Time.unscaledTimeAsDouble;
        s.t = when;

        if (_buf.Count > 0)
        {
            var last = _buf[_buf.Count - 1];
            if (s.t < last.t - 0.01 || s.t - last.t > 1.0) _buf.Clear();
        }

        _buf.Add(s);
        if (_buf.Count > 64) _buf.RemoveAt(0);
    }

    private void TrySetBool(int hash, bool v)
    {
        if (!anim) return;
        foreach (var p in anim.parameters)
            if (p.nameHash == hash && p.type == AnimatorControllerParameterType.Bool)
            {
                anim.SetBool(hash, v);
                return;
            }
    }

    private void TrySetInt(int hash, int v)
    {
        if (!anim) return;
        foreach (var p in anim.parameters)
            if (p.nameHash == hash && p.type == AnimatorControllerParameterType.Int)
            {
                anim.SetInteger(hash, v);
                return;
            }
    }

    private void TrySetFloat(int hash, float v)
    {
        if (!anim) return;
        foreach (var p in anim.parameters)
            if (p.nameHash == hash && p.type == AnimatorControllerParameterType.Float)
            {
                anim.SetFloat(hash, v);
                return;
            }
    }
}

public static class AnimInterpUtil
{
    public static AnimParamInterpolator Attach(GameObject go)
    {
        if (!go) return null;
        var it = go.GetComponent<AnimParamInterpolator>();
        if (!it) it = go.AddComponent<AnimParamInterpolator>();
        return it;
    }
}