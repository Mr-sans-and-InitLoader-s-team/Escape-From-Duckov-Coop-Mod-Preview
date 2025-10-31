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

using Duckov.Buffs;
using ItemStatsSystem;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod;

public class COOPManager
{
    public static HostPlayerApply HostPlayer_Apply;

    public static ClientPlayerApply ClientPlayer_Apply;
    public static LootNet LootNet;
    public static AIHandle AIHandle;
    public static Door Door;
    public static Destructible destructible;
    public static GrenadeM GrenadeM;
    public static HurtM HurtM;
    public static WeaponHandle WeaponHandle;
    public static Weather Weather;
    public static ClientHandle ClientHandle;
    public static PublicHandleUpdate PublicHandleUpdate;
    public static ItemHandle ItemHandle;
    public static AIHealth AIHealth;
    public static Buff_ Buff;
    public static WeaponRequest WeaponRequest;
    public static HostHandle Host_Handle;
    public static ItemRequest ItemRequest;
    private NetService Service => NetService.Instance;

    public static void InitManager()
    {
        HostPlayer_Apply = new HostPlayerApply();
        ClientPlayer_Apply = new ClientPlayerApply();
        LootNet = new LootNet();
        AIHandle = new AIHandle();
        Door = new Door();
        destructible = new Destructible();
        GrenadeM = new GrenadeM();
        HurtM = new HurtM();
        WeaponHandle = new WeaponHandle();
        Weather = new Weather();
        ClientHandle = new ClientHandle();
        PublicHandleUpdate = new PublicHandleUpdate();
        ItemHandle = new ItemHandle();
        AIHealth = new AIHealth();
        Buff = new Buff_();
        WeaponRequest = new WeaponRequest();
        Host_Handle = new HostHandle();
        ItemRequest = new ItemRequest();
    }


    public static async Task<Item> GetItemAsync(int itemId)
    {
        // Debug.Log(itemId);
        return await ItemAssetsCollection.InstantiateAsync(itemId);
    }

    public static void ChangeArmorModel(CharacterModel characterModel, Item item)
    {
        if (item != null)
        {
            var slot = characterModel.characterMainControl.CharacterItem.Slots["Armor"];
            Traverse.Create(slot).Field<Item>("content").Value = item;
        }

        if (item == null)
        {
            var socket = characterModel.ArmorSocket;
            for (var i = socket.childCount - 1; i >= 0; i--) Object.Destroy(socket.GetChild(i).gameObject);
            return;
        }

        var faceMaskSocket = characterModel.ArmorSocket;
        var itemAgent = item.AgentUtilities.CreateAgent(CharacterEquipmentController.equipmentModelHash, ItemAgent.AgentTypes.equipment);
        if (itemAgent == null)
        {
            Debug.LogError("生成的装备Item没有装备agent，Item名称：" + item.gameObject.name);
            return;
        }

        if (itemAgent != null)
        {
            itemAgent.transform.SetParent(faceMaskSocket, false);
            itemAgent.transform.localRotation = Quaternion.identity;
            itemAgent.transform.localPosition = Vector3.zero;
        }
    }


    public static void ChangeHelmatModel(CharacterModel characterModel, Item item)
    {
        if (item != null)
        {
            var slot = characterModel.characterMainControl.CharacterItem.Slots["Helmat"];
            Traverse.Create(slot).Field<Item>("content").Value = item;
        }

        if (item == null)
        {
            var socket = characterModel.HelmatSocket;
            for (var i = socket.childCount - 1; i >= 0; i--) Object.Destroy(socket.GetChild(i).gameObject);
            characterModel.CustomFace.hairSocket.gameObject.SetActive(true);
            characterModel.CustomFace.mouthPart.socket.gameObject.SetActive(true);
            return;
        }

        characterModel.CustomFace.hairSocket.gameObject.SetActive(false);
        characterModel.CustomFace.mouthPart.socket.gameObject.SetActive(false);
        var faceMaskSocket = characterModel.HelmatSocket;
        var itemAgent = item.AgentUtilities.CreateAgent(CharacterEquipmentController.equipmentModelHash, ItemAgent.AgentTypes.equipment);
        if (itemAgent == null)
        {
            Debug.LogError("生成的装备Item没有装备agent，Item名称：" + item.gameObject.name);
            return;
        }

        if (itemAgent != null)
        {
            itemAgent.transform.SetParent(faceMaskSocket, false);
            itemAgent.transform.localRotation = Quaternion.identity;
            itemAgent.transform.localPosition = Vector3.zero;
        }
    }

