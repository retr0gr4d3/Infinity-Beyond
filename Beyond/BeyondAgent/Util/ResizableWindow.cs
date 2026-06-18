using UnityEngine;
using System.Collections.Generic;
using System;


namespace Infinity_TestMod.Util
{
    public static class ResizableWindow
    {
        private const float CornerSize = 12f;

        private static int _resizingId = -1;
        private static int _activeControlId = 0;
        private static float _newWidthAfterDrag = -1f;
        private static float _dragStartMouseX = 0f;
        private static float _dragStartWidth = 0f;
        private static readonly HashSet<int> _userResized = new();

        private static readonly Dictionary<int, float> _baseWidths = new();
        private static readonly Dictionary<int, float> _baseHeights = new();

        private static Vector2 _parentMousePosition = Vector2.zero;

        // Cached resize-grip style — allocated once, colours swapped per-call.
        private static GUIStyle _gripStyle;
        private static readonly Color _gripDefault = new Color(0.28f, 0.31f, 0.37f, 1.0f);
        private static readonly Color _gripHover  = new Color(0.47f, 0.52f, 0.62f, 1.0f);

        // Per-window wrapper state. GUI.Window defers callbacks until after all
        // windows are registered in the same OnGUI pass, so a single static slot
        // would leave every window drawing with the last registered callback.
        private struct WrapState
        {
            public float LocalH;
            public float Scale;
            public float BaseW;
            public Rect ScreenRect;
            public GUI.WindowFunction Inner;
        }

        private static readonly Dictionary<int, WrapState> _wrapStates = new();

        public static bool WasManuallyResized(int windowId) => _userResized.Contains(windowId);

        public static Rect TitleBarDragRect(float windowWidth, float titleHeight = 30f)
        {
            float inset = CornerSize;
            // Exclude the rightmost 28 pixels to ensure the drag rect does not overlap the resize button
            return new Rect(inset, 0, Mathf.Max(0f, windowWidth - inset - 28f), titleHeight);
        }

        // Deprecated: kept as no-ops to avoid compilation errors
        public static float BeginScaling(int windowId, Rect currentRect, float defaultWidth) => 1.0f;
        public static void EndScaling() {}

        public static Rect HandleResize(int windowId, Rect windowRect)
        {
            // Now a no-op since resizing is handled by the top-right drag button
            return windowRect;
        }

        public static Rect DrawScaledWindow(int id, Rect screenRect, float baseWidth, GUI.WindowFunction func, string title, GUIStyle style = null)
        {
            // Position clamping to keep the window on screen and rescue off-screen windows
            screenRect.x = Mathf.Clamp(screenRect.x, 10f, Mathf.Max(10f, Screen.width - 100f));
            screenRect.y = Mathf.Clamp(screenRect.y, 10f, Mathf.Max(10f, Screen.height - 50f));

            // Recover manually resized state if width was modified (e.g. state preserved during hot-reload)
            if (Mathf.Abs(screenRect.width - baseWidth) > 0.01f)
            {
                _userResized.Add(id);
                _baseWidths[id] = baseWidth;
                if (!_baseHeights.ContainsKey(id))
                {
                    float currentScale = screenRect.width / baseWidth;
                    _baseHeights[id] = screenRect.height / currentScale;
                }
            }

            // Capture parent-space mouse coordinates BEFORE changing GUI.matrix
            Event evt = Event.current;
            if (evt != null)
            {
                _parentMousePosition = evt.mousePosition;

                // Global MouseUp detection in parent screen space. 
                // This ensures dragging resets even if the cursor leaves the window.
                if (evt.type == EventType.MouseUp)
                {
                    _resizingId = -1;
                    _newWidthAfterDrag = -1f;
                    if (GUIUtility.hotControl == _activeControlId)
                    {
                        GUIUtility.hotControl = 0;
                    }
                }
            }

            // Track base dimensions if not manually resized
            if (!WasManuallyResized(id))
            {
                _baseWidths[id] = baseWidth;
                _baseHeights[id] = screenRect.height;
            }

            float baseW = _baseWidths.TryGetValue(id, out float bw) ? bw : baseWidth;
            float scale = screenRect.width / baseW;

            Matrix4x4 oldMat = GUI.matrix;
            
            // Set scaling matrix
            GUI.matrix = oldMat * Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            
            // Compute scaled rect for GUI.Window
            Rect scaledRect = new Rect(screenRect.x / scale, screenRect.y / scale, baseW, screenRect.height / scale);
            
            Rect newScaledRect;

            _wrapStates[id] = new WrapState
            {
                LocalH = screenRect.height / scale,
                Scale = scale,
                BaseW = baseW,
                ScreenRect = screenRect,
                Inner = func,
            };

            if (style != null)
            {
                newScaledRect = GUI.Window(id, scaledRect, WrapperCallback, title, style);
            }
            else
            {
                newScaledRect = GUI.Window(id, scaledRect, WrapperCallback, title);
            }
            
            // Restore matrix
            GUI.matrix = oldMat;
            
            // If currently drag-resizing this window, override the returned width and height
            if (_resizingId == id && _newWidthAfterDrag > 0f)
            {
                float newW = _newWidthAfterDrag;
                float baseH = _baseHeights.TryGetValue(id, out float bh) ? bh : screenRect.height;
                float newH = newW * (baseH / baseW);
                newScaledRect.width = newW / scale;
                newScaledRect.height = newH / scale;
            }

            // Convert returned rect back to screen space
            return new Rect(newScaledRect.x * scale, newScaledRect.y * scale, newScaledRect.width * scale, newScaledRect.height * scale);
        }

