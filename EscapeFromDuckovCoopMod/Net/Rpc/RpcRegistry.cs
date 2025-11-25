using System.Collections.Generic;
using System.Reflection;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

public static class RpcRegistry
{
    private static readonly Dictionary<Op, RpcDescriptor> _byOp = new();
    private static readonly Dictionary<Type, RpcDescriptor> _byType = new();
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        Register<PlayerPositionUpdateRpc>(RPCPlayer.HandlePlayerPositionUpdate);
        Register<PlayerAnimationSyncRpc>(RPCPlayer.HandlePlayerAnimationSync);
        Register<ClientStatusUpdateRpc>(RPCPlayer.HandleClientStatusUpdate);
        Register<PlayerStatusUpdateRpc>(RPCPlayer.HandlePlayerStatusUpdate);
        Register<EquipmentUpdateRpc>(RPCPlayer.HandleEquipmentUpdate);
        Register<WeaponUpdateRpc>(RPCPlayer.HandleWeaponUpdate);
        Register<WeaponFireRequestRpc>(RPCWeapon.HandleFireRequest);
        Register<WeaponFireEventRpc>(RPCWeapon.HandleFireEvent);
        Register<MeleeSwingRequestRpc>(RPCWeapon.HandleMeleeSwingRequest);
        Register<MeleeSwingBroadcastRpc>(RPCWeapon.HandleMeleeSwingBroadcast);
        Register<PlayerHealthReportRpc>(RPCHealth.HandlePlayerHealthReport);
        Register<PlayerHealthBroadcastRpc>(RPCHealth.HandlePlayerHealthBroadcast);
        Register<PlayerDamageForwardRpc>(RPCHealth.HandlePlayerDamageForward);
        Register<PlayerBuffReportRpc>(RPCHealth.HandlePlayerBuffReport);
        Register<PlayerBuffBroadcastRpc>(RPCHealth.HandlePlayerBuffBroadcast);
        Register<AudioEventRpc>(RPCAudio.HandleAudioEvent);
        Register<SceneVoteStartRpc>(RPCScene.HandleSceneVoteStart);
        Register<SceneVoteRequestRpc>(RPCScene.HandleSceneVoteRequest);
        Register<SceneReadySetRpc>(RPCScene.HandleSceneReadySet);
        Register<SceneBeginLoadRpc>(RPCScene.HandleSceneBeginLoad);
        Register<SceneVoteCancelRpc>(RPCScene.HandleSceneVoteCancel);
        Register<EnvSnapshotRequestRpc>(RPCEnvironment.HandleSnapshotRequest);
        Register<EnvClockStateRpc>(RPCEnvironment.HandleClockState);
        Register<EnvWeatherStateRpc>(RPCEnvironment.HandleWeatherState);
        Register<EnvLootChunkRpc>(RPCEnvironment.HandleLootChunk);
        Register<EnvDoorChunkRpc>(RPCEnvironment.HandleDoorChunk);
        Register<EnvDestructibleStateRpc>(RPCEnvironment.HandleDestructibleState);
        Register<EnvExplosiveOilBarrelStateRpc>(RPCEnvironment.HandleExplosiveOilBarrelState);
        Register<DeadLootSpawnRpc>(RPCLoot.HandleDeadLootSpawn);
        Register<ItemDropRequestRpc>(RPCItem.HandleDropRequest);
        Register<ItemSpawnRpc>(RPCItem.HandleSpawn);
        Register<ItemPickupRequestRpc>(RPCItem.HandlePickupRequest);
        Register<ItemDespawnRpc>(RPCItem.HandleDespawn);
        Register<ItemDropSnapshotRequestRpc>(RPCItem.HandleDropSnapshotRequest);
        Register<ItemDropSnapshotChunkRpc>(RPCItem.HandleDropSnapshotChunk);
        Register<AISnapshotRequestRpc>(RPCAI.HandleSnapshotRequest);
        Register<AISnapshotChunkRpc>(RPCAI.HandleSnapshotChunk);
        Register<AIActivationRequestRpc>(RPCAI.HandleActivationRequest);
        Register<AIActivationStateRpc>(RPCAI.HandleActivationState);
        Register<AISpawnRpc>(RPCAI.HandleSpawn);
        Register<AIDespawnRpc>(RPCAI.HandleDespawn);
        Register<AIStateUpdateRpc>(RPCAI.HandleStateUpdate);
        Register<AIHealthReportRpc>(RPCAI.HandleHealthReport);
        Register<AIHealthBroadcastRpc>(RPCAI.HandleHealthBroadcast);
        Register<AIBuffReportRpc>(RPCAI.HandleBuffReport);
        Register<AIBuffBroadcastRpc>(RPCAI.HandleBuffBroadcast);
    }

    public static void Register<T>(Action<RpcContext, T> handler)
        where T : struct, IRpcMessage
    {
        var type = typeof(T);
        var attr = type.GetCustomAttribute<RpcAttribute>();
        if (attr == null)
            throw new InvalidOperationException($"Rpc message {type.FullName} missing RpcAttribute");

        var descriptor = new RpcDescriptor(
            attr.Op,
            attr.Delivery,
            attr.Direction,
            type,
            (context, reader) =>
            {
                var message = new T();
                message.Deserialize(reader);
                handler(context, message);
            });

        if (_byOp.ContainsKey(attr.Op))
            throw new InvalidOperationException($"Rpc opcode {attr.Op} already registered");

        _byOp[attr.Op] = descriptor;
        _byType[type] = descriptor;
    }

    public static bool TryHandle(Op op, RpcContext context, NetPacketReader reader)
    {
        EnsureInitialized();

        if (!_byOp.TryGetValue(op, out var descriptor))
            return false;

        descriptor.Invoker(context, reader);
        return true;
    }

    public static RpcDescriptor GetDescriptor<T>() where T : struct, IRpcMessage
    {
        EnsureInitialized();

        var type = typeof(T);
        if (_byType.TryGetValue(type, out var descriptor))
            return descriptor;

        throw new InvalidOperationException($"Rpc message {type.FullName} not registered");
    }

    private static void EnsureInitialized()
    {
        if (!_initialized) Initialize();
    }
}
