# ModNetworkApi 示例：同步“谁击中了哪个 AI”并在攻击者头顶弹字

下例演示如何用“方法注册/发送”的方式集中管理网络通道，并在角色造成伤害时同步文本，再用 `CharacterMainControl.PopText()` 在攻击者头顶弹出提示。示例特别演示：**不用 `FindObjectOfType`，而是通过已有的 `NetService` 映射找到攻击者对应的角色对象再弹字**。

```csharp
using EscapeFromDuckovCoopMod;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

public class HurtSyncExample : MonoBehaviour
{
    private const string HurtChannel = "example:hurt";

    private IDisposable _subscription;
    private CharacterMainControl _control; // 在 OnEnable 中抓取，供 PopText 使用

    public void OnEnable()
    {
        _control = GetComponent<CharacterMainControl>();

        // 统一在这里注册处理器，方法名易查找
        _subscription = ModNetworkApi.RegisterHandler(HurtChannel, OnHurtMessageReceived);

        // 订阅角色受伤事件，触发同步
        Health.OnHurt += OnLocalHurt;
    }

    public void OnDisable()
    {
        // 解绑事件与网络处理器
        Health.OnHurt -= OnLocalHurt;
        _subscription?.Dispose();
        _subscription = null;
    }

    private void OnLocalHurt(Health victim, DamageInfo dmg)
    {
        // 仅关心“我”造成的伤害；未找到攻击者则退出
        var attacker = victim?.TryGetCharacter()?.gunUser?.GetOwner();
        if (attacker == null)
            return;

        var victimChar = victim.TryGetCharacter();
        if (victimChar?.characterPreset == null)
            return;

        // 构造文本：我击中了 X 伤害: Y
        string text = $"我击中了 {victimChar.characterPreset.DisplayName} 伤害: {dmg.damageValue}";

        // 将文本发送给服务器（或根据需要改用 Broadcast）
        SendHurtMessageToServer(text);
    }

    private static void OnHurtMessageReceived(ModMessageContext ctx)
    {
        var reader = new NetDataReader(ctx.Payload.ToArray());
        var attackerId = reader.GetString();
        var text = reader.GetString();

        // 服务器侧：不要在服务器线程直接 PopText（可能导致主机卡死/崩溃），只负责把“攻击者ID+文本”广播给所有客户端
        if (ctx.IsServer)
        {
            BroadcastHurtMessage(attackerId, text);
            return;
        }

        // 客户端：根据 attackerId 用 NetService 的远端映射找到角色再弹字
        PopTextOnClient(attackerId, text);
    }

    // --- 发送端封装：调用处只传 message 方法名 ---

    private static bool SendHurtMessageToServer(string message)
    {
        var attackerId = NetService.Instance != null ? NetService.Instance.GetSelfNetworkId() : string.Empty;
        return ModNetworkApi.SendToServer(HurtChannel, writer => WritePayload(writer, attackerId, message));
    }

    private static int BroadcastHurtMessage(string attackerId, string message)
    {
        return ModNetworkApi.Broadcast(HurtChannel, writer => WritePayload(writer, attackerId, message));
    }

    private static void WritePayload(NetDataWriter writer, string attackerId, string message)
    {
        writer.Put(attackerId ?? string.Empty);
        writer.Put(message ?? string.Empty);
    }

    // --- 客户端弹字 ---

    private static void PopTextOnClient(string attackerId, string message)
    {
        var cmc = ResolveCharacter(attackerId);
        if (cmc == null)
            return;

        cmc.PopText(message, Color.yellow);
    }

    private static CharacterMainControl ResolveCharacter(string attackerId)
    {
        var svc = NetService.Instance;
        if (svc == null || string.IsNullOrEmpty(attackerId))
            return null;

        // 本地玩家自己
        if (svc.IsSelfId(attackerId))
        {
            var main = LevelManager.Instance?.MainCharacter;
            return main != null ? main.GetComponent<CharacterMainControl>() : null;
        }

        // 客户端：clientRemoteCharacters 以 PlayerId(string) 为键
        if (svc.clientRemoteCharacters != null &&
            svc.clientRemoteCharacters.TryGetValue(attackerId, out var cliRemote) && cliRemote != null)
        {
            return cliRemote.GetComponentInChildren<CharacterMainControl>(true);
        }

        // 作为主机/服务器或带 NetPeer 的场景：remoteCharacters 以 NetPeer 为键
        if (svc.remoteCharacters != null && svc.playerStatuses != null)
        {
            foreach (var kv in svc.playerStatuses)
            {
                if (kv.Value != null && kv.Value.EndPoint == attackerId &&
                    svc.remoteCharacters.TryGetValue(kv.Key, out var remote) && remote != null)
                {
                    return remote.GetComponentInChildren<CharacterMainControl>(true);
                }
            }
        }

        return null;
    }
}
```

要点：
- 处理器与发送入口都拆成独立方法，调用处只需引用方法名，便于集中管理。
- `RegisterHandler` 返回的 `IDisposable` 在 `OnDisable` 中 `Dispose`，确保卸载时解除注册。
- `SendToServer` / `Broadcast` 使用独立的 `WritePayload` 封装负载构造，避免在调用处写一行内联委托。
- **服务器侧不直接 PopText**，只负责转播消息，避免在服务器线程上弹 UI 造成主机卡死或崩溃；客户端收到后再用 `PopTextOnClient` 通过 `NetService` 查找攻击者对象并弹字，可根据需求改为针对特定角色实例弹出。
