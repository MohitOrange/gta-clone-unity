using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Every action the touch HUD can offer.
    ///
    /// Appended to, never reordered: the values are serialised on the button prefabs in
    /// City.unity, so inserting into the middle would silently repoint every existing
    /// button at the wrong action.
    /// </summary>
    public enum HudAction
    {
        Jump, Sprint, Interact, Attack, Horn, Handbrake, Gas, Brake, Aim, Reload,
        Crouch, Prone, WeaponSwitch,
    }

    /// <summary>
    /// A context-aware action button.
    ///
    /// Two behaviours in one component because the HUD needs both and they differ only in
    /// how the press is reported: momentary buttons (Jump, Interact) queue a one-shot on
    /// press; hold buttons (Sprint) report a held state for as long as the thumb is down.
    ///
    /// Visibility is driven by <see cref="HudContext"/> rather than by each caller poking
    /// SetActive, so the on-foot/in-vehicle/armed rules live in exactly one place.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class HudButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Behaviour")]
        public HudAction Action = HudAction.Jump;
        [Tooltip("Hold buttons report a continuous held state; momentary buttons fire once per press.")]
        public bool IsHoldButton;

        [Tooltip("Latching buttons toggle on the press and stay lit until pressed again. Used "
                 + "for the run lock, so sprinting does not need a thumb held down.")]
        public bool IsLatching;

        [Header("Visuals")]
        public Graphic Background;
        public Color PressedTint = new Color(1f, 1f, 1f, 1f);
        public Color NormalTint = new Color(1f, 1f, 1f, 0.55f);
        public float FadeSpeed = 12f;

        [Header("State art")]
        [Tooltip("Optional. When both are set the button swaps sprites on press as well as "
                 + "tinting, so a painted button shows its own pressed art rather than the "
                 + "resting art multiplied by a colour.")]
        public Sprite NormalSprite;
        public Sprite PressedSprite;

        CanvasGroup _group;
        bool _visible = true;
        bool _held;
        int _pointer = -1;

        public bool Held => _held;

        /// <summary>Whether context rules currently allow this button on screen.</summary>
        public bool Visible => _visible;

        void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            ApplyTint(false);
            _group.alpha = _visible ? 1f : 0f;
        }

        void Update()
        {
            // The lock can be dropped by the game (entering a vehicle, dying, losing focus),
            // not just by another tap, so the lit state is re-asserted rather than only being
            // set on press.
            if (IsLatching && !_held) ApplyTint(Latched);

            float target = _visible ? 1f : 0f;
            if (!Mathf.Approximately(_group.alpha, target))
                _group.alpha = Mathf.MoveTowards(_group.alpha, target, FadeSpeed * Time.unscaledDeltaTime);

            // A button fading out must stop accepting taps immediately, not at alpha 0.
            _group.blocksRaycasts = _visible;
            _group.interactable = _visible;
        }

        /// <summary>Show or hide with a fade. Releases a held state when hidden.</summary>
        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (!visible) Release();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!_visible || _pointer != -1) return;
            _pointer = e.pointerId;
            _held = true;

            var hub = InputHub.Instance;

            if (IsLatching)
            {
                // The latch owns the tint: it has to survive the finger lifting, which is the
                // whole point of a lock.
                if (Action == HudAction.Sprint && hub != null) hub.ToggleSprintLock();
                ApplyTint(Latched);
                return;
            }

            ApplyTint(true);
            if (hub == null) return;

            if (IsHoldButton)
            {
                SetHeldState(hub, true);
                return;
            }

            switch (Action)
            {
                case HudAction.Jump: hub.QueueJump(); break;
                case HudAction.Interact: hub.QueueInteract(); break;
                case HudAction.Attack: hub.QueueAttack(); break;
                case HudAction.Reload: hub.QueueReload(); break;
                case HudAction.Crouch: hub.QueueCrouch(); break;
                case HudAction.Prone: hub.QueueProne(); break;
                case HudAction.WeaponSwitch: hub.QueueWeaponSwitch(); break;
                case HudAction.Horn: hub.QueueHorn(); break;
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            Release();
        }

        /// <summary>Whether a latching button is currently locked on.</summary>
        public bool Latched
        {
            get
            {
                var hub = InputHub.Instance;
                if (hub == null || !IsLatching) return false;
                return Action == HudAction.Sprint && hub.SprintLocked;
            }
        }

        void Release()
        {
            _pointer = -1;
            _held = false;

            // A latching button keeps whatever state the latch says, not "not pressed".
            ApplyTint(Latched);

            var hub = InputHub.Instance;
            if (hub != null && IsHoldButton && !IsLatching) SetHeldState(hub, false);
        }

        /// <summary>
        /// Routes a hold button's state to the matching field on the hub. Centralised so that
        /// hiding a button mid-press (a context switch) reliably releases whatever it was
        /// holding -- otherwise the throttle would stick on when you exit a vehicle.
        /// </summary>
        void SetHeldState(InputHub hub, bool held)
        {
            switch (Action)
            {
                case HudAction.Sprint: hub.TouchSprintHeld = held; break;
                case HudAction.Aim: hub.TouchAimHeld = held; break;
                case HudAction.Gas: hub.TouchGasHeld = held; break;
                case HudAction.Brake: hub.TouchBrakeHeld = held; break;
                case HudAction.Handbrake: hub.TouchHandbrakeHeld = held; break;

                // FIRE is both. The queue gives a single tap exactly one shot -- which is
                // what melee wants -- and the held flag lets an automatic weapon keep firing
                // at its own cooldown for as long as the thumb stays down.
                case HudAction.Attack:
                    hub.TouchFireHeld = held;
                    if (held) hub.QueueAttack();
                    break;
            }
        }

        void ApplyTint(bool pressed)
        {
            if (Background == null) return;
            Background.color = pressed ? PressedTint : NormalTint;

            // Sprite swap on top of the tint, where the theme supplied both states. A tint
            // multiplies painted art rather than replacing it, so on the GUI kit's buttons the
            // tint alone reads as "the same button, dimmer" instead of "pressed".
            if (NormalSprite == null || PressedSprite == null) return;
            if (Background is Image image) image.sprite = pressed ? PressedSprite : NormalSprite;
        }
    }
}
