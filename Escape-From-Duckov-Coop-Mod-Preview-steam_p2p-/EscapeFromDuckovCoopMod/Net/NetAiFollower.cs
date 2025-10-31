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

public sealed class NetAiFollower : MonoBehaviour
{
    // 参数哈希（要与 MagicBlend 一致）
    private static readonly int hMoveSpeed = Animator.StringToHash("MoveSpeed");
    private static readonly int hMoveDirX = Animator.StringToHash("MoveDirX");
    private static readonly int hMoveDirY = Animator.StringToHash("MoveDirY");
    private static readonly int hHandState = Animator.StringToHash("HandState");
    private static readonly int hGunReady = Animator.StringToHash("GunReady");
    private static readonly int hDashing = Animator.StringToHash("Dashing");
    private Animator _anim;

    private CharacterAnimationControl _animctl;
    private bool _cGunReady, _cDashing;
    private int _cHand;

    private CharacterMainControl _cmc;

    // 本地当前（用于平滑）
    private float _cSpeed, _cDirX, _cDirY;
    private CharacterAnimationControl_MagicBlend _magic;
    private CharacterModel _model;
    private Vector3 _pos, _dir;
    private bool _tGunReady, _tDashing;
    private int _tHand;

    // 目标状态（来自网络）
    private float _tSpeed, _tDirX, _tDirY;


    private void Awake()
    {
        _cmc = GetComponentInParent<CharacterMainControl>(true);

        if (ModBehaviourF.Instance && !AITool.IsRealAI(_cmc))
        {
            Destroy(this);
            return;
        }

        HookModel(_cmc ? _cmc.characterModel : null);
        TryResolveAnimator(true);
    }

    private void Update()
    {
        if (_cmc == null) return;

        // 自愈：Animator 丢失、或存在但未激活/未启用，都重抓
        if (_anim == null || !_anim.isActiveAndEnabled || !_anim.gameObject.activeInHierarchy)
        {
            TryResolveAnimator(true);
            if (_anim == null || !_anim.isActiveAndEnabled || !_anim.gameObject.activeInHierarchy) return;
        }


        // 平滑位置/朝向；旋转同时对齐到 cmc.modelRoot，避免身体和根节点错位
        var t = transform;
        t.position = Vector3.Lerp(t.position, _pos, Time.deltaTime * 20f);

        var rotS = Quaternion.LookRotation(_dir, Vector3.up);
        if (_cmc.modelRoot) _cmc.modelRoot.rotation = rotS;
        t.rotation = rotS;

        // 简单平滑，避免抖动
        var lerp = 15f * Time.deltaTime; // ~15Hz 响应
        _cSpeed = Mathf.Lerp(_cSpeed, _tSpeed, lerp);
        _cDirX = Mathf.Lerp(_cDirX, _tDirX, lerp);
        _cDirY = Mathf.Lerp(_cDirY, _tDirY, lerp);

        _cHand = _tHand;
        _cGunReady = _tGunReady;
        _cDashing = _tDashing;

        ApplyNow();
    }

    private void OnEnable()
    {
        // 距离激活/换壳后，立刻重绑 Animator
        TryResolveAnimator(true);
    }


    private void OnDestroy()
    {
        UnhookModel();
    }

    private void HookModel(CharacterModel m)
    {
        UnhookModel();
        _model = m;
        if (_model != null)
            // 换壳后，CharacterModel 会触发 OnCharacterSetEvent
            // 这里监听它来重绑 Animator / MagicBlend
            try
            {
                _model.OnCharacterSetEvent += OnModelSet;
            }
            catch
            {
            }
    }

    private void UnhookModel()
    {
        if (_model != null)
            try
            {
                _model.OnCharacterSetEvent -= OnModelSet;
            }
            catch
            {
            }

        _model = null;
    }

    private void OnModelSet()
    {
        // 换壳已完成，cmc.characterModel 已指向新模型（见 SetCharacterModel 实现）
        HookModel(_cmc ? _cmc.characterModel : null);
        TryResolveAnimator(true);
    }

    public void ForceRebindAfterModelSwap()
    {
        HookModel(_cmc ? _cmc.characterModel : null);
        TryResolveAnimator(true);
    }

