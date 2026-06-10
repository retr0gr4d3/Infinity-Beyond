using MelonLoader;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Infinity_TestMod.Util
{
    // Scales the active CameraFollow's orthographic size by a multiplier.
    // We capture the game's original size the first time we touch a given
    // CameraFollow instance so Reset (and area changes that swap the camera)
    // restore correctly. camHalfHeight/Width are kept in sync because the
    // game's LateUpdate uses them to clamp the camera inside the area's
    // BoxCollider confiner — stale half-extents would let the camera drift
    // past the room edge after zooming out.
    //
    // CameraFollow.cam, camHalfHeight, and camHalfWidth are all private,
    // so this is all reflection. Field handles are resolved once in the
    // static constructor; if a future game version renames or removes any
    // of them we log a single error there and Apply becomes a no-op rather
    // than spamming the log every frame.
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

        private static CameraFollow _trackedFollow;
        private static float _originalOrthoSize;
        private static float _originalFov;
        private static bool _captured;

        static CameraZoom()
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            _fCam = typeof(CameraFollow).GetField("cam", Flags);
            _fHalfH = typeof(CameraFollow).GetField("camHalfHeight", Flags);
            _fHalfW = typeof(CameraFollow).GetField("camHalfWidth", Flags);

            List<string> missing = new();
            if (_fCam == null) missing.Add("cam");
            if (_fHalfH == null) missing.Add("camHalfHeight");
            if (_fHalfW == null) missing.Add("camHalfWidth");

            _fieldsResolved = missing.Count == 0;
            if (!_fieldsResolved)
            {
                MelonLogger.Error($"[CameraZoom] disabled — CameraFollow is missing expected private field(s): {string.Join(", ", missing)}. Game version probably changed.");
            }
        }

        public static void Apply()
        {
            if (!_fieldsResolved) return;
            try
            {
                CameraFollow follow = Object.FindAnyObjectByType<CameraFollow>();
                if (follow == null) return;
                if (_fCam.GetValue(follow) is not Camera cam) return;

                if (follow != _trackedFollow)
                {
                    _trackedFollow = follow;
                    _originalOrthoSize = cam.orthographicSize;
                    _originalFov = cam.fieldOfView;
                    _captured = true;
                }

                if (!_captured) return;

                // Clamp and write back so any UI reading Multiplier sees the
                // same value the camera is actually using — otherwise a stale
                // out-of-range slider would silently disagree with the view.
                Multiplier = Mathf.Clamp(Multiplier, Min, Max);
                if (cam.orthographic)
                {
                    float size = _originalOrthoSize * Multiplier;
                    cam.orthographicSize = size;
                    _fHalfH.SetValue(follow, size);
                    _fHalfW.SetValue(follow, size * cam.aspect);
                }
                else
                {
                    cam.fieldOfView = Mathf.Clamp(_originalFov * Multiplier, 1f, 179f);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"[CameraZoom] Apply failed: {ex}");
            }
        }

        public static void Reset()
        {
            Multiplier = Default;
            Apply();
        }
    }
}