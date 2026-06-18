using HarmonyLib;
using System;
using System.IO;
using UnityEngine;

namespace Infinity_TestMod.Patches
{
    // Local bundle override. When a requested bundle Filename starts with
    // "local/", load bytes off disk from UserData\Beyond\customBundles\<rest>
    // instead of letting AssetBundleManager fetch from the CDN.
    //
    // Patch shape: prefix AssetBundleLoader.Load(). Original is a one-liner
    // (AssetBundleManager.LoadAssetBundle(this)) so swapping it out for a
    // local AssetBundle.LoadFromFile + Complete(bundle) is a clean swap —
    // Complete() is what the manager would call on success anyway, so all
    // downstream consumers (onComplete subscribers, charItemLoader, etc.)
    // see the bundle land the same way as a real download.
    //
    // The Filename itself is preserved with the "local/" prefix so the spoof
    // patches' catalog/version logic stays inert (no CDN URL is ever built).
    [HarmonyPatch(typeof(AssetBundleLoader), "Load")]
    public static class LocalBundleOverridePatch
    {
        public const string Prefix_ = "local/";

        public static bool Prefix(AssetBundleLoader __instance)
        {
            try
            {
                AssetBundleData data = __instance?.BundleData;
                string fn = data?.Filename;
                if (string.IsNullOrEmpty(fn)) return true;
                if (!fn.StartsWith(Prefix_, StringComparison.OrdinalIgnoreCase)) return true;

                string rest = fn.Substring(Prefix_.Length);
                string root = Path.Combine(BeyondEnv.UserDataDirectory, "Beyond", "customBundles");
                System.IO.Directory.CreateDirectory(root);
                string path = Path.Combine(root, rest);

                // Convenience: allow `local/xellos` without the .unity3d suffix.
                if (!File.Exists(path) && !rest.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
                {
                    string alt = path + ".unity3d";
                    if (File.Exists(alt)) path = alt;
                }

                if (!File.Exists(path))
                {
                    BeyondLog.Warning($"[LocalBundle] missing: {path}");
                    __instance.OnError($"local bundle not found: {path}");
                    return false;
                }

                AssetBundle bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    BeyondLog.Warning($"[LocalBundle] LoadFromFile returned null: {path}");
                    __instance.OnError($"local bundle load failed: {path}");
                    return false;
                }

                BeyondLog.Msg($"[LocalBundle] loaded {fn} <- {path}");
                __instance.Complete(bundle);
                return false;
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[LocalBundle] {ex}");
                return true;
            }
        }
    }
}
