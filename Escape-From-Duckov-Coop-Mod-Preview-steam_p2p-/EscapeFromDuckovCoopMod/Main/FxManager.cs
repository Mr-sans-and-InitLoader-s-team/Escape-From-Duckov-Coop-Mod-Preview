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

using System.Reflection;
using Duckov;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public class MeleeFxStamp : MonoBehaviour
{
    public float lastFxTime;
}

public static class MeleeFx
{
    private static NetService Service => NetService.Instance;

    public static void SpawnSlashFx(CharacterModel ctrl)
    {
        if (!ctrl) return;

        // —— 1) 更稳地获取近战武器 Agent —— 
        ItemAgent_MeleeWeapon melee = null;

        // 优先：从模型常见挂点里找
        Transform[] sockets =
        {
            ctrl.MeleeWeaponSocket,
            // 某些模型可能把近战也挂在左右手
            // 这些字段若不存在/为 null，不会报错
            ctrl.GetType().GetField("RightHandSocket") != null ? (Transform)ctrl.GetType().GetField("RightHandSocket").GetValue(ctrl) : null,
            ctrl.GetType().GetField("LefthandSocket") != null ? (Transform)ctrl.GetType().GetField("LefthandSocket").GetValue(ctrl) : null
        };

        foreach (var s in sockets)
        {
            if (melee) break;
            if (!s) continue;
            melee = s.GetComponentInChildren<ItemAgent_MeleeWeapon>(true);
        }

        // 兜底：从整个人物下搜（可能命中备用/预加载实例，影响极小）
        if (!melee)
            melee = ctrl.GetComponentInChildren<ItemAgent_MeleeWeapon>(true);

        if (!melee || !melee.slashFx) return;

        // —— 2) 去抖，避免同帧重复播 —— 
        var stamp = ctrl.GetComponent<MeleeFxStamp>() ?? ctrl.gameObject.AddComponent<MeleeFxStamp>();
        if (Time.time - stamp.lastFxTime < 0.01f) return; // 去抖
        stamp.lastFxTime = Time.time;

        // —— 3) 按武器定义的延迟 + 合理的前方位置/朝向 —— 
        var delay = Mathf.Max(0f, melee.slashFxDelayTime);

        var t = ctrl.transform;
        var forward = Mathf.Clamp(melee.AttackRange * 0.6f, 0.2f, 2.5f);
        var pos = t.position + t.forward * forward + Vector3.up * 0.6f;
        var rot = Quaternion.LookRotation(t.forward, Vector3.up);

        UniTask.Void(async () =>
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(delay));
                Object.Instantiate(melee.slashFx, pos, rot);
            }
            catch
            {
            }
        });
    }
}

public static class FxManager
{
    private static readonly Dictionary<ItemAgent_Gun, GameObject> _muzzleFxByGun = new();
    private static readonly Dictionary<ItemAgent_Gun, ParticleSystem> _shellPsByGun = new();

    // 给我一个默认的开火FX，用于弓没有配置 muzzleFxPfb 时兜底（在 Inspector 里拖一个合适的特效）Lol
    public static GameObject defaultMuzzleFx;


    // 反射缓存（避免每枪 Traverse）
    private static readonly MethodInfo MI_StartVisualRecoil =
        AccessTools.Method(typeof(ItemAgent_Gun), "StartVisualRecoil");

    private static readonly FieldInfo FI_RecoilBack =
        AccessTools.Field(typeof(ItemAgent_Gun), "_recoilBack");

    private static readonly FieldInfo FI_ShellParticle =
        AccessTools.Field(typeof(ItemAgent_Gun), "shellParticle");

    private static NetService Service => NetService.Instance;
    private static bool IsServer => Service != null && Service.IsServer;
    private static NetManager netManager => Service?.netManager;
    private static NetDataWriter writer => Service?.writer;
    private static NetPeer connectedPeer => Service?.connectedPeer;
    private static PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private static bool networkStarted => Service != null && Service.networkStarted;
    private static Dictionary<NetPeer, GameObject> remoteCharacters => Service?.remoteCharacters;
    private static Dictionary<NetPeer, PlayerStatus> playerStatuses => Service?.playerStatuses;
    private static Dictionary<string, GameObject> clientRemoteCharacters => Service?.clientRemoteCharacters;


