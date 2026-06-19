using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BeyondAgent.Util
{
    // Cluster of tiny "always on" view toggles surfaced as icon buttons next
    // to the main `!` HUD icon: hide UI, hide other players / monsters / NPCs,
    // rotate the skill bar to vertical. Each toggle stashes whatever original
    // state it touches so flipping off cleanly restores.
    //
    // Tick() is called from TestMod.OnUpdate and re-applies on a throttle so
    // newly-spawned entities / canvases get picked up without per-frame cost.
    //
    // Entity-hide pivots off MonoBehaviour markers that sit on each entity's
    // GameObject rather than the C# `Entity`/`Player`/`Monster` classes (which
    // are plain System.Object and reject Resources.FindObjectsOfTypeAll):
    //   - PlayerAnimationControl → player rigs
    //   - MonsterAnimationControl → monster rigs (including NPCs)
    //   - NPCQuestHead → quest-giver overlay; used to split monsters vs NPCs
    public static class HudToggles
    {
        public static bool HideUI;
        public static bool HideOtherPlayers;
        public static bool HideMonsters;
        public static bool HideNPCs;
        public static bool VerticalSkillBar;

        // Throttle: refresh every N frames while any "hide" toggle is on.
        // 5 frames ≈ ~83ms at 60fps — tight enough to catch transient combat
        // VFX/projectiles spawned during a skill cast, still cheap (one scene
        // scan of a few hundred renderers per refresh).
        private const int RefreshFrames = 5;

        private static int _frameCounter;

        // Reflection handles for the auxiliary GameObjects that hang off each
        // Entity (pet, nameplate, popups). Resolved once at first use because
        // these names are stable across game versions we target; if any go
        // missing we just skip that field rather than erroring the whole tick.
        // namePlate / petGO / monTransformGO are plain fields; auraPopUp and
        // popup are property-backed (we noticed `<...>k__BackingField` in the
        // dump), so they're addressable via field too.
        private static readonly string[] EntityAuxFieldNames =
        [
            // The main visual rig — turns out MonsterAnimationControl and
            // PlayerAnimationControl often sit on a controller GameObject
            // that isn't the same as the rig itself. eGO (entity GO) and
            // avtGO (avatar GO) are the authoritative roots the game uses
            // for the entity's visuals, so walking renderers from these
            // catches the actual sprites.
            "eGO",
            "avtGO",
            "namePlate",
            "petGO",
            "<auraPopUp>k__BackingField",
            "<popup>k__BackingField",
            "monTransformGO",
        ];

        private static FieldInfo[] _entityAuxFields;
        private static bool _auxFieldsResolved;

        private static FieldInfo[] ResolveAuxFields()
        {
            if (_auxFieldsResolved)
            {
                return _entityAuxFields;
            }

            _auxFieldsResolved = true;
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            List<FieldInfo> list = [];
            foreach (string name in EntityAuxFieldNames)
            {
                FieldInfo f = typeof(Entity).GetField(name, Flags);
                if (f != null)
                {
                    list.Add(f);
                }
                else
                {
                    BeyondLog.Warning($"[HudToggles] Entity field not found: {name}");
                }
            }
            _entityAuxFields = [.. list];
            return _entityAuxFields;
        }

        // --- Hide UI ----------------------------------------------------------
        // Stash + disable every scene Canvas. IMGUI (our mod menu + the `!`
        // button) bypasses Canvas so our own UI stays visible — by design,
        // otherwise you can't untoggle.
        private static readonly Dictionary<Canvas, bool> _canvasOriginalEnabled = [];

        private static void RefreshHideUI()
        {
            try
            {
                Canvas[] all = Resources.FindObjectsOfTypeAll<Canvas>();
                foreach (Canvas c in all)
                {
                    if (c == null)
                    {
                        continue;
                    }
                    // Scene-only: prefabs in Resources have an invalid scene
                    // handle. Disabling those would write into the prefab
                    // template and persist across the toggle's lifetime.
                    if (!c.gameObject.scene.IsValid())
                    {
                        continue;
                    }

                    if (_canvasOriginalEnabled.ContainsKey(c))
                    {
                        continue;
                    }

                    _canvasOriginalEnabled[c] = c.enabled;
                    c.enabled = false;
                }
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[HudToggles] RefreshHideUI: {ex.Message}");
            }
        }

        private static void RestoreHideUI()
        {
            foreach (KeyValuePair<Canvas, bool> kv in _canvasOriginalEnabled)
            {
                kv.Key?.enabled = kv.Value;
            }
            _canvasOriginalEnabled.Clear();
        }

        // --- Entity-hide ------------------------------------------------------
        // Renderer / Canvas -> original enabled. Rebuilt every refresh from
        // whichever toggles are currently on so flipping any toggle off
        // restores its visuals while leaving the others hidden. The Canvas
        // map here is separate from the Hide-UI one (_canvasOriginalEnabled)
        // so the two toggle restorations don't fight over the same keys.
        private static readonly Dictionary<Renderer, bool> _rendererOriginal = [];

        private static readonly Dictionary<Canvas, bool> _entityAuxCanvasOriginal = [];

        private static void RefreshEntityHide()
        {
            try
            {
                // Restore everything first, then re-hide based on current toggles.
                // Simpler than per-toggle bookkeeping and lets us cope with rigs
                // being recreated (area change, costume swap) — old refs become
                // null and get dropped naturally.
                foreach (KeyValuePair<Renderer, bool> kv in _rendererOriginal)
                {
                    kv.Key?.enabled = kv.Value;
                }
                _rendererOriginal.Clear();
                foreach (KeyValuePair<Canvas, bool> kv in _entityAuxCanvasOriginal)
                {
                    kv.Key?.enabled = kv.Value;
                }
                _entityAuxCanvasOriginal.Clear();

                if (HideOtherPlayers)
                {
                    HideOtherPlayersImpl();
                }

                if (HideMonsters)
                {
                    HideMonstersImpl();
                }

                if (HideNPCs)
                {
                    HideNPCsImpl();
                }
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[HudToggles] RefreshEntityHide: {ex.Message}");
            }
        }

        private static void HideOtherPlayersImpl()
        {
            object mainPlayer = null;
            try { mainPlayer = Entity.mainPlayer; } catch { }

            PlayerAnimationControl[] ctrls = Resources.FindObjectsOfTypeAll<PlayerAnimationControl>();
            foreach (PlayerAnimationControl ctrl in ctrls)
            {
                if (ctrl == null)
                {
                    continue;
                }

                if (!ctrl.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (mainPlayer != null && ReferenceEquals(ctrl.character, mainPlayer))
                {
                    continue;
                }

                HideRenderersUnder(ctrl.gameObject);
                HideEntityAuxiliaries(ctrl.character);
            }
        }

        private static int _diagFrameAccum;

        private static void HideMonstersImpl()
        {
            // Earlier I tried to skip NPCs here by detecting NPCQuestHead on
            // the rig, but that filter caught *every* MAC in the scene and
            // M ended up no-op. NPCs share the MonsterAnimationControl rig
            // so M now hides them too — that's an accepted trade-off; N can
            // still hide NPCs independently when M is off.
            MonsterAnimationControl[] ctrls = Resources.FindObjectsOfTypeAll<MonsterAnimationControl>();
            int total = 0, hit = 0;
            foreach (MonsterAnimationControl ctrl in ctrls)
            {
                total++;
                if (ctrl == null)
                {
                    continue;
                }

                if (!ctrl.gameObject.scene.IsValid())
                {
                    continue;
                }

                hit++;
                HideRenderersUnder(ctrl.gameObject);
                HideEntityAuxiliaries(ctrl.character);
            }
            DiagLog($"HideMonsters: MACs total={total} inScene={hit}");
        }

        private static void HideNPCsImpl()
        {
            NPCQuestHead[] heads = Resources.FindObjectsOfTypeAll<NPCQuestHead>();
            int total = 0, inScene = 0, uiSkipped = 0, eacHits = 0, fallback = 0;
            foreach (NPCQuestHead head in heads)
            {
                total++;
                if (head == null)
                {
                    continue;
                }

                if (!head.gameObject.scene.IsValid())
                {
                    continue;
                }

                inScene++;
                // NPCQuestHead also lives in the HUD (over the player
                // portrait). Anything with a Canvas ancestor is UI — skip
                // so we don't black out the player's own HUD avatar.
                if (head.GetComponentInParent<Canvas>() != null) { uiSkipped++; continue; }

                // Use the base EntityAnimationControl so we catch both
                // monster-style and player-style NPC rigs (Yulgar et al
                // may be tagged as "Player" internally). Fallback when
                // there's no EAC: walk one level up from the quest head,
                // which is usually a child marker pinned above the rig
                // root. The previous topmost-ancestor walk hit the scene
                // root and blacked out the whole world.
                EntityAnimationControl eac = head.GetComponentInParent<EntityAnimationControl>();
                GameObject rigGo;
                object character = null;
                if (eac != null)
                {
                    rigGo = eac.gameObject;
                    character = eac.character;
                    eacHits++;
                }
                else
                {
                    rigGo = head.transform.parent != null
                        ? head.transform.parent.gameObject
                        : head.gameObject;
                    fallback++;
                }
                HideRenderersUnder(rigGo);
                if (character != null)
                {
                    HideEntityAuxiliaries(character);
                }
            }
            DiagLog($"HideNPCs: heads total={total} inScene={inScene} uiSkipped={uiSkipped} eacHits={eacHits} fallback={fallback}");
        }

        // Logs at most once per ~1s of toggle activity so we can see what the
        // scene scan is finding without flooding the the standalone launcher console.
        private static void DiagLog(string msg)
        {
            _diagFrameAccum++;
            if (_diagFrameAccum < 60)
            {
                return;
            }

            _diagFrameAccum = 0;
            BeyondLog.Msg($"[HudToggles] {msg}");
        }

        // Walks the Entity's auxiliary GameObject fields (pet, nameplate,
        // popups, mon-transform rig) and hides the renderers under each.
        // Chat bubbles parent to the nameplate per the game's UIChatActions
        // logic, so killing the nameplate also kills incoming bubbles for
        // hidden players — handy side effect.
        private static void HideEntityAuxiliaries(object entity)
        {
            if (entity == null)
            {
                return;
            }

            FieldInfo[] fields = ResolveAuxFields();
            foreach (FieldInfo f in fields)
            {
                GameObject go;
                try { go = f.GetValue(entity) as GameObject; }
                catch { continue; }
                if (go == null)
                {
                    continue;
                }

                HideRenderersUnder(go);
                // Nameplates render through Canvas, not Renderer. Disable any
                // Canvases under the aux GO too — sprite-based popups also
                // sometimes wrap their visuals in a Canvas for sorting.
                foreach (Canvas c in go.GetComponentsInChildren<Canvas>(includeInactive: true))
                {
                    if (c == null)
                    {
                        continue;
                    }

                    if (_entityAuxCanvasOriginal.ContainsKey(c))
                    {
                        continue;
                    }

                    _entityAuxCanvasOriginal[c] = c.enabled;
                    c.enabled = false;
                }
            }
        }

        private static void HideRenderersUnder(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (Renderer r in renderers)
            {
                if (r == null)
                {
                    continue;
                }

                if (_rendererOriginal.ContainsKey(r))
                {
                    continue;
                }

                _rendererOriginal[r] = r.enabled;
                r.enabled = false;
            }
        }

        // --- Vertical skill bar -----------------------------------------------
        // Rotate UISkillSlots root 90° so the bar stands vertically, docked
        // to the bottom-right corner of its canvas. We rotate around the
        // bar's bottom-left pivot point: with anchor pinned to canvas
        // bottom-right (1,0), the rotated column extends up-and-left along
        // the right edge, so the bottom of the stack lands near where the
        // HP bar lives (familiar muscle-memory zone). Children are
        // counter-rotated so icons and cooldown numbers stay upright.
        private const float VerticalBarMarginX = 24f;

        private const float VerticalBarMarginY = 24f;

        private static Quaternion _skillsOriginalRotation;
        private static Vector2 _skillsOriginalPivot;
        private static Vector2 _skillsOriginalAnchorMin;
        private static Vector2 _skillsOriginalAnchorMax;
        private static Vector2 _skillsOriginalAnchoredPos;
        private static bool _skillsRotated;
        private static readonly List<Transform> _counterRotatedChildren = [];

        public static void ApplyVerticalSkills()
        {
            try
            {
                if (UISkillSlots.Instance == null)
                {
                    return;
                }

                Transform root = UISkillSlots.Instance.transform;
                RectTransform rect = root as RectTransform;

                if (VerticalSkillBar && !_skillsRotated)
                {
                    _skillsOriginalRotation = root.localRotation;

                    if (rect != null)
                    {
                        _skillsOriginalPivot = rect.pivot;
                        _skillsOriginalAnchorMin = rect.anchorMin;
                        _skillsOriginalAnchorMax = rect.anchorMax;
                        _skillsOriginalAnchoredPos = rect.anchoredPosition;

                        // Re-anchor to canvas bottom-right and pivot to the
                        // bar's bottom-left corner. Rotating 90° CCW around
                        // that pivot maps the bar's local +X axis (its
                        // horizontal extent) to screen +Y, so the column
                        // rises from the pivot upward. Negative anchoredPos
                        // pulls it inset from the canvas edge.
                        // Anchor to canvas bottom-right, pivot at bar's
                        // bottom-RIGHT. Rotation of -90° (CW) around that
                        // pivot maps the bar's original left edge (skill 1)
                        // upward and keeps skill 6 near the pivot. Inset
                        // x by marginX + bar height (which becomes the
                        // column's screen-space width after rotation) so
                        // the column sits *inside* the right edge.
                        rect.anchorMin = new Vector2(1f, 0f);
                        rect.anchorMax = new Vector2(1f, 0f);
                        rect.pivot = new Vector2(1f, 0f);
                        float barHeight = rect.rect.size.y;
                        rect.anchoredPosition = new Vector2(-VerticalBarMarginX - barHeight, VerticalBarMarginY);
                    }

                    root.localRotation = Quaternion.Euler(0, 0, -90);

                    _counterRotatedChildren.Clear();
                    foreach (Transform child in root)
                    {
                        child.localRotation = Quaternion.Euler(0, 0, 90);
                        _counterRotatedChildren.Add(child);
                    }
                    _skillsRotated = true;
                }
                else if (!VerticalSkillBar && _skillsRotated)
                {
                    root.localRotation = _skillsOriginalRotation;
                    if (rect != null)
                    {
                        rect.anchorMin = _skillsOriginalAnchorMin;
                        rect.anchorMax = _skillsOriginalAnchorMax;
                        rect.pivot = _skillsOriginalPivot;
                        rect.anchoredPosition = _skillsOriginalAnchoredPos;
                    }
                    foreach (Transform c in _counterRotatedChildren)
                    {
                        c?.localRotation = Quaternion.identity;
                    }
                    _counterRotatedChildren.Clear();
                    _skillsRotated = false;
                }
            }
            catch (Exception ex)
            {
                BeyondLog.Error($"[HudToggles] ApplyVerticalSkills: {ex.Message}");
            }
        }

        public static void Tick()
        {
            ApplyVerticalSkills();

            bool anyHideOn = HideUI || HideOtherPlayers || HideMonsters || HideNPCs;
            bool hadHidden = _canvasOriginalEnabled.Count > 0
                          || _rendererOriginal.Count > 0
                          || _entityAuxCanvasOriginal.Count > 0;

            if (!anyHideOn && hadHidden)
            {
                if (_canvasOriginalEnabled.Count > 0)
                {
                    RestoreHideUI();
                }

                if (_rendererOriginal.Count > 0)
                {
                    foreach (KeyValuePair<Renderer, bool> kv in _rendererOriginal)
                    {
                        kv.Key?.enabled = kv.Value;
                    }

                    _rendererOriginal.Clear();
                }
                if (_entityAuxCanvasOriginal.Count > 0)
                {
                    foreach (KeyValuePair<Canvas, bool> kv in _entityAuxCanvasOriginal)
                    {
                        kv.Key?.enabled = kv.Value;
                    }

                    _entityAuxCanvasOriginal.Clear();
                }
                _frameCounter = 0;
                return;
            }

            if (!anyHideOn)
            {
                return;
            }

            _frameCounter++;
            if (_frameCounter < RefreshFrames)
            {
                return;
            }

            _frameCounter = 0;

            if (HideUI)
            {
                RefreshHideUI();
            }
            else if (_canvasOriginalEnabled.Count > 0)
            {
                RestoreHideUI();
            }

            if (HideOtherPlayers || HideMonsters || HideNPCs
                || _rendererOriginal.Count > 0 || _entityAuxCanvasOriginal.Count > 0)
            {
                RefreshEntityHide();
            }
        }
    }
}