using System.Collections.Generic;

namespace BeyondAgent.Util
{
    /// <summary>
    /// Drop filter driven by a main-thread scan of the game's loot inventory
    /// (<see cref="Game.lootItems"/>). Two independent lists:
    ///   - Keep   -> matching drops are looted into the inventory (MoveToInv / getDrop).
    ///   - Reject -> matching drops are dusted (DiscardItem / discardDrop).
    /// Drops matching neither are left in the loot window. "Delete others" turns
    /// Keep into a strict whitelist: everything not kept is dusted (Reject ignored).
    ///
    /// Because it scans the live loot list every tick, the filter is retroactive:
    /// items already sitting in the loot window when the filter starts are actioned
    /// on the next scan, the same as freshly dropped ones (the game adds every
    /// reward drop to Game.lootItems via ResponseRewardPlayer).
    ///
    /// Each list entry is "Name", a numeric "ID", or "Name:rarity". A bare name/ID
    /// matches at any rarity (rarity is ignored for regular items). The ":rarity"
    /// qualifier only applies to GEMS (a drop with an ItemPattern); a gem's tier
    /// comes from ItemPattern.Quality.
    /// </summary>
    public static class DropFilterEngine
    {
        /// <summary>One parsed filter entry: a name or numeric ID with an optional gem-rarity qualifier.</summary>
        private sealed class Entry
        {
            public string Name;   // null when the entry parsed as a numeric ID or wildcard
            public int? Id;       // set when the entry parsed as an integer
            public string Rarity; // null = any rarity (bare name); else a gem tier
            public bool AnyGem;   // "*gem" wildcard: matches any gem (of Rarity, if set), ignoring name
        }

        private static List<Entry> _keep = new();
        private static List<Entry> _reject = new();
        private static bool _deleteOthers;
        private static readonly object _gate = new();

        // The server rate-limits loot actions and replies "Spam Detected" if they
        // come too fast (observed: bursts <0.2s apart get blocked; actions seconds
        // apart succeed). So we act on ONE drop at a time, spaced by a wall-clock
        // delay, and back off hard whenever the server flags spam.
        //
        // _sentLootIds = drops we've already sent a request for (skipped until the
        // server removes them from the loot list, i.e. confirms). _nextActionTime
        // is the earliest realtime we may send the next action.
        private static readonly HashSet<int> _sentLootIds = new();
        private static float _nextActionTime;

        // Set from the network thread when the server replies "Spam Detected";
        // consumed on the main thread in Tick (Unity's Time API is main-thread only).
        private static volatile bool _spamDetected;

        private const float ActionDelaySeconds = 2.0f;
        private const float SpamBackoffSeconds = 8.0f;

        private static readonly HashSet<string> _knownRarities = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "common", "uncommon", "rare", "epic", "legendary", "mythic",
        };

        /// <summary>
        /// Apply new Keep/Reject lists and the delete-others mode. Clears the
        /// "already sent" set so the new filter re-acts on everything currently
        /// in the loot window (retroactive). If both lists are empty and
        /// deleteOthers is off, filtering is disabled.
        /// </summary>
        public static void ApplyDropFilter(string keepRaw, string rejectRaw, bool deleteOthers)
        {
            List<Entry> keep = ParseEntries(keepRaw);
            List<Entry> reject = ParseEntries(rejectRaw);
            lock (_gate)
            {
                _keep = keep;
                _reject = reject;
                _deleteOthers = deleteOthers;
                _sentLootIds.Clear();
            }
            _nextActionTime = 0f;
            Diag($"ApplyDropFilter keep={keep.Count} reject={reject.Count} deleteOthers={deleteOthers} keepRaw=[{keepRaw}] rejectRaw=[{rejectRaw}]");
        }

        /// <summary>Clear the filter.</summary>
        public static void ClearFilter()
        {
            lock (_gate)
            {
                _keep = new();
                _reject = new();
                _deleteOthers = false;
                _sentLootIds.Clear();
            }
            _nextActionTime = 0f;
        }

