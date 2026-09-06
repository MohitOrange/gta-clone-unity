using UnityEngine;
using UnityEngine.EventSystems;

namespace MiniGTA
{
    /// <summary>
    /// Makes one touch control movable and resizable, and keeps it where the player put it.
    ///
    /// Sits on the control itself rather than being driven from the settings screen, for two
    /// reasons. The interface is rebuilt wholesale by the editor tooling, so a settings panel
    /// holding serialised references to ten widgets would come back with ten nulls; finding
    /// them by id at runtime cannot break that way. And the same component then serves both
    /// jobs -- it applies the saved offset on enable, and it handles the drag while the player
    /// is editing -- so there is one place that knows how a control's placement is expressed.
    ///
    /// The authored position is captured on first enable and never written to. Everything the
    /// player does is a delta on top of it, which is what lets the HUD be redesigned underneath
    /// a saved layout without stranding the buttons.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class HudCustomisable : MonoBehaviour, IDragHandler, IBeginDragHandler, IPointerDownHandler
    {
        [Tooltip("Key this control is saved under. Defaults to the GameObject name.")]
        public string Id;

        /// <summary>True while the settings screen is in layout-editing mode.</summary>
        public static bool EditMode { get; private set; }

        static readonly System.Collections.Generic.List<HudCustomisable> All =
            new System.Collections.Generic.List<HudCustomisable>();

        RectTransform _rt;
        Vector2 _authoredPosition;
        Vector2 _authoredSize;
        bool _captured;

        void Awake()
        {
            _rt = (RectTransform)transform;
            if (string.IsNullOrEmpty(Id)) Id = gameObject.name;

            _authoredPosition = _rt.anchoredPosition;
            _authoredSize = _rt.sizeDelta;
            _captured = true;
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            HudLayoutStore.Changed += Apply;
            Apply();
        }

        void OnDisable()
        {
            All.Remove(this);
            HudLayoutStore.Changed -= Apply;
        }

        /// <summary>Moves this control to wherever the store says it should be.</summary>
        public void Apply()
        {
            if (!_captured) return;

            var entry = HudLayoutStore.Get(Id);
            _rt.anchoredPosition = _authoredPosition + new Vector2(entry.OffsetX, entry.OffsetY);
            _rt.sizeDelta = _authoredSize * entry.Scale;
        }

        /// <summary>Puts the settings screen into or out of layout-editing mode.</summary>
        public static void SetEditMode(bool editing)
        {
            EditMode = editing;
            foreach (var c in All) c.Apply();
        }

        /// <summary>Resizes this control, keeping it where it is.</summary>
        public void Resize(float scale)
        {
            var entry = HudLayoutStore.Get(Id);
            HudLayoutStore.Set(Id, entry.OffsetX, entry.OffsetY, scale);
        }

        /// <summary>Finds a live control by id, or null.</summary>
        public static HudCustomisable Find(string id)
        {
            foreach (var c in All) if (c.Id == id) return c;
            return null;
        }

        // ------------------------------------------------------------------ dragging

        Vector2 _dragStart;

        /// <summary>Touching a control in edit mode points the size slider at it.</summary>
        public void OnPointerDown(PointerEventData e)
        {
            if (!EditMode) return;
            HudLayoutEditor.Select(Id);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!EditMode) return;
            var entry = HudLayoutStore.Get(Id);
            _dragStart = new Vector2(entry.OffsetX, entry.OffsetY);
        }

        public void OnDrag(PointerEventData e)
        {
            // Outside edit mode this component is inert, so a control being dragged as part of
            // normal play -- the joystick, most obviously -- behaves exactly as it always did.
            if (!EditMode) return;

            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            if (scale <= 0f) scale = 1f;

            Vector2 moved = _dragStart + (e.position - e.pressPosition) / scale;

            var entry = HudLayoutStore.Get(Id);
            HudLayoutStore.Set(Id, moved.x, moved.y, entry.Scale);
        }
    }
}
