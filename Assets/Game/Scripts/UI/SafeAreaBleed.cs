using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Expands a RectTransform back out to the full canvas, undoing the inset that
    /// <see cref="SafeAreaFitter"/> applies to everything above it.
    ///
    /// <b>Backgrounds and scrims want the opposite of a safe area from everything else.</b>
    /// Controls must stay inside the notch inset or they get clipped; a starfield or a modal
    /// dimmer must reach the physical screen edge or the inset shows through as a bare band of
    /// whatever the camera cleared to. Phase 13 shipped the lobby backdrop inside the safe-area
    /// container and it was drawn with flat grey strips down both sides and along the bottom on
    /// any device that reports insets — which the Device Simulator always does, and any Android
    /// device does the moment <c>renderOutsideSafeArea</c> is turned on.
    ///
    /// So the two jobs are split: the safe-area container keeps holding every UI root, and the
    /// one Image inside each screen that is *meant* to bleed carries this instead.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class SafeAreaBleed : MonoBehaviour
    {
        RectTransform _rect;
        RectTransform _parent;
        Canvas _canvas;

        Rect _lastParent;
        Rect _lastCanvas;

        void OnEnable()
        {
            _rect = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
            Apply();
        }

        // Polled rather than event-driven for the same reason SafeAreaFitter polls: a rotation
        // or a system-bar change moves the safe area and raises nothing. Two Rect compares.
        void Update() => Apply();

        /// <summary>
        /// Recomputes now rather than on the next tick.
        ///
        /// Public because the editor tooling needs it: an MCP command runs to completion
        /// without a frame elapsing, so a capture taken after changing the simulated safe area
        /// would photograph the previous frame's bleed. Anything that moves the safe area
        /// synchronously has to call this before it measures or draws.
        /// </summary>
        public void Apply()
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null) return;

            _parent = _rect.parent as RectTransform;
            if (_parent == null) return;

            var canvasRect = _canvas.transform as RectTransform;
            if (canvasRect == null) return;

            Rect parentRect = _parent.rect;
            Rect target = canvasRect.rect;
            if (parentRect.width <= 0f || parentRect.height <= 0f) return;

            // Nothing has moved since the last frame.
            if (parentRect == _lastParent && target == _lastCanvas) return;
            _lastParent = parentRect;
            _lastCanvas = target;

            // The canvas's own corners, expressed in the parent's local space. Going through
            // world space rather than subtracting rects keeps this correct if anything in the
            // chain is ever scaled or offset.
            Vector3 min = _parent.InverseTransformPoint(
                canvasRect.TransformPoint(new Vector3(target.xMin, target.yMin, 0f)));
            Vector3 max = _parent.InverseTransformPoint(
                canvasRect.TransformPoint(new Vector3(target.xMax, target.yMax, 0f)));

            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.offsetMin = new Vector2(min.x - parentRect.xMin, min.y - parentRect.yMin);
            _rect.offsetMax = new Vector2(max.x - parentRect.xMax, max.y - parentRect.yMax);
        }
    }
}
