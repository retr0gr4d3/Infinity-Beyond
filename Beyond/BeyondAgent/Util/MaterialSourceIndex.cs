using System;
using System.Collections.Generic;
using System.Linq;

namespace BeyondAgent.Util
{
    public sealed class MaterialDrop
    {
        public int ItemId; public string Name = ""; public double Rate; public int Quantity;
    }
    public sealed class MaterialMonster
    {
        public int Id; public string Name = "", Area = "", Frame = ""; public bool Alive;
    }
    public sealed class MaterialDropRecord
    {
        public string Server = ""; public int MonsterId; public DateTime ReceivedUtc; public List<MaterialDrop> Drops = [];
    }
    // Drops are server-specific; placements always come from the current map.
    public sealed class MaterialSourceIndex
    {
        public List<MaterialDropRecord> Records = [];
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
        public void Record(string server, int monsterId, IEnumerable<MaterialDrop> drops, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(server) || monsterId <= 0) return;
            Records ??= [];
            Records.RemoveAll(r => r == null || r.Drops == null || r.Server == server && r.MonsterId == monsterId);
            Records.Add(new MaterialDropRecord { Server = server, MonsterId = monsterId, ReceivedUtc = now, Drops = drops.ToList() });
            Records.RemoveAll(r => now - r.ReceivedUtc > Lifetime || r.ReceivedUtc > now);
        }
        public MaterialDropRecord Get(string server, int monsterId, DateTime now) => (Records ?? []).FirstOrDefault(r => r != null && r.Drops != null && r.Server == server && r.MonsterId == monsterId && now >= r.ReceivedUtc && now - r.ReceivedUtc <= Lifetime);
        public MaterialMonster Choose(string server, int itemId, IEnumerable<MaterialMonster> monsters, string currentFrame, DateTime now)
        {
            return monsters.Select(m => new { Monster = m, Drop = Get(server, m.Id, now)?.Drops.FirstOrDefault(d => d.ItemId == itemId && d.Rate > 0 && d.Quantity > 0) })
                .Where(x => x.Drop != null).OrderByDescending(x => x.Monster.Alive)
                .ThenByDescending(x => string.Equals(x.Monster.Frame, currentFrame, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.Drop.Rate * x.Drop.Quantity).ThenBy(x => x.Monster.Id).Select(x => x.Monster).FirstOrDefault();
        }
    }
    public sealed class MaterialDiscoverySession
    {
        private readonly MaterialSourceIndex _index;
        public readonly string Server, Area;
        public readonly List<MaterialMonster> Monsters;
        private readonly Queue<int> _queue;
        private int _pending; private double _sentAt, _nextAt, _started;
        public int Completed { get; private set; }
        public int Total { get; }
        public bool Finished { get; private set; }
        public MaterialDiscoverySession(MaterialSourceIndex index, string server, string area, List<MaterialMonster> monsters, double now, DateTime utc)
        {
            _index = index; Server = server; Area = area; Monsters = monsters; _started = now;
            var ids = monsters.Select(m => m.Id).Distinct().Take(64).ToList(); Total = ids.Count;
            _queue = new Queue<int>(ids.Where(id => index.Get(server, id, utc) == null)); Completed = Total - _queue.Count;
        }
        public int Next(double now, DateTime utc)
        {
            if (Finished) return 0;
            if (now - _started >= 90) { Finished = true; return 0; }
            if (_pending != 0)
            {
                if (_index.Get(Server, _pending, utc) != null || now - _sentAt >= 5) { Completed++; _pending = 0; } else return 0;
            }
            if (_queue.Count == 0) { Finished = true; return 0; }
            if (now < _nextAt) return 0;
            _pending = _queue.Dequeue(); _sentAt = now; _nextAt = now + 1; return _pending;
        }
    }
}
