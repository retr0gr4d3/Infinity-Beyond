using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BeyondAgent.Util
{
    // Scales the gameplay camera's orthographic size by a multiplier.
    //
    // The camera we must zoom is Camera.main — that is what renders the world
    // and what HUDManager.Update mirrors onto the UI camera every frame
    // (UICamera.orthographicSize = Camera.main.orthographicSize). Zooming any
    // other CameraFollow instance has no visible effect: the view stays put
    // and HUDManager keeps the UI pinned to the unchanged main size. So we
    // resolve the CameraFollow whose private cam == Camera.main and, failing
    // that, fall back to zooming Camera.main directly.
    //
    // We keep CameraFollow's private camHalfHeight/Width in sync when we have
    // a matching follow because the game's LateUpdate uses them to clamp the
    // camera inside the area's BoxCollider confiner — stale half-extents would
    // let the camera drift past the room edge after zooming out. cam,
    // camHalfHeight, and camHalfWidth are all private, so that path is
    // reflection. Field handles resolve once in the static constructor; if a
    // future game version renames them we log once and the confiner sync is
    // skipped (the zoom itself still applies via Camera.main).
    //
    // Logging goes through UnityEngine.Debug, not BeyondLog: this build no
    // longer runs under the standalone launcher, so BeyondLog may be uninitialized.
    public static class CameraZoom
    {
        public const float Min = 0.5f;
        public const float Max = 3.0f;
        public const float Default = 1.0f;

        public static float Multiplier = Default;

        private static readonly FieldInfo _fCam;
        private static readonly FieldInfo _fHalfH;
        private static readonly FieldInfo _fHalfW;
        private static readonly bool _fieldsResolved;

        private static Camera _trackedCam;
        private static float _originalOrthoSize;
        private static float _originalFov;
        private static float _lastSetSize = float.NaN;
        private static float _loggedMult = float.NaN;

        static CameraZoom()
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            _fCam = typeof(CameraFollow).GetField("cam", Flags);
            _fHalfH = typeof(CameraFollow).GetField("camHalfHeight", Flags);
            _fHalfW = typeof(CameraFollow).GetField("camHalfWidth", Flags);

            List<string> missing = [];
            if (_fCam == null)
            {
                missing.Add("cam");
            }

            if (_fHalfH == null)
            {
                missing.Add("camHalfHeight");
            }

            if (_fHalfW == null)
            {
                missing.Add("camHalfWidth");
            }

            _fieldsResolved = missing.Count == 0;
            if (!_fieldsResolved)
            {
                Debug.LogWarning($"[CameraZoom] CameraFollow missing private field(s): {string.Join(", ", missing)}. Confiner sync disabled; zoom still applies to Camera.main.");
            }
        }

        public static void Apply()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    return;
                }

                // Find the CameraFollow that drives the main camera so we can
                // keep its confiner half-extents in sync. Null is fine — we
                // still zoom Camera.main, just without the clamp update.
                CameraFollow follow = ResolveFollow(cam);

                if (cam != _trackedCam)
                {
                    _trackedCam = cam;
                    _originalOrthoSize = cam.orthographicSize;
                    _originalFov = cam.fieldOfView;
                    _lastSetSize = float.NaN;
                }

                Multiplier = Mathf.Clamp(Multiplier, Min, Max);
                if (cam.orthographic)
                {
                    // Re-capture the base size whenever the game changed it out
                    // from under us (MapCell.InitAsync, cutscenes): if the
                    // current size no longer matches what we last wrote, treat
                    // it as a fresh game-chosen baseline to scale from.
                    if (float.IsNaN(_lastSetSize) ||
                        !Mathf.Approximately(cam.orthographicSize, _lastSetSize))
                    {
                        _originalOrthoSize = cam.orthographicSize;
                    }
                    float size = _originalOrthoSize * Multiplier;
                    cam.orthographicSize = size;
                    _lastSetSize = size;
                    if (follow != null && _fieldsResolved)
                    {
                        _fHalfH.SetValue(follow, size);
                        _fHalfW.SetValue(follow, size * cam.aspect);
                    }
                }
                else
                {
                    cam.fieldOfView = Mathf.Clamp(_originalFov * Multiplier, 1f, 179f);
                }

                if (!Mathf.Approximately(Multiplier, _loggedMult))
                {
                    _loggedMult = Multiplier;
                    Debug.Log($"[CameraZoom] mult={Multiplier:0.00} cam='{cam.name}' ortho={cam.orthographic} size={_originalOrthoSize:0.00}->{cam.orthographicSize:0.00} follow={(follow != null ? follow.gameObject.name : "<none>")}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CameraZoom] Apply failed: {ex}");
            }
        }

        // Pick the CameraFollow whose private cam is the main camera. Returns
        // null when none matches (e.g. CameraFollow lives on a different cam,
        // or fields didn't resolve) — Apply then zooms Camera.main alone.
        private static CameraFollow ResolveFollow(Camera main)
        {
            if (!_fieldsResolved)
            {
                return null;
            }
            // Game's Unity build lacks FindObjectSortMode, so the non-obsolete
            // FindObjectsByType overload won't bind. FindObjectsOfType is the only
            // option here — suppress the deprecation warning for this call.
#pragma warning disable CS0618
            CameraFollow[] all = Object.FindObjectsOfType<CameraFollow>();
#pragma warning restore CS0618
            if (all == null || all.Length == 0)
            {
                return null;
            }

            foreach (CameraFollow f in all)
            {
                if ((_fCam.GetValue(f) as Camera) == main)
                {
                    return f;
                }
            }
            return null;
        }

        public static void Reset()
        {
            Multiplier = Default;
            Apply();
        }
    }
}
