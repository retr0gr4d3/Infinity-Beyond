using BeyondAgent.Util;
using HarmonyLib;
using System.Collections.Generic;

namespace BeyondAgent.Patches
{
    // Pet harvester. PetLoader.LoadItem reads player.Pet.{Bundle, PrefabName,
    // Scale, OffsetX, OffsetY} directly into BundlePrefabLoader, so the spoof
    // needs all of those to reproduce the load. Capture them on ctor.
    [HarmonyPatch(typeof(PetLoader), MethodType.Constructor, new System.Type[] { typeof(HumanoidAvatar) })]
    public static class PetHarvestPatch
    {
        public static void Postfix(HumanoidAvatar p)
        {
            try
            {
                if (p == null || p.character == null)
                {
                    return;
                }

                EquipItem item = p.character.Pet;
                if (item == null || item.Bundle == null)
                {
                    return;
                }

                string name = (item as Item)?.Name ?? "";
                ItemCatalog.RecordPet(item.ID, name, item.Bundle, item.PrefabName,
                                      item.Scale, item.OffsetX, item.OffsetY);
            }
            catch { }
        }
    }

    // Local-only pet visual swap. PetLoader.LoadItem reads bundle, prefab
    // name, scale and offsets directly off player.Pet (no GetBundleData
    // detour like the gear loaders), so the spoof is pure field mutation
    // on Entity.mainPlayer.Pet. Originals stashed in PetSpoofState and
    // restored on Clear. Catalog-required: scale/offsets can't be guessed.

    internal static class PetSpoofState
    {
        public static EquipItem mutatedItem;
        public static AssetBundleData origBundle;
        public static string origPrefab;
        public static double? origScale;
        public static double? origOffX;
        public static double? origOffY;

        public static void Apply(EquipItem item, AssetBundleData newBundle, string newPrefab,
                                 double? newScale, double? newOffX, double? newOffY)
        {
            if (item == null)
            {
                return;
            }

            if (mutatedItem != item)
            {
                Restore();
                origBundle = item.Bundle;
                origPrefab = item.PrefabName;
                origScale = item.Scale;
                origOffX = item.OffsetX;
                origOffY = item.OffsetY;
                mutatedItem = item;
            }
            item.Bundle = newBundle;
            item.PrefabName = newPrefab;
            item.Scale = newScale;
            item.OffsetX = newOffX;
            item.OffsetY = newOffY;
        }

        public static void Restore()
        {
            if (mutatedItem == null)
            {
                return;
            }

            try
            {
                mutatedItem.Bundle = origBundle;
                mutatedItem.PrefabName = origPrefab;
                mutatedItem.Scale = origScale;
                mutatedItem.OffsetX = origOffX;
                mutatedItem.OffsetY = origOffY;
            }
            catch { }
            mutatedItem = null;
        }
    }

    // Re-apply pet field mutation each time a PetLoader is constructed for
    // the main player. Avatar rebuilds trigger this; server-side pet swaps
    // do too (the new EquipItem flows through here).
    [HarmonyPatch(typeof(PetLoader), MethodType.Constructor, new System.Type[] { typeof(HumanoidAvatar) })]
    public static class PetSpoofReapplyPatch
    {
        public static void Postfix(HumanoidAvatar p)
        {
            if (!BeyondAgentClass.petSpoofActive || string.IsNullOrWhiteSpace(BeyondAgentClass.petSpoofBundle))
            {
                return;
            }

            try
            {
                if (p == null || p.character == null)
                {
                    return;
                }

                if (p.character != Entity.mainPlayer)
                {
                    return;
                }

                EquipItem pet = p.character.Pet;
                if (pet == null)
                {
                    return;
                }

                if (!ItemCatalog.TryGetPetOrMonster(BeyondAgentClass.petSpoofBundle, out ItemCatalog.ItemEntry cat))
                {
                    return;
                }

                Dictionary<string, ItemCatalog.ItemEntry> sourceBucket = ItemCatalog.Pets.ContainsKey(BeyondAgentClass.petSpoofBundle)
                    ? ItemCatalog.Pets : ItemCatalog.Monsters;
                AssetBundleData spoofedBundle = BundleBuilder.Build(BeyondAgentClass.petSpoofBundle, sourceBucket, pet.Bundle, pet.Bundle);
                PetSpoofState.Apply(pet, spoofedBundle, cat.prefab, cat.scale, cat.offX, cat.offY);
            }
            catch (System.Exception ex)
            {
                BeyondLog.Error($"[PetSpoofReapplyPatch] {ex.Message}");
            }
        }
    }
}
