using System.Security.Cryptography;
using Launcher;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 1 || !File.Exists(Path.Combine(args[0], "Assembly-CSharp.dll")))
    throw new ArgumentException("Supply the game's Managed directory; only temporary copies are patched.");

string fixture = Path.Combine(Path.GetTempPath(), "BeyondAssemblyChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
try
{
    foreach (string file in Directory.GetFiles(args[0], "*.dll"))
        File.Copy(file, Path.Combine(fixture, Path.GetFileName(file)));
    // A clean install has no BeyondAgent.dll until the launcher copies one in,
    // so fall back to the build's copy (Beyond/build, placed beside this runner).
    string agent = Path.Combine(fixture, "BeyondAgent.dll");
    if (!File.Exists(agent))
    {
        string built = Path.Combine(AppContext.BaseDirectory, "BeyondAgent.dll");
        if (!File.Exists(built))
            throw new FileNotFoundException("BeyondAgent.dll is not in the Managed directory and hasn't been built. Build Beyond/BeyondAgent, then rebuild this runner.", built);
        File.Copy(built, agent);
    }
    string assemblyPath = Path.Combine(fixture, "Assembly-CSharp.dll");
    // The installed client may already be patched. Remove only our startup call
    // from the disposable fixture so every run exercises a complete rewrite.
    using (DefaultAssemblyResolver resolver = new())
    {
        resolver.AddSearchDirectory(fixture);
        using MemoryStream input = new(File.ReadAllBytes(assemblyPath));
        using AssemblyDefinition clean = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
        var start = clean.MainModule.Types.Single(t => t.Name == "AEC").Methods.Single(m => m.Name == "Start");
        var hooks = start.Body.Instructions.Where(i => i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference m && m.DeclaringType.FullName == "BeyondAgent.Util.BeyondLifecycle" &&
            m.Name == "Create").ToArray();
        if (hooks.Length > 0)
        {
            foreach (var hook in hooks) start.Body.GetILProcessor().Remove(hook);
            using MemoryStream output = new();
            clean.Write(output);
            File.WriteAllBytes(assemblyPath, output.ToArray());
        }
    }
    byte[] original = File.ReadAllBytes(assemblyPath);
    string hash = Convert.ToHexString(SHA256.HashData(original));
    string versionedBackup = assemblyPath + ".unpatched-" + hash + ".bak";
    string legacyBackup = assemblyPath + ".bak";

    void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    void Unchanged(string message) => Check(File.ReadAllBytes(assemblyPath).SequenceEqual(original), message);
    bool NoStagedFiles() => Directory.GetFiles(fixture, "*.tmp").Length == 0;
    Guid Mvid(byte[] bytes)
    {
        using AssemblyDefinition a = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
        return a.MainModule.Mvid;
    }
    // The same DLL as another build of the client (only the MVID differs).
    byte[] OtherBuild(byte[] bytes)
    {
        using DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(fixture);
        using AssemblyDefinition a = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes), new ReaderParameters { AssemblyResolver = resolver });
        a.MainModule.Mvid = Guid.NewGuid();
        using MemoryStream output = new();
        a.Write(output);
        return output.ToArray();
    }
    byte[] olderClient = OtherBuild(original);

    // Use a real dependency needed by the current client's enum metadata. This
    // reproduces the write-time failure that used to truncate the installed DLL.
    string audio = Path.Combine(fixture, "UnityEngine.AudioModule.dll");
    File.Move(audio, audio + ".held");
    try
    {
        AssemblyPatcher.Patch(fixture);
        throw new Exception("Expected missing dependency to fail.");
    }
    catch (AssemblyResolutionException) { }
    finally { File.Move(audio + ".held", audio); }
    Unchanged("missing dependency preserves the original game DLL");

    // Simulate a game process holding the installed assembly without delete sharing.
    using (FileStream locked = new(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        bool failed = false;
        try { AssemblyPatcher.Patch(fixture); }
        catch (IOException) { failed = true; }
        catch (UnauthorizedAccessException) { failed = true; }
        Check(failed, "locked destination rejects replacement");
    }
    Unchanged("failed replacement preserves the original game DLL");
    Check(NoStagedFiles(), "failed replacement cleans staged output");

    // Steam installs a different client between Patch reading the DLL and
    // swapping its rewrite in. The file on disk is the newer one here.
    bool rejected = false;
    try { AssemblyPatcher.ReplaceInstalled(assemblyPath, olderClient, [1, 2, 3]); }
    catch (IOException) { rejected = true; }
    Check(rejected, "update during patching is detected");
    Unchanged("update during patching keeps the newly installed game DLL");
    Check(NoStagedFiles(), "update during patching cleans staged output");

    // Backups half-written by an interrupted earlier launch.
    File.WriteAllBytes(versionedBackup, original[..(original.Length / 2)]);
    File.WriteAllBytes(legacyBackup, original[..(original.Length / 2)]);

    AssemblyPatcher.Patch(fixture);
    byte[] patched = File.ReadAllBytes(assemblyPath);
    using (AssemblyDefinition result = AssemblyDefinition.ReadAssembly(new MemoryStream(patched)))
    {
        var start = result.MainModule.Types.Single(t => t.Name == "AEC").Methods.Single(m => m.Name == "Start");
        Check(start.Body.Instructions.Count(i => i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference m && m.DeclaringType.FullName == "BeyondAgent.Util.BeyondLifecycle" &&
            m.Name == "Create") == 1, "current client contains exactly one Beyond startup call");
    }
    Check(Mvid(patched) == Mvid(original), "patched DLL keeps the client's MVID");
    Check(File.ReadAllBytes(versionedBackup).SequenceEqual(original), "truncated version backup is rewritten");
    Check(File.ReadAllBytes(legacyBackup).SequenceEqual(original), "truncated legacy backup is rewritten");
    AssemblyPatcher.Patch(fixture);
    Check(File.ReadAllBytes(assemblyPath).SequenceEqual(patched), "second launch leaves the patched DLL unchanged");

    // MelonLoader restore with a legacy .bak left over from an older client.
    File.WriteAllBytes(legacyBackup, olderClient);
    Check(AssemblyPatcher.Restore(fixture), "restore finds this build's backup");
    Unchanged("restore installs this client's original, not the older legacy backup");
    Check(!AssemblyPatcher.Restore(fixture), "restore leaves an unpatched client alone");

    AssemblyPatcher.Patch(fixture);
    byte[] repatched = File.ReadAllBytes(assemblyPath);
    File.Delete(versionedBackup);
    File.WriteAllBytes(legacyBackup, olderClient);
    Check(!AssemblyPatcher.Restore(fixture), "restore refuses a backup from another build");
    Check(File.ReadAllBytes(assemblyPath).SequenceEqual(repatched), "refused restore leaves the patched DLL in place");
    Check(NoStagedFiles(), "no staged files are left behind");
}
finally { Directory.Delete(fixture, true); }
