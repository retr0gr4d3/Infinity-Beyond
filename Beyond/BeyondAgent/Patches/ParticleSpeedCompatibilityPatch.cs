using BeyondAgent.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BeyondAgent.Patches
{
    // Keep the original spawning, positioning and lifetime logic. Isolate the
    // optional speed adjustment so a mismatched Unity native binding cannot
    // prevent the rest of SpawnParticle from executing.
    [HarmonyPatch(typeof(NodeParticle), nameof(NodeParticle.SpawnParticle))]
    public static class ParticleSpeedCompatibilityPatch
    {
        private static bool _speedUnavailable, _subEmittersUnavailable;
        private static readonly MethodInfo OriginalSubEmitter = AccessTools.Method(typeof(NodeParticle), "IsSubEmitter");

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var setter = AccessTools.PropertySetter(typeof(ParticleSystem.MainModule), "simulationSpeed");
            var safeSetter = AccessTools.Method(typeof(ParticleSpeedCompatibilityPatch), nameof(SetSpeed));
            var safeSubEmitter = AccessTools.Method(typeof(ParticleSpeedCompatibilityPatch), nameof(IsSubEmitter));
            foreach (var instruction in instructions)
            {
                // Modify in place to retain branch labels and exception blocks.
                if (instruction.Calls(setter)) { instruction.opcode = OpCodes.Call; instruction.operand = safeSetter; }
                else if (OriginalSubEmitter != null && instruction.Calls(OriginalSubEmitter)) { instruction.opcode = OpCodes.Call; instruction.operand = safeSubEmitter; }
                yield return instruction;
            }
        }

        private static bool BindingFailure(Exception ex) => ex is MissingMethodException || ex is EntryPointNotFoundException || ex is TypeLoadException;

        public static void SetSpeed(ref ParticleSystem.MainModule module, float speed)
        {
            if (_speedUnavailable) return;
            try { SetNativeSpeed(ref module, speed); }
            catch (Exception ex) when (BindingFailure(ex))
            {
                _speedUnavailable = true;
                BeyondLog.Warning("Particle speed binding unavailable; effects retain prefab speed. " + ex.GetType().Name);
            }
        }

        // A separate non-inlined method keeps Mono's native binding/JIT failure
        // inside the caller's try/catch, rather than before its handler exists.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetNativeSpeed(ref ParticleSystem.MainModule module, float speed) => module.simulationSpeed = speed;

        public static bool IsSubEmitter(ParticleSystem system, ParticleSystem[] all)
        {
            if (_subEmittersUnavailable || OriginalSubEmitter == null) return true;
            try { return (bool)OriginalSubEmitter.Invoke(null, new object[] { system, all }); }
            catch (TargetInvocationException ex) when (ex.InnerException != null && BindingFailure(ex.InnerException))
            {
                _subEmittersUnavailable = true;
                BeyondLog.Warning("Particle sub-emitter binding unavailable; retaining prefab particle speeds.");
                return true;
            }
            catch (Exception ex) when (BindingFailure(ex))
            {
                _subEmittersUnavailable = true;
                BeyondLog.Warning("Particle sub-emitter binding unavailable; retaining prefab particle speeds.");
                return true;
            }
        }
    }
}
