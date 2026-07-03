using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BeyondAgent.Util
{
    /// <summary>
    /// <para>
    /// Map awareness for the quest runner. A loaded AQW map is a single prefab
    /// holding EVERY cell as a child GameObject (see decomp MapCell / MapPrefab);
    /// only the current cell is active, the rest sit inactive but instantiated.
    /// So the whole layout is introspectable at runtime without visiting a thing:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>Area.currentArea.Cells</c> — the authoritative dict of every frame name.</item>
    ///   <item><c>FindObjectsByType&lt;MapMachine&gt;(Include inactive)</c> — every machine in
    ///   every cell; its frame = the nearest MapCell ancestor's name.</item>
    ///   <item><c>Area.currentArea.Monsters</c> — every monster AND friendly NPC (each carries
    ///   its <c>.Frame</c>), so an NPC's cell is a field read.</item>
    /// </list>
    /// <para>
    /// This replaces blind Goto-pad hopping: we look up exactly which cell a
    /// target machine/NPC is in and jump there with a single moveToCell (the
    /// server serves any cell, adjacency-free).
    /// </para>
    /// </summary>
    public static class MapNav
    {
        public class MachineInfo
        {
            public string Name = "";
            public string Frame = "";
            public UnityEngine.GameObject Go;
            public BoxCollider2D Collider;   // cached; drives Interactable each refresh
            public bool Interactable;        // active + collider enabled (not spent/gated)
        }

        /// <summary>Every cell/frame name in the loaded map (empty if none loaded).</summary>
        public static List<string> Cells()
        {
            try
            {
                if (Area.currentArea?.Cells != null)
                {
                    return [.. Area.currentArea.Cells.Keys];
                }
            }
            catch { }
            return [];
        }

        // The machine set of a map is fixed (all cells instantiate with the map
        // prefab), so we scan it ONCE per map and cache it — keyed by the area
        // name, invalidated when the area changes or the cached objects are
        // destroyed (map reload). Only the cheap live state (active + collider
        // enabled, which flips as cells toggle and machines get spent) is
        // refreshed per call. This turns a full FindObjectsByType scan every
        // tick into an in-memory loop.
        private static string _cacheKey;
        private static List<MachineInfo> _cache;

        /// <summary>
        /// Every MapMachine across all cells (inactive included), tagged with the
        /// cell it lives in. Cached per map; only live interactable-state is
        /// refreshed each call.
        /// </summary>
        public static List<MachineInfo> AllMachines()
        {
            string key = "";
            try { key = Area.currentArea?.Name ?? ""; } catch { }

            bool valid = _cache != null && key.Length > 0 && key == _cacheKey
                && (_cache.Count == 0 || _cache[0].Go != null);   // Unity null = destroyed
            if (!valid)
            {
                _cache = ScanMachines();
                _cacheKey = key;
            }

            foreach (MachineInfo m in _cache)
            {
                m.Interactable = m.Go != null && m.Go.activeInHierarchy
                    && (m.Collider == null || m.Collider.enabled);
            }
            return _cache;
        }

        private static List<MachineInfo> ScanMachines()
        {
            List<MachineInfo> outp = [];
            try
            {
                HashSet<string> cells = new(Cells(), StringComparer.OrdinalIgnoreCase);
                MapMachine[] machines = UnityEngine.Object.FindObjectsByType<MapMachine>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (MapMachine mm in machines)
                {
                    if (mm == null)
                    {
                        continue;
                    }
                    outp.Add(new MachineInfo
                    {
                        Name = mm.gameObject.name ?? "",
                        Frame = FrameOf(mm.transform, cells),
                        Go = mm.gameObject,
                        Collider = mm.gameObject.GetComponent<BoxCollider2D>(),
                    });
                }
            }
            catch { }
            return outp;
        }

        /// <summary>
        /// The best MapMachine matching the objective's RefArray tokens, tiered
        /// (exact &gt; prefix &gt; contains), preferring an interactable one and,
        /// within a tier, one in the current frame. Null if nothing matches.
        /// </summary>
        public static MachineInfo FindMachine(string[] refs, string currentFrame)
        {
            if (refs == null || refs.Length == 0)
            {
                return null;
            }

            MachineInfo best = null;
            int bestScore = int.MinValue;
            foreach (MachineInfo m in AllMachines())
            {
                int tier = MatchTier(m.Name, refs);
                if (tier < 0)
                {
                    continue;
                }
                // Lower tier is better; prefer interactable; prefer current frame.
                int score = -tier * 100
                    + (m.Interactable ? 20 : 0)
                    + (string.Equals(m.Frame, currentFrame, StringComparison.OrdinalIgnoreCase) ? 10 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>Diagnostic: "name@frame" for every machine in the map.</summary>
        public static string DumpMachines()
        {
            List<MachineInfo> all = AllMachines();
            return all.Count == 0
                ? "(no MapMachine objects in map)"
                : string.Join(", ", all.Select(m => $"{m.Name}@{m.Frame}{(m.Interactable ? "" : "(off)")}"));
        }

        /// <summary>
        /// The frame a friendly NPC lives in — matched by apopID (preferred) or
        /// catalog ID. Scans every monster/NPC in the map, not just the current
        /// cell. Returns null if none matches.
        /// </summary>
        public static Monster FindNpc(int wantApop, int wantNpcId)
        {
            try
            {
                if (Area.currentArea?.Monsters == null)
                {
                    return null;
                }
                foreach (Monster m in Area.currentArea.Monsters.Values)
                {
                    if (m == null || m.reactionType == Entity.ReactionType.Hostile)
                    {
                        continue;
                    }
                    if (wantApop > 0 && m.apopID == wantApop)
                    {
                        return m;
                    }
                    if (wantNpcId > 0 && m.ID == wantNpcId)
                    {
                        return m;
                    }
                }
            }
            catch { }
            return null;
        }

        // Walk up parents to the ancestor that is a known cell (its name == a
        // frame), or that carries a MapCell component.
        private static string FrameOf(Transform t, HashSet<string> cells)
        {
            for (Transform c = t; c != null; c = c.parent)
            {
                if (cells.Contains(c.gameObject.name))
                {
                    return c.gameObject.name;
                }
            }
            for (Transform c = t; c != null; c = c.parent)
            {
                if (c.GetComponent<MapCell>() != null)
                {
                    return c.gameObject.name;
                }
            }
            return "";
        }

        // -1 = no match; else 0 exact, 1 prefix, 2 contains (either direction).
        public static int MatchTier(string name, string[] refs)
        {
            int best = -1;
            foreach (string r in refs)
            {
                string tok = (r ?? "").Trim();
                if (tok.Length == 0)
                {
                    continue;
                }
                if (string.Equals(name, tok, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }
                if (name.StartsWith(tok, StringComparison.OrdinalIgnoreCase))
                {
                    best = best < 0 ? 1 : Math.Min(best, 1);
                }
                else if (name.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0
                         || tok.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    best = best < 0 ? 2 : Math.Min(best, 2);
                }
            }
            return best;
        }
    }
}