        /// <summary>
        /// The server replied "Spam Detected" — back off and let pending drops be
        /// retried after the cooldown. Called from the s2c packet hook.
        /// </summary>
        public static void NotifySpamDetected()
        {
            // Runs on the network thread — no Unity API here. Tick applies the
            // backoff on the main thread.
            lock (_gate) { _sentLootIds.Clear(); }
            _spamDetected = true;
        }

        /// <summary>
        /// Scan the live loot inventory and act on matches. Main thread only —
        /// call from OnUpdate. Throttled internally.
        /// </summary>
        public static void Tick()
        {
            bool active;
            lock (_gate) { active = _keep.Count > 0 || _reject.Count > 0 || _deleteOthers; }
            if (!active)
            {
                return;
            }

            Loot loot = Game.lootItems;
            if (loot == null)
            {
                return;
            }

            List<InventoryItem> list;
            try { list = loot.getLootList(); }
            catch { return; }
            if (list == null)
            {
                return;
            }

            var present = new HashSet<int>();
            foreach (InventoryItem it in list)
            {
                if (it != null && it.LootID > 0) present.Add(it.LootID);
            }

            // Forget drops the server has removed (our request succeeded), so they
            // can't pile up and a fresh drop reusing the LootID can be re-actioned.
            lock (_gate) { _sentLootIds.RemoveWhere(id => !present.Contains(id)); }

            float now = UnityEngine.Time.realtimeSinceStartup;

            // The server flagged spam (set on the network thread): back off hard.
            if (_spamDetected)
            {
                _spamDetected = false;
                _nextActionTime = now + SpamBackoffSeconds;
                Diag($"spam detected -> backing off {SpamBackoffSeconds}s");
            }

            // Wall-clock rate limit: at most one loot action per ActionDelaySeconds.
            if (now < _nextActionTime)
            {
                return;
            }

            // Act on exactly one drop per pass, then wait out the delay.
            foreach (InventoryItem item in list)
            {
                if (item == null)
                {
                    continue;
                }

                int lootId = item.LootID;
                if (lootId <= 0)
                {
                    continue;
                }

                // Already requested and not yet removed -> leave it (avoid re-spamming
                // the same drop; it's retried after a spam backoff or re-apply).
                lock (_gate)
                {
                    if (_sentLootIds.Contains(lootId))
                    {
                        continue;
                    }
                }

                string name = item.Name ?? "";
                int id = item.ID;

                // The game treats a drop as a gem iff it has an ItemPattern
                // (CombatLootDrop.SetItem). Gems take their rarity from
                // ItemPattern.Quality; regular items have no gem-rarity.
                bool isGem = item.ItemPattern != null;
                string rarity = isGem ? QualityToRarity(item.ItemPattern.Quality) : null;

                bool keep, reject;
                lock (_gate)
                {
                    keep = MatchesAny(_keep, name, id, isGem, rarity);
                    if (keep)
                    {
                        reject = false;
                    }
                    else if (_deleteOthers)
                    {
                        // Strict whitelist: everything not kept is dusted.
                        reject = true;
                    }
                    else
                    {
                        reject = MatchesAny(_reject, name, id, isGem, rarity);
                    }
                }

                if (!keep && !reject)
                {
                    continue;
                }

                try
                {
                    // The game's own methods build the exact RequestGetDrop /
                    // RequestDiscardDrop a real client sends.
                    if (keep)
                    {
                        loot.MoveToInv(item);
                    }
                    else
                    {
                        loot.DiscardItem(item);
                    }

                    lock (_gate) { _sentLootIds.Add(lootId); }
                    _nextActionTime = now + ActionDelaySeconds;
                    Diag($"{(keep ? "KEEP getDrop" : "REJECT discardDrop")} Name=[{name}] ID={id} LootID={lootId} gem={isGem} rarity=[{rarity}]");
                }
                catch (System.Exception ex)
                {
                    lock (_gate) { _sentLootIds.Add(lootId); }
                    _nextActionTime = now + ActionDelaySeconds;
                    Diag($"send failed ID={id} LootID={lootId}: {ex.Message}");
                    BeyondLog.Error($"[DropFilter] send failed: {ex.Message}");
                }

                return; // one action per pass; wait out the delay
            }
        }

