using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Launcher
{
    /// <summary>
    /// Resolves the game executable inside a configured install directory. The game
    /// ships under more than one release name, so nothing here assumes a fixed file
    /// name: it prefers the Unity main executable (the one paired with a
    /// "&lt;name&gt;_Data" folder) and falls back to the first non-helper .exe.
    /// </summary>
    public static class GameLocator
    {
        public const string NotFoundMessage =
            "No game found. Please add the game directory in the Configurator.";

        /// <summary>
        /// Finds the game executable in <paramref name="gameDirectory"/>.
        /// </summary>
        /// <returns><c>true</c> and the full path when found; otherwise <c>false</c>.</returns>
        public static bool TryResolveGameExe(string? gameDirectory, out string? exePath)
        {
            exePath = null;
            if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            {
                return false;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return TryResolveMacApp(gameDirectory!, out exePath);
            }

            string[] exes;
            try
            {
                exes = Directory.GetFiles(gameDirectory, "*.exe", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return false;
            }

            // Prefer the Unity player exe: "<name>.exe" sitting next to "<name>_Data".
            foreach (string exe in exes)
            {
                string name = Path.GetFileNameWithoutExtension(exe);
                if (Directory.Exists(Path.Combine(gameDirectory, name + "_Data")))
                {
                    exePath = Path.GetFullPath(exe);
                    return true;
                }
            }

            // Fallback: first executable that isn't a known Unity helper.
            foreach (string exe in exes)
            {
                string name = Path.GetFileNameWithoutExtension(exe);
                if (name.IndexOf("UnityCrashHandler", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                exePath = Path.GetFullPath(exe);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The game's managed-assemblies folder ("&lt;name&gt;_Data/Managed"),
        /// derived from a resolved executable path.
        /// </summary>
        public static string? GetManagedDir(string exePath)
        {
            // macOS Unity bundle: <Game>.app/Contents/MacOS/<exe> and the managed
            // assemblies live in <Game>.app/Contents/Resources/Data/Managed. The
            // resolved exe is the inner Mach-O, so walk up MacOS -> Contents.
            int appIdx = exePath.IndexOf(".app" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (appIdx >= 0)
            {
                string contents = exePath[..(appIdx + 4)] + Path.DirectorySeparatorChar + "Contents";
                return Path.Combine(contents, "Resources", "Data", "Managed");
            }

            string? dir = Path.GetDirectoryName(exePath);
            if (dir == null)
            {
                return null;
            }
            string name = Path.GetFileNameWithoutExtension(exePath);
            return Path.Combine(dir, name + "_Data", "Managed");
        }

        // Resolves the inner Mach-O executable of a macOS Unity ".app" bundle.
        // gameDirectory may be the folder that contains the bundle, or the bundle
        // itself. Returns the executable named in the bundle's Info.plist folder
        // convention: Contents/MacOS/<name>, preferring the one whose Resources
        // folder holds the managed assemblies.
        private static bool TryResolveMacApp(string gameDirectory, out string? exePath)
        {
            exePath = null;

            string bundle;
            if (gameDirectory.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                bundle = gameDirectory;
            }
            else
            {
                string[] apps;
                try { apps = Directory.GetDirectories(gameDirectory, "*.app", SearchOption.TopDirectoryOnly); }
                catch { return false; }
                if (apps.Length == 0) { return false; }

                // Prefer a bundle that actually carries the managed assemblies.
                bundle = apps[0];
                foreach (string a in apps)
                {
                    if (File.Exists(Path.Combine(a, "Contents", "Resources", "Data", "Managed", "Assembly-CSharp.dll")))
                    {
                        bundle = a;
                        break;
                    }
                }
            }

            string macOsDir = Path.Combine(bundle, "Contents", "MacOS");
            if (!Directory.Exists(macOsDir)) { return false; }

            string[] inner;
            try { inner = Directory.GetFiles(macOsDir, "*", SearchOption.TopDirectoryOnly); }
            catch { return false; }

            // Skip the Unity crash handler; take the first real executable.
            foreach (string f in inner)
            {
                if (Path.GetFileName(f).IndexOf("UnityCrashHandler", StringComparison.OrdinalIgnoreCase) >= 0) { continue; }
                exePath = Path.GetFullPath(f);
                return true;
            }

            return false;
        }

        /// <summary>True when a game executable can be found in the directory.</summary>
        public static bool Exists(string? gameDirectory)
        {
            return TryResolveGameExe(gameDirectory, out _);
        }
    }
}
