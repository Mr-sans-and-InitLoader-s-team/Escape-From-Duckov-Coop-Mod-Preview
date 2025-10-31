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

public class AIRequest : MonoBehaviour
{
    public static AIRequest Instance;

    private static NetService Service => NetService.Instance;
    private static bool IsServer => Service != null && Service.IsServer;
    private static NetManager netManager => Service?.netManager;
    private static NetDataWriter writer => Service?.writer;
    private static NetPeer connectedPeer => Service?.connectedPeer;
    private static PlayerStatus localPlayerStatus => Service?.localPlayerStatus;
    private static bool networkStarted => Service != null && Service.networkStarted;

    public void Init()
    {
        Instance = this;
    }

    // 主机：针对单个 Root 发送增量种子（包含 guid 与兼容 id 两条映射）
    public void Server_SendRootSeedDelta(CharacterSpawnerRoot r, NetPeer target = null)
    {
        if (!IsServer || r == null) return;

        var idA = AITool.StableRootId(r); // 现有策略：优先用 SpawnerGuid
        var idB = AITool.StableRootId_Alt(r); // 兼容策略：忽略 guid，用 名称+位置+场景

        var seed = AITool.DeriveSeed(COOPManager.AIHandle.sceneSeed, idA);
        COOPManager.AIHandle.aiRootSeeds[idA] = seed; // 主机本地记录，便于调试

        var w = writer;
        if (w == null) return;
        w.Reset();
        w.Put((byte)Op.AI_SEED_PATCH);
        var count = idA == idB ? 1 : 2;
        w.Put(count);
        w.Put(idA);
        w.Put(seed);
        if (count == 2)
        {
            w.Put(idB);
            w.Put(seed);
        }

        if (target == null) CoopTool.BroadcastReliable(w);
        else target.Send(w, DeliveryMethod.ReliableOrdered);
    }

    public void Server_TryRebroadcastIconLater(int aiId, CharacterMainControl cmc)
    {
        if (!IsServer || aiId == 0 || !cmc) return;
        if (!AIName._iconRebroadcastScheduled.Add(aiId)) return; // 只安排一次

        StartCoroutine(AIName.IconRebroadcastRoutine(aiId, cmc));
    }
}