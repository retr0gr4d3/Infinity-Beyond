using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Launcher;

public static class BotStorage
{
    private static readonly object Gate = new();
    private sealed class ImportedBot
    {
        public string Target { get; set; } = "";
        public string Hash { get; set; } = "";
    }

    public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "bots");

    public static string Ensure(string? baseDirectory = null)
    {
        lock (Gate)
        {
            string root = baseDirectory ?? AppContext.BaseDirectory;
            string target = Path.Combine(root, "bots");
            string legacy = Path.Combine(root, "FarmingPlans");
            if (Directory.Exists(legacy) && !Directory.Exists(target)) Directory.Move(legacy, target);
            Directory.CreateDirectory(target);
            if (Directory.Exists(legacy))
            {
                Import(legacy, target);
                // Preserve conflicting custom versions and non-JSON files as a unit.
                string backups = Path.Combine(root, "repair-backups");
                Directory.CreateDirectory(backups);
                Directory.Move(legacy, Path.Combine(backups, "FarmingPlans-" + Guid.NewGuid().ToString("N")));
            }
            return target;

        }
    }

    public static void Import(string source, string? target = null)
    {
        lock (Gate)
        {
            if (!Directory.Exists(source)) return;
            target ??= Ensure();
            Directory.CreateDirectory(target);
            string stateDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, "UserData");
            Directory.CreateDirectory(stateDirectory);
            string manifest = Path.Combine(stateDirectory, "bot-imports.json");
            Dictionary<string, ImportedBot> imports;
            try
            {
                imports = File.Exists(manifest)
                    ? JsonConvert.DeserializeObject<Dictionary<string, ImportedBot>>(File.ReadAllText(manifest)) ?? []
                    : [];
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Bot import history unavailable; preserving existing files: {ex.Message}");
                imports = [];
            }

            foreach (string file in Directory.GetFiles(source, "*.json"))
            {
                string destination = Path.Combine(target, Path.GetFileName(file));
                if (Path.GetFullPath(file).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) continue;
                string key = Path.GetFullPath(file);
                string sourceHash = Hash(file);
                imports.TryGetValue(key, out ImportedBot? previous);
                if (File.Exists(destination))
                {
                    string localHash = Hash(destination);
                    if (localHash != sourceHash && (previous == null || previous.Target != destination || localHash != previous.Hash))
                        continue; // A local edit wins over a downloaded update.
                }
                if (!File.Exists(destination) || Hash(destination) != sourceHash) File.Copy(file, destination, true);
                imports[key] = new ImportedBot { Target = destination, Hash = sourceHash };
            }
            string temporary = manifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(imports, Formatting.Indented));
                File.Move(temporary, manifest, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }

        }
    }

    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));

    public static string Prepare(string? gameDirectory)
    {
        lock (Gate)
        {
            string target = Ensure();
            if (string.IsNullOrWhiteSpace(gameDirectory)) return target;
            string user = Path.Combine(gameDirectory, "UserData", "Beyond");
            string overrides = Path.Combine(user, "chains.json");
            if (File.Exists(overrides))
            {
                try
                {
                    foreach (var property in JObject.Parse(File.ReadAllText(overrides)).Properties()
                        .Where(p => !p.Name.StartsWith('_') && p.Value is JArray or JObject))
                    {
                        string name = string.Concat(property.Name.Where(char.IsLetterOrDigit));
                        if (name.Length == 0) continue;
                        string file = Path.Combine(target, name + ".json");
                        if (File.Exists(file) && JObject.Parse(File.ReadAllText(file)).Property(property.Name) == null)
                        {
                            string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(property.Name)))[..8];
                            file = Path.Combine(target, name + "-" + suffix + ".json");
                        }
                        if (!File.Exists(file)) File.WriteAllText(file, new JObject { [property.Name] = property.Value.DeepClone() }.ToString());
                    }
                }
                catch (Exception ex) { Trace.WriteLine("Legacy bot import: " + ex.Message); }
            }
            Import(Path.Combine(user, "Infinity-Files", "Scripts"), target);
            return target;

        }
    }

    public static string Resolve(string previousPath, string? baseDirectory = null)
    {
        string parent = Path.GetDirectoryName(previousPath) ?? "";
        bool legacy = string.Equals(Path.GetFileName(parent), "FarmingPlans", StringComparison.OrdinalIgnoreCase)
            || parent.EndsWith(Path.Combine("Infinity-Files", "Scripts"), StringComparison.OrdinalIgnoreCase)
            || parent.EndsWith(Path.Combine("UserData", "Beyond", "Scripts"), StringComparison.OrdinalIgnoreCase);
        if (!legacy) return previousPath;
        string candidate = Path.Combine(Ensure(baseDirectory), Path.GetFileName(previousPath));
        return File.Exists(candidate) ? candidate : previousPath;
    }
}