    public static void ChangeHeadsetModel(CharacterModel characterModel, Item item)
    {
        if (item != null)
        {
            var slot = characterModel.characterMainControl.CharacterItem.Slots["Headset"];
            Traverse.Create(slot).Field<Item>("content").Value = item;
        }

        if (item == null)
        {
            var socket = characterModel.HelmatSocket;
            for (var i = socket.childCount - 1; i >= 0; i--) Object.Destroy(socket.GetChild(i).gameObject);
            characterModel.CustomFace.hairSocket.gameObject.SetActive(true);
            characterModel.CustomFace.mouthPart.socket.gameObject.SetActive(true);
            return;
        }

        characterModel.CustomFace.hairSocket.gameObject.SetActive(false);
        characterModel.CustomFace.mouthPart.socket.gameObject.SetActive(false);
        var faceMaskSocket = characterModel.HelmatSocket;
        var itemAgent = item.AgentUtilities.CreateAgent(CharacterEquipmentController.equipmentModelHash, ItemAgent.AgentTypes.equipment);
        if (itemAgent == null)
        {
            Debug.LogError("生成的装备Item没有装备agent，Item名称：" + item.gameObject.name);
            return;
        }

        if (itemAgent != null)
        {
            itemAgent.transform.SetParent(faceMaskSocket, false);
            itemAgent.transform.localRotation = Quaternion.identity;
            itemAgent.transform.localPosition = Vector3.zero;
        }
    }

    public static void ChangeBackpackModel(CharacterModel characterModel, Item item)
    {
        if (item != null)
        {
            var slot = characterModel.characterMainControl.CharacterItem.Slots["Backpack"];
            Traverse.Create(slot).Field<Item>("content").Value = item;
        }

        if (item == null)
        {
            var socket = characterModel.BackpackSocket;
            for (var i = socket.childCount - 1; i >= 0; i--) Object.Destroy(socket.GetChild(i).gameObject);
            return;
        }

        var faceMaskSocket = characterModel.BackpackSocket;
        var itemAgent = item.AgentUtilities.CreateAgent(CharacterEquipmentController.equipmentModelHash, ItemAgent.AgentTypes.equipment);
        if (itemAgent == null)
        {
            Debug.LogError("生成的装备Item没有装备agent，Item名称：" + item.gameObject.name);
            return;
        }

        if (itemAgent != null)
        {
            itemAgent.transform.SetParent(faceMaskSocket, false);
            itemAgent.transform.localRotation = Quaternion.identity;
            itemAgent.transform.localPosition = Vector3.zero;
        }
    }


    public static void ChangeFaceMaskModel(CharacterModel characterModel, Item item)
    {
        if (item != null)
        {
            var slot = characterModel.characterMainControl.CharacterItem.Slots["FaceMask"];
            Traverse.Create(slot).Field<Item>("content").Value = item;
        }

        if (item == null)
        {
            var socket = characterModel.FaceMaskSocket;
            for (var i = socket.childCount - 1; i >= 0; i--) Object.Destroy(socket.GetChild(i).gameObject);
            return;
        }

        var faceMaskSocket = characterModel.FaceMaskSocket;
        var itemAgent = item.AgentUtilities.CreateAgent(CharacterEquipmentController.equipmentModelHash, ItemAgent.AgentTypes.equipment);
        if (itemAgent == null)
        {
            Debug.LogError("生成的装备Item没有装备agent，Item名称：" + item.gameObject.name);
            return;
        }

        if (itemAgent != null)
        {
            itemAgent.transform.SetParent(faceMaskSocket, false);
            itemAgent.transform.localRotation = Quaternion.identity;
            itemAgent.transform.localPosition = Vector3.zero;
        }
    }

    //public static void ChangeWeaponModel(CharacterModel characterModel, Item item, HandheldSocketTypes handheldSocket)
    //{
    //	if (item == null)
    //	{
    //		if(handheldSocket == HandheldSocketTypes.normalHandheld)
    //              {
    //			Transform socket = characterModel.RightHandSocket;
    //			for (int i = socket.childCount - 1; i >= 0; i--)
    //			{
    //				UnityEngine.Object.Destroy(socket.GetChild(i).gameObject);
    //			}
    //		}
    //		if (handheldSocket == HandheldSocketTypes.meleeWeapon)
    //		{
    //			Transform socket = characterModel.MeleeWeaponSocket;
    //			for (int i = socket.childCount - 1; i >= 0; i--)
    //			{
    //				UnityEngine.Object.Destroy(socket.GetChild(i).gameObject);
    //			}
    //		}
    //		if (handheldSocket == HandheldSocketTypes.leftHandSocket)
    //		{
    //			Transform socket = characterModel.LefthandSocket;
    //			for (int i = socket.childCount - 1; i >= 0; i--)
    //			{
    //				UnityEngine.Object.Destroy(socket.GetChild(i).gameObject);
    //			}
    //		}