    private void TryResolveAnimator(bool forceRebind = false)
    {
        _magic = null;
        _anim = null;
        _animctl = null;

        // 1) 优先在“当前模型子树”里找：先找 MagicBlend / CharacterAnimationControl 自带的 animator
        var model = _cmc ? _cmc.characterModel : null;
        if (model != null)
            try
            {
                // 严格匹配：MagicBlend/CharAnimCtrl 组件挂在“当前 characterModel”下面
                var magics = model.GetComponentsInChildren<CharacterAnimationControl_MagicBlend>(true);
                foreach (var m in magics)
                {
                    if (!m) continue;
                    if ((m.characterModel == null || m.characterModel == model) && m.animator)
                        if (m.animator.isActiveAndEnabled && m.animator.gameObject.activeInHierarchy)
                        {
                            _magic = m;
                            _anim = m.animator;
                            break;
                        }
                }

                if (_anim == null)
                {
                    var ctrls = model.GetComponentsInChildren<CharacterAnimationControl>(true);
                    foreach (var c in ctrls)
                    {
                        if (!c) continue;
                        if ((c.characterModel == null || c.characterModel == model) && c.animator)
                            if (c.animator.isActiveAndEnabled && c.animator.gameObject.activeInHierarchy)
                            {
                                _animctl = c;
                                _anim = c.animator;
                                break;
                            }
                    }
                }

                //当前模型子树里随便找一个可用的 Animator（有些壳没挂前两个控制器）
                if (_anim == null)
                {
                    var anims = model.GetComponentsInChildren<Animator>(true);
                    foreach (var a in anims)
                    {
                        if (!a) continue;
                        if (a.isActiveAndEnabled && a.gameObject.activeInHierarchy)
                        {
                            _anim = a;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

        // 整棵对象（不局限于当前 model），先找 MagicBlend/CharAnimCtrl 的 animator
        if (_anim == null)
        {
            try
            {
                var m = GetComponentInChildren<CharacterAnimationControl_MagicBlend>(true);
                if (m && m.animator && m.animator.isActiveAndEnabled && m.animator.gameObject.activeInHierarchy)
                {
                    _magic = m;
                    _anim = m.animator;
                }
            }
            catch
            {
            }

            if (_anim == null)
                try
                {
                    var c = GetComponentInChildren<CharacterAnimationControl>(true);
                    if (c && c.animator && c.animator.isActiveAndEnabled && c.animator.gameObject.activeInHierarchy)
                    {
                        _animctl = c;
                        _anim = c.animator;
                    }
                }
                catch
                {
                }
        }

        // 整棵对象随便找一个可用的 Animator
        if (_anim == null)
            try
            {
                var anims = GetComponentsInChildren<Animator>(true);
                foreach (var a in anims)
                {
                    if (!a) continue;
                    if (a.isActiveAndEnabled && a.gameObject.activeInHierarchy)
                    {
                        _anim = a;
                        break;
                    }
                }
            }
            catch
            {
            }

        // 找到了就设置基本属性 & 可选 Rebind lol
        if (_anim != null)
        {
            _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _anim.updateMode = AnimatorUpdateMode.Normal;
            _anim.applyRootMotion = false; // 避免根运动与网络位移打架
            if (forceRebind)
                try
                {
                    _anim.Rebind();
                    _anim.Update(0f);
                }
                catch
                {
                }
        }
    }


    // 服务器每帧/多帧喂目标
    public void SetTarget(Vector3 pos, Vector3 dir)
    {
        _pos = pos;
        _dir = dir;
    }

    public void SetAnim(float speed, float dirX, float dirY, int hand, bool gunReady, bool dashing)
    {
        _tSpeed = speed;
        _tDirX = dirX;
        _tDirY = dirY;
        _tHand = hand;
        _tGunReady = gunReady;
        _tDashing = dashing;

        // 首帧立刻对齐，避免卡一帧
        if (_anim && _cHand == 0 && _cSpeed == 0f && _cDirX == 0f && _cDirY == 0f)
        {
            _cSpeed = _tSpeed;
            _cDirX = _tDirX;
            _cDirY = _tDirY;
            _cHand = _tHand;
            _cGunReady = _tGunReady;
            _cDashing = _tDashing;
            ApplyNow();
        }
    }

    private void ApplyNow()
    {
        if (!_anim) return;
        _anim.SetFloat(hMoveSpeed, _cSpeed);
        _anim.SetFloat(hMoveDirX, _cDirX);
        _anim.SetFloat(hMoveDirY, _cDirY);
        _anim.SetInteger(hHandState, _cHand);
        _anim.SetBool(hGunReady, _cGunReady);
        _anim.SetBool(hDashing, _cDashing);
    }

    // 网络触发攻击动画，可直接调这个，但是是个废弃方法 来自:InitLoader
    public void PlayAttack()
    {
        if (_magic != null) _magic.OnAttack();
        if (_animctl != null) _animctl.OnAttack();
    }
}