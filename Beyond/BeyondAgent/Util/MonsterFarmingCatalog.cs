using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using BeyondAgent.Patches;
using HarmonyLib;

namespace BeyondAgent.Util
{
    public static class MonsterFarmingCatalog
    {
        public static readonly MaterialSourceIndex Sources = LoadSources();
        private static string Server => Main.WebApiURL ?? "";
        private static string CachePath => Path.Combine(BeyondEnv.UserDataDirectory, "Beyond", "drop-sources.json");
        private static MaterialSourceIndex LoadSources()
        {
            try { var index = File.Exists(CachePath) ? JsonConvert.DeserializeObject<MaterialSourceIndex>(File.ReadAllText(CachePath)) ?? new MaterialSourceIndex() : new MaterialSourceIndex(); index.Records ??= new List<MaterialDropRecord>(); return index; }
            catch { return new MaterialSourceIndex(); }
        }
        public static void ObserveDrops(ResponseGetMonDrops response)
        {
            Sources.Record(Server, response.monID, (response.ItemDrops ?? []).Where(d => d != null && !d.Hide && d.ItemData != null)
                .Select(d => new MaterialDrop { ItemId = d.itemID, Name = d.ItemData.Name, Rate = d.Rate, Quantity = d.Quantity }), DateTime.UtcNow);
            try { System.IO.Directory.CreateDirectory(Path.GetDirectoryName(CachePath)); File.WriteAllText(CachePath, JsonConvert.SerializeObject(Sources)); } catch { }
        }
        public static List<MaterialMonster> CurrentSources() => Area.currentArea?.Monsters?.Values
            .Where(m => m != null && m.reactionType == Entity.ReactionType.Hostile)
            .Select(m => new MaterialMonster { Id = m.ID, Name = m.Name, Area = Area.currentArea.Name, Frame = m.Frame, Alive = m.currentState != Entity.State.Dead }).ToList() ?? [];
        public static MaterialDiscoverySession BeginDiscovery() => new(Sources, Server, Area.currentArea?.Name ?? "", CurrentSources(), Time.time, DateTime.UtcNow);
        public static MaterialMonster FindSource(int itemId) => Sources.Choose(Server, itemId, CurrentSources(), Entity.mainPlayer?.Frame ?? "", DateTime.UtcNow);

        public static void SendMonsters()
        {
            string frame = Entity.mainPlayer?.Frame ?? "";
            var monsters = Area.currentArea?.Monsters?.Values;
            LauncherServer.Send(new
            {
                Type = "FarmingMonsters",
                Area = Area.currentArea?.Name ?? "",
                Monsters = monsters == null ? new object[0] : monsters
                    .Where(m => m != null && m.reactionType == Entity.ReactionType.Hostile)
                    .GroupBy(m => m.ID)
                    .Select(g => g.OrderByDescending(m => string.Equals(m.Frame, frame, StringComparison.OrdinalIgnoreCase)).First())
                    .OrderBy(m => m.Name)
                    .Select(m => (object)new { Id = m.ID, Name = m.Name, Frame = m.Frame }).ToArray()
            });
        }

        public static void RequestDrops(int monsterId)
        {
            if (monsterId <= 0 || AEC.Instance == null) return;
            AEC.Instance.sendRequest(new RequestGetMonDrops(monsterId));
        }
    }

    [HarmonyPatch(typeof(ResponseGetMonDrops), nameof(ResponseGetMonDrops.Execute))]
    public static class MonsterFarmingDropsPatch
    {
        public static void Postfix(ResponseGetMonDrops __instance)
        {
            if (__instance == null) return;
            try
            {
                try { MonsterFarmingCatalog.ObserveDrops(__instance); }
                catch (Exception ex) { BeyondLog.Warning($"Drop cache unavailable: {ex.Message}"); }
                LauncherServer.Send(new
                {
                    Type = "FarmingDrops",
                    MonsterId = __instance.monID,
                    Items = (__instance.ItemDrops ?? []).Where(d => d != null && !d.Hide && d.ItemData != null)
                        .Select(d => new
                        {
                            Id = d.itemID,
                            Name = d.ItemData.Name,
                            Owned = ItemGoals.Count(d.itemID.ToString()),
                            StackSize = d.ItemData.StackSize
                        }).ToArray()
                });
            }
            catch (Exception ex) { BeyondLog.Warning($"Monster drop list failed: {ex.Message}"); }
        }
    }
}