    //		return;
    //	}
    //	Transform transform = null;
    //	if (handheldSocket == HandheldSocketTypes.normalHandheld)
    //          {
    //		transform = characterModel.RightHandSocket;
    //	}
    //	if (handheldSocket == HandheldSocketTypes.meleeWeapon)
    //	{
    //		transform = characterModel.MeleeWeaponSocket;
    //	}
    //	if (handheldSocket == HandheldSocketTypes.leftHandSocket)
    //	{
    //		transform = characterModel.LefthandSocket;
    //	}

    //	ItemAgent itemAgent = item.CreateHandheldAgent();

    //	var currentHoldItemAgent = (itemAgent as global::DuckovItemAgent);
    //	if (currentHoldItemAgent == null)
    //	{
    //		global::UnityEngine.Object.Destroy(itemAgent.gameObject);
    //		return;
    //	}

    //	currentHoldItemAgent.transform.SetParent(transform, false);
    //	currentHoldItemAgent.transform.localPosition = global::UnityEngine.Vector3.zero;
    //	currentHoldItemAgent.transform.localRotation = global::UnityEngine.Quaternion.identity;

    //}


    public static async Task<Grenade> GetGrenadePrefabByItemIdAsync(int itemId)
    {
        Item item = null;
        try
        {
            item = await GetItemAsync(itemId);
            if (item == null) return null;
            var skill = item.GetComponent<Skill_Grenade>();
            return skill != null ? skill.grenadePfb : null;
        }
        finally
        {
            if (item != null && item.gameObject)
                Object.Destroy(item.gameObject);
        }
    }

    public static Grenade GetGrenadePrefabByItemIdBlocking(int itemId)
    {
        return GetGrenadePrefabByItemIdAsync(itemId).GetAwaiter().GetResult();
    }

    public static void EnsureRemotePlayersHaveHealthBar()
    {
        foreach (var kv in NetService.Instance.remoteCharacters)
        {
            var go = kv.Value;
            if (!go) continue;
            if (!go.GetComponent<AutoRequestHealthBar>())
                go.AddComponent<AutoRequestHealthBar>(); // Start() 会自动申请血条
        }
    }

