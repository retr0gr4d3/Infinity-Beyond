using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.IO;
using System.Linq;

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
            using MemoryStream ms = new(assemblyBytes);
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(ms);
            TypeDefinition? aecType = assembly.MainModule.Types.FirstOrDefault(t => t.Name == "AEC");
            if (aecType == null)
            {
                throw new TypeLoadException("Could not find class AEC in Assembly-CSharp.dll");
            }

            MethodDefinition? startMethod = aecType.Methods.FirstOrDefault(m => m.Name == "Start");
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
            foreach (Instruction? instruction in startMethod.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Call &&
                    instruction.Operand is MethodReference methodRef &&
                    methodRef.DeclaringType.FullName == "BeyondAgent.Util.BeyondLifecycle" &&
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
            using (AssemblyDefinition agentAssembly = AssemblyDefinition.ReadAssembly(agentPath))
            {
                TypeDefinition? agentType = agentAssembly.MainModule.Types.FirstOrDefault(t => t.FullName == "BeyondAgent.Util.BeyondLifecycle");
                if (agentType == null)
                {
                    throw new TypeLoadException("Could not find class BeyondAgent.Util.BeyondLifecycle in BeyondAgent.dll");
                }

                MethodDefinition? createMethod = agentType.Methods.FirstOrDefault(m => m.Name == "Create" && m.Parameters.Count == 0);
                if (createMethod == null)
                {
                    throw new EntryPointNotFoundException("Could not find method Create in BeyondAgent.BeyondLifecycle");
                }

                // Import the method reference
                MethodReference createMethodRef = assembly.MainModule.ImportReference(createMethod);

                // Insert the call instruction at the very beginning of AEC.Start()
                ILProcessor il = startMethod.Body.GetILProcessor();
                Instruction firstInstruction = startMethod.Body.Instructions[0];
                Instruction callInstruction = il.Create(OpCodes.Call, createMethodRef);
                il.InsertBefore(firstInstruction, callInstruction);
            }

            // Save the assembly
            assembly.Write(assemblyPath);
            System.Diagnostics.Trace.WriteLine("Successfully patched Assembly-CSharp.dll to load Standalone Launcher Agent.");
        }
    }
}
