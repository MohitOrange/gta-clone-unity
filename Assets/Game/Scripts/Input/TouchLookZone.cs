using UnityEngine;
using UnityEngine.EventSystems;

namespace MiniGTA
{
    /// <summary>
    /// Right-thumb camera look pad: an invisible full-height panel that turns finger drag
    /// into camera rotation.
    ///
    /// It must sit *behind* the action buttons in the hierarchy so that a tap landing on a
    /// button is consumed by the button; anything that misses a button falls through to
    /// here and becomes a camera swipe.
    ///
    /// Deltas are accumulated (+=) rather than assigned, so a multi-finger drag or two
    /// events in one frame both contribute instead of overwriting each other.
    /// </summary>
    public class TouchLookZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Tooltip("Ignore drags this small (in pixels) to stop a tap registering as a nudge.")]
        public float MoveThreshold = 0.5f;

        int _activePointer = -1;

        public void OnPointerDown(PointerEventData e)
        {
            if (_activePointer == -1) _activePointer = e.pointerId;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _activePointer) return;
            if (InputHub.Instance == null) return;

            Vector2 d = e.delta;
            if (d.sqrMagnitude < MoveThreshold * MoveThreshold) return;

            InputHub.Instance.TouchLookDelta += d;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == _activePointer) _activePointer = -1;
        }
    }
}