    public static void PlayMuzzleFxAndShell(string shooterId, int weaponType, Vector3 muzzlePos, Vector3 finalDir)
    {
        try
        {
            // 1) 定位 shooter GameObject
            GameObject shooterGo = null;
            if (NetService.Instance.IsSelfId(shooterId))
            {
                var cmSelf = LevelManager.Instance?.MainCharacter?.GetComponent<CharacterMainControl>();
                if (cmSelf) shooterGo = cmSelf.gameObject;
            }
            else if (!string.IsNullOrEmpty(shooterId) && shooterId.StartsWith("AI:"))
            {
                if (int.TryParse(shooterId.Substring(3), out var aiId))
                    if (AITool.aiById.TryGetValue(aiId, out var cmc) && cmc)
                        shooterGo = cmc.gameObject;
            }
            else
            {
                if (IsServer)
                {
                    // Server：EndPoint -> NetPeer -> remoteCharacters
                    NetPeer foundPeer = null;
                    foreach (var kv in playerStatuses)
                        if (kv.Value != null && kv.Value.EndPoint == shooterId)
                        {
                            foundPeer = kv.Key;
                            break;
                        }

                    if (foundPeer != null) remoteCharacters.TryGetValue(foundPeer, out shooterGo);
                }
                else
                {
                    // Client：直接用 shooterId 查远端克隆
                    clientRemoteCharacters.TryGetValue(shooterId, out shooterGo);
                }
            }

            // 2) 尝试命中缓存（避免每包 GetComponentInChildren）
            ItemAgent_Gun gun = null;
            Transform muzzleTf = null;
            if (!string.IsNullOrEmpty(shooterId))
                if (LoaclPlayerManager.Instance._gunCacheByShooter.TryGetValue(shooterId, out var cached) && cached.gun)
                {
                    gun = cached.gun;
                    muzzleTf = cached.muzzle;
                }

            // 3) 缓存未命中 → 扫描一次并写入缓存
            if (shooterGo && (!gun || !muzzleTf))
            {
                var cmc = shooterGo.GetComponent<CharacterMainControl>();
                var model = cmc ? cmc.characterModel : null;

                if (!gun && model)
                {
                    if (model.RightHandSocket && !gun) gun = model.RightHandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                    if (model.LefthandSocket && !gun) gun = model.LefthandSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                    if (model.MeleeWeaponSocket && !gun) gun = model.MeleeWeaponSocket.GetComponentInChildren<ItemAgent_Gun>(true);
                }

                if (!gun) gun = cmc ? cmc.CurrentHoldItemAgent as ItemAgent_Gun : null;

                if (gun && gun.muzzle && !muzzleTf) muzzleTf = gun.muzzle;

                if (!string.IsNullOrEmpty(shooterId) && gun) LoaclPlayerManager.Instance._gunCacheByShooter[shooterId] = (gun, muzzleTf);
            }

            // 4) 没有 muzzle 就用兜底挂点（只负责火光，不做抛壳/回座力）
            GameObject tmp = null;
            if (!muzzleTf)
            {
                tmp = new GameObject("TempMuzzleFX");
                tmp.transform.position = muzzlePos;
                tmp.transform.rotation = Quaternion.LookRotation(finalDir, Vector3.up);
                muzzleTf = tmp.transform;
            }

            // 5) 真正播放（包含火光 + 抛壳 + 回座力；gun==null 时内部仅火光）
            Client_PlayLocalShotFx(gun, muzzleTf, weaponType);

            if (tmp) GameObject.Destroy(tmp, 0.2f);

            // 6) 非主机端本地顺带触发一次攻击动画（和你原逻辑一致）
            if (!IsServer && shooterGo)
            {
                var anim = shooterGo.GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
                if (anim && anim.animator) anim.OnAttack();
            }
        }
        catch
        {
            // 保底，避免任何异常打断网络流
        }
    }