    public static async UniTask<Buff> ResolveBuffAsync(int weaponTypeId, int buffId)
    {
        // 1) 从武器里拿（最稳，因为不同 id 可能共用/复用）
        if (weaponTypeId > 0)
            try
            {
                var item = await ItemAssetsCollection.InstantiateAsync(weaponTypeId);
                var gunAgent = item?.AgentUtilities?.ActiveAgent as ItemAgent_Gun;
                var prefab = gunAgent?.GunItemSetting?.buff;
                if (prefab != null) return prefab;
            }
            catch
            {
            }

        // 2) 兜底：在已加载的 Buff 资源里按 id 匹配（适用于手雷/技能以外的通用 Buff）
        try
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Buff>())
                if (b && b.ID == buffId)
                    return b;
        }
        catch
        {
        }

        return null;
    }

    public static void ChangeWeaponModel(CharacterModel characterModel, Item item, HandheldSocketTypes handheldSocket)
    {
        if (characterModel == null) return;

        // 解析目标手持插槽（右手/左手/近战等）
        var tSocket = ResolveHandheldSocket(characterModel, handheldSocket);
        if (tSocket == null) return;

        // 不管换上还是清空，先把该 socket 槽位里的旧可视对象清掉，避免残留
        ClearChildren(tSocket);

        if (item == null) return;

        ItemAgent itemAgent = null;
        try
        {
            itemAgent = item.ActiveAgent;
        }
        catch
        {
        }

        if (itemAgent == null)
            try
            {
                itemAgent = item.CreateHandheldAgent();
            }
            catch (Exception e)
            {
                Debug.Log($"[COOP] CreateHandheldAgent 失败：{e.Message}");
                return;
            }

        if (itemAgent == null) return;

        // 告诉 DuckovItemAgent 目标要挂到哪个手持槽
        var duck = itemAgent.GetComponent<DuckovItemAgent>();
        if (duck != null)
            duck.handheldSocket = handheldSocket;

        // 设为 socket 的子物体并归零局部变换
        var tr = itemAgent.transform;
        tr.SetParent(tSocket, true);
        tr.localPosition = Vector3.zero;
        tr.localRotation = Quaternion.identity;
        tr.localScale = Vector3.one;

        var go = itemAgent.gameObject;
        if (go && !go.activeSelf) go.SetActive(true);
    }

    //public static void ChangeWeaponModel(CharacterModel characterModel, Item item, HandheldSocketTypes handheldSocket)
    //{
    //	if (!characterModel) return;

    //	// 解析插槽，带兜底
    //	var socket = ResolveHandheldSocket(characterModel, handheldSocket);
    //	if (!socket)
    //	{
    //		Debug.LogWarning($"[COOP] ChangeWeaponModel: socket '{handheldSocket}' not found on model '{characterModel?.name}'.");
    //		return;
    //	}

    //	// 清空：item==null 表示仅清该插槽
    //	if (item == null)
    //	{
    //		ClearChildren(socket);
    //		return;
    //	}

    //	// 创建手持 Agent（某些物品可能返回 null）
    //	ItemAgent itemAgent = null;
    //	try { itemAgent = item.CreateHandheldAgent(); }
    //	catch (Exception e)
    //	{
    //		Debug.LogError($"[COOP] CreateHandheldAgent failed for item '{item?.name}': {e}");
    //		return;
    //	}
    //	if (!itemAgent) return; // 别再 Destroy(null.gameObject) 了

    //	// 必须是 DuckovItemAgent（否则挂不上手）
    //	var duck = itemAgent as DuckovItemAgent ?? itemAgent.GetComponent<DuckovItemAgent>();
    //	if (!duck)
    //	{
    //		if (itemAgent && itemAgent.gameObject) UnityEngine.Object.Destroy(itemAgent.gameObject);
    //		Debug.LogWarning($"[COOP] Handheld agent isn't DuckovItemAgent: {item?.name}");
    //		return;
    //	}

    //	// 标记插槽类型（有些逻辑会用到）
    //	try { duck.handheldSocket = handheldSocket; } catch { }


    //	duck.transform.SetParent(socket, false);
    //	duck.transform.localPosition = Vector3.zero;
    //	duck.transform.localRotation = Quaternion.identity;

    //	characterModel.characterMainControl.ChangeHoldItem(item);
    //}

    private static Transform ResolveHandheldSocket(CharacterModel model, HandheldSocketTypes socket)
    {
        switch (socket)
        {
            case HandheldSocketTypes.meleeWeapon:
                return model.MeleeWeaponSocket ? model.MeleeWeaponSocket
                    : model.RightHandSocket ? model.RightHandSocket : model.LefthandSocket;
            case HandheldSocketTypes.leftHandSocket:
                return model.LefthandSocket ? model.LefthandSocket
                    : model.RightHandSocket ? model.RightHandSocket : model.MeleeWeaponSocket;
            case HandheldSocketTypes.normalHandheld:
            default:
                return model.RightHandSocket ? model.RightHandSocket
                    : model.MeleeWeaponSocket ? model.MeleeWeaponSocket : model.LefthandSocket;
        }
    }

    private static void ClearChildren(Transform t)
    {
        if (!t) return;
        for (var i = t.childCount - 1; i >= 0; --i)
        {
            var c = t.GetChild(i);
            if (c) Object.Destroy(c.gameObject);
        }
    }

    private static Animator ResolveRemoteAnimator(GameObject remoteObj)
    {
        var cmc = remoteObj.GetComponent<CharacterMainControl>();
        if (cmc == null || cmc.characterModel == null) return null;
        var model = cmc.characterModel;

        var mb = model.GetComponent<CharacterAnimationControl_MagicBlend>();
        if (mb != null && mb.animator != null) return mb.animator;

        var cac = model.GetComponent<CharacterAnimationControl>();
        if (cac != null && cac.animator != null) return cac.animator;

        // 兜底：直接拿模型上的 Animator
        return model.GetComponent<Animator>();
    }

    public static void StripAllHandItems(CharacterMainControl cmc)
    {
        if (!cmc) return;
        var model = cmc.characterModel;
        if (!model) return;

        void KillChildren(Transform root)
        {
            if (!root) return;
            try
            {
                foreach (var g in root.GetComponentsInChildren<ItemAgent_Gun>(true))
                    if (g && g.gameObject)
                        Object.Destroy(g.gameObject);

                foreach (var m in root.GetComponentsInChildren<ItemAgent_MeleeWeapon>(true))
                    if (m && m.gameObject)
                        Object.Destroy(m.gameObject);

                foreach (var x in root.GetComponentsInChildren<DuckovItemAgent>(true))
                    if (x && x.gameObject)
                        Object.Destroy(x.gameObject);

                var baseType = typeof(Component).Assembly.GetType("ItemAgent");
                if (baseType != null)
                    foreach (var c in root.GetComponentsInChildren(baseType, true))
                        if (c is Component comp && comp.gameObject)
                            Object.Destroy(comp.gameObject);
            }
            catch
            {
            }
        }

        try
        {
            KillChildren(model.RightHandSocket);
        }
        catch
        {
        }

        try
        {
            KillChildren(model.LefthandSocket);
        }
        catch
        {
        }

        try
        {
            KillChildren(model.MeleeWeaponSocket);
        }
        catch
        {
        }
    }
}