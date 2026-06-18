using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace Infinity_TestMod.Util
{
    /// <summary>
    /// While the player is in combat, cycles random animation clips on the
    /// spoofed pet's Animator. The pet GO is just the monster prefab with
    /// Walk/FollowerGameObject bolted on, so its original Animator + clip
    /// list ride along.
    ///
    /// Why Playables, not Animator.Play(name): Animator.Play resolves by
    /// AnimatorController STATE name, not clip name. AQW prefabs frequently
    /// have state names that don't match their clip names, so Play silently
    /// no-ops and the rig freezes mid-pose. AnimationPlayableUtilities.PlayClip
    /// drives the Animator directly with a clip Playable, bypassing the
    /// state machine entirely — works for every clip regardless of naming.
    ///
    /// On combat-exit we destroy the graph and Rebind() so the controller's
    /// default state (idle/walk) takes over again.
    /// </summary>
    public static class PetCombatAnimDriver
    {
        private static GameObject _trackedPet;
        private static Animator _anim;
        private static List<AnimationClip> _clips;
        private static int _lastClipIdx = -1;
        private static float _nextPickTime = 0f;
        private static bool _wasInCombat = false;
        private static PlayableGraph _graph;
        private static bool _graphValid;

        public static void Tick()
        {
            if (!TestMod.petCombatAnimActive) { ResetIfTracking(); return; }
            if (Entity.mainPlayer == null) { ResetIfTracking(); return; }

            GameObject pet = Entity.mainPlayer.petGO;
            if (pet == null) { ResetIfTracking(); return; }

            if (pet != _trackedPet)
            {
                DestroyGraph();
                _trackedPet = pet;
                _anim = pet.GetComponentInChildren<Animator>();
                _clips = null;
                _lastClipIdx = -1;
                if (_anim != null && _anim.runtimeAnimatorController != null)
                {
                    _clips = new List<AnimationClip>();
                    foreach (AnimationClip c in _anim.runtimeAnimatorController.animationClips)
                    {
                        if (c == null || string.IsNullOrEmpty(c.name)) continue;
                        // Skip stun + death clips — they freeze the rig or
                        // play the death pose, which defeats the cycle.
                        string n = c.name.ToLowerInvariant();
                        if (n.Contains("stun") || n.Contains("death") || n.Contains("die"))
                            continue;
                        _clips.Add(c);
                    }
                }
                _wasInCombat = false;
            }

            if (_anim == null || _clips == null || _clips.Count == 0) return;

            bool inCombat = false;
            try { inCombat = Entity.mainPlayer.currentState == Entity.State.Combat; } catch { }

            if (inCombat)
            {
                if (!_wasInCombat || Time.time >= _nextPickTime)
                {
                    int idx = Random.Range(0, _clips.Count);
                    if (_clips.Count > 1 && idx == _lastClipIdx)
                        idx = (idx + 1) % _clips.Count;
                    _lastClipIdx = idx;
                    AnimationClip clip = _clips[idx];
                    PlayClip(clip);
                    // clip.length is 0 for static poses — hold for a fixed
                    // beat so we don't churn at frame rate.
                    float hold = clip.length > 0.05f ? clip.length : 0.5f;
                    _nextPickTime = Time.time + hold;
                }
                _wasInCombat = true;
            }
            else if (_wasInCombat)
            {
                DestroyGraph();
                try { _anim.Rebind(); } catch { }
                _wasInCombat = false;
                _nextPickTime = 0f;
            }
        }

        private static void PlayClip(AnimationClip clip)
        {
            try
            {
                DestroyGraph();
                AnimationPlayableUtilities.PlayClip(_anim, clip, out _graph);
                _graphValid = true;
            }
            catch (System.Exception ex)
            {
                BeyondLog.Warning($"[PetCombatAnim] PlayClip failed for '{clip.name}': {ex.Message}");
            }
        }

        private static void DestroyGraph()
        {
            if (!_graphValid) return;
            try { if (_graph.IsValid()) _graph.Destroy(); } catch { }
            _graphValid = false;
        }

        private static void ResetIfTracking()
        {
            if (_trackedPet == null && _anim == null && !_graphValid) return;
            DestroyGraph();
            if (_wasInCombat && _anim != null)
            {
                try { _anim.Rebind(); } catch { }
            }
            _trackedPet = null;
            _anim = null;
            _clips = null;
            _lastClipIdx = -1;
            _wasInCombat = false;
            _nextPickTime = 0f;
        }
    }
}
