using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Left-thumb movement stick.
    ///
    /// Operates in "floating" mode: the stick re-centres wherever the thumb first lands
    /// inside its zone, rather than forcing the player to find a fixed circle. That is the
    /// behaviour every GTA/PUBG-style mobile port uses, because thumbs do not land in the
    /// same spot twice.
    ///
    /// Drives <see cref="InputHub.TouchMove"/>. Uses uGUI pointer events, so a mouse drag
    /// on desktop exercises the identical path as a finger on device.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Header("Wiring")]
        public RectTransform Ring;
        public RectTransform Knob;
        public Graphic[] FadeTargets;

        [Header("Feel")]
        [Tooltip("Max thumb travel from centre, in canvas units.")]
        public float Radius = 110f;
        [Tooltip("Deadzone as a fraction of Radius; below this the stick reads zero.")]
        [Range(0f, 0.5f)] public float DeadZone = 0.12f;
        [Tooltip("Alpha when untouched. 0 hides the stick until the player reaches for it.")]
        [Range(0f, 1f)] public float IdleAlpha = 0.35f;
        public float FadeSpeed = 8f;

        RectTransform _zone;
        Canvas _canvas;
        int _activePointer = -1;
        Vector2 _origin;
        float _targetAlpha;

        void Awake()
        {
            _zone = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
            _targetAlpha = IdleAlpha;
            ResetStick();
        }

        void Update()
        {
            float a = Mathf.MoveTowards(CurrentAlpha(), _targetAlpha, FadeSpeed * Time.unscaledDeltaTime);
            SetAlpha(a);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_activePointer != -1) return;
            _activePointer = e.pointerId;

            if (ScreenToLocal(e, out Vector2 local))
            {
                _origin = local;
                if (Ring != null) Ring.anchoredPosition = local;
                if (Knob != null) Knob.anchoredPosition = local;
            }

            _targetAlpha = 1f;
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _activePointer) return;
            if (!ScreenToLocal(e, out Vector2 local)) return;

            Vector2 offset = local - _origin;
            Vector2 clamped = Vector2.ClampMagnitude(offset, Radius);

            if (Knob != null) Knob.anchoredPosition = _origin + clamped;

            Vector2 raw = clamped / Radius;
            float mag = raw.magnitude;

            // Rescale past the deadzone so the very first pixel of real travel maps to a
            // small speed rather than snapping straight to the deadzone threshold.
            Vector2 value = mag <= DeadZone
                ? Vector2.zero
                : raw.normalized * ((mag - DeadZone) / (1f - DeadZone));

            if (InputHub.Instance != null) InputHub.Instance.TouchMove = value;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _activePointer) return;
            _activePointer = -1;
            _targetAlpha = IdleAlpha;
            ResetStick();
        }

        void ResetStick()
        {
            if (InputHub.Instance != null) InputHub.Instance.TouchMove = Vector2.zero;
            if (Ring != null) Ring.anchoredPosition = Vector2.zero;
            if (Knob != null) Knob.anchoredPosition = Vector2.zero;
        }

        bool ScreenToLocal(PointerEventData e, out Vector2 local)
        {
            var cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _zone, e.position, cam, out local);
        }

        float CurrentAlpha()
        {
            if (FadeTargets == null || FadeTargets.Length == 0 || FadeTargets[0] == null) return 1f;
            return FadeTargets[0].color.a;
        }

        void SetAlpha(float a)
        {
            if (FadeTargets == null) return;
            foreach (var g in FadeTargets)
            {
                if (g == null) continue;
                var c = g.color;
                c.a = a;
                g.color = c;
            }
        }
    }
}
