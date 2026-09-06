using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Insets a full-screen RectTransform to <see cref="Screen.safeArea"/> so nothing lands
    /// under a notch, a punch-hole or a gesture bar.
    ///
    /// This sits on a single container between the canvas and every UI root, rather than on
    /// each screen: a notch does not care which panel is open, and one component that every
    /// screen inherits cannot be forgotten when the next screen is added.
    ///
    /// Note for this project specifically: Android is configured with
    /// <c>renderOutsideSafeArea = false</c>, so the OS already letterboxes the app away from
    /// the cutout and <c>Screen.safeArea</c> comes back equal to the full render area (measured
    /// on the vivo I2019: safeArea = 0,0,2318x1080 with a full 2400px display). This component
    /// is therefore a no-op on that configuration and becomes load-bearing the moment
    /// renderOutsideSafeArea is turned on, the app runs on a device that reports insets anyway,
    /// or a gesture bar overlaps the render area.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rect;
        Rect _lastSafeArea;
        Vector2Int _lastScreen;

        void Awake()
        {
            _rect = GetComponent<RectTransform>();
            Apply();
        }

        // The safe area changes on rotation and when the system bars come and go, neither of
        // which raises an event, so it is polled. Two Rect compares a frame is not a cost.
        void Update()
        {
            if (Screen.safeArea == _lastSafeArea &&
                Screen.width == _lastScreen.x && Screen.height == _lastScreen.y) return;
            Apply();
        }

        void Apply()
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();

            int w = Screen.width, h = Screen.height;
            // A zero-sized screen happens for a frame during some orientation changes; applying
            // it would divide by zero and collapse the whole UI to a point.
            if (w <= 0 || h <= 0) return;

            Rect safe = Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f) return;

            _lastSafeArea = safe;
            _lastScreen = new Vector2Int(w, h);

            Vector2 min = new Vector2(safe.xMin / w, safe.yMin / h);
            Vector2 max = new Vector2(safe.xMax / w, safe.yMax / h);

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;

            Debug.Log("[SafeArea] screen=" + w + "x" + h + " safeArea=" + safe
                      + " -> anchors " + min + ".." + max);
        }
    }
}
