using BeyondAgent.Patches;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BeyondAgent.Util
{
    /// <summary>
    /// <para>
    /// End-to-end quest automation. Drives one quest ID repeatedly through
    /// accept → hunt (kill/collect via target+autoskills) → turn-in, halting
    /// on any mismatch and surfacing the reason.
    /// </para>
    /// <para>
    /// Reads all progress from live in-process state — no packet replay:
    ///   - Quest defs:        Quest.Get(qid).Turnins[]
    ///   - Current progress:  Entity.mainPlayer.Quests.IsObjectiveComplete(qoid)
    ///   - Target setting:    Entity.mainPlayer.target = monster
    ///   - Combat:            piggybacks on TestMod.autoskillsActive
    ///   - Mob enumeration:   Area.currentArea.GetMonstersInFrame()
    ///   - Requests:          RequestQuestAccept / RequestTryQuestComplete
    /// </para>
    /// <para>
    /// Designed to be ticked from BeyondAgentClass.OnUpdate so it lives on the main
    /// Unity thread (no marshalling needed for any of the game-side calls).
    /// </para>
    /// </summary>
    public class QuestRunner
    {
        public enum RunState
        {
            Idle,
            Accepting,
            AwaitingAccepted,
            Traveling,
            Hunting,
            TurningIn,
            AwaitingComplete,
            Cooldown,
            Respawning,
            LoadingShop,
            DiscoveringSources,
            Buying,
            Done,
            Failed
        }

        // --- config ---
        public int QuestID { get; private set; }
        public int Iterations { get; private set; }
        // Travel target. Sequence inside Traveling state:
        //   1. If TargetArea is non-empty AND currentArea.Name != TargetArea:
        //      send tfer(name, area, "0", frame, pad) and wait for arrival.
        //      tfer drops us at the right frame+pad so step 2 is usually a no-op.
        //   2. If TargetFrame is non-empty AND mainPlayer.Frame != TargetFrame:
        //      send moveToCell(frame, pad) and wait for the frame change.
        // Empty TargetArea = stay in current area. Empty TargetFrame = stay
        // in current frame.
        public string TargetArea { get; private set; } = "";
        public string TargetFrame { get; private set; } = "";
        public string TargetPad { get; private set; } = "Spawn";
        // Per-entry monster filter (names and/or numeric catalog ids). When set,
        // hunting only engages matching hostiles — this is how a chain authored
        // for live AQW (no RefIDs client-side) still kills the right mob.
        public List<string> TargetMons { get; private set; } = [];

        // Chain mode: when ChainEntries is non-null, the runner walks it
        // sequentially. Each entry rebinds QuestID / TargetFrame / TargetPad
        // / Iterations and re-enters Accepting. ChainIndex tracks position;
        // ChainName is just for status display.
        public List<QuestChains.Entry> ChainEntries { get; private set; }
        public int ChainIndex { get; private set; }
        public string ChainName { get; private set; } = "";

        // Farm phase: once the chain's own entries are done (or were already
        // completed), the runner swaps ChainEntries for the chain's farm list
        // and loops it — FarmLoops passes, 0 = until stopped. Story entries
        // are skipped when already complete; farm entries never are, since
        // farm quests are repeatable by definition.
        public List<QuestChains.Entry> FarmEntries { get; private set; }
        public int FarmLoops { get; private set; }
        public int FarmPass { get; private set; }
        public bool IsFarming { get; private set; }
        // A farm session runs for hours, so one dropped packet or blocked path
        // shouldn't end it: retry the current farm entry this many times in a
        // row (reset by every successful turn-in) before giving up.
        public int MaxFarmRetries = 3;
        public float FarmRetryDelaySec = 5f;
        private int _farmFailures;

        // Per-state timeouts. Halt-on-mismatch is the QA value prop.
        public float AcceptTimeout = 5f;
        // One silent re-send before failing an accept — the server drops
        // acceptQuest during brief post-join windows, and a retry is cheaper
        // than aborting a whole chain over one swallowed packet.
        public int MaxAcceptRetries = 1;
        // Cross-area tfer plays a join cutscene that runs ~15s before the
        // new area becomes joinable, plus actual load time. 25s leaves
        // headroom for slow loads. In-area moveToCell is instant, so the
        // 6s budget stays generous for stage 2.
        public float TferTimeout = 25f;
        // Post-tfer settle is signal-driven now: the join cutscene's
        // Start/End (RuntimeEvents.CutsceneActive) tells us exactly when the
        // server will take quest requests again. These tune the edges:
        // buffer after the cutscene ends, grace when no cutscene plays at all
        // (asset loads can delay its start), and a runaway cap in case an
        // End event is never observed.
        public float PostCutsceneBufferSec = 1.5f;
        public float NoCutsceneGraceSec = 5f;
        public float CutsceneWatchdogSec = 30f;
        // Arrival is "the character stopped moving": position stable for this
        // long = walk animation done, capped so an obstruction can't hold us.
        public float MoveStableSec = 0.5f;
        public float PostHopSettleCapSec = 4f;
        // In-cell walk budget. Now that we walk-onto-pad instead of teleporting
        // via moveToCell, this needs to cover crossing the whole cell on foot.
        public float TravelTimeout = 15f;
        // Hunt liveness: fail after this long with NO KILLS AND no objective
        // progress. Kills reset it — a 10%-drop objective legitimately takes a
        // dozen kills, and that must not read as "stuck".
        public float HuntTimeoutNoProgress = 90f;
        // ...but if we ARE killing and the objective still hasn't moved after
        // this long, the kills aren't credited (wrong mob / wrong zone) — the
        // one case blind persistence can't fix.
        public float HuntHardCapSec = 600f;
        // Engage envelope: a target farther than this (or with a Blocker wall
        // in the way) gets a pathed approach first — the game's own charge
        // walk is straight-line and gives up when blocked.
        public float MaxEngageDist = 9f;
        public float CompleteTimeout = 8f;
        // Brief delay between consecutive turn-ins / iterations so we don't
        // trip the server's per-action spam cooldown (observed: tryQuestComplete
        // back-to-back returns rNotify "Spam Detected").
        public float InterIterCooldown = 2.5f;

        // --- state ---
        public RunState State { get; private set; } = RunState.Idle;
        public int CurrentIteration { get; private set; }
        public int Deaths { get; private set; }
        public string LastError { get; private set; } = "";
        public string StatusLine { get; private set; } = "idle";
        // How long a death may take to resolve (the respawn UI runs a ~10s
        // timer, then the revive lands) before we give up on the run.
        public float RespawnTimeout = 30f;

        private float _stateEnteredAt;
        private float _lastProgressAt;
        private int _lastProgressSum;
        // Save autoskills state so we don't fight the user's manual toggle.
        private bool _autoskillsWasOn;

        // Logs are surfaced via this callback to the GUI's event list.
        public Action<string> OnLog;

        // Fired when the run starts/stops or moves to another chain/farm entry,
        // so the launcher can show progress and flip its Start/Stop controls.
        public Action OnProgressChanged;

        // --- public API ---

        public bool IsRunning =>
            State is not RunState.Idle and not RunState.Done and not RunState.Failed;

        // Paused runs keep their place (chain index, farm pass) but stop ticking
        // and fighting; Resume re-checks travel from the current entry.
        public bool IsPaused { get; private set; }
        private RunState _pausedState;
        private RunState _beforeDeath;

        public void Pause()
        {
            if (!IsRunning || IsPaused)
            {
                return;
            }

            _pausedState = State;
            IsPaused = true;
            _path?.Cancel();
            StopAutoskills();
            StatusLine = "paused";
            Log("[pause] paused by user");
            OnProgressChanged?.Invoke();
        }

        public void Resume()
        {
            if (!IsRunning || !IsPaused)
            {
                return;
            }

            IsPaused = false;
            Log("[resume] resuming");
            if (_pausedState is RunState.LoadingShop or RunState.Buying or RunState.DiscoveringSources)
            {
                if (_pausedState == RunState.DiscoveringSources) _discovery = MonsterFarmingCatalog.BeginDiscovery();
                EnterState(_pausedState);
            }
            else
            {
                EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
            }
            OnProgressChanged?.Invoke();
        }

        public void Start(int questId, int iterations, string targetArea = "", string targetFrame = "", string targetPad = "Spawn", List<string> targetMons = null)
        {
            if (IsRunning)
            {
                return;
            }

            ChainEntries = null;
            ChainName = "";
            ChainIndex = 0;
            ResetFarm(null, 0);
            Buy = null;
            _shop = null;
            _needs.Clear();
            IsPaused = false;
            Deaths = 0;
            BindEntry(questId, iterations, targetArea, targetFrame, targetPad, targetMons);
            _autoskillsWasOn = BeyondAgentClass.autoskillsActive;
            string travelNote =
                !string.IsNullOrEmpty(TargetArea) ? $", tfer to {TargetArea}/{TargetFrame}/{TargetPad}" :
                !string.IsNullOrEmpty(TargetFrame) ? $", hop to {TargetFrame}/{TargetPad}" : "";
            Log($"[start] quest {questId} × {iterations}{travelNote}");

            // Travel first when a target is set — some quests are area-gated
            // server-side and reject acceptQuest from the wrong zone.
            EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
        }

        // A script run: `entries` once in order (story, skipping completed
        // quests), then `farmEntries` looped (farmLoops passes, 0 = until
        // stopped or every step's item goal is met), then `buy` — purchases
        // from a shop. Any part may be empty, but not all three.
        public void StartChain(string chainName, List<QuestChains.Entry> entries,
            List<QuestChains.Entry> farmEntries = null, int farmLoops = 0,
            QuestChains.BuyPlan buy = null)
        {
            if (IsRunning)
            {
                return;
            }

            bool hasStory = entries is { Count: > 0 };
            if (!hasStory && farmEntries is not { Count: > 0 } && buy == null)
            {
                Fail("script is empty");
                return;
            }
            if (buy == null && (entries ?? []).Concat(farmEntries ?? []).Any(e => e.HasGoal && e.goalQty < 0))
            {
                Fail("an inventory goal without a quantity requires a merge-shop buy plan");
                return;
            }
            ChainName = chainName ?? "";
            ChainIndex = 0;
            ResetFarm(farmEntries, farmLoops);
            _shoppingAfterFarm = false;
            Buy = buy;
            IsPaused = false;
            _shop = null;
            _needs.Clear();
            Deaths = 0;
            _autoskillsWasOn = BeyondAgentClass.autoskillsActive;
            RefreshRunnerKeep();

            string plan = (hasStory ? $"{entries.Count} story step(s)" : "no story")
                + (FarmEntries != null ? $", then farm {FarmEntries.Count} step(s) {FarmLoopsText()}" : "")
                + (Buy != null ? $", then buy {Buy.items.Count} item(s) from shop {Buy.shop}" : "");
            Log($"[start] '{chainName}': {plan}");
            ChatUtils.SendChatAndLog("Starting activity", name: "System", channel: "Admin");

            if (hasStory)
            {
                ChainEntries = entries;
                QuestChains.Entry first = ChainEntries[0];
                BindEntry(first.qid, first.items, first.area, first.frame, first.pad, first.mons);
                Log($"[chain] 1/{ChainEntries.Count}: {first}");
                EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
                return;
            }
            ChainEntries = [];
            BeginFarmOrFinish();
        }

        // --- farm goals & shop ---

        public QuestChains.BuyPlan Buy { get; private set; }
        public float ProgressPushSec = 3f;
        private float _lastProgressPush;
        private shopData _shop;
        private MaterialDiscoverySession _discovery;
        private bool _sourcesPlanned;
        // Material -> total count needed for the buy list (by name and by id),
        // from the shop's prices. Goals without an explicit qty farm to this.
        private readonly Dictionary<string, int> _needs = new(StringComparer.OrdinalIgnoreCase);
        private bool _shoppingAfterFarm;
        private bool _shopRequested;
        private float _shopRequestedAt;
        private bool _shopTferSent;
        public float ShopLoadTimeout = 12f;
        private int _buyIndex;
        private bool _buyWaiting;
        private float _buySentAt;
        // Owned count when the last purchase was sent; a confirmed buy that
        // doesn't raise it stops the loop instead of buying again.
        private int _buyOwnedBefore = -1;
        private float _nextBuyAt;
        public float BuyResponseTimeout = 8f;

        public QuestChains.Entry CurrentStep =>
            ChainEntries != null && ChainIndex >= 0 && ChainIndex < ChainEntries.Count ? ChainEntries[ChainIndex] : null;

        // Target count for a step's goal item, or -1 when it has no reachable target.
        private int GoalTarget(QuestChains.Entry step)
        {
            if (step is not { HasGoal: true })
            {
                return -1;
            }
            if (step.goalQty >= 0)
            {
                return step.goalQty;
            }
            // "until enough": what the buy list needs (0 if it needs none of it).
            return _needs.TryGetValue(step.goalItem, out int n) ? n : (Buy != null ? 0 : -1);
        }

        private bool GoalMet(QuestChains.Entry step)
        {
            int target = GoalTarget(step);
            return target >= 0 && ItemGoals.Count(step.goalItem) >= target;
        }

        private string GoalText(QuestChains.Entry step)
        {
            int target = GoalTarget(step);
            return $"{step.goalItem} {ItemGoals.Count(step.goalItem)}/{(target >= 0 ? target.ToString() : "∞")}";
        }

        // One-line goal progress for the launcher ("Mogloween Candy Corn 340/1500").
        public string GoalProgress => CurrentStep is { HasGoal: true } s ? GoalText(s) : "";

        public string Phase => State switch
        {
            RunState.LoadingShop => "shop",
            RunState.DiscoveringSources => "discovery",
            RunState.Buying => "buy",
            _ => IsFarming ? "farm" : "story"
        };

        // Goal items + buy materials are auto-looted while the script runs.
        private void RefreshRunnerKeep()
        {
            HashSet<string> keep = new(StringComparer.OrdinalIgnoreCase);
            foreach (QuestChains.Entry e in FarmEntries ?? [])
            {
                if (e.HasGoal)
                {
                    keep.Add(e.goalItem);
                }
            }
            foreach (string k in _needs.Keys)
            {
                keep.Add(k);
            }
            DropFilterEngine.SetRunnerKeep(keep);
        }

        // After the story: load the shop (if buying, so "enough" goals are
        // known), then farm, then buy / finish.
        private void BeginFarmOrFinish()
        {
            if (Buy != null && _shop == null)
            {
                _shopRequested = false;
                _shopTferSent = false;
                EnterState(RunState.LoadingShop);
                return;
            }
            if (FarmEntries != null)
            {
                if (!IsFarming)
                {
                    IsFarming = true;
                    ChainEntries = FarmEntries;
                    FarmPass = 1;
                }
                if (StartFarmStep(0))
                {
                    return;
                }
                Log("[farm] every goal is already met");
            }
            FinishOrBuy();
        }

        // Start the first farm step at or after `from` whose goal isn't met.
        private bool StartFarmStep(int from)
        {
            for (int i = from; i < FarmEntries.Count; i++)
            {
                QuestChains.Entry e = FarmEntries[i];
                if (GoalMet(e))
                {
                    continue;
                }
                ChainIndex = i;
                BindEntry(e.qid, e.items, e.area, e.frame, e.pad, e.mons);
                Log($"[farm] pass {FarmPass}{(FarmLoops > 0 ? $"/{FarmLoops}" : "")}: {i + 1}/{FarmEntries.Count}: {e}"
                    + (e.HasGoal ? $" ({GoalText(e)})" : ""));
                EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
                OnProgressChanged?.Invoke();
                return true;
            }
            return false;
        }

        private bool AllFarmGoalsMet()
        {
            return FarmEntries != null && FarmEntries.All(e => e.HasGoal && GoalMet(e));
        }

        private void FinishOrBuy()
        {
            if (Buy != null)
            {
                if (!string.IsNullOrEmpty(Buy.area) && !AreaMatches(Area.currentArea?.Name ?? "", Buy.area))
                {
                    _shoppingAfterFarm = true;
                    _shopRequested = false;
                    _shopTferSent = false;
                    EnterState(RunState.LoadingShop);
                    return;
                }
                _buyIndex = 0;
                _buyWaiting = false;
                _buyOwnedBefore = -1;
                _nextBuyAt = 0f;
                EnterState(RunState.Buying);
                OnProgressChanged?.Invoke();
                return;
            }
            Log(IsFarming ? $"[done] farmed '{ChainName}' ({FarmPass} pass(es))" : $"[done] '{ChainName}' complete");
            EnterState(RunState.Done);
        }

        private void TickLoadingShop()
        {
            // Some shops only load from their own map — go there first.
            string hereArea = Area.currentArea?.Name ?? "";
            if (!string.IsNullOrEmpty(Buy.area) && !AreaMatches(hereArea, Buy.area))
            {
                if (!_shopTferSent)
                {
                    _shopTferSent = true;
                    _stateEnteredAt = Time.time;
                    try
                    {
                        AEC.Instance.sendRequest(new Request("tfer", [Entity.mainPlayer?.Name ?? "", Buy.area, "0", "Enter", "Spawn"]));
                        Log($"  [shop] tfer to {Buy.area}");
                    }
                    catch { }
                }
                if (StateAge() > TferTimeout)
                {
                    Fail($"couldn't reach '{Buy.area}' to open shop {Buy.shop}");
                }
                StatusLine = $"traveling to {Buy.area} for shop {Buy.shop}";
                return;
            }

            if (!_shopRequested)
            {
                _shopRequested = true;
                _shopRequestedAt = Time.time;
                _stateEnteredAt = Time.time;
                try
                {
                    AEC.Instance.sendRequest(new RequestLoadShop(Buy.shop));
                    Log($"  [shop] loading shop {Buy.shop}");
                }
                catch (Exception ex)
                {
                    Fail($"loadShop send failed: {ex.Message}");
                    return;
                }
            }

            shopData loaded = RuntimeEvents.LastShop;
            if (RuntimeEvents.LastShopTime >= _shopRequestedAt && loaded != null && loaded.shopID == Buy.shop)
            {
                _shop = loaded;
                _needs.Clear();
                foreach (KeyValuePair<string, int> kv in ItemGoals.Requirements(_shop, Buy))
                {
                    _needs[kv.Key] = kv.Value;
                }
                foreach ((string item, int qty) in Buy.items)
                {
                    ShopItem si = ItemGoals.FindShopItem(_shop, item);
                    if (si == null)
                    {
                        Fail($"shop {Buy.shop} no longer sells {item}; reload the reward list");
                        return;
                    }
                    Log($"  [shop] {item} ×{qty}: costs {string.Join(", ", ItemGoals.Costs(si).Select(c => $"{c.qty} {c.name}"))}");
                }
                List<string> needText = [.. _needs.Where(kv => !int.TryParse(kv.Key, out _)).Select(kv => $"{kv.Key} {ItemGoals.Count(kv.Key)}/{kv.Value}")];
                Log($"  [shop] '{_shop.Name}' loaded — need {(needText.Count > 0 ? string.Join(", ", needText) : "nothing more")}");
                RefreshRunnerKeep();
                if (_shoppingAfterFarm)
                {
                    FinishOrBuy();
                }
                else if (!_sourcesPlanned && Buy.autoDiscover && RequiredMaterials().Any(c => ItemGoals.Count(c.id.ToString()) < c.qty))
                {
                    _discovery = MonsterFarmingCatalog.BeginDiscovery();
                    Log($"[sources] checking live drop lists for {_discovery.Total} monster types on {Area.currentArea?.Name}");
                    EnterState(RunState.DiscoveringSources);
                }
                else
                {
                    if (PlanMaterialRoutes(false)) BeginFarmOrFinish();
                }
                return;
            }

            if (StateAge() > ShopLoadTimeout)
            {
                Fail($"shop {Buy.shop} didn't load within {ShopLoadTimeout:0}s — wrong shop id, or it only opens from its map/NPC");
                return;
            }
            StatusLine = $"loading shop {Buy.shop}… ({StateAge():0.0}s)";
        }

        private List<(int id, string name, int qty)> RequiredMaterials()
        {
            return Buy.items.Select(i => ItemGoals.FindShopItem(_shop, i.item)).Where(si => si != null)
                .SelectMany(si => ItemGoals.Costs(si)).GroupBy(c => c.id)
                .Select(g => (g.Key, g.First().name, _needs.TryGetValue(g.Key.ToString(), out int n) ? n : 0)).ToList();
        }
        private void TickDiscoveringSources()
        {
            if (!AreaMatches(Area.currentArea?.Name ?? "", _discovery.Area))
            {
                Fail("map changed during source discovery; restart this activity on the map to search"); return;
            }
            int request = _discovery.Next(Time.time, DateTime.UtcNow);
            if (request > 0)
            {
                try { MonsterFarmingCatalog.RequestDrops(request); }
                catch (Exception ex) { Log($"[sources] drop request {request} failed: {ex.Message}"); }
            }
            StatusLine = $"Discovering material sources: {_discovery.Completed}/{_discovery.Total} monster types";
            if (_discovery.Finished && PlanMaterialRoutes(true)) BeginFarmOrFinish();
        }
        private bool PlanMaterialRoutes(bool useLive)
        {
            var planned = new List<QuestChains.Entry>();
            foreach (var material in RequiredMaterials())
            {
                string id = material.id.ToString();
                if (ItemGoals.Count(id) >= material.qty) continue;
                var source = useLive ? MonsterFarmingCatalog.FindSource(material.id) : null;
                if (source != null)
                {
                    planned.Add(new QuestChains.Entry { isHunt = true, mons = new List<string> { source.Id.ToString() }, area = source.Area, frame = source.Frame, goalItem = id, goalQty = -1 });
                    Log($"[sources] {material.name}: {source.Name} ({source.Id}) in {source.Area}/{source.Frame}, confirmed by server drop list");
                    continue;
                }
                var authored = FarmEntries?.FirstOrDefault(e => e.HasGoal && (e.goalItem == id || string.Equals(e.goalItem, material.name?.Trim(), StringComparison.OrdinalIgnoreCase)));
                if (authored != null) { planned.Add(new QuestChains.Entry { qid = authored.qid, isHunt = authored.isHunt, items = authored.items, mons = authored.mons, area = authored.area, frame = authored.frame, pad = authored.pad, goalItem = id, goalQty = -1 }); Log($"[sources] {material.name}: using script route; no live source confirmed on this map"); continue; }
                Fail($"no confirmed source for {material.name} on this map and no script route. Visit a source map or add a material route; no farming started."); return false;
            }
            FarmEntries = planned.Count > 0 ? planned : null;
            _sourcesPlanned = true;
            RefreshRunnerKeep();
            return true;
        }

        private void TickBuying()
        {
            if (_buyWaiting)
            {
                if (RuntimeEvents.LastBuyTime > _buySentAt)
                {
                    _buyWaiting = false;
                    _nextBuyAt = Time.time + InterIterCooldown;
                    if (!RuntimeEvents.LastBuySuccess)
                    {
                        Fail($"purchase refused: {RuntimeEvents.LastBuyMessage}");
                        return;
                    }
                    else
                    {
                        Log("  [buy] bought");
                    }
                }
                else if (Time.time - _buySentAt > BuyResponseTimeout)
                {
                    Fail("purchase timed out; check inventory before restarting");
                    return;
                }
                StatusLine = "waiting for purchase…";
                return;
            }
            if (Time.time < _nextBuyAt)
            {
                StatusLine = $"buy cooldown {_nextBuyAt - Time.time:0.0}s";
                return;
            }

            for (; _buyIndex < Buy.items.Count; _buyIndex++)
            {
                (string item, int qty) = Buy.items[_buyIndex];
                ShopItem si = ItemGoals.FindShopItem(_shop, item);
                if (si == null)
                {
                    Fail($"shop {Buy.shop} does not sell {item}");
                    return;
                }
                int owned = ItemGoals.Count(si.ID.ToString());
                if (owned >= qty)
                {
                    _buyOwnedBefore = -1;
                    continue;
                }
                if (_buyOwnedBefore >= 0 && owned <= _buyOwnedBefore)
                {
                    Fail($"bought {si.Name} but the inventory still shows {owned}; stopped so nothing is bought twice");
                    return;
                }
                if (!ItemGoals.CanAfford(si, out string missing))
                {
                    Fail($"cannot buy {si.Name} — missing {missing}");
                    return;
                }
                try
                {
                    AEC.Instance.sendRequest(new RequestBuyItem(si.ID, _shop.shopID, si.ShopItemID));
                    Log($"  [buy] buying {si.Name} ({owned + 1}/{qty})");
                    _buyWaiting = true;
                    _buySentAt = Time.time;
                    _buyOwnedBefore = owned;
                }
                catch (Exception ex)
                {
                    Fail($"purchase send failed: {ex.Message}");
                    return;
                }
                return;   // one purchase at a time; same item is re-checked next
            }

            Log($"[done] '{ChainName}' finished — shopping done");
            EnterState(RunState.Done);
        }

        private void ResetFarm(List<QuestChains.Entry> farmEntries, int farmLoops)
        {
            FarmEntries = farmEntries is { Count: > 0 } ? farmEntries : null;
            FarmLoops = Math.Max(0, farmLoops);
            FarmPass = 0;
            IsFarming = false;
            _farmFailures = 0;
            _sourcesPlanned = false;
            _discovery = null;
        }

        private string FarmLoopsText()
        {
            return FarmLoops > 0 ? $"× {FarmLoops}" : "until stopped";
        }

        private void BindEntry(int qid, int items, string area, string frame, string pad, List<string> mons = null)
        {
            QuestID = qid;
            Iterations = Math.Max(1, items);
            TargetArea = area ?? "";
            TargetFrame = frame ?? "";
            TargetPad = string.IsNullOrEmpty(pad) ? "Spawn" : pad;
            TargetMons = mons ?? [];
            CurrentIteration = 0;
            _huntObservedFrame = "";
            _acceptRetries = 0;
            LastError = "";
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            Log("[stop] user requested");
            IsPaused = false;
            StopAutoskills();
            EnterState(RunState.Idle);
        }

        // --- tick ---

        public void Tick()
        {
            if (!IsRunning || IsPaused)
            {
                return;
            }

            // Live goal counts for the launcher ("Candy Corn 340/1500").
            if (Time.time - _lastProgressPush > ProgressPushSec)
            {
                _lastProgressPush = Time.time;
                OnProgressChanged?.Invoke();
            }

            try
            {
                TrackMovement();
                if (IsMapLoading())
                {
                    return;
                }

                // Global rate-limit backoff. The server answers too-fast
                // requests (accept / complete / interact) with an rNotify
                // "Spam Detected: wait before attempting this action again".
                // Hammering through it used to FAIL the whole chain; instead,
                // when we see it, pause ALL actions for a few seconds and let
                // it clear — nothing is lost, we just slow down.
                if (RuntimeEvents.LastNotifyTime > _lastSpamSeen)
                {
                    string nlow = (RuntimeEvents.LastNotifyMsg ?? "").ToLowerInvariant();
                    if (nlow.Contains("spam") || nlow.Contains("wait before"))
                    {
                        _lastSpamSeen = RuntimeEvents.LastNotifyTime;
                        _spamBackoffUntil = Time.time + SpamBackoffSec;
                        Log($"  [rate-limit] server asked us to slow down — backing off {SpamBackoffSec:0}s");
                    }
                }
                if (Time.time < _spamBackoffUntil)
                {
                    StatusLine = $"rate-limited — waiting {_spamBackoffUntil - Time.time:0.0}s";
                    return;
                }

                // Death preempts everything: stop fighting, ride out the
                // respawn timer, then RE-TRAVEL — the revive drops us at the
                // map's spawn frame, which is usually full of the WRONG mobs
                // (die to wyverns in R2, wake up among the draconians at
                // Enter). Without this the hunt just engaged whatever was
                // standing at the respawn point.
                if (State != RunState.Respawning && PlayerIsDead())
                {
                    _beforeDeath = State;
                    Deaths++;
                    Log($"[death] died during {State} (death #{Deaths}) — waiting for respawn");
                    StopAutoskills();
                    EnterState(RunState.Respawning);
                    return;
                }

                switch (State)
                {
                    case RunState.Accepting: TickAccept(); break;
                    case RunState.AwaitingAccepted: TickAwaitAccepted(); break;
                    case RunState.Traveling: TickTravel(); break;
                    case RunState.Hunting: TickHunt(); break;
                    case RunState.TurningIn: TickTurnIn(); break;
                    case RunState.AwaitingComplete: TickAwaitComplete(); break;
                    case RunState.Cooldown: TickCooldown(); break;
                    case RunState.Respawning: TickRespawning(); break;
                    case RunState.LoadingShop: TickLoadingShop(); break;
                    case RunState.DiscoveringSources: TickDiscoveringSources(); break;
                    case RunState.Buying: TickBuying(); break;
                }
            }
            catch (Exception ex)
            {
                Fail($"unhandled exception in state {State}: {ex.Message}");
            }
        }

        // --- state handlers ---

        private void TickAccept()
        {
            if (Entity.mainPlayer == null)
            {
                StatusLine = "waiting for mainPlayer";
                return;
            }

            if (SkipIfChainQuestDone())
            {
                return;
            }

            // Hunt steps have no quest to accept — straight to killing.
            if (CurrentStep is { isHunt: true })
            {
                CurrentIteration = 1;
                Iterations = 1;
                EnterState(RunState.Hunting);
                return;
            }

            // Skip the send if we're already on this quest — server treats it
            // as a no-op anyway but avoids a wasted packet. Still go through
            // the travel check; previously we jumped straight to Hunting and
            // would hunt in whatever area we happened to be in.
            if (IsQuestAccepted(QuestID))
            {
                CurrentIteration++;
                Log($"  iter {CurrentIteration}/{Iterations}: already accepted, skipping send");
                EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Hunting);
                return;
            }

            try
            {
                Log($"  [accepting] sending request to accept quest {QuestID}...");
                AEC.Instance.sendRequest(new RequestQuestAccept(QuestID));
            }
            catch (Exception ex)
            {
                Fail($"acceptQuest send failed: {ex.Message}");
                return;
            }
            EnterState(RunState.AwaitingAccepted);
        }

        private void TickAwaitAccepted()
        {
            if (IsQuestAccepted(QuestID))
            {
                CurrentIteration++;
                _acceptRetries = 0;
                Log($"  iter {CurrentIteration}/{Iterations}: accepted");
                // We already traveled in the pre-Accept phase, so go straight
                // to Hunting. (NeedsCellHop should be false here; if it's not,
                // something weird happened mid-state — Hunting will fail fast
                // with "no hostile mob in frame" and surface it.)
                EnterState(RunState.Hunting);
                return;
            }
            if (StateAge() > AcceptTimeout)
            {
                // The server silently drops acceptQuest in a few windows (mid
                // join, spam guard) — re-send before concluding it's a real
                // prereq/level gate.
                if (_acceptRetries < MaxAcceptRetries)
                {
                    _acceptRetries++;
                    Log($"  accept unanswered after {AcceptTimeout:0.0}s — retrying ({_acceptRetries}/{MaxAcceptRetries})");
                    EnterState(RunState.Accepting);
                    return;
                }
                Fail($"quest {QuestID} not in accepted list after {MaxAcceptRetries + 1} attempts — likely a prereq or level gate");
            }
            else
            {
                StatusLine = $"waiting for accept… ({StateAge():0.0}s)";
            }
        }
        private int _acceptRetries;

        private void TickCooldown()
        {
            float remaining = InterIterCooldown - StateAge();
            if (remaining > 0f)
            {
                StatusLine = $"cooldown {remaining:0.0}s (avoid spam detect)";
                return;
            }
            // Decide what's next. Always re-check travel before re-accepting
            // — for chain advances the target may differ from current pos.
            // A farm quest with an item goal stops repeating once it's stocked.
            bool goalDone = IsFarming && CurrentStep is { HasGoal: true } step && GoalMet(step);
            if (goalDone)
            {
                Log($"  goal reached: {GoalText(CurrentStep)}");
            }
            if (CurrentIteration < Iterations && !goalDone)
            {
                EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
                return;
            }
            AdvanceChainOrFinish();
        }

        // Story entries the character already finished are skipped up front —
        // before any travel — so re-running a done chain goes straight to its
        // farm phase instead of touring every map on the way.
        private bool SkipIfChainQuestDone()
        {
            if (ChainEntries == null || IsFarming || QuestID <= 0
                || Entity.mainPlayer?.Quests == null
                || !Entity.mainPlayer.Quests.isQuestComplete(QuestID))
            {
                return false;
            }
            Log($"  quest {QuestID} is already completed, skipping");
            AdvanceChainOrFinish();
            return true;
        }

        private void AdvanceChainOrFinish()
        {
            // Single-quest runner (no chain): just done.
            if (ChainEntries == null)
            {
                Log("[done] all iterations complete");
                EnterState(RunState.Done);
                return;
            }

            if (!IsFarming)
            {
                if (ChainIndex + 1 < ChainEntries.Count)
                {
                    ChainIndex++;
                    QuestChains.Entry next = ChainEntries[ChainIndex];
                    BindEntry(next.qid, next.items, next.area, next.frame, next.pad, next.mons);
                    Log($"[chain] advancing to {ChainIndex + 1}/{ChainEntries.Count}: {next}");
                    EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
                    OnProgressChanged?.Invoke();
                    return;
                }
                if (FarmEntries != null || Buy != null)
                {
                    Log($"[chain] '{ChainName}' story complete");
                    BeginFarmOrFinish();
                    return;
                }
                FinishOrBuy();
                return;
            }

            // Farming: next unmet step this pass, else the next pass.
            if (StartFarmStep(ChainIndex + 1))
            {
                return;
            }
            if (AllFarmGoalsMet())
            {
                Log("[farm] all goals reached");
                FinishOrBuy();
                return;
            }
            if (FarmLoops == 0 || FarmPass < FarmLoops)
            {
                FarmPass++;
                if (StartFarmStep(0))
                {
                    return;
                }
            }
            FinishOrBuy();
        }

        private void TickTravel()
        {
            if (SkipIfChainQuestDone())
            {
                return;
            }

            string hereArea = Area.currentArea?.Name ?? "";
            string hereFrame = Entity.mainPlayer?.Frame ?? "";

            // ----- Stage 1: cross-area tfer -----
            // If TargetArea is set and we're not in a matching instance yet,
            // send tfer first. tfer drops us at the target frame+pad in one
            // shot, so once the area matches we usually also have the right
            // frame and stage 2 is a no-op.
            if (!string.IsNullOrEmpty(TargetArea) && !AreaMatches(hereArea, TargetArea))
            {
                if (!_tferSent)
                {
                    try
                    {
                        string name = Entity.mainPlayer?.Name ?? "";
                        // tfer params: [charname, area, instance("0" = any), frame, pad]
                        Request pkt = new("tfer",
                        [
                            name,
                            TargetArea,
                            "0",
                            TargetFrame ?? "",
                            TargetPad ?? "Spawn",
                        ]);
                        AEC.Instance.sendRequest(pkt);
                        _tferSent = true;
                        Log($"  tfer({TargetArea}, {TargetFrame}, {TargetPad})");
                    }
                    catch (Exception ex)
                    {
                        Fail($"tfer send failed: {ex.Message}");
                        return;
                    }
                }
                if (StateAge() > TferTimeout)
                {
                    Fail($"didn't reach area '{TargetArea}' within {TferTimeout:0.0}s (currently in '{hereArea}') — wrong area name, no room, or server rejected");
                    return;
                }
                StatusLine = $"tfer {hereArea} → {TargetArea}  ({StateAge():0.0}s)";
                return;
            }

            // ----- Stage 1b: cutscene settle -----
            // Only when we actually tfered THIS travel session — the join
            // cutscene plays on cross-area transitions, not when we were
            // already in the target area (e.g. chain advance r1 → r2 inside
            // lair). Area name flips on AreaJoin but the cutscene then runs
            // while acceptQuest gets dropped server-side. Instead of a fixed
            // sleep, watch the cutscene's actual Start/End (RuntimeEvents):
            //   - cutscene playing        → hold (watchdog-capped)
            //   - cutscene ended          → hold a short buffer, then go
            //   - no cutscene played      → hold a grace window (its start can
            //                               lag behind AreaJoin on slow loads)
            if (_tferSent)
            {
                if (_areaFirstMatchedAt < 0f)
                {
                    _areaFirstMatchedAt = Time.time;
                }

                float sinceArrive = Time.time - _areaFirstMatchedAt;
                bool cutsceneRunning = RuntimeEvents.CutsceneActive
                    && Time.time - RuntimeEvents.LastCutsceneStartTime < CutsceneWatchdogSec;
                if (cutsceneRunning)
                {
                    if (RuntimeEvents.LastCutsceneStartTime >= _areaFirstMatchedAt - 2f)
                    {
                        _sawJoinCutscene = true;
                    }
                    StatusLine = $"in {hereArea}, cutscene playing ({sinceArrive:0.0}s)";
                    return;
                }
                float readyAt = _sawJoinCutscene
                    ? RuntimeEvents.LastCutsceneEndTime + PostCutsceneBufferSec
                    : _areaFirstMatchedAt + NoCutsceneGraceSec;
                if (Time.time < readyAt)
                {
                    StatusLine = $"in {hereArea}, settling ({sinceArrive:0.0}s)";
                    return;
                }
            }

            // ----- Stage 2: in-area moveToCell -----
            // Give stage 2 its own fresh TravelTimeout budget, independent of
            // however long stages 1/1b took. Without this, a chain advance
            // that spent 14s settling would immediately fail stage 2 because
            // StateAge() already exceeds TravelTimeout.
            if (!_cellHopBudgetReset)
            {
                _stateEnteredAt = Time.time;
                _cellHopBudgetReset = true;
            }

            // Frame matches? Arrived once the character actually STOPS — the
            // client animates the walk from the old cell's exit pad to the
            // new cell's entry pad, and mobs stay "out of range" until it
            // finishes. Position-stable beats the old fixed 3s sleep: a
            // same-frame landing passes instantly, a long walk gets exactly
            // as long as it needs (capped so an obstruction can't hold us).
            if (string.IsNullOrEmpty(TargetFrame)
                || string.Equals(hereFrame, TargetFrame, StringComparison.OrdinalIgnoreCase))
            {
                if (_frameFirstMatchedAt < 0f)
                {
                    _frameFirstMatchedAt = Time.time;
                }

                float settled = Time.time - _frameFirstMatchedAt;
                bool stopped = Time.time - _lastMoveAt > MoveStableSec;
                if (!string.IsNullOrEmpty(TargetFrame) && !stopped && settled < PostHopSettleCapSec)
                {
                    StatusLine = $"in {hereArea}/{hereFrame}, finishing walk ({settled:0.0}s)";
                    return;
                }
                Log($"  arrived at {hereArea}/{hereFrame} ({settled:0.0}s settle)");
                EnterState(RunState.Accepting);
                return;
            }

            // Prefer walking onto the in-world Goto pad over a raw
            // moveToCell server request. The pad's OnTriggerEnter2D fires
            // GoToCell() naturally — and crucially leaves the character
            // physically next to the destination's entry pad rather than
            // teleporting them to its name-based coordinate (which earlier
            // produced "in r2/Spawn but visually still in Enter" desync).
            if (_pickedGotoPad == null)
            {
                _pickedGotoPad = FindGotoPad(TargetFrame);
                if (_pickedGotoPad != null)
                {
                    Log($"  walking to Goto pad → {TargetFrame} at world {_pickedGotoPad.transform.position}");
                }
            }
            if (_pickedGotoPad != null && !_traveSent)
            {
                // Wall-aware walk to the pad: PathWalker plans around Blocker
                // colliders (A* + string-pull) and drives the game's own
                // walkTo per waypoint — no more dragging along curved walls.
                if (!_walkIssued)
                {
                    GameObject playerGO = Entity.mainPlayer?.getGameObject();
                    if (playerGO?.transform.parent != null)
                    {
                        Vector3 padLocal = playerGO.transform.parent
                            .InverseTransformPoint(_pickedGotoPad.transform.position);
                        _path ??= new PathWalker(Log);
                        _path.Begin(padLocal);
                        _walkIssued = true;
                        _pathDoneAt = -1f;
                    }
                }
                else
                {
                    _path?.Tick();
                    if (_path != null && _path.Failed)
                    {
                        Log("  path walk failed — falling back to moveToCell");
                        SendMoveToCell();
                    }
                    else if (_path != null && _path.Done)
                    {
                        // Standing on the pad's position but the frame never
                        // flipped — trigger radius missed us. Give it a beat,
                        // then take the server-side hop.
                        if (_pathDoneAt < 0f)
                        {
                            _pathDoneAt = Time.time;
                        }
                        else if (Time.time - _pathDoneAt > 2f)
                        {
                            Log("  reached pad but frame didn't change — falling back to moveToCell");
                            SendMoveToCell();
                        }
                    }
                }
                StatusLine = $"walking to Goto pad → {TargetFrame}  ({_path?.Status ?? "starting"}, {StateAge():0.0}s)";
            }
            else if (!_traveSent)
            {
                // Fallback: no pad found in the current scene (different
                // layout, or we're already in the cell but mis-detected) →
                // do the old server-side moveToCell.
                Log($"  no Goto pad for '{TargetFrame}' — falling back to moveToCell({TargetFrame}, {TargetPad})");
                SendMoveToCell();
            }
            if (StateAge() > TravelTimeout)
            {
                Fail($"didn't reach frame '{TargetFrame}' within {TravelTimeout:0.0}s (currently in '{hereFrame}') — wrong frame name or movement blocked");
                return;
            }
            StatusLine = $"traveling {hereFrame} → {TargetFrame}/{TargetPad}  ({StateAge():0.0}s)";
        }

        private void SendMoveToCell()
        {
            try
            {
                AEC.Instance.sendRequest(new RequestMoveToCell(TargetFrame, TargetPad));
                _traveSent = true;
            }
            catch (Exception ex)
            {
                Fail($"moveToCell send failed: {ex.Message}");
            }
        }
        private bool _traveSent;
        private bool _tferSent;
        private bool _cellHopBudgetReset;
        // Time at which Area.currentArea.Name first matched TargetArea.
        // -1 = not matched yet this travel session.
        private float _areaFirstMatchedAt = -1f;
        // Time at which Entity.mainPlayer.Frame first matched TargetFrame.
        // Used with position-stability to decide the cell walk is done.
        private float _frameFirstMatchedAt = -1f;
        // Resolved MapGoToCell pad GameObject we're walking toward in stage 2.
        // Reset each time we enter Traveling; null = haven't found one yet
        // / no pad exists for this target (will fall back to moveToCell).
        private UnityEngine.GameObject _pickedGotoPad;
        // Whether a join cutscene was observed for THIS tfer (its End event is
        // then the go signal instead of the no-cutscene grace window).
        private bool _sawJoinCutscene;
        // Wall-aware walker (lazy; reused across travels) + bookkeeping:
        // _walkIssued = a path to the current pad has been started;
        // _pathDoneAt = when the walker reported arrival (frame-flip watchdog).
        private PathWalker _path;
        private bool _walkIssued;
        private float _pathDoneAt = -1f;
        // Player position tracking (local space), updated every Tick. A stale
        // _lastMoveAt means the character is standing still.
        private Vector3 _lastPlayerPos;
        private float _lastMoveAt;
        // Hunt liveness + approach bookkeeping: the target we're watching for
        // a death transition (= a kill), when the last kill landed, and
        // whether PathWalker is currently closing distance to a target.
        private Monster _watchedTarget;
        private float _lastKillAt;
        private bool _huntPathing;

        // Update the moving/stopped signal every tick. Local position moves in
        // the same space WalkVector drives, so a ~0.03-unit delta is a real
        // step and idle sway doesn't count as movement.
        private void TrackMovement()
        {
            try
            {
                GameObject go = Entity.mainPlayer?.getGameObject();
                if (go == null)
                {
                    return;
                }

                Vector3 p = go.transform.localPosition;
                if ((p - _lastPlayerPos).sqrMagnitude > 0.001f)
                {
                    _lastPlayerPos = p;
                    _lastMoveAt = Time.time;
                }
            }
            catch { }
        }

        private string _huntObservedFrame = "";
        private float _huntFrameSeenAt;

        private void TickHunt()
        {
            // Hunt step: no quest — kill the step's monsters for its drop until
            // the inventory goal is met (progress = item count, not objectives).
            QuestChains.Entry step = CurrentStep;
            bool dropHunt = step is { isHunt: true };
            Quest q = null;
            QuestTurninItem nextObjective = null;
            if (dropHunt)
            {
                if (GoalMet(step))
                {
                    StopAutoskills();
                    Log($"  goal reached: {GoalText(step)}");
                    CurrentIteration = Iterations;
                    EnterState(RunState.Cooldown);
                    return;
                }
            }
            else
            {
                q = Quest.Get(QuestID);
                if (q == null)
                {
                    Fail($"no quest def cached for {QuestID} — open the quest UI once to populate Quest.Get()");
                    return;
                }

                // Find the first incomplete objective. If none, we're ready to
                // turn in. The quest def's Turnins are the source of truth here —
                // we don't try to interpret them, just check completion per QOID.
                nextObjective = NextIncompleteObjective(q);
                if (nextObjective == null)
                {
                    StopAutoskills();
                    Log("  all objectives complete; turning in");
                    EnterState(RunState.TurningIn);
                    return;
                }

                // Interact objectives (QOType 2): a map machine — a door, a lever,
                // the DragonSlayer armor pieces — that the player CLICKS. Nothing
                // to fight; walk to the machine and trigger it. RefArray holds the
                // machine GameObject name(s) (exact or a prefix like "DSPiece"
                // covering DSPiece1..6). Handled entirely apart from combat.
                if (nextObjective.QOType == QuestObjectiveType.Interact)
                {
                    TickInteract(nextObjective);
                    return;
                }

                // Apop objectives (QOType 4): "talk to <NPC>" — walk to the friendly
                // NPC and click it. NPCButton.Interact() plays any pre-dialog (auto-
                // skipped) then ShowApop() auto-sends openApopQO, which credits the
                // objective. RefArray[0] is the apopID; the NPC is the one carrying
                // that apop.
                // Apop (4) and Talk (3) both resolve by going to the quest NPC and
                // clicking it — same handler.
                if (nextObjective.QOType == QuestObjectiveType.Apop
                    || nextObjective.QOType == QuestObjectiveType.Talk)
                {
                    TickApop(nextObjective);
                    return;
                }

                // Cutscene (5): "watch cutscene N" — e.g. "Find the Bones" clicks a
                // prop that plays a cutscene. RefArray[0] is the cutscene/dialog id.
                if (nextObjective.QOType == QuestObjectiveType.Cutscene)
                {
                    TickCutscene(nextObjective);
                    return;
                }
            }

            string huntFor = nextObjective?.Name ?? (step?.HasGoal == true ? step.goalItem : step?.MonText ?? "drops");

            // Kill detection: a watched target flipping to Dead is a kill —
            // that's hunt liveness even when the objective is a low-chance
            // drop that takes a dozen kills to advance.
            if (_watchedTarget != null && _watchedTarget.currentState == Entity.State.Dead)
            {
                _lastKillAt = Time.time;
                _watchedTarget = null;
            }

            // Acquire / refresh target on each tick. The setter on
            // Entity.mainPlayer.target no-ops if the value is unchanged AND
            // refuses dead targets, so calling it every frame is cheap.
            Monster tgt = PickBestHostile(nextObjective);
            if (tgt == null)
            {
                StopAutoskills();
                // MAP-AWARE: the target monster may live in another cell — the
                // entry frame can be empty (bludrut R2 has zero mobs; the undead
                // are in Enter/R3/R4/R10). Scan every monster in the map (all
                // carry .Frame) for a cell holding a matching hostile and jump
                // straight there. Also recovers from a respawn that dropped us
                // in the wrong room.
                string here = Entity.mainPlayer?.Frame ?? "";
                if (!string.Equals(_huntObservedFrame, here, StringComparison.OrdinalIgnoreCase))
                {
                    _huntObservedFrame = here;
                    _huntFrameSeenAt = Time.time;
                }
                if (Time.time - _huntFrameSeenAt < 2f)
                {
                    StatusLine = $"waiting for monsters in {here} to load";
                    return;
                }
                string targetFrame = FindHostileFrame(nextObjective);
                // A known spawn is still a valid destination while its monster
                // is dead. Sweeping away here can prevent its respawn/loading.
                if (string.IsNullOrEmpty(targetFrame)) targetFrame = FindHostileFrame(nextObjective, includeDead: true);
                if (!string.IsNullOrEmpty(targetFrame)
                    && !string.Equals(targetFrame, here, StringComparison.OrdinalIgnoreCase))
                {
                    GoToFrame(targetFrame, $"hunt '{huntFor}'");
                    return;
                }
                if (string.IsNullOrEmpty(targetFrame))
                {
                    // No matching hostile loaded anywhere — sweep cells (lazy
                    // maps) then fail with a hint if the map is genuinely dry.
                    TickCellSweep(here, $"hostile for '{huntFor}'",
                        () => PickBestHostile(nextObjective) != null || !string.IsNullOrEmpty(FindHostileFrame(nextObjective)));
                    return;
                }
                StatusLine = $"no hostile in {here} for '{huntFor}' — waiting to respawn";
                CheckHuntTimeout();
                return;
            }
            _watchedTarget = tgt;

            // Wall-aware approach. The game's own charge walk toward a target
            // is straight-line and gives up when a Blocker collider is in the
            // way (blockedMoveTimer), leaving autoskills spamming "Target out
            // of range!" forever — bludrut's pillars do exactly this. If the
            // target is far or not in clear line of sight, path to it first,
            // then engage; casting is held while closing (a cast cancels the
            // walk).
            GameObject meGO = Entity.mainPlayer.getGameObject();
            GameObject tgtGO2 = tgt.getGameObject();
            if (meGO != null && tgtGO2 != null)
            {
                Vector3 me = meGO.transform.localPosition;
                Vector3 to = tgtGO2.transform.localPosition;
                float dist = Vector2.Distance(me, to);
                if (dist > MaxEngageDist || !PathWalker.LineClearLocal(me, to))
                {
                    _path ??= new PathWalker(Log);
                    if (!_huntPathing || Vector2.Distance(_path.Goal, to) > 1.5f)
                    {
                        _path.Begin(to);
                        _huntPathing = true;
                    }
                    _path.Tick();
                    if (!_path.Done && !_path.Failed)
                    {
                        BeyondAgentClass.autoskillsActive = false;
                        StatusLine = $"approaching {tgt.Name} ({_path.Status}, {dist:0.0}u)";
                        CheckHuntTimeout();
                        return;
                    }
                    // Done or Failed: fall through and let the charge try —
                    // pathing got us as close as it could.
                    _huntPathing = false;
                }
                else if (_huntPathing)
                {
                    _path?.Cancel();
                    _huntPathing = false;
                }
            }

            // Engage by mimicking exactly what a player click does — two
            // calls to Targetable.ClickMe(): first call assigns target +
            // newTarget(), second call triggers chargeAuto() → Charge(0) →
            // RequestStartCharge.
            if (Entity.mainPlayer.target != tgt)
            {
                Log($"  [hunting] engaging target: {tgt.Name} (Level {tgt.Level}, HP: {tgt.HP}/{tgt.MaxHP})");
                try
                {
                    GameObject go = tgt.getGameObject();
                    Targetable tb = go?.GetComponent<Targetable>();
                    if (tb != null)
                    {
                        tb.ClickMe();
                        tb.ClickMe();
                    }
                    else
                    {
                        Entity.mainPlayer.target = tgt;
                        Entity.mainPlayer.Charge(0);
                    }
                }
                catch (Exception ex)
                {
                    Log($"  engage failed: {ex.Message}");
                }
            }

            EnsureAutoskillsOn();

            int progressSum = dropHunt ? ItemGoals.Count(step.goalItem) : SumObjectiveProgress(q);
            if (progressSum != _lastProgressSum)
            {
                _lastProgressSum = progressSum;
                _lastProgressAt = Time.time;
                StatusLine = $"hunting {tgt.Name} (id={tgt.ID})  progress={progressSum}";
            }
            else
            {
                StatusLine = $"hunting {tgt.Name}  no progress {Time.time - _lastProgressAt:0.0}s";
            }

            CheckHuntTimeout();
        }

        private void CheckHuntTimeout()
        {
            // Kills count as liveness: a probabilistic objective (e.g. the
            // bludrut key at a low drop rate) can take many kills with zero
            // "progress" — only silence on BOTH fronts means we're stuck.
            float lastActivity = Mathf.Max(_lastProgressAt, _lastKillAt);
            if (Time.time - lastActivity > HuntTimeoutNoProgress)
            {
                Fail($"no kills or objective progress in {HuntTimeoutNoProgress:0}s — quest likely needs a different zone, mob type, or interaction we don't handle yet");
                return;
            }
            // Rare boss drops may legitimately take longer than a quest objective.
            if (CurrentStep is not { isHunt: true } && Time.time - _lastProgressAt > HuntHardCapSec)
            {
                Fail($"killing for {HuntHardCapSec / 60f:0} minutes with zero objective progress — these kills aren't credited (wrong mob or wrong zone)");
            }
        }

        // Walk to the quest's map machine and click it. Runs each tick while an
        // Interact objective is the next incomplete one.
        //
        // Frame-aware + cell-hopping: a machine only exists in the loaded cell,
        // and some machines (doors like "GoInside") transfer you to the NEXT
        // room — which IS the objective. So we do NOT force-travel back to the
        // chain entry's frame. Instead: click any matching machine in the
        // current cell; if none is here, hop to an unexplored adjacent cell via
        // its Goto pad and look again. Multi-piece objectives (DSPiece1..6) fall
        // out naturally — each click consumes one piece (collider disabled) and
        // we pick the next nearest, hopping cells if they're spread out.
        private void TickInteract(QuestTurninItem obj)
        {
            SuppressAutoskills();

            if (obj.QOID != _interactQoid)
            {
                _interactQoid = obj.QOID;
                _interactVisited.Clear();
                _interactTarget = null;
                _sweepFrame = null;
            }

            // Credit landed? Advance — clears the current target so the next
            // piece (or the next objective) re-locates from scratch.
            int progress = SumObjectiveProgress(Quest.Get(QuestID));
            if (progress != _lastProgressSum)
            {
                _lastProgressSum = progress;
                _lastProgressAt = Time.time;
                _interactTarget = null;
                _interactClickedAt = -1f;
            }

            GameObject me = Entity.mainPlayer?.getGameObject();
            if (me == null)
            {
                return;
            }
            string here = Entity.mainPlayer?.Frame ?? "";

            // MAP-AWARE LOCATE: scan every machine in every cell of the loaded
            // map (inactive included) and find the one matching this objective —
            // we KNOW which cell it's in, no guessing, no pad-wandering.
            MapNav.MachineInfo info = MapNav.FindMachine(obj.RefArray, here);
            if (info == null)
            {
                // Not in the instantiated hierarchy. Most maps hold all cells at
                // once so this is rare; when it happens, sweep every cell by
                // name (authoritative list) to force each to load and re-scan.
                TickCellSweep(here, RefsText(obj), () => MapNav.FindMachine(obj.RefArray, Entity.mainPlayer?.Frame ?? "") != null);
                return;
            }

            // In a different cell than the machine → jump straight there.
            if (!string.IsNullOrEmpty(info.Frame)
                && !string.Equals(info.Frame, here, StringComparison.OrdinalIgnoreCase))
            {
                GoToFrame(info.Frame, $"machine '{info.Name}'");
                return;
            }

            if (_interactTarget != info.Go)
            {
                _interactTarget = info.Go;
                _path?.Cancel();
                _huntPathing = false;
                _interactClickedAt = -1f;
                Log($"  [interact] machine '{info.Name}' in {info.Frame} for '{obj.Name}'");
            }

            Vector3 mePos = me.transform.localPosition;
            Vector3 machineLocal = me.transform.parent != null
                ? me.transform.parent.InverseTransformPoint(_interactTarget.transform.position)
                : _interactTarget.transform.position;

            // Close + clear LOS → click. Interact() runs the whole action
            // pipeline (any GotoCell hop AND the machineInteract that credits
            // the objective), exactly like a mouse click.
            if (Vector2.Distance(mePos, machineLocal) <= InteractReachDist
                && PathWalker.LineClearLocal(mePos, machineLocal))
            {
                _path?.Cancel();
                _huntPathing = false;
                if (_interactClickedAt < 0f || Time.time - _interactClickedAt > InteractRetrySec)
                {
                    ClickMachine(_interactTarget);
                    _interactClickedAt = Time.time;
                }
                StatusLine = $"interacting with {_interactTarget.name} ({StateAge():0.0}s)";
                CheckHuntTimeout();
                return;
            }

            // Approach (wall-aware). Doors sit embedded in walls, so A* may call
            // the goal unreachable — fine, a machine click needs no proximity, so
            // click from here on a path fail rather than loop.
            _path ??= new PathWalker(Log);
            if (!_huntPathing || Vector2.Distance(_path.Goal, machineLocal) > 1.5f)
            {
                _path.Begin(machineLocal);
                _huntPathing = true;
            }
            _path.Tick();
            if (_path.Failed)
            {
                _huntPathing = false;
                if (_interactClickedAt < 0f || Time.time - _interactClickedAt > InteractRetrySec)
                {
                    Log($"  can't path to '{_interactTarget.name}' (embedded in geometry) — clicking from here");
                    ClickMachine(_interactTarget);
                    _interactClickedAt = Time.time;
                }
            }
            StatusLine = $"reaching {_interactTarget?.name} ({_path.Status})";
            CheckHuntTimeout();
        }

        // Direct cell jump: moveToCell to a named frame and wait for arrival.
        // The server serves any cell (adjacency-free), so this teleports us to
        // exactly where the target lives. Throttled re-send in case the first
        // request is dropped mid-load.
        private void GoToFrame(string frame, string why, string pad = "Spawn")
        {
            string here = Entity.mainPlayer?.Frame ?? "";
            if (string.Equals(here, frame, StringComparison.OrdinalIgnoreCase))
            {
                return;   // arrived — caller proceeds next tick
            }
            if (!string.Equals(frame, _navFrame, StringComparison.OrdinalIgnoreCase)
                || Time.time - _navSentAt > NavResendSec)
            {
                _navFrame = frame;
                _navSentAt = Time.time;
                Log($"  [nav] {here} → {frame} for {why}");
                try { AEC.Instance.sendRequest(new RequestMoveToCell(frame, string.IsNullOrEmpty(pad) ? "Spawn" : pad)); } catch { }
            }
            StatusLine = $"traveling to {frame} for {why} ({Time.time - _navSentAt:0.0}s)";
            CheckHuntTimeout();
        }

        // Fallback for maps that DON'T hold every cell at once: walk the
        // authoritative cell list, moveToCell into each unvisited one, and let
        // `found` re-check after each load. Deterministic and complete — fails
        // with a full machine dump only once every cell has been checked.
        private void TickCellSweep(string here, string want, Func<bool> found)
        {
            _interactVisited.Add(here);
            if (found())
            {
                _sweepFrame = null;
                return;   // appeared after the last load
            }
            if (_sweepFrame != null && !string.Equals(here, _sweepFrame, StringComparison.OrdinalIgnoreCase))
            {
                GoToFrame(_sweepFrame, $"searching for '{want}'");
                return;
            }
            string next = MapNav.Cells().FirstOrDefault(c => !_interactVisited.Contains(c));
            if (next == null)
            {
                Fail($"'{want}' not found in any cell of this map. Machines present: {MapNav.DumpMachines()}");
                return;
            }
            _sweepFrame = next;
            GoToFrame(next, $"searching for '{want}'");
        }
        private UnityEngine.GameObject _interactTarget;
        private readonly HashSet<string> _interactVisited = new(StringComparer.OrdinalIgnoreCase);
        private int _interactQoid = -1;
        private float _interactClickedAt = -1f;
        private string _sweepFrame;
        private string _navFrame;
        private float _navSentAt;
        public float NavResendSec = 8f;
        public float InteractReachDist = 2.5f;
        public float InteractRetrySec = 2.5f;

        // Walk to the quest's friendly NPC and click it to satisfy an Apop
        // ("talk to X") objective. RefArray[0] is the target apopID; the NPC is
        // the friendly Monster carrying that apop. Clicking (NPCButton.Interact)
        // plays any pre-dialog — auto-skipped by our cutscene patch — then
        // ShowApop() fires openApopQO, crediting the objective. Same cell-hop
        // search as interact when the NPC is in another room. A direct
        // RequestOpenApopQO fallback fires if the click doesn't land credit
        // (belt-and-suspenders for our server; harmless if already credited).
        private void TickApop(QuestTurninItem obj)
        {
            SuppressAutoskills();

            if (obj.QOID != _interactQoid)
            {
                _interactQoid = obj.QOID;
                _interactVisited.Clear();
                _apopNpc = null;
                _sweepFrame = null;
                _interactClickedAt = -1f;
            }

            int progress = SumObjectiveProgress(Quest.Get(QuestID));
            if (progress != _lastProgressSum)
            {
                _lastProgressSum = progress;
                _lastProgressAt = Time.time;
                return;      // credited — NextIncompleteObjective advances next tick
            }

            GameObject me = Entity.mainPlayer?.getGameObject();
            if (me == null)
            {
                return;
            }
            string here = Entity.mainPlayer?.Frame ?? "";

            int wantApop = (obj.RefArray != null && obj.RefArray.Length > 0
                && int.TryParse(obj.RefArray[0], out int a)) ? a : -1;
            int wantNpc = Quest.Get(QuestID)?.NPCID ?? -1;

            // MAP-AWARE LOCATE: the NPC carries its own .Frame, and every NPC in
            // the map is in Area.currentArea.Monsters — so we know exactly which
            // cell the spirit is in without walking.
            _apopNpc = MapNav.FindNpc(wantApop, wantNpc);
            if (_apopNpc == null)
            {
                TickCellSweep(here, $"NPC apop {wantApop}", () => MapNav.FindNpc(wantApop, wantNpc) != null);
                return;
            }

            string npcFrame = _apopNpc.Frame ?? "";
            if (!string.IsNullOrEmpty(npcFrame)
                && !string.Equals(npcFrame, here, StringComparison.OrdinalIgnoreCase))
            {
                GoToFrame(npcFrame, $"NPC '{_apopNpc.Name}'");
                return;
            }

            GameObject npcGO = _apopNpc.getGameObject();
            if (npcGO == null)
            {
                // In our cell per data but its GameObject hasn't spawned yet —
                // give the cell a tick to finish loading.
                StatusLine = $"waiting for NPC '{_apopNpc.Name}' to spawn in {here}";
                CheckHuntTimeout();
                return;
            }
            Vector3 mePos = me.transform.localPosition;
            Vector3 npcLocal = me.transform.parent != null
                ? me.transform.parent.InverseTransformPoint(npcGO.transform.position)
                : npcGO.transform.position;

            if (Vector2.Distance(mePos, npcLocal) <= InteractReachDist
                && PathWalker.LineClearLocal(mePos, npcLocal))
            {
                _path?.Cancel();
                _huntPathing = false;
                if (_interactClickedAt < 0f || Time.time - _interactClickedAt > InteractRetrySec)
                {
                    ClickNpc(npcGO, wantApop);
                    _interactClickedAt = Time.time;
                }
                StatusLine = $"talking to {_apopNpc.Name} ({StateAge():0.0}s)";
                CheckHuntTimeout();
                return;
            }

            _path ??= new PathWalker(Log);
            if (!_huntPathing || Vector2.Distance(_path.Goal, npcLocal) > 1.5f)
            {
                _path.Begin(npcLocal);
                _huntPathing = true;
            }
            _path.Tick();
            if (_path.Failed)
            {
                // NPC unreachable by grid (behind geometry) — clicking needs no
                // proximity, so talk from here rather than loop.
                _huntPathing = false;
                if (_interactClickedAt < 0f || Time.time - _interactClickedAt > InteractRetrySec)
                {
                    Log($"  can't path to NPC '{_apopNpc.Name}' — talking from here");
                    ClickNpc(npcGO, wantApop);
                    _interactClickedAt = Time.time;
                }
            }
            StatusLine = $"reaching {_apopNpc?.Name} ({_path.Status})";
            CheckHuntTimeout();
        }
        private Monster _apopNpc;

        // Click an NPC exactly as the player would (NPCButton.Interact), then —
        // as a reliable fallback for our server — directly send openApopQO for
        // the target apop. The server credits by apopID and caps at the required
        // count, so a redundant send is harmless.
        private void ClickNpc(GameObject npcGO, int wantApop)
        {
            try
            {
                NPCButton btn = npcGO.GetComponentInChildren<NPCButton>(includeInactive: true);
                if (btn != null)
                {
                    Log($"  [apop] clicking NPC button for '{_apopNpc?.Name}'");
                    btn.Interact();
                }
                if (wantApop > 0 && _apopNpc != null)
                {
                    AEC.Instance.sendRequest(new RequestOpenApopQO(wantApop, _apopNpc.monMapID));
                }
            }
            catch (Exception ex)
            {
                Log($"  ClickNpc error: {ex.Message}");
            }
        }

        // Complete a Cutscene objective (QOType 5, e.g. "Find the Bones" is
        // cutscene 35). RefArray[0] is the cutscene/dialog id.
        //
        // We send watchCutscene DIRECTLY rather than playing the cutscene. The
        // client's own completion path (Dialogger_Manager.EndPressed) sends the
        // exact same RequestWatchCutscene(id) with no other context, so the
        // server can't tell the difference and credits it. Crucially this avoids
        // the BLACK-SCREEN bug that a triggered RequestGetCutscene left when the
        // played cutscene didn't cleanly tear down its black overlay — and it
        // never touches the user's Cutscene Auto-Skip toggle. Retries are widely
        // spaced so they can't trip the server's spam guard.
        private void TickCutscene(QuestTurninItem obj)
        {
            SuppressAutoskills();

            if (obj.QOID != _csQoid)
            {
                _csQoid = obj.QOID;
                _csSentAt = -1f;
            }

            int progress = SumObjectiveProgress(Quest.Get(QuestID));
            if (progress != _lastProgressSum)
            {
                _lastProgressSum = progress;
                _lastProgressAt = Time.time;
                return;   // credited — advance next tick
            }

            int csid = (obj.RefArray != null && obj.RefArray.Length > 0
                && int.TryParse(obj.RefArray[0], out int c)) ? c : -1;
            if (csid <= 0)
            {
                Fail($"cutscene objective '{obj.Name}' has no cutscene id in RefIDs");
                return;
            }

            if (_csSentAt < 0f || Time.time - _csSentAt > CutsceneRetrySec)
            {
                Log($"  [cutscene] watchCutscene({csid}) for '{obj.Name}'");
                try { AEC.Instance.sendRequest(new RequestWatchCutscene(csid)); } catch { }
                _csSentAt = Time.time;
            }
            StatusLine = $"completing cutscene {csid} for '{obj.Name}' ({StateAge():0.0}s)";
            CheckHuntTimeout();
        }
        private int _csQoid = -1;
        private float _csSentAt = -1f;
        public float CutsceneRetrySec = 4f;

        // Global rate-limit backoff, driven from Tick(): the server's "Spam
        // Detected" rNotify sets a short window during which we send nothing.
        private float _lastSpamSeen = -1f;
        private float _spamBackoffUntil;
        public float SpamBackoffSec = 6f;

        // Trigger a machine exactly as a mouse click would: MapMachine.Interact()
        // is public and runs the whole action pipeline, including the
        // QuestObjective action's Finisher that fires RequestMachineInteraction
        // (the machineInteract c2s the server credits). Face it first so any
        // interact animation reads right.
        private void ClickMachine(UnityEngine.GameObject machineGO)
        {
            try
            {
                MapMachine mm = machineGO.GetComponent<MapMachine>();
                if (mm == null)
                {
                    Log($"  machine '{machineGO.name}' lost its MapMachine component");
                    return;
                }
                Log($"  [interact] clicking '{machineGO.name}'");
                mm.Interact();
            }
            catch (Exception ex)
            {
                Log($"  ClickMachine error: {ex.Message}");
            }
        }

        private static string RefsText(QuestTurninItem obj)
        {
            return obj?.RefArray != null ? string.Join(",", obj.RefArray) : "";
        }

        // Interact objectives don't want autoskills running (a cast cancels the
        // walk and there's nothing to fight); force it off. The user's toggle is
        // restored by StopAutoskills() when the runner stops or the quest ends.
        private void SuppressAutoskills()
        {
            if (BeyondAgentClass.autoskillsActive)
            {
                BeyondAgentClass.autoskillsActive = false;
            }
        }

        private bool PlayerIsDead()
        {
            try
            {
                return Entity.mainPlayer != null
                    && Entity.mainPlayer.currentState == Entity.State.Dead;
            }
            catch
            {
                return false;
            }
        }

        private void TickRespawning()
        {
            if (PlayerIsDead())
            {
                if (StateAge() > RespawnTimeout)
                {
                    Fail($"still dead after {RespawnTimeout:0}s — respawn never landed");
                    return;
                }
                StatusLine = $"dead — waiting for respawn ({StateAge():0.0}s)";
                return;
            }
            // Alive again. Give the revive a beat to place the character,
            // then re-run the travel check — the spawn frame is almost never
            // the hunt frame, and even when it is, Accepting's already-
            // accepted path drops us straight back into Hunting.
            if (StateAge() < 1.5f)
            {
                StatusLine = "respawned — settling";
                return;
            }
            if (_beforeDeath is RunState.LoadingShop or RunState.Buying or RunState.DiscoveringSources)
            {
                if (_beforeDeath == RunState.DiscoveringSources) _discovery = MonsterFarmingCatalog.BeginDiscovery();
                EnterState(_beforeDeath);
                return;
            }
            Log($"  respawned; resuming (travel check: {(NeedsCellHop() ? "yes" : "no")})");
            EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
        }

        private void TickTurnIn()
        {
            // Most quests turn in at a specific NPC in a specific cell (Veddrian
            // in bludrut R2 for "Remembering...") — TurnInType.NPC. The kill/
            // interact usually finishes in a DIFFERENT cell, so we must travel to
            // the turn-in NPC before completing, else tryQuestComplete fires into
            // the void and the run stalls. AnyWhere/Auto turn-ins complete from
            // wherever we stand.
            if (!AtTurnInLocation(Quest.Get(QuestID)))
            {
                return;   // still traveling to the turn-in NPC
            }

            try
            {
                Log($"  [turning-in] sending request to complete quest {QuestID}...");
                AEC.Instance.sendRequest(new RequestTryQuestComplete(QuestID));
            }
            catch (Exception ex)
            {
                Fail($"tryQuestComplete send failed: {ex.Message}");
                return;
            }
            _turnInSentAt = Time.time;
            EnterState(RunState.AwaitingComplete);
        }
        private float _turnInSentAt;
        private bool _turnInTferSent;
        private float _turnInTferAt;

        // True when we're where this quest can be turned in — at the turn-in
        // NPC's cell (and area) for TurnInType.NPC, or anywhere for AnyWhere/
        // Auto. While false, it drives travel (cross-area tfer or in-area cell
        // hop) toward the turn-in NPC and returns false so TickTurnIn waits.
        private bool AtTurnInLocation(Quest q)
        {
            if (q == null || q.TurnInType != Quest.TurnInTypes.NPC)
            {
                return true;   // AnyWhere / Auto — no travel needed
            }

            string tmap = (q.TurnInMapName ?? "").Trim();
            string tframe = (q.TurnInFrame ?? "").Trim();
            string tpad = string.IsNullOrEmpty(q.TurnInPad) ? "Spawn" : q.TurnInPad;
            string hereArea = Area.currentArea?.Name ?? "";
            string hereFrame = Entity.mainPlayer?.Frame ?? "";

            // Cross-area turn-in (e.g. finished in a sub-map, turn in back in the
            // hub): tfer to the turn-in map/frame/pad and wait for arrival.
            if (!string.IsNullOrEmpty(tmap) && !AreaMatches(hereArea, tmap))
            {
                if (!_turnInTferSent || Time.time - _turnInTferAt > TferTimeout)
                {
                    _turnInTferSent = true;
                    _turnInTferAt = Time.time;
                    try
                    {
                        string name = Entity.mainPlayer?.Name ?? "";
                        AEC.Instance.sendRequest(new Request("tfer",
                        [
                            name, tmap, "0", tframe, tpad,
                        ]));
                        Log($"  [turnin] tfer to {tmap}/{tframe} to reach {q.TurnInNPCName}");
                    }
                    catch { }
                }
                StatusLine = $"traveling to {tmap}/{tframe} to turn in to {q.TurnInNPCName}";
                CheckHuntTimeout();
                return false;
            }
            _turnInTferSent = false;

            // Same area, wrong cell: jump to the turn-in NPC's frame.
            if (!string.IsNullOrEmpty(tframe)
                && !string.Equals(hereFrame, tframe, StringComparison.OrdinalIgnoreCase))
            {
                GoToFrame(tframe, $"turn in to {q.TurnInNPCName}", tpad);
                return false;
            }
            return true;
        }

        private void TickAwaitComplete()
        {
            // Real success signal: ResponseQuestComplete.Execute fired for our
            // quest with Success=true. Captured by Harmony patch into
            // RuntimeEvents (LastCompleteQid / LastCompleteTime / Success).
            // We compare LastCompleteTime against _turnInSentAt so a stale
            // QComp from a previous attempt doesn't false-positive.
            if (RuntimeEvents.LastCompleteQid == QuestID
                && RuntimeEvents.LastCompleteTime > _turnInSentAt
                && RuntimeEvents.LastCompleteSuccess)
            {
                Log($"  iter {CurrentIteration}: turn-in confirmed (QComp success)");
                _farmFailures = 0;
                // Always cool down briefly before the next action — the
                // server's spam-detect window is short but firm. Cooldown
                // state decides what comes next based on remaining work.
                EnterState(RunState.Cooldown);
                return;
            }
            // Watch for an rNotify error after our send. The server uses
            // these for rate-limiting ("Spam Detected") and for quest-side
            // rejection ("You haven't completed…"). Surface them immediately
            // rather than waiting out the full timeout.
            if (RuntimeEvents.LastNotifyTime > _turnInSentAt
                && !string.IsNullOrEmpty(RuntimeEvents.LastNotifyMsg))
            {
                string msg = RuntimeEvents.LastNotifyMsg;
                string lower = msg.ToLowerInvariant();
                if (lower.Contains("spam") || lower.Contains("wait before"))
                {
                    // Rate-limited, NOT a real rejection — back off and re-send
                    // the turn-in rather than killing the whole chain (the old
                    // behaviour, which stranded the runner mid-chain).
                    _spamBackoffUntil = Time.time + SpamBackoffSec;
                    _lastSpamSeen = RuntimeEvents.LastNotifyTime;
                    Log($"  turn-in rate-limited — backing off {SpamBackoffSec:0}s and retrying");
                    EnterState(RunState.TurningIn);
                    return;
                }
                if (lower.Contains("requirement") || lower.Contains("haven't")
                    || lower.Contains("complete this quest") || lower.Contains("not met"))
                {
                    Fail($"server rejected turn-in: \"{msg}\"");
                    return;
                }
            }
            if (StateAge() > CompleteTimeout)
            {
                Fail($"no QComp success for quest {QuestID} within {CompleteTimeout:0.0}s — last rNotify: \"{RuntimeEvents.LastNotifyMsg}\"");
            }
            else
            {
                StatusLine = $"waiting for QComp… ({StateAge():0.0}s)";
            }
        }

        // --- helpers ---

        // True if either area or frame differs from current — covers both
        // stages of TickTravel. Either branch alone (just area, just frame)
        // is also valid and handled there.
        private bool NeedsCellHop()
        {
            string hereArea = Area.currentArea?.Name ?? "";
            string hereFrame = Entity.mainPlayer?.Frame ?? "";
            return (!string.IsNullOrEmpty(TargetArea) && !AreaMatches(hereArea, TargetArea)) || (!string.IsNullOrEmpty(TargetFrame)
                && !string.Equals(hereFrame, TargetFrame, StringComparison.OrdinalIgnoreCase));
        }

        // Areas come back from the server with an instance suffix appended
        // (e.g. "lair-3", "battleon-545454", "infinityportal-7"). The chain
        // entry only knows the base name. Treat them as equal if the
        // current area equals the target or starts with "target-".
        private static bool AreaMatches(string here, string target)
        {
            return string.IsNullOrEmpty(target) || (!string.IsNullOrEmpty(here) && (here == target || here.StartsWith(target + "-")));
        }

        private bool IsQuestAccepted(int id)
        {
            try
            {
                return Entity.mainPlayer?.Quests?.GetQuest(id) != null;
            }
            catch
            {
                return false;
            }
        }

        private QuestTurninItem NextIncompleteObjective(Quest q)
        {
            if (q?.Turnins == null)
            {
                return null;
            }

            PlayerQuestData pq = Entity.mainPlayer?.Quests;
            if (pq == null)
            {
                return null;
            }

            foreach (QuestTurninItem t in q.Turnins)
            {
                if (!pq.IsObjectiveComplete(t.QOID))
                {
                    return t;
                }
            }
            return null;
        }

        private int SumObjectiveProgress(Quest q)
        {
            if (q?.Turnins == null)
            {
                return 0;
            }

            PlayerQuestData pq = Entity.mainPlayer?.Quests;
            if (pq == null)
            {
                return 0;
            }

            int sum = 0;
            foreach (QuestTurninItem t in q.Turnins)
            {
                sum += pq.getQuestObjective(t.QOID)?.Quantity ?? 0;
            }
            return sum;
        }

        // The frame of a cell holding a live hostile that matches this
        // objective's target (entry `mon` filter, else the Killcount RefArray
        // MonID, else any hostile). Every monster in Area.currentArea.Monsters
        // carries its .Frame — including those in cells we haven't loaded — so
        // this is a pure lookup, no walking. Returns the current frame if a
        // match is already here, else the most-populated matching frame, else
        // "" when nothing in the whole map matches.
        // The quest DB's monster ids for this objective, or null when the DB
        // doesn't know the quest/objective (then the older signals apply).
        private List<int> ObjectiveMonsters(QuestTurninItem obj)
        {
            if (obj == null || !QuestDB.TryGet(QuestID, out QuestDB.QuestInfo q))
            {
                return null;
            }
            QuestDB.Objective o = q.Objectives.FirstOrDefault(x => x.QOID == obj.QOID);
            return o is { IsKill: true, Monsters.Count: > 0 } ? o.Monsters : null;
        }

        private string FindHostileFrame(QuestTurninItem obj, bool includeDead = false)
        {
            if (Area.currentArea?.Monsters == null)
            {
                return "";
            }

            string here = Entity.mainPlayer?.Frame ?? "";
            Dictionary<string, int> byFrame = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Monster m in Area.currentArea.Monsters.Values)
                {
                    if (m == null
                        || m.reactionType != Entity.ReactionType.Hostile
                        || (!includeDead && m.currentState == Entity.State.Dead)
                        || !MatchesTarget(m, obj))
                    {
                        continue;
                    }
                    string f = m.Frame ?? "";
                    if (string.IsNullOrEmpty(f))
                    {
                        continue;
                    }
                    if (string.Equals(f, here, StringComparison.OrdinalIgnoreCase))
                    {
                        return here;   // a match is already in our cell
                    }
                    byFrame[f] = byFrame.TryGetValue(f, out int c) ? c + 1 : 1;
                }
            }
            catch { }
            return byFrame.Count == 0 ? ""
                : byFrame.OrderByDescending(kv => kv.Value).First().Key;
        }

        private Monster PickBestHostile(QuestTurninItem obj)
        {
            if (Area.currentArea == null || Entity.mainPlayer == null)
            {
                return null;
            }

            // Sticky targeting: if our current target is still alive, hostile,
            // matches the filter, and is in the player's frame, keep it. Avoids
            // the bot oscillating between two equally-close mobs when their
            // positions shift slightly each tick (which broke combat against
            // the pair of water draconians flanking the player).
            string playerFrameForStick = Entity.mainPlayer.Frame ?? "";
            if (Entity.mainPlayer.target is Monster current
                && current != null
                && current.currentState != Entity.State.Dead
                && current.reactionType == Entity.ReactionType.Hostile
                && MatchesTarget(current, obj)
                && string.Equals(current.Frame ?? "", playerFrameForStick, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            // Frame comparison case-insensitive: server-emitted Frame names
            // are capitalized (e.g. "R2") but moveToCell args and our chain
            // entries are typically lowercase. Game's own GetMonstersInFrame()
            // does case-sensitive equality, which would miss every wyvern.
            string playerFrame = Entity.mainPlayer.Frame ?? "";
            IEnumerable<Monster> candidates;
            try
            {
                candidates = Area.currentArea.Monsters?.Values;
            }
            catch
            {
                return null;
            }
            if (candidates == null)
            {
                return null;
            }

            IEnumerable<Monster> alive = candidates.Where(m =>
                m != null
                && string.Equals(m.Frame ?? "", playerFrame, StringComparison.OrdinalIgnoreCase)
                && m.currentState != Entity.State.Dead
                && m.reactionType == Entity.ReactionType.Hostile
                && MatchesTarget(m, obj));

            // Nearest by Combat's own range semantics — IsInSight first,
            // then distance. Reusing the game's comparer keeps targeting
            // consistent with what a manual-clicking player would pick.
            List<Monster> list = [.. alive];
            if (list.Count == 0)
            {
                return null;
            }

            list.Sort(new TargetDistanceComparer(Entity.mainPlayer));
            return list[0];
        }

        // Is this monster a valid target for the objective? PERMISSIVE UNION of
        // every target signal we have, so it works across servers:
        //   • the chain entry's `mon` hint (catalog ids or name substrings), and
        //   • the client objective's RefArray MonIDs (what live AE itself marks
        //     as the target, when it ships them client-side).
        // A mob matching EITHER counts. Only when NO signal exists at all do we
        // fall back to "any hostile" (so an unmapped collect objective still
        // grinds the room and lets the server credit whatever it credits).
        private bool MatchesTarget(Monster m, QuestTurninItem obj)
        {
            // Per-objective targets from the quest DB beat the entry-wide `mon`
            // hint: a farm quest like "Manticore of Infinity" needs Infinity
            // Manticores (R4) for one objective and Earth Elementals (R2/R3)
            // for the other, and an entry-wide filter would keep killing the
            // wrong one for whichever objective is current.
            List<int> objectiveMons = ObjectiveMonsters(obj);
            if (objectiveMons != null)
            {
                return objectiveMons.Contains(m.ID);
            }

            bool hasSignal = false;

            if (TargetMons.Count > 0)
            {
                hasSignal = true;
                string name = m.Name ?? "";
                foreach (string tok in TargetMons)
                {
                    if (int.TryParse(tok, out int id))
                    {
                        if (m.ID == id)
                        {
                            return true;
                        }
                    }
                    else if (name.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            if (obj != null && obj.QOType == QuestObjectiveType.Killcount && obj.RefArray != null)
            {
                foreach (string r in obj.RefArray)
                {
                    if (int.TryParse(r, out int id))
                    {
                        hasSignal = true;
                        if (m.ID == id)
                        {
                            return true;
                        }
                    }
                }
            }

            return !hasSignal;   // no target signal → any hostile is fair game
        }

        // Find a MapGoToCell pad (the in-world cell-transition trigger)
        // whose TargetCell matches the given target frame, case-insensitive.
        // Returns the pad's GameObject so we can read transform.position and
        // walk the player onto it; null if no such pad exists in the current
        // scene (e.g. wrong area, or pad gated by a quest we haven't done).
        private static UnityEngine.GameObject FindGotoPad(string targetFrame)
        {
            if (string.IsNullOrEmpty(targetFrame))
            {
                return null;
            }

            try
            {
                MapGoToCell[] pads = UnityEngine.Object.FindObjectsByType<MapGoToCell>(
                    UnityEngine.FindObjectsSortMode.None);
                foreach (MapGoToCell p in pads)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    if (string.Equals(p.TargetCell ?? "", targetFrame, StringComparison.OrdinalIgnoreCase))
                    {
                        return p.gameObject;
                    }
                }
            }
            catch { }
            return null;
        }

        private void EnsureAutoskillsOn()
        {
            if (!BeyondAgentClass.autoskillsActive)
            {
                BeyondAgentClass.autoskillsActive = true;
            }
        }

        private void StopAutoskills()
        {
            // Restore user's autoskills toggle to whatever they had before
            // we started. If they had it off, leave it off — they shouldn't
            // discover the bot left their character spinning.
            BeyondAgentClass.autoskillsActive = _autoskillsWasOn;
        }

        private void EnterState(RunState s)
        {
            bool wasRunning = IsRunning;
            State = s;
            _stateEnteredAt = Time.time;
            Log($"[state] {s}");
            if (s == RunState.Hunting)
            {
                _lastProgressAt = Time.time;
                _lastKillAt = Time.time;
                _lastProgressSum = CurrentStep is { isHunt: true } hs
                    ? ItemGoals.Count(hs.goalItem)
                    : SumObjectiveProgress(Quest.Get(QuestID));
                _watchedTarget = null;
                _huntPathing = false;
                _interactTarget = null;
                _apopNpc = null;
                _interactClickedAt = -1f;
                _sweepFrame = null;
                _navFrame = null;
                _interactQoid = -1;
                _csQoid = -1;
                _csSentAt = -1f;
                _interactVisited.Clear();
                _path?.Cancel();
            }
            if (s == RunState.TurningIn)
            {
                _turnInTferSent = false;
                _navFrame = null;   // let GoToFrame re-issue for the turn-in cell
            }
            if (s == RunState.Traveling)
            {
                _traveSent = false;
                _tferSent = false;
                _cellHopBudgetReset = false;
                _areaFirstMatchedAt = -1f;
                _frameFirstMatchedAt = -1f;
                _pickedGotoPad = null;
                _sawJoinCutscene = false;
                _walkIssued = false;
                _pathDoneAt = -1f;
                _path?.Cancel();
            }

            if (wasRunning && !IsRunning && ChainEntries != null)
            {
                ChatUtils.SendChatAndLog("Chain Stopped", name: "System", channel: "Admin");
            }
            if (!IsRunning)
            {
                IsPaused = false;
                if (wasRunning)
                {
                    DropFilterEngine.SetRunnerKeep([]);
                }
            }
            if (wasRunning != IsRunning)
            {
                OnProgressChanged?.Invoke();
            }
        }

        private float StateAge()
        {
            return Time.time - _stateEnteredAt;
        }

        private void Fail(string why)
        {
            LastError = why;
            if (IsFarming && State is not (RunState.Buying or RunState.LoadingShop or RunState.DiscoveringSources)
                && _farmFailures < MaxFarmRetries)
            {
                // Restart the current farm entry from its travel step after a
                // pause (the spam backoff gates every state, so reuse it).
                _farmFailures++;
                Log($"[farm] {why} — retrying in {FarmRetryDelaySec:0}s ({_farmFailures}/{MaxFarmRetries})");
                StopAutoskills();
                QuestChains.Entry cur = ChainEntries[ChainIndex];
                BindEntry(cur.qid, cur.items, cur.area, cur.frame, cur.pad, cur.mons);
                _spamBackoffUntil = Time.time + FarmRetryDelaySec;
                EnterState(RunState.Traveling);
                return;
            }
            StatusLine = $"FAIL: {why}";
            Log($"[fail] {why}");
            StopAutoskills();
            EnterState(RunState.Failed);
        }

        private void Log(string line)
        {
            try { OnLog?.Invoke(line); } catch { }
            BeyondLog.Msg($"[QuestRunner] {line}");
        }

        private static readonly System.Reflection.FieldInfo _mapLoaderField =
            typeof(Area).GetField("mapLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static readonly System.Reflection.FieldInfo _questsCompleteField =
            typeof(PlayerQuestData).GetField("questsComplete", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private float _lastLoadingLogAt = 0f;
        private string _lastLoadingReason = "";

        private bool IsQuestListLoaded(int id)
        {
            if (Entity.mainPlayer?.Quests == null)
            {
                return false;
            }

            try
            {
                System.Collections.IList list = _questsCompleteField?.GetValue(Entity.mainPlayer.Quests) as System.Collections.IList;
                if (list != null && list.Count > (id >> 3))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        private string GetLoadingReason()
        {
            if (Entity.mainPlayer == null)
            {
                return "waiting for mainPlayer entity…";
            }
            if (Area.currentArea == null)
            {
                return "waiting for currentArea initialization…";
            }

            if (UILoader.Instance != null && UILoader.Instance.gameObject != null && UILoader.Instance.gameObject.activeInHierarchy)
            {
                return "waiting for loading screen to clear…";
            }

            try
            {
                if (_mapLoaderField != null)
                {
                    AssetBundleLoader loader = _mapLoaderField.GetValue(Area.currentArea) as AssetBundleLoader;
                    if (loader != null && !loader.IsDone)
                    {
                        return $"loading map asset bundles ({Mathf.RoundToInt(loader.GetProgress() * 100)}%)…";
                    }
                }
            }
            catch { }

            if (QuestID > 0 && !IsQuestListLoaded(QuestID))
            {
                return "waiting for completed quest list synchronization…";
            }

            return null;
        }

        private bool IsMapLoading()
        {
            string reason = GetLoadingReason();
            if (reason != null)
            {
                StatusLine = reason;
                if (Time.time - _lastLoadingLogAt > 4f || reason != _lastLoadingReason)
                {
                    Log($"  [waiting] {reason}");
                    _lastLoadingLogAt = Time.time;
                    _lastLoadingReason = reason;
                }
                return true;
            }

            if (_lastLoadingReason != "")
            {
                Log("  [waiting] map and data load complete, preparing next steps");
                _lastLoadingReason = "";
            }
            return false;
        }
    }
}
