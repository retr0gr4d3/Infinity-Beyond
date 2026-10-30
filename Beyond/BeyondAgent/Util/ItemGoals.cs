using System;
using System.Collections.Generic;
using System.Linq;

namespace BeyondAgent.Util
{
    /// <summary>
    /// Inventory counts and shop prices for QuestRunner's item goals and buy
    /// step. Items are named by display name (case-insensitive) or numeric id,
    /// the same way scripts and the drop filter name them.
    /// </summary>
    public static class ItemGoals
    {
        private static Inventory PlayerInventory => Entity.myPlayerData?.playerInventory;

        /// <summary>How many of the item the player holds (bag, not bank). 0 if unknown.</summary>
        public static int Count(string item)
        {
            Inventory inv = PlayerInventory;
            if (inv == null || string.IsNullOrEmpty(item))
            {
                return 0;
            }

            try
            {
                if (int.TryParse(item, out int id))
                {
                    return inv.getItemQuantity(id);
                }

                int total = 0;
                foreach (InventoryItem it in inv)
                {
                    if (it != null && string.Equals(it.Name?.Trim(), item.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        total += Math.Max(1, it.Quantity);
                    }
                }
                return total;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>The shop item named by `item` (name or item id), or null.</summary>
        public static ShopItem FindShopItem(shopData shop, string item)
        {
            if (shop?.items == null)
            {
                return null;
            }

            bool byId = int.TryParse(item, out int id);
            return shop.items.FirstOrDefault(si => si != null
                && (byId ? si.ID == id : string.Equals(si.Name?.Trim(), item.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Everything one unit of `si` costs, as (item id, item name, qty): its
        /// merge materials (turnins) and/or its token price (TokID/TokQty).
        /// Gold/AC prices aren't farmable, so they're not listed.
        /// </summary>
        public static List<(int id, string name, int qty)> Costs(ShopItem si)
        {
            List<(int, string, int)> costs = [];
            if (si == null)
            {
                return costs;
            }
            if (si.turnins != null)
            {
                foreach (mergeItemTurnin t in si.turnins)
                {
                    if (t != null && t.ItemID > 0)
                    {
                        costs.Add((t.ItemID, t.getName() ?? t.Name ?? "", t.getQuantity()));
                    }
                }
            }
            if (si.TokID > 0 && si.TokQty > 0)
            {
                costs.Add((si.TokID, si.TokName ?? "", si.TokQty));
            }
            return costs;
        }

        /// <summary>True when the player holds every material for one unit of `si`.</summary>
        public static bool CanAfford(ShopItem si, out string missing)
        {
            missing = "";
            if (si != null && si.Coins && si.Cost > 0)
            {
                missing = $"{si.Cost} AC (the bot never spends AC)";
                return false;
            }
            Inventory inv = PlayerInventory;
            if (inv == null)
            {
                missing = "inventory not loaded";
                return false;
            }
            foreach (var cost in Costs(si).GroupBy(c => c.id))
            {
                int id = cost.Key;
                string name = cost.First().name;
                int qty = checked(cost.Sum(c => c.qty));
                int have = inv.getItemQuantity(id);
                if (have < qty)
                {
                    missing = $"{name} {have}/{qty}";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Total materials still needed to buy every item in `plan` (minus what's
        /// already owned of each target), keyed by material name and id. This is
        /// what "until enough" goals farm toward.
        /// </summary>
        public static Dictionary<string, int> Requirements(shopData shop, QuestChains.BuyPlan plan)
        {
            Dictionary<string, int> need = new(StringComparer.OrdinalIgnoreCase);
            if (shop == null || plan == null)
            {
                return need;
            }
            // Quantities are desired inventory totals, not repeated purchases.
            // Resolve aliases before grouping so a name and ID cannot double-count a reward.
            var targets = plan.items.Select(target => new { ShopItem = FindShopItem(shop, target.item), target.qty })
                .Where(target => target.ShopItem != null).GroupBy(target => target.ShopItem.ID);
            foreach (var target in targets)
            {
                ShopItem si = target.First().ShopItem;
                int toBuy = Math.Max(0, target.Max(t => t.qty) - Count(si.ID.ToString()));
                foreach ((int id, string name, int each) in Costs(si))
                {
                    int total = checked(toBuy * each);
                    if (!string.Equals(name?.Trim(), id.ToString(), StringComparison.OrdinalIgnoreCase)) Add(need, name, total);
                    Add(need, id.ToString(), total);
                }
            }
            return need;

            static void Add(Dictionary<string, int> d, string key, int n)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    d[key.Trim()] = checked((d.TryGetValue(key.Trim(), out int cur) ? cur : 0) + n);
                }
            }
        }
    }
}
