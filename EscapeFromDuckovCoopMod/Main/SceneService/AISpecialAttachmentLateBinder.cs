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

using UnityEngine;

namespace EscapeFromDuckovCoopMod;

internal sealed class AISpecialAttachmentLateBinder : MonoBehaviour
{
    private const int MaxAttempts = 300;

    private CharacterMainControl _cmc;
    private int _attempts;

    public void Init(CharacterMainControl cmc)
    {
        _cmc = cmc;
    }

    private void Awake()
    {
        if (!_cmc)
            _cmc = GetComponent<CharacterMainControl>();
    }

    private void Update()
    {
        if (!_cmc)
        {
            Destroy(this);
            return;
        }

        if (AISpecialAttachmentLateBinderUtil.Ensure(_cmc))
        {
            Destroy(this);
            return;
        }

        _attempts++;
        if (_attempts >= MaxAttempts)
        {
            Destroy(this);
        }
    }
}

internal static class AISpecialAttachmentLateBinderUtil
{
    internal static bool Ensure(CharacterMainControl cmc)
    {
        if (!cmc || !cmc.characterPreset) return false;

        var bases = cmc.characterPreset.specialAttachmentBases;
        if (bases == null || bases.Count == 0) return true;

        var ai = ResolveController(cmc);
        if (!ai) return false;

        var existing = cmc.GetComponentsInChildren<AISpecialAttachmentBase>(true);
        var allReady = true;

        foreach (var specialAttachmentBasis in bases)
        {
            if (!specialAttachmentBasis) continue;

            AISpecialAttachmentBase attachmentInstance = null;
            foreach (var attachment in existing)
            {
                if (attachment && attachment.GetType() == specialAttachmentBasis.GetType())
                {
                    attachmentInstance = attachment;
                    break;
                }
            }

            if (!attachmentInstance)
            {
                allReady = false;
                attachmentInstance = UnityEngine.Object.Instantiate(specialAttachmentBasis, cmc.transform);
                attachmentInstance.transform.localPosition = Vector3.zero;
                attachmentInstance.transform.localRotation = Quaternion.identity;
            }

            if (!attachmentInstance.aiCharacterController)
            {
                allReady = false;
                try
                {
                    attachmentInstance.Init(ai, cmc);
                }
                catch
                {
                }
            }
        }

        return allReady;
    }

    private static AICharacterController ResolveController(CharacterMainControl cmc)
    {
        var ai = cmc.aiCharacterController;
        if (!ai)
            ai = cmc.GetComponent<AICharacterController>();
        if (!ai)
            ai = cmc.GetComponentInChildren<AICharacterController>(true);
        if (ai && !cmc.aiCharacterController)
            cmc.aiCharacterController = ai;

        if (!cmc.aiCharacterController)
        {
            var aiBase = Traverse.Create(cmc.characterPreset).Field<AICharacterController>("aiController").Value;
            if (aiBase)
            {
                ai = (cmc.aiCharacterController = UnityEngine.Object.Instantiate(aiBase));
                ai.Init(cmc, cmc.transform.position);
            }
        }

        return ai;
    }
}
