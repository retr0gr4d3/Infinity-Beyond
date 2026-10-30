using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Launcher;

public static class AgentDeployment
{
    public static void Install(string launcherDirectory, string gameDirectory, string managedDirectory)
    {
        foreach (string name in new[] { "BeyondAgent.dll", "0Harmony.dll" })
        {
            string source = Path.Combine(launcherDirectory, name);
            if (!File.Exists(source)) throw new FileNotFoundException("The launcher is missing " + name, source);
            Sync(source, Path.Combine(managedDirectory, name));
            // Doorstop/Mono may resolve assemblies beside the executable before
            // Managed. An existing legacy root copy must never shadow this build.
            string root = Path.Combine(gameDirectory, name);
            if (File.Exists(root)) Sync(source, root);
        }
    }

    private static void Sync(string source, string destination)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return;
        string expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        if (File.Exists(destination))
        {
            string previous = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination)));
            if (expected == previous) return;
            string backup = destination + ".backup-" + previous[..12];
            if (!File.Exists(backup)) File.Copy(destination, backup);
            PruneBackups(destination);
            try
            {
                File.Copy(source, destination, true);
            }
            catch (IOException ex)
            {
                // Another session's game has the agent loaded. That copy still
                // works, so launch with it rather than refusing to start.
                Trace.WriteLine($"[Launcher] {destination} is in use; keeping the installed copy ({previous[..12]}): {ex.Message}");
                return;
            }
        }
        else
        {
            File.Copy(source, destination);
        }
        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination)));
        if (actual != expected) throw new IOException("Agent verification failed: " + destination);
        Trace.WriteLine($"[Launcher] Synchronized {destination} ({expected[..12]})");
    }

    // Each new build leaves one backup; keep the newest few.
    private static void PruneBackups(string destination, int keep = 3)
    {
        string? dir = Path.GetDirectoryName(destination);
        if (dir == null) return;
        foreach (var old in new DirectoryInfo(dir).GetFiles(Path.GetFileName(destination) + ".backup-*")
            .OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
        {
            try { old.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
