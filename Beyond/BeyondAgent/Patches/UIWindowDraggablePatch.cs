using HarmonyLib;
using Infinity_TestMod.Util;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Infinity_TestMod.Patches
{
    // Every gameplay window (ItemShop, MergeShop, UIQuests, UIQuestDetail,
    // UIQuestComplete, UIInventory, SkillForge, EnhWindow, EnhCompare,
    // MapEditor, DevConsole, …) inherits UIWindow. We want one patch that
    // covers all of them.
    //
    // We use OnEnable (Unity lifecycle) instead of Show because the game
    // pools window GameObjects and re-displays them via SetActive(true),
    // which fires OnEnable but skips the explicit Show() API — that's why
    // the earlier Show-hook never logged anything when a shop opened.
    //
    // Two complications with OnEnable:
    //   1) UIWindow.OnEnable is PRIVATE. Subclasses can't chain to it
    //      (base is inaccessible), so patching UIWindow.OnEnable misses
    //      every subclass that declares its own OnEnable.
    //   2) ItemShop / MergeShop / UIQuestDetail all DO declare their own.
    //
    // Harmony's TargetMethods lets us return multiple methods to apply the
    // same Postfix to. We sweep every UIWindow subclass at attribute-load
    // time, pick the topmost declared OnEnable up the chain for each type,
    // and patch it. Duplicates are de-duped by MethodBase identity so we
    // don't double-patch UIWindow.OnEnable itself when many subclasses
    // happen to inherit it unchanged.
    //
    // Excluded types — NPC/popup-style UIWindow subclasses where drag-to-
    // move is more annoying than helpful (they hover-anchor or auto-dismiss).
    [HarmonyPatch]
    public static class UIWindowDraggablePatch
    {
        private static readonly HashSet<string> ExcludedTypeNames = new()
        {
            "ItemPreview",
            "ItemPreviewNew",
            "itemDrop",
            "NPCEdit",
        };

        // Harmony calls this to resolve which methods to patch.
        // Returns each UIWindow-derived type's own OnEnable (the declared
        // one if it has one, otherwise the inherited base method).
        private static IEnumerable<MethodBase> TargetMethods()
        {
            HashSet<MethodBase> seen = new();
            const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type t in typeof(UIWindow).Assembly.GetTypes())
            {
                if (t == null) continue;
                if (!typeof(UIWindow).IsAssignableFrom(t)) continue;
                // Walk up the chain to find the most-derived declared OnEnable
                // for THIS type. GetMethod alone gives us the inherited method
                // when nothing is declared, which is exactly what we want.
                MethodInfo m = t.GetMethod("OnEnable", InstanceAll, null, System.Type.EmptyTypes, null);
                if (m == null) continue;
                if (seen.Add(m)) yield return m;
            }
        }

        private static void Postfix(UIWindow __instance)
        {
            if (__instance == null) return;
            string typeName = __instance.GetType().Name;
            if (ExcludedTypeNames.Contains(typeName)) return;
            DragPanel.AttachToWindowRoot(__instance, $"UIWindowDraggable:{typeName}");
        }
    }
}