        /// <summary>
        /// Parse "Lucky Cape Gem:rare, Blinding Light of Destiny, 5357" into entries.
        /// Comma-separated; multi-word names are preserved. A trailing ":rarity" is
        /// only split off when the suffix is a known tier, so names containing a
        /// colon survive intact.
        /// </summary>
        private static List<Entry> ParseEntries(string raw)
        {
            var result = new List<Entry>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return result;
            }

            foreach (string part in raw.Split(','))
            {
                string token = part.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                string rarity = null;
                int colon = token.LastIndexOf(':');
                if (colon >= 0)
                {
                    string suffix = token.Substring(colon + 1).Trim();
                    if (_knownRarities.Contains(suffix))
                    {
                        rarity = suffix.ToLower();
                        token = token.Substring(0, colon).Trim();
                    }
                }

                if (token.Length == 0)
                {
                    continue;
                }

                var entry = new Entry { Rarity = rarity };
                if (string.Equals(token, "*gem", System.StringComparison.OrdinalIgnoreCase))
                {
                    // "*gem:rare" -> any rare gem; "*gem" alone -> any gem, any tier.
                    entry.AnyGem = true;
                }
                else if (int.TryParse(token, out int id))
                {
                    entry.Id = id;
                }
                else
                {
                    entry.Name = token;
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>Caller must hold _gate.</summary>
        private static bool MatchesAny(List<Entry> entries, string name, int id, bool isGem, string rarity)
        {
            foreach (Entry e in entries)
            {
                // "*gem" wildcard: match any gem, optionally constrained to a tier.
                if (e.AnyGem)
                {
                    if (isGem && (e.Rarity == null || string.Equals(e.Rarity, rarity, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                    continue;
                }

                bool nameMatch = e.Id.HasValue
                    ? e.Id.Value == id
                    : string.Equals(e.Name, name, System.StringComparison.OrdinalIgnoreCase);
                if (!nameMatch)
                {
                    continue;
                }

                // Bare name/ID -> matches at any rarity (universal, the rule for items).
                if (e.Rarity == null)
                {
                    return true;
                }

                // A rarity qualifier only ever matches a gem of that exact tier.
                if (isGem && string.Equals(e.Rarity, rarity, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Map a gem's ItemPattern.Quality to a rarity tier. Positionally mirrors
        /// the game's CombatLootDrop.QualityToLootRarity (5+ ladder, &lt;5 = common),
        /// using Beyond's label vocabulary. Returns "" for unknown qualities.
        /// </summary>
        private static string QualityToRarity(int q) => q switch
        {
            >= 1 and <= 5 => "common",
            6 => "uncommon",
            7 => "rare",
            8 => "epic",
            9 => "legendary",
            10 => "mythic",
            _ => "",
        };

        /// <summary>
        /// Diagnostic breadcrumb. BeyondLog -> Debug.Log isn't captured in this
        /// build's Player.log, so we also drop a synthetic entry into the
        /// always-on packets.jsonl, where it's reliably greppable as "__dropfilter".
        /// </summary>
        private static void Diag(string msg)
        {
            BeyondLog.Msg($"[DropFilter] {msg}");
            try
            {
                PacketLog.Write("s2c", $"{{\"Cmd\":\"__dropfilter\",\"msg\":{Newtonsoft.Json.JsonConvert.SerializeObject(msg)}}}", synthetic: true);
            }
            catch { }
        }
    }
}
