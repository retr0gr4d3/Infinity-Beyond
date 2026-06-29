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

        // Chain mode: when ChainEntries is non-null, the runner walks it
        // sequentially. Each entry rebinds QuestID / TargetFrame / TargetPad
        // / Iterations and re-enters Accepting. ChainIndex tracks position;
        // ChainName is just for status display.
        public List<QuestChains.Entry> ChainEntries { get; private set; }
        public int ChainIndex { get; private set; }
        public string ChainName { get; private set; } = "";

        // Per-state timeouts. Halt-on-mismatch is the QA value prop.
        public float AcceptTimeout = 5f;
        // Cross-area tfer plays a join cutscene that runs ~15s before the
        // new area becomes joinable, plus actual load time. 25s leaves
        // headroom for slow loads. In-area moveToCell is instant, so the
        // 6s budget stays generous for stage 2.
        public float TferTimeout = 25f;
        // After Area.currentArea.Name first matches the target, the join
        // cutscene still plays for ~10-15s before the player can interact
        // (and before the server will accept quest-related requests in the
        // new zone). Hold in stage 1 for this many seconds after the first
        // name match, regardless of how fast the name actually flipped.
        public float PostTferSettleSec = 14f;
        // After moveToCell server-confirms the Frame change, the client still
        // animates the player walking from the old cell's exit pad to the new
        // cell's entry pad. Wyverns in the new frame become "out of range"
        // until the walk completes. Hold for this many seconds after the
        // Frame field matches before declaring arrival.
        public float PostCellHopSettleSec = 3f;
        // In-cell walk budget. Now that we walk-onto-pad instead of teleporting
        // via moveToCell, this needs to cover crossing the whole cell on foot.
        public float TravelTimeout = 15f;
        public float HuntTimeoutNoProgress = 90f;
        public float CompleteTimeout = 8f;
        // Brief delay between consecutive turn-ins / iterations so we don't
        // trip the server's per-action spam cooldown (observed: tryQuestComplete
        // back-to-back returns rNotify "Spam Detected").
        public float InterIterCooldown = 1.5f;

        // --- state ---
        public RunState State { get; private set; } = RunState.Idle;
        public int CurrentIteration { get; private set; }
        public string LastError { get; private set; } = "";
        public string StatusLine { get; private set; } = "idle";

        private float _stateEnteredAt;
        private float _lastProgressAt;
        private int _lastProgressSum;
        // Save autoskills state so we don't fight the user's manual toggle.
        private bool _autoskillsWasOn;

        // Logs are surfaced via this callback to the GUI's event list.
        public Action<string> OnLog;

        // --- public API ---

        public bool IsRunning =>
            State is not RunState.Idle and not RunState.Done and not RunState.Failed;

        public void Start(int questId, int iterations, string targetArea = "", string targetFrame = "", string targetPad = "Spawn")
        {
            if (IsRunning)
            {
                return;
            }

            ChainEntries = null;
            ChainName = "";
            ChainIndex = 0;
            BindEntry(questId, iterations, targetArea, targetFrame, targetPad);
            _autoskillsWasOn = BeyondAgentClass.autoskillsActive;
            string travelNote =
                !string.IsNullOrEmpty(TargetArea) ? $", tfer to {TargetArea}/{TargetFrame}/{TargetPad}" :
                !string.IsNullOrEmpty(TargetFrame) ? $", hop to {TargetFrame}/{TargetPad}" : "";
            Log($"[start] quest {questId} × {iterations}{travelNote}");

            // Travel first when a target is set — some quests are area-gated
            // server-side and reject acceptQuest from the wrong zone.
            EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
        }

        public void StartChain(string chainName, List<QuestChains.Entry> entries)
        {
            if (IsRunning)
            {
                return;
            }

            if (entries == null || entries.Count == 0)
            {
                Fail("chain is empty");
                return;
            }
            ChainEntries = entries;
            ChainName = chainName ?? "";
            ChainIndex = 0;

            QuestChains.Entry first = ChainEntries[ChainIndex];
            BindEntry(first.qid, first.items, first.area, first.frame, first.pad);
            _autoskillsWasOn = BeyondAgentClass.autoskillsActive;
            Log($"[start] chain '{chainName}' starting at {ChainIndex + 1}/{ChainEntries.Count}: {first}");
            ChatUtils.SendChatAndLog("Starting Chain", name: "System", channel: "Admin");
            EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
        }

        private void BindEntry(int qid, int items, string area, string frame, string pad)
        {
            QuestID = qid;
            Iterations = Math.Max(1, items);
            TargetArea = area ?? "";
            TargetFrame = frame ?? "";
            TargetPad = string.IsNullOrEmpty(pad) ? "Spawn" : pad;
            CurrentIteration = 0;
            LastError = "";
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            Log("[stop] user requested");
            StopAutoskills();
            EnterState(RunState.Idle);
        }

        // --- tick ---

        public void Tick()
        {
            if (!IsRunning)
            {
                return;
            }

            try
            {
                if (IsMapLoading())
                {
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

            if (ChainEntries != null && Entity.mainPlayer.Quests != null && Entity.mainPlayer.Quests.isQuestComplete(QuestID))
            {
                Log($"  quest {QuestID} is already completed, advancing/finishing");
                AdvanceChainOrFinish();
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
                Fail($"quest {QuestID} not in accepted list after {AcceptTimeout:0.0}s — likely a prereq or level gate");
            }
            else
            {
                StatusLine = $"waiting for accept… ({StateAge():0.0}s)";
            }
        }

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
            if (CurrentIteration < Iterations)
            {
                EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
                return;
            }
            AdvanceChainOrFinish();
        }

        private void AdvanceChainOrFinish()
        {
            if (ChainEntries != null && ChainIndex + 1 < ChainEntries.Count)
            {
                ChainIndex++;
                QuestChains.Entry next = ChainEntries[ChainIndex];
                BindEntry(next.qid, next.items, next.area, next.frame, next.pad);
                Log($"[chain] advancing to {ChainIndex + 1}/{ChainEntries.Count}: {next}");
                EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
            }
            else
            {
                Log(ChainEntries != null
                    ? $"[done] chain '{ChainName}' complete"
                    : "[done] all iterations complete");
                EnterState(RunState.Done);
            }
        }

        private void TickTravel()
        {
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
            // for ~10-15s during which acceptQuest gets dropped server-side.
            if (_tferSent)
            {
                if (_areaFirstMatchedAt < 0f)
                {
                    _areaFirstMatchedAt = Time.time;
                }

                float settled = Time.time - _areaFirstMatchedAt;
                if (settled < PostTferSettleSec)
                {
                    StatusLine = $"in {hereArea}, waiting for cutscene ({settled:0.0}/{PostTferSettleSec:0.0}s)";
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

            // Frame matches? Hold for PostCellHopSettleSec so the walk
            // animation between cells finishes — otherwise the player is
            // still spatially in the old cell when we enter Hunting and
            // every target shows "out of range" until the walk completes.
            if (string.IsNullOrEmpty(TargetFrame)
                || string.Equals(hereFrame, TargetFrame, StringComparison.OrdinalIgnoreCase))
            {
                if (_frameFirstMatchedAt < 0f)
                {
                    _frameFirstMatchedAt = Time.time;
                }

                float settled = Time.time - _frameFirstMatchedAt;
                if (!string.IsNullOrEmpty(TargetFrame) && settled < PostCellHopSettleSec)
                {
                    StatusLine = $"in {hereArea}/{hereFrame}, walking to pad ({settled:0.0}/{PostCellHopSettleSec:0.0}s)";
                    return;
                }
                Log($"  arrived at {hereArea}/{hereFrame}");
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
            if (_pickedGotoPad != null)
            {
                // Drive the walk each tick. WalkToward handles the
                // world→player-parent-local conversion the same way
                // ClickableWalk does for mouse clicks.
                WalkTowardWorld(_pickedGotoPad.transform.position);
                StatusLine = $"walking to Goto pad → {TargetFrame}  ({StateAge():0.0}s)";
            }
            else if (!_traveSent)
            {
                // Fallback: no pad found in the current scene (different
                // layout, or we're already in the cell but mis-detected) →
                // do the old server-side moveToCell.
                try
                {
                    AEC.Instance.sendRequest(new RequestMoveToCell(TargetFrame, TargetPad));
                    _traveSent = true;
                    Log($"  no Goto pad for '{TargetFrame}' — falling back to moveToCell({TargetFrame}, {TargetPad})");
                }
                catch (Exception ex)
                {
                    Fail($"moveToCell send failed: {ex.Message}");
                    return;
                }
            }
            if (StateAge() > TravelTimeout)
            {
                Fail($"didn't reach frame '{TargetFrame}' within {TravelTimeout:0.0}s (currently in '{hereFrame}') — wrong frame name or movement blocked");
                return;
            }
            StatusLine = $"traveling {hereFrame} → {TargetFrame}/{TargetPad}  ({StateAge():0.0}s)";
        }
        private bool _traveSent;
        private bool _tferSent;
        private bool _cellHopBudgetReset;
        // Time at which Area.currentArea.Name first matched TargetArea. Used
        // to enforce PostTferSettleSec so we don't try to acceptQuest mid-
        // cutscene. -1 = not matched yet this travel session.
        private float _areaFirstMatchedAt = -1f;
        // Time at which Entity.mainPlayer.Frame first matched TargetFrame.
        // Used to enforce PostCellHopSettleSec so we don't engage mid-walk.
        private float _frameFirstMatchedAt = -1f;
        // Resolved MapGoToCell pad GameObject we're walking toward in stage 2.
        // Reset each time we enter Traveling; null = haven't found one yet
        // / no pad exists for this target (will fall back to moveToCell).
        private UnityEngine.GameObject _pickedGotoPad;

        private void TickHunt()
        {
            Quest q = Quest.Get(QuestID);
            if (q == null)
            {
                Fail($"no quest def cached for {QuestID} — open the quest UI once to populate Quest.Get()");
                return;
            }

            // Find the first incomplete objective. If none, we're ready to
            // turn in. The quest def's Turnins are the source of truth here —
            // we don't try to interpret them, just check completion per QOID.
            QuestTurninItem nextObjective = NextIncompleteObjective(q);
            if (nextObjective == null)
            {
                StopAutoskills();
                Log("  all objectives complete; turning in");
                EnterState(RunState.TurningIn);
                return;
            }

            // Acquire / refresh target on each tick. The setter on
            // Entity.mainPlayer.target no-ops if the value is unchanged AND
            // refuses dead targets, so calling it every frame is cheap.
            Monster tgt = PickBestHostile(nextObjective);
            if (tgt == null)
            {
                StopAutoskills();
                StatusLine = $"no hostile mob in frame for objective '{nextObjective.Name}' ({nextObjective.QOType})";
                // Don't fail yet — frame might be transitioning, mobs respawning.
                CheckHuntTimeout();
                return;
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

            // (WalkToward removed — it broke combat by re-setting the
            //  player's targetPosition every tick, pulling the character
            //  out of attack range mid-swing. Cross-cell navigation needs
            //  to happen via Goto-pad walking before Hunting starts, not
            //  during it. Once we're in the same cell as the mob, Charge's
            //  built-in approach handles the final ~1m walk.)
            EnsureAutoskillsOn();

            int progressSum = SumObjectiveProgress(q);
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
            if (Time.time - _lastProgressAt > HuntTimeoutNoProgress)
            {
                Fail($"no objective progress in {HuntTimeoutNoProgress:0}s — quest likely needs a different zone, mob type, or interaction we don't handle yet");
            }
        }

        private void TickTurnIn()
        {
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
                    Fail($"server rate-limited turn-in: \"{msg}\" — bump InterIterCooldown");
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

        private Monster PickBestHostile(QuestTurninItem obj)
        {
            if (Area.currentArea == null || Entity.mainPlayer == null)
            {
                return null;
            }

            // Killcount objectives ref a MonID. For Turnin/item objectives
            // we can't reliably resolve "which mob drops this item" without
            // server-side loot tables we don't have access to here, so we
            // fall back to "any hostile in frame" — works for the common
            // case where the player parked next to the right respawn point.
            int? requiredMonId = null;
            if (obj.QOType == QuestObjectiveType.Killcount && obj.RefArray?.Length > 0
                && int.TryParse(obj.RefArray[0], out int parsed))
            {
                requiredMonId = parsed;
            }

            // Sticky targeting: if our current target is still alive, hostile,
            // matches the MonID, and is in the player's frame, keep it. Avoids
            // the bot oscillating between two equally-close mobs when their
            // positions shift slightly each tick (which broke combat against
            // the pair of water draconians flanking the player).
            string playerFrameForStick = Entity.mainPlayer.Frame ?? "";
            if (Entity.mainPlayer.target is Monster current
                && current != null
                && current.currentState != Entity.State.Dead
                && current.reactionType == Entity.ReactionType.Hostile
                && (requiredMonId == null || current.ID == requiredMonId.Value)
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
                && (requiredMonId == null || m.ID == requiredMonId.Value));

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

        // Cached reflection handle for EntityMovementUpdater.targetPosition.
        // Resolved lazily on first WalkToward call — public field on most
        // entity controllers; we probe via reflection so we don't need a
        // hard reference to the game's internal mover type.
        private static System.Reflection.FieldInfo _moverTargetPosField;
        private static bool _moverProbed;

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

        // Cached MethodInfo for Player.WalkVector(Vector2). It's `internal`
        // so we can't call it directly from another assembly — reflection
        // lets us. WalkVector is the canonical "walk to a localPosition"
        // method: it sets eMover.targetPosition AND MovementController.
        // Direction (the unit vector toward the target), without which the
        // mover knows where to go but has no walking vector.
        private static System.Reflection.MethodInfo _walkVectorMethod;
        private static bool _walkVectorProbed;

        // Walk toward a WORLD-space point. Converts to the local space of
        // the player's parent transform — same conversion ClickableWalk
        // does for mouse-click-to-walk (see decomp ClickableWalk.cs:69).
        // Without this conversion, WalkVector's direction vector points
        // nowhere useful and the character sits still.
        private void WalkTowardWorld(UnityEngine.Vector3 worldPos)
        {
            try
            {
                if (Entity.mainPlayer == null)
                {
                    return;
                }

                EnsureWalkVectorProbed();
                if (_walkVectorMethod == null)
                {
                    return;
                }

                GameObject playerGO = Entity.mainPlayer.getGameObject();
                if (playerGO == null || playerGO.transform.parent == null)
                {
                    return;
                }

                UnityEngine.Vector3 local = playerGO.transform.parent.InverseTransformPoint(worldPos);
                Vector2 v = new(local.x, local.y);
                _walkVectorMethod.Invoke(Entity.mainPlayer, [v]);
            }
            catch (Exception ex)
            {
                Log($"  WalkTowardWorld error: {ex.Message}");
            }
        }

        private void EnsureWalkVectorProbed()
        {
            if (_walkVectorProbed)
            {
                return;
            }

            _walkVectorProbed = true;
            try
            {
                _walkVectorMethod = typeof(Player).GetMethod("WalkVector",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    types: [typeof(Vector2)],
                    modifiers: null);
            }
            catch { }
            Log(_walkVectorMethod != null
                ? "  walker: Player.WalkVector(Vector2)"
                : "  walker: Player.WalkVector(Vector2) not found — walk disabled");
        }

        private void WalkToward(Entity tgt)
        {
            try
            {
                GameObject playerGO = Entity.mainPlayer?.getGameObject();
                GameObject targetGO = tgt?.getGameObject();
                if (playerGO == null || targetGO == null)
                {
                    return;
                }

                // Find the movement updater component on the player. The
                // exact type may be ClientMovementController, EntityMovement-
                // Updater, etc. — anything with a "targetPosition" field.
                if (!_moverProbed)
                {
                    foreach (Component comp in playerGO.GetComponents<UnityEngine.Component>())
                    {
                        if (comp == null)
                        {
                            continue;
                        }

                        FieldInfo f = comp.GetType().GetField("targetPosition",
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance);
                        if (f != null && f.FieldType == typeof(UnityEngine.Vector3))
                        {
                            _moverTargetPosField = f;
                            _moverCompType = comp.GetType();
                            break;
                        }
                    }
                    _moverProbed = true;
                    Log(_moverTargetPosField != null
                        ? $"  walker: {_moverCompType.Name}.targetPosition"
                        : "  walker: no targetPosition field found — walk disabled");
                }
                if (_moverTargetPosField == null)
                {
                    return;
                }

                Component mover = playerGO.GetComponent(_moverCompType);
                if (mover == null)
                {
                    return;
                }

                _moverTargetPosField.SetValue(mover, targetGO.transform.position);
            }
            catch (Exception ex)
            {
                Log($"  WalkToward error: {ex.Message}");
            }
        }
        private static System.Type _moverCompType;

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
                _lastProgressSum = SumObjectiveProgress(Quest.Get(QuestID));
            }
            if (s == RunState.Traveling)
            {
                _traveSent = false;
                _tferSent = false;
                _cellHopBudgetReset = false;
                _areaFirstMatchedAt = -1f;
                _frameFirstMatchedAt = -1f;
                _pickedGotoPad = null;
            }

            if (wasRunning && !IsRunning && ChainEntries != null)
            {
                ChatUtils.SendChatAndLog("Chain Stopped", name: "System", channel: "Admin");
            }
        }

        private float StateAge()
        {
            return Time.time - _stateEnteredAt;
        }

        private void Fail(string why)
        {
            LastError = why;
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
