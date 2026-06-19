using System;
using System.IO;

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
            string? dir = Path.GetDirectoryName(exePath);
            if (dir == null)
            {
                return null;
            }
            string name = Path.GetFileNameWithoutExtension(exePath);
            return Path.Combine(dir, name + "_Data", "Managed");
        }

        /// <summary>True when a game executable can be found in the directory.</summary>
        public static bool Exists(string? gameDirectory)
        {
            return TryResolveGameExe(gameDirectory, out _);
        }
    }
}
