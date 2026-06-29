using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BeyondAgent.Util
{
    /// <summary>
    /// Drop filter that can whitelist (accept only) or blacklist (reject) items.
    /// Filters by name, ID, and/or rarity. Drops arrive in "rewardPlayer" s2c
    /// packets; anything the filter rejects is dusted via a "discardDrop" c2s
    /// request.
    ///
    /// Threading: HandleRewardPacket runs on the network thread, ApplyDropFilter
    /// on the main thread — filter state is guarded by _gate. Actual sends are
    /// queued and flushed by DrainDiscards() on the main thread (OnUpdate).
    /// </summary>
    public static class DropFilterEngine
    {
        private static List<string> _filterItemNames = new();
        private static List<int> _filterItemIds = new();
        private static List<string> _filterRarities = new();
        private static bool _acceptOnly = true; // true = accept (whitelist), false = reject (blacklist)
        private static readonly object _gate = new();

        // Drops to dust, produced on the network thread, flushed on the main thread.
        private static readonly ConcurrentQueue<(long itemId, long lootId)> _pendingDiscards = new();

        /// <summary>
        /// Apply a new filter configuration. Clears previous filter.
        /// If no items/rarities specified, disables filtering entirely.
        /// </summary>
        public static void ApplyDropFilter(List<string> itemNames, List<int> itemIds, List<string> rarities, string action)
        {
            lock (_gate)
            {
                if (itemNames?.Count > 0 || itemIds?.Count > 0 || rarities?.Count > 0)
                {
                    _filterItemNames = itemNames ?? new();
                    _filterItemIds = itemIds ?? new();
                    _filterRarities = rarities?.Select(r => r.ToLower()).ToList() ?? new();
                    _acceptOnly = action?.ToLower() == "accept";
                }
                else
                {
                    DisableFilter();
                }
            }
        }

        /// <summary>
        /// Inspect a "rewardPlayer" packet and queue any rejected drops for dusting.
        /// Cheap no-op when no filter is active. Safe to call on the network thread.
        /// </summary>
        public static void HandleRewardPacket(string rawJson)
        {
            if (!HasActiveFilter() || string.IsNullOrEmpty(rawJson))
            {
                return;
            }

            JObject obj;
            try { obj = JObject.Parse(rawJson); }
            catch { return; }

            if ((string)obj["Cmd"] != "rewardPlayer" || obj["items"] is not JArray items)
            {
                return;
            }

            foreach (JToken tok in items)
            {
                if (tok is not JObject item)
                {
                    continue;
                }

                string name = (string)item["Name"] ?? "";
                int id = (int?)item["ID"] ?? 0;
                long lootId = (long?)item["LootID"] ?? 0;
                if (lootId == 0)
                {
                    continue;
                }

                // The gem drop packet carries no per-item rarity (only the
                // ItemPattern's Quality, which is the target gear's). Until the
                // Quality->tier mapping is known, rarity is left empty and the
                // safety guard in ShouldAllowDrop never dusts on rarity alone.
                if (!ShouldAllowDrop(name, id, ""))
                {
                    _pendingDiscards.Enqueue((id, lootId));
                }
            }
        }

        /// <summary>
        /// Flush queued dusts as discardDrop requests. Main thread only.
        /// </summary>
        public static void DrainDiscards()
        {
            if (_pendingDiscards.IsEmpty || AEC.Instance == null)
            {
                return;
            }

            while (_pendingDiscards.TryDequeue(out (long itemId, long lootId) drop))
            {
                try
                {
                    JObject payload = new()
                    {
                        ["ItemID"] = drop.itemId,
                        ["LootID"] = drop.lootId,
                        ["Cmd"] = "discardDrop",
                        ["Params"] = new JArray(drop.itemId.ToString(), drop.lootId.ToString()),
                    };
                    AEC.Instance.sendRequest(new Request(payload.ToString(Formatting.None)));
                    BeyondLog.Msg($"[DropFilter] Dusted drop ItemID={drop.itemId} LootID={drop.lootId}");
                }
                catch (System.Exception ex)
                {
                    BeyondLog.Error($"[DropFilter] discard failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Check if an item drop should be allowed based on current filter.
        /// Returns true if DROP IS ALLOWED, false if it should be dusted.
        /// </summary>
        public static bool ShouldAllowDrop(string itemName, int itemId, string itemRarity)
        {
            lock (_gate)
            {
                if (!HasActiveFilterLocked()) return true; // No filter = allow all

                // Never dust an item whose rarity we can't determine when a
                // rarity filter is active — fail safe toward keeping the item.
                if (_filterRarities.Count > 0 && string.IsNullOrEmpty(itemRarity))
                {
                    return true;
                }

                bool matchesName = _filterItemNames.Count == 0 || _filterItemNames.Contains(itemName, System.StringComparer.OrdinalIgnoreCase);
                bool matchesId = _filterItemIds.Count == 0 || _filterItemIds.Contains(itemId);
                bool matchesRarity = _filterRarities.Count == 0 || _filterRarities.Contains(itemRarity?.ToLower());

                bool isMatch = matchesName && matchesId && matchesRarity;

                return _acceptOnly ? isMatch : !isMatch;
            }
        }

        /// <summary>
        /// Clear any active filter.
        /// </summary>
        public static void ClearFilter()
        {
            lock (_gate)
            {
                DisableFilter();
            }
        }

        private static void DisableFilter()
        {
            _filterItemNames.Clear();
            _filterItemIds.Clear();
            _filterRarities.Clear();
            _acceptOnly = true;
        }

        private static bool HasActiveFilter()
        {
            lock (_gate)
            {
                return HasActiveFilterLocked();
            }
        }

        private static bool HasActiveFilterLocked() =>
            _filterItemNames.Count > 0 || _filterItemIds.Count > 0 || _filterRarities.Count > 0;
    }
}
