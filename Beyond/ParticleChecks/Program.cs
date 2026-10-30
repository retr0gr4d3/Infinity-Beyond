using BeyondAgent.Patches;
using HarmonyLib;
using System.Reflection.Emit;
using System.Reflection;

static class Program
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Main()
    {
        var setter = AccessTools.PropertySetter(typeof(UnityEngine.ParticleSystem.MainModule), "simulationSpeed");
        var instruction = new CodeInstruction(OpCodes.Call, setter); instruction.labels.Add(default);
        var sub = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NodeParticle), "IsSubEmitter"));
        var other = new CodeInstruction(OpCodes.Nop);
        var rewritten = ParticleSpeedCompatibilityPatch.Transpiler(new[] { instruction, sub, other }).ToArray();
        Check(((MethodInfo)rewritten[0].operand).Name == "SetSpeed" && rewritten[0].labels.Count == 1, "speed call must be replaced without losing branch labels");
        Check(((MethodInfo)rewritten[1].operand).DeclaringType == typeof(ParticleSpeedCompatibilityPatch) && ReferenceEquals(rewritten[2], other), "only optional speed-related calls may change");
        var module = new UnityEngine.ParticleSystem.MainModule();
        ParticleSpeedCompatibilityPatch.SetSpeed(ref module, 2); Check(UnityEngine.ParticleSystem.LastSpeed == 2, "supported speed adjustment must be preserved");
        UnityEngine.ParticleSystem.Fail = true; ParticleSpeedCompatibilityPatch.SetSpeed(ref module, 3); ParticleSpeedCompatibilityPatch.SetSpeed(ref module, 4);
        Check(BeyondAgent.Util.BeyondLog.Warnings.Count == 1 && UnityEngine.ParticleSystem.Attempts == 2, "missing native setter must warn once and stop retrying");
        NodeParticle.Fail = true; Check(ParticleSpeedCompatibilityPatch.IsSubEmitter(null, []) && ParticleSpeedCompatibilityPatch.IsSubEmitter(null, []), "missing sub-emitter binding must skip speed adjustment");
        Check(NodeParticle.Attempts == 1 && BeyondAgent.Util.BeyondLog.Warnings.Count == 2, "sub-emitter binding failure must be cached and warn once");
        Console.WriteLine("PASS: transpiler call replacements, labels preserved, normal speeds retained, missing bindings contained and retried only once");
    }
}
public static class NodeParticle
{
    public static bool Fail; public static int Attempts;
    public static void SpawnParticle() { }
    private static bool IsSubEmitter(UnityEngine.ParticleSystem system, UnityEngine.ParticleSystem[] all) { Attempts++; if (Fail) throw new MissingMethodException(); return false; }
}
namespace UnityEngine
{
    public class ParticleSystem
    {
        public static bool Fail; public static int Attempts; public static float LastSpeed;
        public struct MainModule { public float simulationSpeed { set { Attempts++; if (Fail) throw new MissingMethodException(); LastSpeed = value; } } }
    }
}
namespace BeyondAgent.Util { public static class BeyondLog { public static List<string> Warnings = []; public static void Warning(string msg) => Warnings.Add(msg); } }
