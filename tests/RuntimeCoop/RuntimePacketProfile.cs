using System.Reflection;
using HarmonyLib;

namespace CoopRuntimeTests;

internal static class RuntimePacketProfile
{
    private sealed class Sample { internal int Count; internal long Bytes; internal int Maximum; }
    private static readonly Dictionary<string, Sample> Samples = new();
    private static Type _op;
    private static bool _installed;
    private static void Sending(byte[] data, int offset, int length, object deliveryMethod)
    {
        if (length == 0) return;
        var name = Enum.GetName(_op, data[offset]) ?? data[offset].ToString();
        if (name == "SHARED_HOME" && length > 1) name += "/" + data[offset + 1];
        name += "/" + deliveryMethod;
        if (!Samples.TryGetValue(name, out var sample)) Samples[name] = sample = new Sample();
        sample.Count++;
        sample.Bytes += length;
        sample.Maximum = Math.Max(sample.Maximum, length);
    }
    internal static object Run(Assembly mod, bool reset)
    {
        if (!_installed)
        {
            _op = mod.GetType("EscapeFromDuckovCoopMod.Op");
            var send = mod.GetType("EscapeFromDuckovCoopMod.LiteNetPeer").GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Single(m => m.Name == "Send" && m.GetParameters().Length == 4);
            new Harmony("local.coop.packet-profile").Patch(send,
                prefix: new HarmonyMethod(typeof(RuntimePacketProfile), nameof(Sending)));
            _installed = true;
        }
        var result = Samples.OrderByDescending(p => p.Value.Bytes).Select(p => new { op = p.Key, count = p.Value.Count,
            bytes = p.Value.Bytes, maximum = p.Value.Maximum }).ToArray();
        if (reset) Samples.Clear();
        return result;
    }
}