        private static void HandleResizeEvents(int id, float baseW, float localH, float scale, Rect screenRect)
        {
            Event evt = Event.current;
            if (evt != null)
            {
                Vector2 localMouse = evt.mousePosition;
                Rect handleRect = new Rect(baseW - 20f, localH - 20f, 20f, 20f);
                
                if (evt.type == EventType.MouseDown && handleRect.Contains(localMouse))
                {
                    _resizingId = id;
                    _dragStartMouseX = _parentMousePosition.x;
                    _dragStartWidth = screenRect.width;
                    _newWidthAfterDrag = screenRect.width;
                    _activeControlId = GUIUtility.GetControlID(FocusType.Passive);
                    GUIUtility.hotControl = _activeControlId;
                    evt.Use();
                }
                
                if (_resizingId == id)
                {
                    if (evt.type == EventType.MouseDrag)
                    {
                        // Calculate change relative to initial mouse down point to prevent snapping on click
                        float deltaX = _parentMousePosition.x - _dragStartMouseX;
                        float newWidth = _dragStartWidth + deltaX;
                        
                        // Enforce reasonable minimum and maximum scaling sizes
                        float minW = Mathf.Max(50f, baseW * 0.4f);
                        float maxW = Mathf.Min(Screen.width, baseW * 3.0f);
                        newWidth = Mathf.Clamp(newWidth, minW, maxW);
                        
                        _newWidthAfterDrag = newWidth;
                        _userResized.Add(id);
                        evt.Use();
                    }
                    else if (evt.type == EventType.MouseUp)
                    {
                        _resizingId = -1;
                        _newWidthAfterDrag = -1f;
                        if (GUIUtility.hotControl == _activeControlId)
                        {
                            GUIUtility.hotControl = 0;
                        }
                        evt.Use();
                    }
                }
            }
        }

        private static void WrapperCallback(int winId)
        {
            if (!_wrapStates.TryGetValue(winId, out WrapState state))
                return;

            HandleResizeEvents(winId, state.BaseW, state.LocalH, state.Scale, state.ScreenRect);
            state.Inner(winId);
            DrawResizeBoxVisual(state.BaseW, state.LocalH);
        }

        private static void DrawResizeBoxVisual(float baseW, float localH)
        {
            Rect handleRect = new Rect(baseW - 20f, localH - 20f, 20f, 20f);

            // Lazily create the cached grip style once.
            if (_gripStyle == null)
            {
                _gripStyle = new GUIStyle();
                _gripStyle.alignment = TextAnchor.LowerRight;
                _gripStyle.fontSize = 11;
                _gripStyle.fontStyle = FontStyle.Bold;
                _gripStyle.padding = new RectOffset(0, 4, 0, 4);
            }

            bool isHovering = handleRect.Contains(Event.current.mousePosition);
            _gripStyle.normal.textColor = isHovering ? _gripHover : _gripDefault;

            GUI.Label(handleRect, "///", _gripStyle);
        }
    }
}