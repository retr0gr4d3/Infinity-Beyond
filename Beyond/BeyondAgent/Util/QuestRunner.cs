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
        public float InterIterCooldown = 1.5f;

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

        // --- public API ---

        public bool IsRunning =>
            State is not RunState.Idle and not RunState.Done and not RunState.Failed;

        public void Start(int questId, int iterations, string targetArea = "", string targetFrame = "", string targetPad = "Spawn", List<string> targetMons = null)
        {
            if (IsRunning)
            {
                return;
            }

            ChainEntries = null;
            ChainName = "";
            ChainIndex = 0;
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
            Deaths = 0;

            QuestChains.Entry first = ChainEntries[ChainIndex];
            BindEntry(first.qid, first.items, first.area, first.frame, first.pad, first.mons);
            _autoskillsWasOn = BeyondAgentClass.autoskillsActive;
            Log($"[start] chain '{chainName}' starting at {ChainIndex + 1}/{ChainEntries.Count}: {first}");
            ChatUtils.SendChatAndLog("Starting Chain", name: "System", channel: "Admin");
            EnterState(NeedsCellHop() ? RunState.Traveling : RunState.Accepting);
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
                TrackMovement();
                if (IsMapLoading())
                {
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
                BindEntry(next.qid, next.items, next.area, next.frame, next.pad, next.mons);
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
                string targetFrame = FindHostileFrame(nextObjective);
                if (!string.IsNullOrEmpty(targetFrame)
                    && !string.Equals(targetFrame, here, StringComparison.OrdinalIgnoreCase))
                {
                    GoToFrame(targetFrame, $"hunt '{nextObjective.Name}'");
                    return;
                }
                if (string.IsNullOrEmpty(targetFrame))
                {
                    // No matching hostile loaded anywhere — sweep cells (lazy
                    // maps) then fail with a hint if the map is genuinely dry.
                    TickCellSweep(here, $"hostile for '{nextObjective.Name}'",
                        () => PickBestHostile(nextObjective) != null || !string.IsNullOrEmpty(FindHostileFrame(nextObjective)));
                    return;
                }
                StatusLine = $"no hostile in {here} for '{nextObjective.Name}' — waiting to respawn";
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
            // Kills count as liveness: a probabilistic objective (e.g. the
            // bludrut key at a low drop rate) can take many kills with zero
            // "progress" — only silence on BOTH fronts means we're stuck.
            float lastActivity = Mathf.Max(_lastProgressAt, _lastKillAt);
            if (Time.time - lastActivity > HuntTimeoutNoProgress)
            {
                Fail($"no kills or objective progress in {HuntTimeoutNoProgress:0}s — quest likely needs a different zone, mob type, or interaction we don't handle yet");
                return;
            }
            if (Time.time - _lastProgressAt > HuntHardCapSec)
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

        // Complete a Cutscene objective (QOType 5, e.g. "Find the Bones" plays
        // cutscene 35). RefArray[0] is the cutscene/dialog id. Faithful path:
        // request the cutscene so the client plays it; our auto-skip patch ends
        // it, and Dialogger_Manager.EndPressed then auto-sends watchCutscene when
        // HasCutsceneObjective — crediting it, same as watching it in-world.
        // Fallback: if no credit lands, send watchCutscene directly (works on
        // our server; harmless if already credited).
        private void TickCutscene(QuestTurninItem obj)
        {
            SuppressAutoskills();

            if (obj.QOID != _csQoid)
            {
                _csQoid = obj.QOID;
                _csPhase = 0;
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

            if (_csPhase == 0)
            {
                // Force auto-skip so the cutscene ends by itself (a bot can't
                // press "End"), then trigger it. EndPressed auto-sends the
                // watchCutscene that credits the objective.
                BeyondAgentClass.autoSkipCutscenes = true;
                Log($"  [cutscene] triggering cutscene {csid} for '{obj.Name}'");
                try { AEC.Instance.sendRequest(new RequestGetCutscene(csid)); } catch { }
                _csSentAt = Time.time;
                _csPhase = 1;
            }
            else if (_csPhase == 1 && Time.time - _csSentAt > 6f)
            {
                Log($"  [cutscene] no credit after play — sending watchCutscene({csid}) directly");
                try { AEC.Instance.sendRequest(new RequestWatchCutscene(csid)); } catch { }
                _csSentAt = Time.time;
                _csPhase = 2;
            }
            else if (_csPhase == 2 && Time.time - _csSentAt > 6f)
            {
                // Retry the direct send once more, then let the hunt timeout
                // surface it if the server just won't credit.
                try { AEC.Instance.sendRequest(new RequestWatchCutscene(csid)); } catch { }
                _csSentAt = Time.time;
            }
            StatusLine = $"cutscene {csid} for '{obj.Name}' ({Time.time - _csSentAt:0.0}s)";
            CheckHuntTimeout();
        }
        private int _csQoid = -1;
        private int _csPhase;
        private float _csSentAt;

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

        // The frame of a cell holding a live hostile that matches this
        // objective's target (entry `mon` filter, else the Killcount RefArray
        // MonID, else any hostile). Every monster in Area.currentArea.Monsters
        // carries its .Frame — including those in cells we haven't loaded — so
        // this is a pure lookup, no walking. Returns the current frame if a
        // match is already here, else the most-populated matching frame, else
        // "" when nothing in the whole map matches.
        private string FindHostileFrame(QuestTurninItem obj)
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
                        || m.currentState == Entity.State.Dead
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
                _lastProgressSum = SumObjectiveProgress(Quest.Get(QuestID));
                _watchedTarget = null;
                _huntPathing = false;
                _interactTarget = null;
                _apopNpc = null;
                _interactClickedAt = -1f;
                _sweepFrame = null;
                _navFrame = null;
                _interactQoid = -1;
                _csQoid = -1;
                _csPhase = 0;
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
