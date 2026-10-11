using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

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
            // Cecil may resolve enum parameter types while writing metadata. Resolve
            // against this game's assemblies, not the launcher's working directory.
            using DefaultAssemblyResolver resolver = new();
            resolver.AddSearchDirectory(managedDir);
            ReaderParameters readerParameters = new() { AssemblyResolver = resolver };
            using MemoryStream ms = new(assemblyBytes);
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(ms, readerParameters);
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

            if (IsPatched(assembly))
            {
                System.Diagnostics.Trace.WriteLine("Assembly-CSharp.dll is already patched. Skipping.");
                return;
            }

            // Load agent assembly to resolve BeyondLifecycle.Create
            using (AssemblyDefinition agentAssembly = AssemblyDefinition.ReadAssembly(agentPath, readerParameters))
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

            // Never let Cecil open/truncate the installed DLL. Finish and validate
            // the entire rewrite in memory before touching any installed files.
            using MemoryStream output = new();
            assembly.Write(output);
            byte[] patchedBytes = output.ToArray();
            using (MemoryStream validationStream = new(patchedBytes))
            using (AssemblyDefinition validated = AssemblyDefinition.ReadAssembly(validationStream, readerParameters))
            {
                if (!IsPatched(validated))
                {
                    throw new InvalidOperationException("Patched assembly failed BeyondAgent startup validation.");
                }
            }

            SaveBackups(assemblyPath, assemblyBytes);
            ReplaceInstalled(assemblyPath, assemblyBytes, patchedBytes);
            System.Diagnostics.Trace.WriteLine("Successfully patched Assembly-CSharp.dll to load Standalone Launcher Agent.");
        }

        // Put the unpatched Assembly-CSharp.dll back for the client that is
        // installed now (MelonLoader takes over from the built-in agent). Only
        // a backup of the same build qualifies — same module MVID, which Cecil
        // keeps when patching — because a previous release's DLL over today's
        // client stops the game loading. Returns false when the installed DLL
        // isn't patched or no backup matches it.
        public static bool Restore(string managedDir)
        {
            string assemblyPath = Path.Combine(managedDir, "Assembly-CSharp.dll");
            if (!File.Exists(assemblyPath))
            {
                return false;
            }

            byte[] installed = File.ReadAllBytes(assemblyPath);
            Guid mvid;
            using (AssemblyDefinition current = AssemblyDefinition.ReadAssembly(new MemoryStream(installed)))
            {
                if (!IsPatched(current))
                {
                    return false;
                }
                mvid = current.MainModule.Mvid;
            }

            string[] candidates = [.. Directory.GetFiles(managedDir, "Assembly-CSharp.dll.unpatched-*.bak"), assemblyPath + ".bak"];
            foreach (string candidate in candidates)
            {
                byte[]? original = ReadIntactBackup(candidate);
                if (original == null)
                {
                    continue;
                }
                try
                {
                    using AssemblyDefinition backup = AssemblyDefinition.ReadAssembly(new MemoryStream(original));
                    if (backup.MainModule.Mvid != mvid || IsPatched(backup))
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;   // a damaged legacy .bak that Cecil can't read
                }
                ReplaceInstalled(assemblyPath, installed, original);
                return true;
            }

            System.Diagnostics.Trace.WriteLine("No backup matches the installed Assembly-CSharp.dll; leaving it patched. Verify the game files in Steam to restore it.");
            return false;
        }

        // Does AEC.Start call BeyondLifecycle.Create?
        private static bool IsPatched(AssemblyDefinition assembly)
        {
            MethodDefinition? start = assembly.MainModule.Types
                .FirstOrDefault(t => t.Name == "AEC")?.Methods.FirstOrDefault(m => m.Name == "Start");
            return start != null && start.HasBody && start.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Call &&
                i.Operand is MethodReference m &&
                m.DeclaringType.FullName == "BeyondAgent.Util.BeyondLifecycle" &&
                m.Name == "Create");
        }

        // One backup per client version, named by its hash so a damaged or
        // half-written copy is detected and rewritten rather than trusted. The
        // legacy .bak follows the newest original: older launchers restore it
        // blindly, and a previous release's DLL can't run today's client.
        private static void SaveBackups(string assemblyPath, byte[] original)
        {
            string versioned = assemblyPath + ".unpatched-" + Sha256Hex(original) + ".bak";
            if (ReadIntactBackup(versioned) == null)
            {
                WriteAtomically(versioned, original);
            }

            string legacy = assemblyPath + ".bak";
            if (!File.Exists(legacy) || !File.ReadAllBytes(legacy).SequenceEqual(original))
            {
                WriteAtomically(legacy, original);
            }
        }

        // A backup's bytes, or null when it is missing or (for a versioned
        // backup) no longer matches the hash in its name.
        private static byte[]? ReadIntactBackup(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            byte[] bytes = File.ReadAllBytes(path);
            const string marker = ".unpatched-";
            string name = Path.GetFileName(path);
            int at = name.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
            {
                return bytes;
            }
            string hash = name[(at + marker.Length)..^".bak".Length];
            return string.Equals(hash, Sha256Hex(bytes), StringComparison.OrdinalIgnoreCase) ? bytes : null;
        }

        // Swap `replacement` in for the installed DLL only if the installed DLL
        // is still `expected`. File.Replace moves the old file aside in the same
        // step, so the check runs on exactly what was displaced: if Steam (or
        // another launch) installed a different DLL after we read it, that file
        // goes back and ours is dropped. Anything written after the swap wins.
        internal static void ReplaceInstalled(string assemblyPath, byte[] expected, byte[] replacement)
        {
            string staged = assemblyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string displaced = assemblyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteDurably(staged, replacement);
                File.Replace(staged, assemblyPath, displaced);
            }
            catch
            {
                // ReplaceFile can fail after moving the installed file aside.
                if (File.Exists(displaced) && !File.Exists(assemblyPath))
                {
                    File.Move(displaced, assemblyPath);
                }
                throw;
            }
            finally
            {
                if (File.Exists(staged)) File.Delete(staged);
            }

            if (File.ReadAllBytes(displaced).SequenceEqual(expected))
            {
                File.Delete(displaced);
                return;
            }
            File.Move(displaced, assemblyPath, overwrite: true);
            throw new IOException("Assembly-CSharp.dll changed while Beyond was patching it (a game update?). Kept the new file; launch again once the update finishes.");
        }

        // Install `bytes` at `path` through a temp file and rename, so an
        // interrupted write never leaves a truncated file under the real name.
        private static void WriteAtomically(string path, byte[] bytes)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteDurably(temp, bytes);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private static void WriteDurably(string path, byte[] bytes)
        {
            using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    }
}
