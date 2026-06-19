using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BeyondAgent.Util
{
    // Generic "make a uGUI panel draggable" component. Attach to a RectTransform
    // and pointer drags on that RectTransform's empty space move it around.
    // Button/scroll-view children still receive their own events because uGUI's
    // event system only bubbles drag events to ancestors when no descendant
    // consumed pointer-down — and buttons consume pointer-down.
    //
    // We just add eventData.delta to anchoredPosition. delta is in screen
    // pixels, which matches anchoredPosition's space at a 1:1 canvas scale.
    // For canvases with non-unit scale we divide by the parent canvas's
    // scaleFactor so the panel still tracks the cursor.
    public class DragPanel : MonoBehaviour, IDragHandler
    {
        private RectTransform _rt;
        private Canvas _canvas;

        private void Awake()
        {
            _rt = transform as RectTransform;
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnDrag(PointerEventData e)
        {
            if (_rt == null)
            {
                return;
            }

            float scale = (_canvas?.scaleFactor > 0f) ? _canvas.scaleFactor : 1f;
            _rt.anchoredPosition += e.delta / scale;
        }

        // Walks up from the script's transform looking for the window's prefab
        // root, then attaches DragPanel there. The window root is the highest
        // ancestor still in a "single-window chain" — we keep climbing while
        // the parent has at most one RectTransform child, and we stop the
        // moment the parent has multiple RectTransform children (that parent
        // is UIWindowManager's shared container holding sibling windows, and
        // dragging it would move unrelated windows too).
        //
        // History:
        // 1. Attach to source.transform directly → worked for UIBank (script
        //    on root) but failed for ItemShop/MergeShop (script on inner
        //    child, so we only grabbed a subcomponent).
        // 2. Walk to topmost RectTransform under Canvas → grabbed the shared
        //    container, so opening the Bank also dragged the open NPC bubble.
        // 3. (current) Walk up the single-child chain → correct in both.
        // Flip to true if a window's drag target ever feels wrong — we'll
        // log the full ancestor chain on first attach to see the actual
        // hierarchy. Off by default to keep the the standalone launcher console quiet.
        public static bool DumpHierarchyOnAttach = false;

        public static bool AttachToWindowRoot(Component source, string logTag)
        {
            if (source == null)
            {
                return false;
            }

            RectTransform start = source.transform as RectTransform;
            if (start == null)
            {
                BeyondLog.Warning($"[{logTag}] {source.GetType().Name} has no RectTransform — UI layout changed?");
                return false;
            }
            RectTransform target = FindWindowRoot(start);
            if (target.GetComponent<DragPanel>() != null)
            {
                return false; // already attached, no log
            }

            if (DumpHierarchyOnAttach)
            {
                LogAncestorChain(start, logTag);
            }

            target.gameObject.AddComponent<DragPanel>();
            BeyondLog.Msg($"[{logTag}] drag handler attached to '{target.name}'");
            return true;
        }

        private static void LogAncestorChain(RectTransform start, string logTag)
        {
            StringBuilder sb = new();
            sb.Append($"[{logTag}] ancestors: ");
            int depth = 0;
            for (Transform t = start; t != null; t = t.parent, depth++)
            {
                RectTransform rt = t as RectTransform;
                int rtKids = rt != null ? CountRectTransformChildren(t) : -1;
                bool isCanvas = t.GetComponent<Canvas>() != null;
                sb.Append($"\n  [{depth}] '{t.name}' rt={rt != null} rtKids={rtKids}{(isCanvas ? " <-Canvas" : "")}");
                if (isCanvas)
                {
                    break;
                }
            }
            BeyondLog.Msg(sb.ToString());
        }

        private static RectTransform FindWindowRoot(RectTransform start)
        {
            RectTransform current = start;
            while (true)
            {
                Transform parent = current.parent;
                if (parent == null)
                {
                    break;
                }

                if (parent.GetComponent<Canvas>() != null)
                {
                    break; // hit the canvas
                }

                if (parent is not RectTransform parentRt)
                {
                    break;
                }

                if (CountRectTransformChildren(parent) > 1)
                {
                    break; // shared container
                }

                current = parentRt;
            }
            return current;
        }

        private static int CountRectTransformChildren(Transform t)
        {
            int n = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                if (t.GetChild(i) is RectTransform)
                {
                    n++;
                }
            }
            return n;
        }
    }
}
