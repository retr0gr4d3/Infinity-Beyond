using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeyondAgent.Util
{
    /// <summary>
    /// <para>
    /// Wall-aware walking. A straight WalkVector toward a distant point drags
    /// the character along every curved wall between here and there (the game
    /// physics just slides the Rigidbody2D until EntityMovementUpdater's
    /// blockedMoveTimer kills the walk). This plans around the walls instead:
    /// </para>
    /// <para>
    ///   1. Straight shot: if a Physics2D raycast on the game's "Blocker"
    ///      layer (the same test EntityMovementUpdater.ScanPathForBlockers
    ///      uses) says the line is clear, just walk it.
    ///   2. Otherwise: A* over a coarse grid sampled from the Blocker layer
    ///      (OverlapCircle per cell, sized to the character's clearance),
    ///      string-pulled down to the few corner waypoints that matter.
    /// </para>
    /// <para>
    /// Each waypoint is fed to the game's own EntityMovementUpdater.walkTo()
    /// (public — the exact method mouse-click charges use), so animation,
    /// facing, server RequestMovement sync, and blocked-stop behaviour all
    /// stay stock. Tick() advances waypoints and re-plans once on a stall;
    /// a second stall or an unpathable grid sets Failed and the caller falls
    /// back to a server-side moveToCell.
    /// </para>
    /// All positions are LOCAL to the player's parent transform (the space
    /// targetPosition / monster coords / walkTo use) unless named World.
    /// </summary>
    public class PathWalker
    {
        // Grid tuning. Cell 0.5 local units ≈ half a character footprint —
        // fine enough to find door gaps, coarse enough to plan in <1ms on the
        // biggest cells. Clearance inflates blockers so corners aren't
        // clipped; margin pads the search box around start/goal.
        public float CellSize = 0.5f;
        public float ClearanceRadius = 0.35f;
        public float BoundsMargin = 6f;
        public int MaxGridCells = 160 * 160;
        public float WaypointReachedDist = 0.45f;
        public float StallSec = 1.2f;

        public bool Active { get; private set; }
        public bool Done { get; private set; }
        public bool Failed { get; private set; }
        public string Status { get; private set; } = "";
        /// <summary>Where the current walk is headed (local). Callers compare
        /// against a moving target to decide when to re-plan.</summary>
        public Vector3 Goal => _goal;

        private Vector3 _goal;
        private List<Vector3> _waypoints = [];
        private int _idx;
        private bool _walkIssued;
        private float _issuedAt;
        private Vector3 _lastPos;
        private float _lastMoveAt;
        private bool _replanned;

        private static int _blockerMask = -1;
        private static int BlockerMask => _blockerMask >= 0 ? _blockerMask : _blockerMask = LayerMask.GetMask("Blocker");

        private readonly Action<string> _log;

        public PathWalker(Action<string> log = null)
        {
            _log = log;
        }

        public void Begin(Vector3 goalLocal)
        {
            _goal = goalLocal;
            Active = true;
            Done = false;
            Failed = false;
            _replanned = false;
            _walkIssued = false;
            _lastPos = PlayerLocal();
            _lastMoveAt = Time.time;
            Plan();
        }

        public void Cancel()
        {
            Active = false;
        }

        /// <summary>Drive the walk. Call once per frame while traveling.</summary>
        public void Tick()
        {
            if (!Active || Done || Failed)
            {
                return;
            }

            Vector3 here = PlayerLocal();
            if ((here - _lastPos).sqrMagnitude > 0.001f)
            {
                _lastPos = here;
                _lastMoveAt = Time.time;
            }

            // Advance past every waypoint we've reached — and string-pull:
            // skip ahead to the furthest waypoint we can see, so the walk cuts
            // corners a grid path would staircase around.
            while (_idx < _waypoints.Count
                   && Vector2.Distance(here, _waypoints[_idx]) <= WaypointReachedDist)
            {
                _idx++;
                _walkIssued = false;
            }
            if (_idx < _waypoints.Count)
            {
                int furthest = _idx;
                for (int i = _waypoints.Count - 1; i > _idx; i--)
                {
                    if (LineClear(here, _waypoints[i]))
                    {
                        furthest = i;
                        break;
                    }
                }
                if (furthest != _idx)
                {
                    _idx = furthest;
                    _walkIssued = false;
                }
            }

            if (_idx >= _waypoints.Count)
            {
                Done = true;
                Active = false;
                Status = "arrived";
                return;
            }

            bool stalled = _walkIssued
                && Time.time - _issuedAt > 0.6f
                && Time.time - _lastMoveAt > StallSec;
            if (stalled)
            {
                if (_replanned)
                {
                    Failed = true;
                    Active = false;
                    Status = "stalled twice";
                    _log?.Invoke("  [path] stalled twice — giving up");
                    return;
                }
                _log?.Invoke("  [path] stalled — re-planning");
                _replanned = true;
                Plan();
                if (Failed)
                {
                    return;
                }
            }

            if (!_walkIssued)
            {
                WalkTo(_waypoints[_idx]);
                _walkIssued = true;
                _issuedAt = Time.time;
            }
            Status = $"waypoint {_idx + 1}/{_waypoints.Count}";
        }

        // ---- planning ----

        private void Plan()
        {
            _waypoints.Clear();
            _idx = 0;
            _walkIssued = false;
            Vector3 start = PlayerLocal();

            // Straight shot covers the common case (open cell) with zero cost.
            if (LineClear(start, _goal))
            {
                _waypoints.Add(_goal);
                Status = "direct";
                return;
            }

            List<Vector3> path = GridPath(start, _goal);
            if (path == null || path.Count == 0)
            {
                Failed = true;
                Active = false;
                Status = "no path";
                _log?.Invoke("  [path] A* found no route — blocked grid or unreachable goal");
                return;
            }
            _waypoints = path;
            _log?.Invoke($"  [path] planned {path.Count} waypoint(s) around blockers");
        }

        /// <summary>
        /// A* on a grid covering start+goal (+margin), blocked cells sampled
        /// from the Blocker layer, result string-pulled to corner waypoints.
        /// Returns null when unpathable.
        /// </summary>
        private List<Vector3> GridPath(Vector3 start, Vector3 goal)
        {
            float minX = Mathf.Min(start.x, goal.x) - BoundsMargin;
            float maxX = Mathf.Max(start.x, goal.x) + BoundsMargin;
            float minY = Mathf.Min(start.y, goal.y) - BoundsMargin;
            float maxY = Mathf.Max(start.y, goal.y) + BoundsMargin;
            int w = Mathf.CeilToInt((maxX - minX) / CellSize);
            int h = Mathf.CeilToInt((maxY - minY) / CellSize);
            if (w <= 0 || h <= 0 || w * h > MaxGridCells)
            {
                return null;
            }

            // Sample the blockers once. OverlapCircle in WORLD space with the
            // clearance radius scaled by the parent transform, so a "free"
            // cell really fits the character.
            Transform parent = PlayerParent();
            if (parent == null)
            {
                return null;
            }

            float worldRadius = ClearanceRadius * parent.lossyScale.x;
            bool[,] blocked = new bool[w, h];
            for (int gx = 0; gx < w; gx++)
            {
                for (int gy = 0; gy < h; gy++)
                {
                    Vector3 local = new(minX + (gx + 0.5f) * CellSize, minY + (gy + 0.5f) * CellSize, start.z);
                    Vector3 world = parent.TransformPoint(local);
                    blocked[gx, gy] = Physics2D.OverlapCircle(world, worldRadius, BlockerMask) != null;
                }
            }

            (int x, int y) Cell(Vector3 p) => (
                Mathf.Clamp(Mathf.FloorToInt((p.x - minX) / CellSize), 0, w - 1),
                Mathf.Clamp(Mathf.FloorToInt((p.y - minY) / CellSize), 0, h - 1));
            Vector3 Center((int x, int y) c) => new(minX + (c.x + 0.5f) * CellSize, minY + (c.y + 0.5f) * CellSize, start.z);

            (int, int) sc = NearestFree(Cell(start), blocked, w, h);
            (int, int) gc = NearestFree(Cell(goal), blocked, w, h);
            if (sc.Item1 < 0 || gc.Item1 < 0)
            {
                return null;
            }

            // Plain A*, 8-directional, no corner cutting through blocked orthogonals.
            var open = new SortedSet<(float f, int x, int y)>();
            var g = new Dictionary<(int, int), float> { [sc] = 0f };
            var from = new Dictionary<(int, int), (int, int)>();
            float Heu((int x, int y) c) => Vector2.Distance(new Vector2(c.x, c.y), new Vector2(gc.Item1, gc.Item2));
            open.Add((Heu(sc), sc.Item1, sc.Item2));
            int[] dx = [1, -1, 0, 0, 1, 1, -1, -1];
            int[] dy = [0, 0, 1, -1, 1, -1, 1, -1];
            bool found = false;
            int guard = w * h * 8;
            while (open.Count > 0 && guard-- > 0)
            {
                (float f, int x, int y) = open.Min;
                open.Remove(open.Min);
                (int, int) cur = (x, y);
                if (cur == gc)
                {
                    found = true;
                    break;
                }
                for (int i = 0; i < 8; i++)
                {
                    int nx = x + dx[i], ny = y + dy[i];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || blocked[nx, ny])
                    {
                        continue;
                    }
                    if (i >= 4 && (blocked[x, ny] || blocked[nx, y]))
                    {
                        continue;   // don't cut a blocked corner diagonally
                    }
                    float ng = g[cur] + (i >= 4 ? 1.41421f : 1f);
                    (int, int) nc = (nx, ny);
                    if (!g.TryGetValue(nc, out float old) || ng < old)
                    {
                        g[nc] = ng;
                        from[nc] = cur;
                        open.Add((ng + Heu(nc), nx, ny));
                    }
                }
            }
            if (!found)
            {
                return null;
            }

            // Rebuild, convert to local points, append the true goal, then
            // string-pull: keep only the corners a raycast says we need.
            List<Vector3> raw = [];
            (int, int) walk = gc;
            while (walk != sc)
            {
                raw.Add(Center(walk));
                walk = from[walk];
            }
            raw.Reverse();
            raw.Add(goal);

            List<Vector3> pulled = [];
            Vector3 anchor = start;
            int k = 0;
            while (k < raw.Count)
            {
                int next = k;
                for (int j = raw.Count - 1; j > k; j--)
                {
                    if (LineClear(anchor, raw[j]))
                    {
                        next = j;
                        break;
                    }
                }
                pulled.Add(raw[next]);
                anchor = raw[next];
                k = next + 1;
            }
            return pulled;
        }

        private static (int, int) NearestFree((int x, int y) c, bool[,] blocked, int w, int h)
        {
            if (!blocked[c.x, c.y])
            {
                return c;
            }
            for (int r = 1; r <= 4; r++)
            {
                for (int gx = Math.Max(0, c.x - r); gx <= Math.Min(w - 1, c.x + r); gx++)
                {
                    for (int gy = Math.Max(0, c.y - r); gy <= Math.Min(h - 1, c.y + r); gy++)
                    {
                        if (!blocked[gx, gy])
                        {
                            return (gx, gy);
                        }
                    }
                }
            }
            return (-1, -1);
        }

        // ---- game glue ----

        private static Vector3 PlayerLocal()
        {
            try { return Entity.mainPlayer?.getGameObject()?.transform.localPosition ?? Vector3.zero; }
            catch { return Vector3.zero; }
        }

        private static Transform PlayerParent()
        {
            try { return Entity.mainPlayer?.getGameObject()?.transform.parent; }
            catch { return null; }
        }

        /// <summary>Public line-of-sight probe for callers deciding whether a
        /// straight engage will work (e.g. hunt approach vs pathing).</summary>
        public static bool LineClearLocal(Vector3 fromLocal, Vector3 toLocal)
        {
            return LineClear(fromLocal, toLocal);
        }

        /// <summary>Blocker-layer line-of-sight in LOCAL space (converted to world for physics).</summary>
        private static bool LineClear(Vector3 fromLocal, Vector3 toLocal)
        {
            try
            {
                Transform parent = PlayerParent();
                if (parent == null)
                {
                    return true;
                }
                Vector3 a = parent.TransformPoint(fromLocal);
                Vector3 b = parent.TransformPoint(toLocal);
                Vector2 d = new(b.x - a.x, b.y - a.y);
                return Physics2D.Raycast(a, d.normalized, d.magnitude, BlockerMask).collider == null;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>The game's own click-walk: EntityMovementUpdater.walkTo(localPos, cellSpeed).</summary>
        private void WalkTo(Vector3 local)
        {
            try
            {
                GameObject go = Entity.mainPlayer?.getGameObject();
                EntityMovementUpdater emu = go?.GetComponent<EntityMovementUpdater>();
                if (emu != null)
                {
                    emu.walkTo(local, EntityMovementUpdater.cellSpeed);
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"  [path] walkTo failed: {ex.Message}");
            }
        }
    }
}
