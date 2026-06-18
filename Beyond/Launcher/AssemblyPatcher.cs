using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Launcher
{
    public static class AssemblyPatcher
    {
        public static void Patch(string managedDir)
        {
            string assemblyPath = Path.Combine(managedDir, "Assembly-CSharp.dll");
            string agentPath = Path.Combine(managedDir, "BeyondAgent.dll");
            
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException("Assembly-CSharp.dll not found", assemblyPath);
            }
            if (!File.Exists(agentPath))
            {
                throw new FileNotFoundException("BeyondAgent.dll not found", agentPath);
            }

            // Read the Assembly-CSharp assembly into memory first to avoid file locks when writing back
            byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
            using (var ms = new MemoryStream(assemblyBytes))
            using (var assembly = AssemblyDefinition.ReadAssembly(ms))
            {
                var aecType = assembly.MainModule.Types.FirstOrDefault(t => t.Name == "AEC");
                if (aecType == null)
                {
                    throw new TypeLoadException("Could not find class AEC in Assembly-CSharp.dll");
                }

                var startMethod = aecType.Methods.FirstOrDefault(m => m.Name == "Start");
                if (startMethod == null)
                {
                    throw new EntryPointNotFoundException("Could not find method AEC.Start in Assembly-CSharp.dll");
                }

                if (!startMethod.HasBody)
                {
                    throw new InvalidOperationException("AEC.Start method has no body.");
                }

                // Check if already patched: does it call BeyondLifecycle.Create?
                bool alreadyPatched = false;
                foreach (var instruction in startMethod.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Call && 
                        instruction.Operand is MethodReference methodRef && 
                        methodRef.DeclaringType.FullName == "Infinity_TestMod.BeyondLifecycle" && 
                        methodRef.Name == "Create")
                    {
                        alreadyPatched = true;
                        break;
                    }
                }

                if (alreadyPatched)
                {
                    System.Diagnostics.Trace.WriteLine("Assembly-CSharp.dll is already patched. Skipping.");
                    return;
                }

                // Make a backup copy of original Assembly-CSharp.dll if it doesn't exist yet
                string backupPath = assemblyPath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(assemblyPath, backupPath, true);
                    System.Diagnostics.Trace.WriteLine($"Created backup of Assembly-CSharp.dll at {backupPath}");
                }

                // Load agent assembly to resolve BeyondLifecycle.Create
                using (var agentAssembly = AssemblyDefinition.ReadAssembly(agentPath))
                {
                    var agentType = agentAssembly.MainModule.Types.FirstOrDefault(t => t.FullName == "Infinity_TestMod.BeyondLifecycle");
                    if (agentType == null)
                    {
                        throw new TypeLoadException("Could not find class Infinity_TestMod.BeyondLifecycle in BeyondAgent.dll");
                    }

                    var createMethod = agentType.Methods.FirstOrDefault(m => m.Name == "Create" && m.Parameters.Count == 0);
                    if (createMethod == null)
                    {
                        throw new EntryPointNotFoundException("Could not find method Create in Infinity_TestMod.BeyondLifecycle");
                    }

                    // Import the method reference
                    var createMethodRef = assembly.MainModule.ImportReference(createMethod);

                    // Insert the call instruction at the very beginning of AEC.Start()
                    var il = startMethod.Body.GetILProcessor();
                    var firstInstruction = startMethod.Body.Instructions[0];
                    var callInstruction = il.Create(OpCodes.Call, createMethodRef);
                    il.InsertBefore(firstInstruction, callInstruction);
                }

                // Save the assembly
                assembly.Write(assemblyPath);
                System.Diagnostics.Trace.WriteLine("Successfully patched Assembly-CSharp.dll to load Standalone Launcher Agent.");
            }
        }
    }
}
