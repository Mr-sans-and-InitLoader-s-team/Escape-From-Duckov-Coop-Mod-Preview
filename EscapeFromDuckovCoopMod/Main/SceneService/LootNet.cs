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

﻿using HarmonyLib;
using ItemStatsSystem;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EscapeFromDuckovCoopMod
{
    public class LootNet
    {
        private NetService Service => NetService.Instance;

        private bool IsServer => Service != null && Service.IsServer;
        private NetManager netManager => Service?.netManager;
        private NetDataWriter writer => Service?.writer;
        private NetPeer connectedPeer => Service?.connectedPeer;
        private PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
        private bool networkStarted => Service != null && Service.networkStarted;
        // 暴露客户端是否正在应用服务器下发的容器快照
        public bool ApplyingLootState => _applyingLootState;

        // ====== Lootbox 同步：运行期标识/状态 ======
        public bool _applyingLootState = false;         // 客户端：应用主机快照时抑制 Prefix
        public bool _serverApplyingLoot = false;        // 主机：处理客户端请求时抑制 Postfix 二次广播

        public readonly Dictionary<uint, Item> _cliPendingPut = new Dictionary<uint, Item>();

        private readonly Dictionary<uint, ItemStatsSystem.Item> _cliPendingSlotPlug = new Dictionary<uint, ItemStatsSystem.Item>();

        // 客户端：本地 put 请求的 token -> Item 实例（用于 put 成功后从玩家背包删去这个本地实例）
        public uint _nextLootToken = 1;

        public readonly Dictionary<ItemStatsSystem.Item, (ItemStatsSystem.Item newItem,
                                                ItemStatsSystem.Inventory destInv, int destPos,
                                                ItemStatsSystem.Items.Slot destSlot)>
        _cliSwapByVictim = new Dictionary<ItemStatsSystem.Item, (ItemStatsSystem.Item, ItemStatsSystem.Inventory, int, ItemStatsSystem.Items.Slot)>();

        public void Client_RequestLootState(Inventory lootInv)
        {
            if (!networkStarted || IsServer || connectedPeer == null || lootInv == null) return;

            if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;

            var w = writer;
            if (w == null) return;
            w.Reset();
            w.Put((byte)Op.LOOT_REQ_OPEN);

            // 原有三元标识（scene + posKey + instanceId）
            LootManager.Instance.PutLootId(w, lootInv);

            // 请求版本 + 位置提示（cm 压缩）
            byte reqVer = 1;
            w.Put(reqVer);

            Vector3 pos;
            if (!LootManager.Instance.TryGetLootboxWorldPos(lootInv, out pos)) pos = Vector3.zero;
            w.PutV3cm(pos);

            if (connectedPeer != null)
            {
                connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.SendData(w.Data, w.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }


        // 主机：应答快照（发给指定 peer 或广播）
        public void Server_SendLootboxState(NetPeer toPeer, Inventory inv)
        {
            // ★ 新增：仅当群发(toPeer==null)时才受静音窗口影响
            if (toPeer == null && LootManager.Instance.Server_IsLootMuted(inv)) return;

            if (!IsServer || inv == null) return;
            if (!LootboxDetectUtil.IsLootboxInventory(inv) || LootboxDetectUtil.IsPrivateInventory(inv))
                return;

            var w = new NetDataWriter();
            w.Put((byte)Op.LOOT_STATE);
            LootManager.Instance.PutLootId(w, inv);

            int capacity = inv.Capacity;
            w.Put(capacity);

            // 统计非空格子数量
            int count = 0;
            var content = inv.Content;
            for (int i = 0; i < content.Count; ++i)
                if (content[i] != null) count++;
            w.Put(count);

            // 逐个写：位置 + 物品快照
            for (int i = 0; i < content.Count; ++i)
            {
                var it = content[i];
                if (it == null) continue;
                w.Put(i);
                ItemTool.WriteItemSnapshot(w, it);
            }

            if (toPeer != null) toPeer.Send(w, DeliveryMethod.ReliableOrdered);
            else CoopTool.BroadcastReliable(w);
        }


        public void Client_ApplyLootboxState(NetPacketReader r)
        {
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();

            int capacity = r.GetInt();
            int count = r.GetInt();

            Inventory inv = null;

            // ★ 1) 优先用稳定 ID 解析
            if (lootUid >= 0 && LootManager.Instance._cliLootByUid.TryGetValue(lootUid, out var byUid) && byUid) inv = byUid;

            // 2) 失败再走旧逻辑（posKey / 扫场景）
            if (inv == null && (!LootManager.Instance.TryResolveLootById(scene, posKey, iid, out inv) || inv == null))
            {
                if (LootboxDetectUtil.IsPrivateInventory(inv)) return;
                // ★ 若带了稳定 ID，则缓存到 uid 下；否则就按 posKey 缓存（次要）
                var list = new List<(int pos, ItemSnapshot snap)>(count);
                for (int k = 0; k < count; ++k)
                {
                    int p = r.GetInt();
                    var snap = ItemTool.ReadItemSnapshot(r);
                    list.Add((p, snap));
                }

                if (lootUid >= 0)
                    LootManager.Instance._pendingLootStatesByUid[lootUid] = (capacity, list);
                else
                {
                    // 旧路径的兜底（可选）：如果你之前已经做了 posKey 缓存，这里也可以顺手放一份
                }
                return;
            }

            if (LootboxDetectUtil.IsPrivateInventory(inv)) return;

            // ★ 容量安全阈值：防止因为误匹配把 UI 撑爆（真正根因是冲突/错配）
            capacity = Mathf.Clamp(capacity, 1, 128);

            _applyingLootState = true;
            try
            {
                inv.SetCapacity(capacity);
                inv.Loading = false;

                for (int i = inv.Content.Count - 1; i >= 0; --i)
                {
                    Item removed; inv.RemoveAt(i, out removed);
                    if (removed) Object.Destroy(removed.gameObject);
                }

                for (int k = 0; k < count; ++k)
                {
                    int pos = r.GetInt();
                    var snap = ItemTool.ReadItemSnapshot(r);
                    var item = ItemTool.BuildItemFromSnapshot(snap);
                    if (item == null) continue;
                    inv.AddAt(item, pos);
                }
            }
            finally { _applyingLootState = false; }


            try
            {
                var lv = Duckov.UI.LootView.Instance;
                if (lv && lv.open && ItemTool.ReferenceEquals(lv.TargetInventory, inv))
                {
                    // 轻量刷新：不强制重开，只更新细节/按钮与容量文本
                    AccessTools.Method(typeof(Duckov.UI.LootView), "RefreshDetails")?.Invoke(lv, null);
                    AccessTools.Method(typeof(Duckov.UI.LootView), "RefreshPickAllButton")?.Invoke(lv, null);
                    AccessTools.Method(typeof(Duckov.UI.LootView), "RefreshCapacityText")?.Invoke(lv, null);
                }
            }
            catch { }

        }



        // Mod.cs
        public void Client_SendLootPutRequest(Inventory lootInv, Item item, int preferPos)
        {
            if (!networkStarted || IsServer || connectedPeer == null || lootInv == null || item == null) return;

            if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;


            // 同一物品的在途 PUT 防重
            foreach (var kv in _cliPendingPut)
            {
                var pending = kv.Value;
                if (pending && ItemTool.ReferenceEquals(pending, item))
                {
                    // 已经有一个在途请求了，丢弃重复点击
                    Debug.Log("[LOOT] Duplicate PUT suppressed for item: " + item.DisplayName);
                    return;
                }
            }

            uint token = _nextLootToken++;
            _cliPendingPut[token] = item;

            var w = writer;
            if (w == null) return;
            w.Reset();
            w.Put((byte)Op.LOOT_REQ_PUT);
            LootManager.Instance.PutLootId(w, lootInv);
            w.Put(preferPos);
            w.Put(token);
            ItemTool.WriteItemSnapshot(w, item);
            
            if (connectedPeer != null)
            {
                connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                var hybrid = EscapeFromDuckovCoopMod.Net.Steam.HybridNetworkService.Instance;
                if (hybrid != null && hybrid.IsConnected)
                {
                    hybrid.SendData(w.Data, w.Length, DeliveryMethod.ReliableOrdered);
                }
            }
        }


        // 作用：发送 TAKE 请求（携带目标信息）；客户端暂不落位，等回包
        // 兼容旧调用：不带目的地
        public void Client_SendLootTakeRequest(ItemStatsSystem.Inventory lootInv, int position)
        {
            Client_SendLootTakeRequest(lootInv, position, null, -1, null);
        }

        // 新：带目的地（背包+格 或 装备槽）
        public uint Client_SendLootTakeRequest(
            ItemStatsSystem.Inventory lootInv,
            int position,
            ItemStatsSystem.Inventory destInv,
            int destPos,
            ItemStatsSystem.Items.Slot destSlot)
        {
            if (!networkStarted || IsServer || connectedPeer == null || lootInv == null) return 0;
            if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return 0;

            // 目标如果还是“容器”，就当作没指定（容器内换位由主机权威刷新）
            if (destInv != null && LootboxDetectUtil.IsLootboxInventory(destInv))
                destInv = null;

            uint token = _nextLootToken++;

            if (destInv != null || destSlot != null)
                LootManager.Instance._cliPendingTake[token] = new PendingTakeDest
                {
                    inv = destInv,
                    pos = destPos,
                    slot = destSlot,
                    //记录来源容器与来源格子（用于交换时回填）
                    srcLoot = lootInv,
                    srcPos = position
                };

            var w = writer;
            //if (w == null) return;
            w.Reset();
            w.Put((byte)Op.LOOT_REQ_TAKE);
            LootManager.Instance.PutLootId(w, lootInv); // 只写 inv 身份（scene/posKey/instance/uid）
            w.Put(position);
            w.Put(token);          // 附带 token
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
            return token;
        }


        // 主机：处理 PUT（客户端 -> 主机）
        public void Server_HandleLootPutRequest(NetPeer peer, NetPacketReader r)
        {
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();   // 对齐 PutLootId 多写的稳定ID
            int prefer = r.GetInt();
            uint token = r.GetUInt();

            ItemSnapshot snap;
            try
            {
                snap = ItemTool.ReadItemSnapshot(r);
            }
            catch (DecoderFallbackException ex)
            {
                Debug.LogError("[LOOT][PUT] snapshot decode failed: " + ex.Message);
                Server_SendLootDeny(peer, "bad_snapshot");
                return;
            }
            catch (Exception ex)
            {
                Debug.LogError("[LOOT][PUT] snapshot parse failed: " + ex);
                Server_SendLootDeny(peer, "bad_snapshot");
                return;
            }

            // ★ 可选：如果未来客户端也会带有效的 lootUid，可优先用它定位
            Inventory inv = null;
            if (lootUid >= 0) LootManager.Instance._srvLootByUid.TryGetValue(lootUid, out inv);
            if (inv == null && !LootManager.Instance.TryResolveLootById(scene, posKey, iid, out inv))
            {
                Server_SendLootDeny(peer, "no_inv"); return;
            }

            if (LootboxDetectUtil.IsPrivateInventory(inv)) { Server_SendLootDeny(peer, "no_inv"); return; }

            //if (!TryResolveLootById(scene, posKey, iid, out var inv) || inv == null)
            //{ Server_SendLootDeny(peer, "no_inv"); return; }

            var item = ItemTool.BuildItemFromSnapshot(snap);
            if (item == null) { Server_SendLootDeny(peer, "bad_item"); return; }

            _serverApplyingLoot = true;
            bool ok = false;
            try
            {
                ok = ItemUtilities.AddAndMerge(inv, item, prefer);
                if (!ok) Object.Destroy(item.gameObject);
            }
            catch (Exception ex)
            {
                Debug.LogError("[LOOT][PUT] AddAndMerge exception: " + ex);
                ok = false;
            }
            finally { _serverApplyingLoot = false; }

            if (!ok) { Server_SendLootDeny(peer, "add_fail"); return; }

            var ack = new NetDataWriter();
            ack.Put((byte)Op.LOOT_PUT_OK);
            ack.Put(token);
            peer.Send(ack, DeliveryMethod.ReliableOrdered);

            Server_SendLootboxState(null, inv);
        }


        public void Server_HandleLootTakeRequest(NetPeer peer, NetPacketReader r)
        {
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();      // 对齐 PutLootId
            int position = r.GetInt();
            uint token = r.GetUInt();      // 读取 token

            Inventory inv = null;
            if (lootUid >= 0) LootManager.Instance._srvLootByUid.TryGetValue(lootUid, out inv);
            if (inv == null && !LootManager.Instance.TryResolveLootById(scene, posKey, iid, out inv))
            { Server_SendLootDeny(peer, "no_inv"); return; }

            if (LootboxDetectUtil.IsPrivateInventory(inv)) { Server_SendLootDeny(peer, "no_inv"); return; }


            _serverApplyingLoot = true;
            bool ok = false; Item removed = null;
            try
            {
                if (position >= 0 && position < inv.Capacity)
                {
                    try { ok = inv.RemoveAt(position, out removed); }
                    catch (ArgumentOutOfRangeException) { ok = false; removed = null; }
                }
            }
            finally { _serverApplyingLoot = false; }

            if (!ok || removed == null)
            {
                Server_SendLootDeny(peer, "rm_fail");
                Server_SendLootboxState(peer, inv); // ⬅️ 刷新请求方 UI 的索引认知
                return;
            }

            var wCli = new NetDataWriter();
            wCli.Put((byte)Op.LOOT_TAKE_OK);
            wCli.Put(token);                 // ★ 回 token
            ItemTool.WriteItemSnapshot(wCli, removed);
            peer.Send(wCli, DeliveryMethod.ReliableOrdered);

            try { UnityEngine.Object.Destroy(removed.gameObject); } catch { }
            Server_SendLootboxState(null, inv);
        }

        public void Server_SendLootDeny(NetPeer peer, string reason)
        {
            var w = new NetDataWriter();
            w.Put((byte)Op.LOOT_DENY);
            w.Put(reason ?? "");
            peer?.Send(w, DeliveryMethod.ReliableOrdered);
        }

        // 客户端：收到 PUT_OK -> 把“本地发起的那件物品”从自己背包删掉
        public void Client_OnLootPutOk(NetPacketReader r)
        {
            uint token = r.GetUInt();

            if (_cliPendingSlotPlug.TryGetValue(token, out var victim) && victim)
            {
                try
                {
                    var srcInv = victim.InInventory;
                    if (srcInv) { try { srcInv.RemoveItem(victim); } catch { } }
                    UnityEngine.Object.Destroy(victim.gameObject);
                }
                catch { }
                finally
                {
                    _cliPendingSlotPlug.Remove(token);
                }
                return; // 不再继续走“普通 PUT”流程
            }

            if (_cliPendingPut.TryGetValue(token, out var localItem) && localItem)
            {
                _cliPendingPut.Remove(token);

                // —— 交换路径：这次 PUT 的 localItem 是否正是我们等待交换的 victim？——
                if (_cliSwapByVictim.TryGetValue(localItem, out var ctx))
                {
                    _cliSwapByVictim.Remove(localItem);

                    // 1) victim 已经成功 PUT 到容器：本地把它清理掉
                    try { localItem.Detach(); } catch { }
                    try { UnityEngine.Object.Destroy(localItem.gameObject); } catch { }

                    // 2) 把“新物”真正落位（槽或背包格）
                    try
                    {
                        if (ctx.destSlot != null)
                        {
                            if (ctx.destSlot.CanPlug(ctx.newItem))
                                ctx.destSlot.Plug(ctx.newItem, out var _);
                        }
                        else if (ctx.destInv != null && ctx.destPos >= 0)
                        {
                            // 目标格此时应为空（victim 已被 PUT 走）
                            ctx.destInv.AddAt(ctx.newItem, ctx.destPos);
                        }
                    }
                    catch { }

                    // 3) 清理可能遗留的同物品 pending
                    var toRemove = new List<uint>();
                    foreach (var kv in _cliPendingPut)
                        if (!kv.Value || ItemTool.ReferenceEquals(kv.Value, localItem)) toRemove.Add(kv.Key);
                    foreach (var k in toRemove) _cliPendingPut.Remove(k);

                    return; // 交换流程结束
                }

                // —— 普通 PUT 成功：维持你原有的清理逻辑 —— 
                try { localItem.Detach(); } catch { }
                try { UnityEngine.Object.Destroy(localItem.gameObject); } catch { }

                var stale = new List<uint>();
                foreach (var kv in _cliPendingPut)
                    if (!kv.Value || ItemTool.ReferenceEquals(kv.Value, localItem)) stale.Add(kv.Key);
                foreach (var k in stale) _cliPendingPut.Remove(k);
            }
        }


        public void Client_OnLootTakeOk(NetPacketReader r)
        {
            uint token = r.GetUInt();

            // 1) 还原物品
            var snap = ItemTool.ReadItemSnapshot(r);
            var newItem = ItemTool.BuildItemFromSnapshot(snap);
            if (newItem == null) return;

            // —— 取出期望目的地（可能为空）——
            PendingTakeDest dest;
            if (LootManager.Instance._cliPendingTake.TryGetValue(token, out dest))
                LootManager.Instance._cliPendingTake.Remove(token);
            else
                dest = default;

            // —— 小工具A：不入队、不打 token 的“放回来源容器”——
            // 注意参数名用 srcInfo，避免与上面的 dest 冲突（修复 CS0136）
            void PutBackToSource_NoTrack(ItemStatsSystem.Item item, PendingTakeDest srcInfo)
            {
                var loot = srcInfo.srcLoot != null ? srcInfo.srcLoot
                          : (Duckov.UI.LootView.Instance ? Duckov.UI.LootView.Instance.TargetInventory : null);
                int preferPos = srcInfo.srcPos >= 0 ? srcInfo.srcPos : -1;

                try
                {
                    if (networkStarted && !IsServer && connectedPeer != null && loot != null && item != null)
                    {
                        var w = writer;
                        if (w == null) return;
                        w.Reset();
                        w.Put((byte)Op.LOOT_REQ_PUT);
                        LootManager.Instance.PutLootId(w, loot);
                        w.Put(preferPos);
                        w.Put((uint)0);              // 不占用 _cliPendingPut，避免 Duplicate PUT
                        ItemTool.WriteItemSnapshot(w, item);
                        connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
                    }
                }
                catch { }

                // 本地立刻清掉临时实例，防止“幽灵物品”
                try { item.Detach(); } catch { }
                try { UnityEngine.Object.Destroy(item.gameObject); } catch { }

                // 请求刷新容器状态
                try
                {
                    var lv = Duckov.UI.LootView.Instance;
                    var inv = lv ? lv.TargetInventory : null;
                    if (inv) Client_RequestLootState(inv);
                }
                catch { }
            }

            // 2) 容器内“重排/换位”：有标记则直接 PUT 回目标格
            if (LootManager.Instance._cliPendingReorder.TryGetValue(token, out var reo))
            {
                LootManager.Instance._cliPendingReorder.Remove(token);
                Client_SendLootPutRequest(reo.inv, newItem, reo.pos);
                return;
            }

            // 3) 目标是装备槽：尝试直插或交换；失败则拒绝（放回来源容器）
            if (dest.slot != null)
            {
                ItemStatsSystem.Item victim = null;
                try { victim = dest.slot.Content; } catch { }

                if (victim != null)
                {
                    _cliSwapByVictim[victim] = (newItem, null, -1, dest.slot);
                    var srcLoot = dest.srcLoot ?? (Duckov.UI.LootView.Instance ? Duckov.UI.LootView.Instance.TargetInventory : null);
                    Client_SendLootPutRequest(srcLoot, victim, dest.srcPos);
                    return;
                }
                else
                {
                    try
                    {
                        if (dest.slot.CanPlug(newItem) && dest.slot.Plug(newItem, out var _))
                            return; // 穿戴成功
                    }
                    catch { }

                    // 插槽不兼容/失败：拒绝并放回
                    PutBackToSource_NoTrack(newItem, dest);
                    return;
                }
            }

            // 4) 目标是具体背包：AddAt/合并/普通加入；失败则拒绝并放回
            if (dest.inv != null)
            {
                ItemStatsSystem.Item victim = null;
                try { if (dest.pos >= 0) victim = dest.inv.GetItemAt(dest.pos); } catch { }

                if (dest.pos >= 0 && victim != null)
                {
                    _cliSwapByVictim[victim] = (newItem, dest.inv, dest.pos, null);
                    var srcLoot = dest.srcLoot ?? (Duckov.UI.LootView.Instance ? Duckov.UI.LootView.Instance.TargetInventory : null);
                    Client_SendLootPutRequest(srcLoot, victim, dest.srcPos);
                    return;
                }

                try { if (dest.pos >= 0 && dest.inv.AddAt(newItem, dest.pos)) return; } catch { }
                try { if (global::ItemUtilities.AddAndMerge(dest.inv, newItem, UnityEngine.Mathf.Max(0, dest.pos))) return; } catch { }
                try { if (dest.inv.AddItem(newItem)) return; } catch { }

                // 背包放不下：拒绝并放回来源容器（绝不落地）
                PutBackToSource_NoTrack(newItem, dest);
                return;
            }

            // 5) 未指定目的地：尝试主背包；失败则拒绝并放回
            var mc = global::LevelManager.Instance ? global::LevelManager.Instance.MainCharacter : null;
            var backpack = mc ? (mc.CharacterItem != null ? mc.CharacterItem.Inventory : null) : null;

            if (backpack != null)
            {
                try { if (global::ItemUtilities.AddAndMerge(backpack, newItem, 0)) return; } catch { }
                try { if (backpack.AddItem(newItem)) return; } catch { }
            }

            // 主背包也塞不进：拒绝并放回
            PutBackToSource_NoTrack(newItem, dest);
        }

        public static void Client_ApplyLootVisibility(Dictionary<int, bool> vis)
        {
            try
            {
                var core = Duckov.Scenes.MultiSceneCore.Instance;
                if (core == null || vis == null) return;

                foreach (var kv in vis)
                    core.inLevelData[kv.Key] = kv.Value; // 没有就加，有就覆盖

                // 刷新当前场景已存在的 LootBoxLoader 显示
                var loaders = UnityEngine.Object.FindObjectsOfType<Duckov.Utilities.LootBoxLoader>(true);
                foreach (var l in loaders)
                {
                    try
                    {
                        int k = LootManager.Instance.ComputeLootKey(l.transform);
                        if (vis.TryGetValue(k, out bool on))
                            l.gameObject.SetActive(on);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void Server_HandleLootSlotPlugRequest(NetPeer peer, NetPacketReader r)
        {
            // 1) 容器定位
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();
            var inv = LootManager.Instance.ResolveLootInv(scene, posKey, iid, lootUid);
            if (inv == null || LootboxDetectUtil.IsPrivateInventory(inv)) { Server_SendLootDeny(peer, "no_inv"); return; }

            // 2) 目标主件 + 槽位
            var master = LootManager.Instance.ReadItemRef(r, inv);
            string slotKey = r.GetString();
            if (!master) { Server_SendLootDeny(peer, "bad_weapon"); Server_SendLootboxState(peer, inv); return; }
            var dstSlot = master?.Slots?.GetSlot(slotKey);
            if (dstSlot == null) { Server_SendLootDeny(peer, "bad_slot"); Server_SendLootboxState(peer, inv); return; }

            // 3) 源
            bool srcInLoot = r.GetBool();
            Item srcItem = null;
            uint token = 0;
            ItemSnapshot snap = default;

            if (srcInLoot)
            {
                srcItem = LootManager.Instance.ReadItemRef(r, inv);
                if (!srcItem)
                {
                    Server_SendLootDeny(peer, "bad_src");
                    Server_SendLootboxState(peer, inv);   // 便于客户端立刻对齐
                    return;
                }
            }
            else
            {
                token = r.GetUInt();
                snap = ItemTool.ReadItemSnapshot(r);
            }

            // 4) 执行
            _serverApplyingLoot = true;
            bool ok = false;
            Item unplugged = null;
            try
            {
                Item child = srcItem;
                if (!srcInLoot)
                {
                    // 从 snapshot 重建对象
                    child = ItemTool.BuildItemFromSnapshot(snap);
                    if (!child)
                    {
                        Server_SendLootDeny(peer, "build_fail");
                        Server_SendLootboxState(peer, inv);
                        return;
                    }
                }
                else
                {
                    // 从容器树/格子中摘出来
                    try { child.Detach(); } catch { }
                }

                ok = dstSlot.Plug(child, out unplugged);

                if (ok)
                {
                    // 背包来源：给发起者一个回执，让对方删除本地背包配件
                    if (!srcInLoot)
                    {
                        var ack = new NetDataWriter();
                        ack.Put((byte)Op.LOOT_PUT_OK);  // 复用 PUT 的 OK 回执
                        ack.Put(token);
                        peer.Send(ack, DeliveryMethod.ReliableOrdered);
                    }

                    // 一如既往广播最新容器快照
                    Server_SendLootboxState(null, inv);
                }
                else
                {
                    Server_SendLootDeny(peer, "slot_plug_fail");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[LOOT][PLUG] " + ex);
                ok = false;
            }
            finally { _serverApplyingLoot = false; }

            if (!ok)
            {
                // 回滚：如果是 snapshot 创建的 child，需要销毁以免泄露
                if (!srcInLoot) { try { /* child 在 Plug 失败时仍在内存里 */ } catch { } }
                Server_SendLootDeny(peer, "plug_fail");
                Server_SendLootboxState(peer, inv);
                return;
            }

            // 若顶掉了原先的一个附件，把它放回容器格子
            if (unplugged)
            {
                if (!ItemUtilities.AddAndMerge(inv, unplugged, 0))
                {
                    try { if (unplugged) UnityEngine.Object.Destroy(unplugged.gameObject); } catch { }
                }
            }

            // (B) 源自玩家背包的情况：下发 LOOT_PUT_OK 让发起者删除本地那件
            if (!srcInLoot && token != 0)
            {
                var w2 = new NetDataWriter();
                w2.Put((byte)Op.LOOT_PUT_OK);
                w2.Put(token);
                peer.Send(w2, DeliveryMethod.ReliableOrdered);
            }

            // 5) 广播容器新状态
            Server_SendLootboxState(null, inv);
        }

        private uint _cliLocalToken
        {
            get => _nextLootToken;
            set => _nextLootToken = value;
        }

        public void Client_RequestLootSlotPlug(Inventory inv, Item master, string slotKey, Item child)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return;

            var w = new NetDataWriter();
            w.Put((byte)Op.LOOT_REQ_SLOT_PLUG);

            // 容器定位
            LootManager.Instance.PutLootId(w, inv);
            LootManager.Instance.WriteItemRef(w, inv, master);
            w.Put(slotKey);

            bool srcInLoot = LootboxDetectUtil.IsLootboxInventory(child ? child.InInventory : null);
            w.Put(srcInLoot);

            if (srcInLoot)
            {
                // 源自容器：发容器内 Item 引用
                LootManager.Instance.WriteItemRef(w, child.InInventory, child);
            }
            else
            {
                // 源自背包：发 token + 快照，并在本地登记“待删”
                uint token = ++_cliLocalToken;           // 你项目里已有递增 token 的字段/方法就用现成的
                _cliPendingSlotPlug[token] = child;
                w.Put(token);
                ItemTool.WriteItemSnapshot(w, child);
            }

            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        internal uint Client_RequestSlotUnplugToBackpack(Inventory lootInv, Item master, string slotKey, Inventory destInv, int destPos)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return 0;
            if (!lootInv || !master || string.IsNullOrEmpty(slotKey)) return 0;
            if (!LootboxDetectUtil.IsLootboxInventory(lootInv) || LootboxDetectUtil.IsPrivateInventory(lootInv)) return 0;
            if (destInv && LootboxDetectUtil.IsLootboxInventory(destInv)) destInv = null; // 兜底用的😮sans

            // 1) 分配 token 并登记“TAKE_OK 的落位目的地”
            uint token = _nextLootToken++;
            if (destInv)
            {
                LootManager.Instance._cliPendingTake[token] = new PendingTakeDest
                {
                    inv = destInv,
                    pos = destPos,
                    slot = null,
                    srcLoot = lootInv,
                    srcPos = -1
                };
            }

            // 2) 发送“卸下 + 直落背包”的请求（在旧负载末尾追加 takeToBackpack + token）
            Client_RequestLootSlotUnplug(lootInv, master, slotKey, true, token);
            return token;
        }

        internal void Client_RequestLootSlotUnplug(Inventory inv, Item master, string slotKey)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return;
            if (!inv || !master || string.IsNullOrEmpty(slotKey)) return;

            var w = writer;
            if (w == null) return;
            w.Reset();
            w.Put((byte)Op.LOOT_REQ_SLOT_UNPLUG);
            LootManager.Instance.PutLootId(w, inv);            // 容器标识（scene/posKey/iid 或 uid）
            LootManager.Instance.WriteItemRef(w, inv, master); // 在该容器里“主件”的路径
            w.Put(slotKey ?? string.Empty); // 要拔的 slot key
                                            // —— 旧负载到此为止（不带 takeToBackpack / token）——
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        internal void Client_RequestLootSlotUnplug(Inventory inv, Item master, string slotKey, bool takeToBackpack, uint token)
        {
            if (!networkStarted || IsServer || connectedPeer == null) return;
            if (!inv || !master || string.IsNullOrEmpty(slotKey)) return;

            var w = writer;
            if (w == null) return;
            w.Reset();
            w.Put((byte)Op.LOOT_REQ_SLOT_UNPLUG);
            LootManager.Instance.PutLootId(w, inv);             // 容器标识
            LootManager.Instance.WriteItemRef(w, inv, master);  // 主件路径
            w.Put(slotKey ?? string.Empty);// slot key

            w.Put(takeToBackpack);
            w.Put(token);
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }

        public void Server_HandleLootSlotUnplugRequest(NetPeer peer, NetPacketReader r)
        {
            // 1) 容器定位
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();

            var inv = LootManager.Instance.ResolveLootInv(scene, posKey, iid, lootUid);
            if (inv == null || LootboxDetectUtil.IsPrivateInventory(inv))
            {
                Server_SendLootDeny(peer, "no_inv");
                return;
            }

            // 2) 主件与槽位（新格式）
            var master = LootManager.Instance.ReadItemRef(r, inv);
            string slotKey = r.GetString();
            if (!master)
            {
                Server_SendLootDeny(peer, "bad_weapon");
                return;
            }
            var slot = master?.Slots?.GetSlot(slotKey);
            if (slot == null)
            {
                Server_SendLootDeny(peer, "bad_slot");
                Server_SendLootboxState(peer, inv); // 只回请求方刷新
                return;
            }

            // 3) 追加字段（向后兼容：旧包没有这俩字段）
            bool takeToBackpack = false;
            uint token = 0;
            if (r.AvailableBytes >= 5) // 1(bool) + 4(uint) 
            {
                try
                {
                    takeToBackpack = r.GetBool();
                    token = r.GetUInt();
                }
                catch { }
            }

            // 4) 执行卸下
            Item removed = null;
            bool ok = false;
            _serverApplyingLoot = true; // 抑制服务端自己触发的后续广播/后处理
            try
            {
                removed = slot.Unplug();
                ok = removed != null;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[LOOT][UNPLUG] " + ex);
                ok = false;
            }
            finally { _serverApplyingLoot = false; }

            if (!ok || !removed)
            {
                Server_SendLootDeny(peer, "slot_unplug_fail");
                Server_SendLootboxState(peer, inv); // 只回请求方刷新
                return;
            }

            // 5) 分支：回容器 或 直落背包
            if (!takeToBackpack)
            {
                if (!ItemUtilities.AddAndMerge(inv, removed, 0))
                {
                    try { if (removed) UnityEngine.Object.Destroy(removed.gameObject); } catch { }
                    Server_SendLootDeny(peer, "add_fail");
                    Server_SendLootboxState(peer, inv);
                    return;
                }
                Server_SendLootboxState(null, inv); // 广播：武器该槽已空，容器新添一件
                return;
            }

            // 让客户端在 Client_OnLootTakeOk 中落袋
            var wCli = new NetDataWriter();
            wCli.Put((byte)Op.LOOT_TAKE_OK);
            wCli.Put(token);
            ItemTool.WriteItemSnapshot(wCli, removed);
            peer.Send(wCli, DeliveryMethod.ReliableOrdered);

            try { if (removed) UnityEngine.Object.Destroy(removed.gameObject); } catch { }
            Server_SendLootboxState(null, inv);
        }

        public void Client_SendLootSplitRequest(ItemStatsSystem.Inventory lootInv, int srcPos, int count, int preferPos)
        {
            if (!networkStarted || IsServer || connectedPeer == null || lootInv == null) return;
            if (LootboxDetectUtil.IsPrivateInventory(lootInv)) return;
            if (count <= 0) return;

            var w = writer;
            if (w == null) return;
            w.Reset();
            w.Put((byte)Op.LOOT_REQ_SPLIT);
            LootManager.Instance.PutLootId(w, lootInv);   // scene/posKey/iid/lootUid
            w.Put(srcPos);
            w.Put(count);
            w.Put(preferPos);        // -1 可让主机自行找空格
            connectedPeer.Send(w, DeliveryMethod.ReliableOrdered);
        }
        public void Server_HandleLootSplitRequest(NetPeer peer, NetPacketReader r)
        {
            int scene = r.GetInt();
            int posKey = r.GetInt();
            int iid = r.GetInt();
            int lootUid = r.GetInt();
            int srcPos = r.GetInt();
            int count = r.GetInt();
            int prefer = r.GetInt();

            // 定位容器（优先用 lootUid）
            ItemStatsSystem.Inventory inv = null;
            if (lootUid >= 0) LootManager.Instance._srvLootByUid.TryGetValue(lootUid, out inv);
            if (inv == null && !LootManager.Instance.TryResolveLootById(scene, posKey, iid, out inv))
            { Server_SendLootDeny(peer, "no_inv"); return; }

            if (LootboxDetectUtil.IsPrivateInventory(inv))
            { Server_SendLootDeny(peer, "no_inv"); return; }

            var srcItem = inv.GetItemAt(srcPos);
            if (!srcItem || count <= 0 || !srcItem.Stackable || count >= srcItem.StackCount)
            { Server_SendLootDeny(peer, "split_bad"); return; }

            ItemTool.Server_DoSplitAsync(inv, srcPos, count, prefer).Forget();
        }


        public struct ItemSnapshot
        {
            public int typeId;
            public int stack;
            public float durability;
            public float durabilityLoss;
            public bool inspected;
            public List<(string key, ItemSnapshot child)> slots;     // 附件树
            public List<ItemSnapshot> inventory;                     // 容器内容
        }
        public struct PendingTakeDest
        {
            // 目的地（背包格或装备槽）
            public ItemStatsSystem.Inventory inv;
            public int pos;
            public ItemStatsSystem.Items.Slot slot;

            // 源信息（从哪个容器的哪个格子拿出来）
            public ItemStatsSystem.Inventory srcLoot;
            public int srcPos;
        }

    }
}
