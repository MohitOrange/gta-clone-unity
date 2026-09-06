using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Flips the touch controls between right- and left-handed layouts.
    ///
    /// Only the controls move. The readouts stay where they are: a left-handed player still
    /// reads a map in the same place, and moving the health bar under the thumb that is now
    /// covering that corner would make the setting worse, not better.
    /// </summary>
    public class HudLayout : MonoBehaviour
    {
        static HudLayout _instance;

        public static HudLayout Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<HudLayout>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Tooltip("Control widgets that swap sides. Readouts are deliberately absent.")]
        public RectTransform[] Mirrored = System.Array.Empty<RectTransform>();

        [Tooltip("The three control clusters, hidden whenever a menu is open.")]
        public GameObject[] ControlRoots = System.Array.Empty<GameObject>();

        struct Anchoring
        {
            public Vector2 Min, Max, Pivot, Position, OffsetMin, OffsetMax;
        }

        Anchoring[] _original;
        bool _leftHanded;

        public bool IsLeftHanded => _leftHanded;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;

            Capture();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Capture()
        {
            _original = new Anchoring[Mirrored.Length];

            for (int i = 0; i < Mirrored.Length; i++)
            {
                var rt = Mirrored[i];
                if (rt == null) continue;

                _original[i] = new Anchoring
                {
                    Min = rt.anchorMin,
                    Max = rt.anchorMax,
                    Pivot = rt.pivot,
                    Position = rt.anchoredPosition,
                    OffsetMin = rt.offsetMin,
                    OffsetMax = rt.offsetMax,
                };
            }
        }

        /// <summary>
        /// Rebuilds every widget's anchoring from the layout it was authored with, so toggling
        /// back and forth cannot drift. Mirroring the current values instead would accumulate
        /// rounding on stretched rects.
        /// </summary>
        public void SetLeftHanded(bool leftHanded)
        {
            _leftHanded = leftHanded;
            if (_original == null || _original.Length != Mirrored.Length) Capture();

            for (int i = 0; i < Mirrored.Length; i++)
            {
                var rt = Mirrored[i];
                if (rt == null) continue;

                var o = _original[i];

                if (!leftHanded)
                {
                    rt.anchorMin = o.Min;
                    rt.anchorMax = o.Max;
                    rt.pivot = o.Pivot;
                    rt.anchoredPosition = o.Position;
                    // Order matters for stretched rects: anchors first, then offsets, because
                    // changing an anchor rewrites the offsets Unity derives from it.
                    if (IsStretchedHorizontally(o)) { rt.offsetMin = o.OffsetMin; rt.offsetMax = o.OffsetMax; }
                    continue;
                }

                rt.anchorMin = new Vector2(1f - o.Max.x, o.Min.y);
                rt.anchorMax = new Vector2(1f - o.Min.x, o.Max.y);
                rt.pivot = new Vector2(1f - o.Pivot.x, o.Pivot.y);
                rt.anchoredPosition = new Vector2(-o.Position.x, o.Position.y);

                if (IsStretchedHorizontally(o))
                {
                    rt.offsetMin = new Vector2(-o.OffsetMax.x, o.OffsetMin.y);
                    rt.offsetMax = new Vector2(-o.OffsetMin.x, o.OffsetMax.y);
                }
            }
        }

        /// <summary>
        /// Re-asserts control visibility every frame.
        ///
        /// Not paranoia: <see cref="HudContext"/> owns these widgets too and switches them as
        /// the player gets in and out of a car, and its rules know nothing about menus. Left to
        /// a single call at the moment a menu opens, the first context change behind that menu
        /// would put the joystick back on top of it. Whoever asks last wins, so this asks last.
        /// </summary>
        void LateUpdate()
        {
            bool visible = !MenuState.AnyOpen;

            foreach (var root in ControlRoots)
                if (root != null && root.activeSelf != visible) root.SetActive(visible);
        }

        /// <summary>
        /// Hides the touch controls while a menu is up.
        ///
        /// Deactivated rather than faded: the look pad covers most of the screen and would
        /// happily swing the camera behind a transparent menu, and the joystick would keep
        /// walking the player around under it.
        /// </summary>
        public void SetControlsVisible(bool visible)
        {
            foreach (var root in ControlRoots)
                if (root != null) root.SetActive(visible);

            // A stick that was mid-drag when the menu opened would otherwise leave the player
            // running in that direction for as long as the menu stays up.
            if (!visible && InputHub.Instance != null)
            {
                InputHub.Instance.TouchMove = Vector2.zero;
                InputHub.Instance.TouchLookDelta = Vector2.zero;
                InputHub.Instance.TouchSprintHeld = false;
                InputHub.Instance.TouchGasHeld = false;
                InputHub.Instance.TouchBrakeHeld = false;
                InputHub.Instance.TouchHandbrakeHeld = false;
            }
        }

        static bool IsStretchedHorizontally(Anchoring a) => !Mathf.Approximately(a.Min.x, a.Max.x);
    }
}