    public static void Client_PlayAiDeathFxAndSfx(CharacterMainControl cmc)
    {
        if (!cmc) return;
        var model = cmc.characterModel;
        if (!model) return;

        object hv = null;
        try
        {
            var fi = model.GetType().GetField("hurtVisual",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null) hv = fi.GetValue(model);
        }
        catch
        {
        }

        if (hv == null)
            try
            {
                hv = model.GetComponentInChildren(typeof(HurtVisual), true);
            }
            catch
            {
            }

        if (hv != null)
            try
            {
                var miDead = hv.GetType().GetMethod("OnDead",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (miDead != null)
                {
                    var di = new DamageInfo
                    {
                        // OnDead 本身只需要一个 DamageInfo；就传个位置即可
                        damagePoint = cmc.transform.position,
                        damageNormal = Vector3.up
                    };
                    miDead.Invoke(hv, new object[] { di });

                    if (FmodEventExists("event:/e_KillMarker")) AudioManager.Post("e_KillMarker");
                }
            }
            catch
            {
            }
    }

    public static void Client_PlayLocalShotFx(ItemAgent_Gun gun, Transform muzzleTf, int weaponType)
    {
        if (!muzzleTf) return;

        GameObject ResolveMuzzlePrefab()
        {
            GameObject fxPfb = null;
            LoaclPlayerManager.Instance._muzzleFxCacheByWeaponType.TryGetValue(weaponType, out fxPfb);
            if (!fxPfb && gun && gun.GunItemSetting) fxPfb = gun.GunItemSetting.muzzleFxPfb;
            if (!fxPfb) fxPfb = defaultMuzzleFx;
            return fxPfb;
        }

        void PlayFxGameObject(GameObject go)
        {
            if (!go) return;
            var ps = go.GetComponent<ParticleSystem>();
            if (ps)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
            else
            {
                go.SetActive(false);
                go.SetActive(true);
            }
        }

        // ========== 有“枪实例”→ 走池化，零 GC 可能？==========
        if (gun != null)
        {
            if (!_muzzleFxByGun.TryGetValue(gun, out var fxGo) || !fxGo)
            {
                var fxPfb = ResolveMuzzlePrefab();
                if (fxPfb)
                {
                    fxGo = GameObject.Instantiate(fxPfb, muzzleTf, false);
                    fxGo.transform.localPosition = Vector3.zero;
                    fxGo.transform.localRotation = Quaternion.identity;
                    _muzzleFxByGun[gun] = fxGo;
                }
            }

            PlayFxGameObject(fxGo);

            if (!_shellPsByGun.TryGetValue(gun, out var shellPs) || shellPs == null)
            {
                try
                {
                    shellPs = (ParticleSystem)FI_ShellParticle?.GetValue(gun);
                }
                catch
                {
                    shellPs = null;
                }

                _shellPsByGun[gun] = shellPs;
            }

            try
            {
                if (shellPs) shellPs.Emit(1);
            }
            catch
            {
            }

            TryStartVisualRecoil_NoAlloc(gun);
            return;
        }

        // ========== 没有“枪实例”（比如远端首包）→ 一次性临时 FX（低频） ==========
        var pfb = ResolveMuzzlePrefab();
        if (pfb)
        {
            var tempFx = GameObject.Instantiate(pfb, muzzleTf, false);
            tempFx.transform.localPosition = Vector3.zero;
            tempFx.transform.localRotation = Quaternion.identity;

            var ps = tempFx.GetComponent<ParticleSystem>();
            if (ps)
            {
                ps.Play(true);
            }
            else
            {
                tempFx.SetActive(false);
                tempFx.SetActive(true);
            }

            GameObject.Destroy(tempFx, 0.5f);
        }
    }

    public static void TryStartVisualRecoil_NoAlloc(ItemAgent_Gun gun)
    {
        if (!gun) return;
        try
        {
            MI_StartVisualRecoil?.Invoke(gun, null);
            return;
        }
        catch
        {
        }

        try
        {
            FI_RecoilBack?.SetValue(gun, true);
        }
        catch
        {
        }
    }

    private static bool FmodEventExists(string path)
    {
        try
        {
            var sys = RuntimeManager.StudioSystem;
            if (!sys.isValid()) return false;
            EventDescription desc;
            var r = sys.getEvent(path, out desc);
            return r == RESULT.OK && desc.isValid();
        }
        catch
        {
            return false;
        }
    }
}