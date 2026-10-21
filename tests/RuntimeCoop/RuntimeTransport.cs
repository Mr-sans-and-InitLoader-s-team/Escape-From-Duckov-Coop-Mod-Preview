using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace CoopRuntimeTests;

internal static class RuntimeTransport
{
    internal static object Snapshot(object service)
    {
        if(service==null)return null;
        var manager=AccessTools.Field(service.GetType(),"netManager")?.GetValue(service)
            ?? AccessTools.Property(service.GetType(),"netManager")?.GetValue(service);
        if(manager==null)return null;
        var peers=AccessTools.Property(manager.GetType(),"ConnectedPeerList").GetValue(manager) as IEnumerable;
        return peers.Cast<object>().Select(p=>
        {
            var native=AccessTools.Field(p.GetType(),"_peer")?.GetValue(p);
            var method=native?.GetType().GetMethod("GetPacketsCountInReliableQueue");
            var channels=native==null?null:AccessTools.Field(native.GetType(),"_channels")?.GetValue(native) as Array;
            var orderedChannel=channels?.GetValue(2);
            var pending=orderedChannel==null?null:AccessTools.Field(orderedChannel.GetType(),"_pendingPackets")?.GetValue(orderedChannel) as Array;
            return new {id=AccessTools.Property(p.GetType(),"Id").GetValue(p),
                ping=AccessTools.Property(p.GetType(),"Ping").GetValue(p),
                ordered=method?.Invoke(native,new object[]{(byte)0,true}),
                unordered=method?.Invoke(native,new object[]{(byte)0,false}),
                pending=pending?.Cast<object>().Count(q=>AccessTools.Field(q.GetType(),"_packet").GetValue(q)!=null),
                resendDelay=native==null?null:AccessTools.Property(native.GetType(),"ResendDelay")?.GetValue(native)};
        }).ToArray();
    }
}
