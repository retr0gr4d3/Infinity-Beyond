using HarmonyLib;
using Infinity_TestMod.Util;

namespace Infinity_TestMod.Patches
{
    // Weapon harvester. WeaponLoader.GetBundleData reads player.Weapon.Bundle;
    // we catalog it on construction along with PrefabName and ItemType so the
    // spoof can later reproduce the prefab lookup and hold-pose selection.
    [HarmonyPatch(typeof(WeaponLoader), MethodType.Constructor, new System.Type[] { typeof(HumanoidAvatar) })]
    public static class WeaponHarvestPatch
    {
        public static void Postfix(HumanoidAvatar p)
        {
            try
            {
                if (p == null || p.character == null) return;
                EquipItem item = p.character.Weapon;
                if (item == null || item.Bundle == null) return;
                string name = (item as Item)?.Name ?? "";
                ItemCatalog.RecordWeapon(item.ID, name, item.Bundle, item.PrefabName, (int)item.ItemType);
            }
            catch { }
        }
    }

    // Local-only weapon visual swap. Unlike helm/armor/back, the receiver
    // (HumanoidAvatar.onBundleLoaded) reads two more fields off the live
    // EquipItem:
    //   * character.Weapon.PrefabName  -> which prefab inside the bundle to spawn
    //   * character.Weapon.ItemType    -> drives CC.LoadWeapon's hold pose
    //                                     (Sword/Staff/Dagger/Bow/etc.)
    // Bundle-only override therefore isn't enough: a fresh bundle with the
    // old PrefabName won't resolve, and a Dagger held with a Sword pose
    // floats off the hand. So in addition to the GetBundleData postfix we
    // mutate those two fields on Entity.mainPlayer.Weapon while the spoof
    // is active. Originals are stashed and restored on Clear.
    //
    // Catalog-required: without an ItemCatalog entry for the target bundle
    // we have no PrefabName/ItemType to feed in, so Apply bails. The catalog
    // is fed passively by WeaponHarvestPatch � seeing the target weapon on
    // any character once is enough.

    /// <summary>
    /// Shared state for the weapon spoof's field mutation: which EquipItem
    /// we touched and the values we displaced. Held on the patch side so
    /// the WeaponLoader-ctor postfix can re-apply automatically when the
    /// server hands the player a new Weapon mid-spoof.
    /// </summary>
    internal static class WeaponSpoofState
    {
        public static EquipItem mutatedItem;
        public static string origPrefab;
        public static iType origType;

        /// <summary>
        /// Stash originals (first time on this item) and overwrite with
        /// spoof values. Safe to call repeatedly with the same item � only
        /// the first call captures originals.
        /// </summary>
        public static void Apply(EquipItem item, string newPrefab, iType newType)
        {
            if (item == null) return;
            if (mutatedItem != item)
            {
                // Different EquipItem instance � restore the previous one
                // (server may have swapped weapons) before claiming this one.
                Restore();
                origPrefab = item.PrefabName;
                origType = item.ItemType;
                mutatedItem = item;
            }
            item.PrefabName = newPrefab;
            item.ItemType = newType;
        }

        public static void Restore()
        {
            if (mutatedItem == null) return;
            try
            {
                mutatedItem.PrefabName = origPrefab;
                mutatedItem.ItemType = origType;
            }
            catch { }
            mutatedItem = null;
        }
    }

    // 1. Bundle override � same shape as ArmorSpoofPatch.
    [HarmonyPatch(typeof(WeaponLoader), "GetBundleData")]
    public static class WeaponSpoofPatch
    {
        private static readonly AccessTools.FieldRef<charItemLoader, HumanoidAvatar> _avtRef =
            AccessTools.FieldRefAccess<charItemLoader, HumanoidAvatar>("avt");

        public static void Postfix(WeaponLoader __instance, ref AssetBundleData __result)
        {
            if (!TestMod.weaponSpoofActive || string.IsNullOrWhiteSpace(TestMod.weaponSpoofBundle))
                return;
            try
            {
                HumanoidAvatar avt = _avtRef(__instance);
                if (avt == null || avt.character == null) return;
                if (avt.character != Entity.mainPlayer) return;

                __result = BundleBuilder.Build(TestMod.weaponSpoofBundle, ItemCatalog.Weapons, avt.character.Weapon?.Bundle, __result);
            }
            catch (System.Exception ex)
            {
                BeyondLog.Error($"[WeaponSpoofPatch] {ex.Message}");
            }
        }
    }

    // 2. Re-apply PrefabName/ItemType mutation each time a WeaponLoader is
    // constructed for the main player. Avatar rebuilds trigger this; so do
    // server-side weapon swaps (the new EquipItem flows through here).
    // If the catalog doesn't have an entry for the target bundle we can't
    // pick a sane PrefabName/ItemType � leave the live values alone and log.
    [HarmonyPatch(typeof(WeaponLoader), MethodType.Constructor, new System.Type[] { typeof(HumanoidAvatar) })]
    public static class WeaponSpoofReapplyPatch
    {
        public static void Postfix(HumanoidAvatar p)
        {
            if (!TestMod.weaponSpoofActive || string.IsNullOrWhiteSpace(TestMod.weaponSpoofBundle))
                return;
            try
            {
                if (p == null || p.character == null) return;
                if (p.character != Entity.mainPlayer) return;
                EquipItem weapon = p.character.Weapon;
                if (weapon == null) return;
                if (!ItemCatalog.Weapons.TryGetValue(TestMod.weaponSpoofBundle, out ItemCatalog.ItemEntry cat)) return;
                WeaponSpoofState.Apply(weapon, cat.prefab, (iType)cat.itemType);
            }
            catch (System.Exception ex)
            {
                BeyondLog.Error($"[WeaponSpoofReapplyPatch] {ex.Message}");
            }
        }
    }
}
