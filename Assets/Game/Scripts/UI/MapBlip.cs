using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Makes one map marker tappable, and remembers what it stands for.
    ///
    /// A blip's <see cref="RectTransform"/> only knows where it is on a UI rect; the thing the
    /// player wants is the place in the world it represents. This carries that across, so a tap
    /// can set a waypoint without the map screen having to re-derive which pickup or shop the
    /// player just hit from a screen coordinate.
    ///
    /// Added to the pool templates, so every pooled copy inherits it -- <see cref="Minimap"/>
    /// creates its pools by cloning the template.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MapBlip : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>Where in the world this marker is. Written by <see cref="Minimap"/>.</summary>
        [HideInInspector] public Vector3 World;

        /// <summary>
        /// The object this marker represents, so tapping the same one again clears the
        /// waypoint rather than re-setting it to the same place.
        /// </summary>
        [HideInInspector] public Object Owner;

        [Tooltip("Off for the corner minimap, which is far too small to aim at and already has "
                 + "a full-rect button over it that opens the full map.")]
        public bool Selectable;

        [Tooltip("Optional. Brightened while this marker is the active waypoint.")]
        public Graphic Highlight;

        Color _base;
        bool _captured;

        void Awake()
        {
            if (Highlight == null) Highlight = GetComponent<Graphic>();
            if (Highlight != null) { _base = Highlight.color; _captured = true; }
        }

        void OnEnable()
        {
            MapWaypoint.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => MapWaypoint.Changed -= Refresh;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Selectable) return;
            MapWaypoint.Toggle(World, Owner);
        }

        /// <summary>
        /// Shows which marker is currently the waypoint.
        ///
        /// Full white rather than a different hue: the category colours are already carrying
        /// meaning, so selection has to be a change the eye reads as "this one" without
        /// implying the marker has become a different kind of thing.
        /// </summary>
        void Refresh()
        {
            if (!_captured || Highlight == null) return;

            bool selected = Selectable && MapWaypoint.Active
                            && Owner != null && MapWaypoint.Owner == Owner;

            Highlight.color = selected ? Color.white : _base;
            transform.localScale = selected ? Vector3.one * 1.35f : Vector3.one;
        }
    }
}
