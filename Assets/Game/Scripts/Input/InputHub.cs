using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MiniGTA
{
    /// <summary>
    /// Single source of truth for player intent, whatever the device.
    ///
    /// On-screen widgets (joystick, look pad, buttons) push into the Touch* fields;
    /// keyboard and mouse are polled here. Gameplay code only ever reads the merged
    /// properties, so it never needs to know which scheme is driving it -- and both
    /// schemes stay live simultaneously, which is what makes desktop testing of a
    /// mobile control layout practical.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class InputHub : MonoBehaviour
    {
        static InputHub _instance;

        /// <summary>
        /// The hub, resolved lazily if the static reference has been lost.
        ///
        /// Every other singleton in this project falls back to a scene search; this one did
        /// not, and it is the one thing every system asks for. An Editor domain reload during
        /// Play mode clears the static without re-running Awake, so the reference stayed null
        /// for the rest of the session and all input silently stopped -- movement, interact,
        /// attack, driving. A player build never domain-reloads, so this was Editor-only, but
        /// it made Editor testing unreliable in a way that looked like game bugs.
        /// </summary>
        public static InputHub Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<InputHub>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Driving stick")]
        [Tooltip("Stick travel below this reads as centred. A virtual thumbstick never returns "
                 + "exactly to zero -- a resting thumb leaves a few percent of drift, which "
                 + "without this creeps the car forward at a standstill.")]
        [Range(0f, 0.5f)] public float DriveDeadzone = 0.15f;

        [Tooltip("Seconds for the throttle to travel its full range. Acceleration and lift-off "
                 + "are blended over this so a flicked stick does not step the torque.")]
        public float ThrottleSmoothing = 0.12f;

        [Tooltip("Seconds to cross the whole range when the stick reverses sign. Deliberately "
                 + "quicker than ThrottleSmoothing: slamming forward-to-back is a braking "
                 + "input, and a driver expects that to bite immediately.")]
        public float ThrottleReversalSmoothing = 0.05f;

        [Header("Look sensitivity")]
        [Tooltip("Degrees per 100 pixels of finger travel.")]
        public float TouchLookSensitivity = 22f;
        [Tooltip("Degrees per 100 units of mouse delta.")]
        public float MouseLookSensitivity = 12f;

        // --- Written by the on-screen HUD widgets ---
        [HideInInspector] public Vector2 TouchMove;      // normalised joystick vector
        [HideInInspector] public Vector2 TouchLookDelta; // pixels this frame, additive
        [HideInInspector] public bool TouchSprintHeld;

        /// <summary>
        /// Held for as long as the thumb is on FIRE.
        ///
        /// The trigger is a hold, not a tap. It used to be momentary -- one queued shot per
        /// press -- which meant a rifle with a 0.09 s cooldown fired at whatever rate the
        /// player could tap, and "the gun does not work" was the reasonable conclusion.
        /// The one-shot queue below is kept alongside it so a single tap still fires exactly
        /// once, which is what melee and a keyboard press want.
        /// </summary>
        [HideInInspector] public bool TouchFireHeld;

        /// <summary>Held while the thumb is on AIM. Drives the aim-down-sights stance.</summary>
        [HideInInspector] public bool TouchAimHeld;

        // --- Driving widgets ---
        [HideInInspector] public bool TouchGasHeld;
        [HideInInspector] public bool TouchBrakeHeld;
        [HideInInspector] public bool TouchHandbrakeHeld;

        bool _jumpQueued;
        bool _interactQueued;
        bool _attackQueued;
        bool _reloadQueued;
        bool _hornQueued;

        bool _keyboardHandbrake;

        Vector2 _keyboardMove;
        Vector2 _mouseLookDelta;
        bool _keyboardSprint;
        bool _keyboardFire;
        bool _keyboardAim;

        float _throttle;

        /// <summary>Desired movement in local input space, magnitude clamped to 1.</summary>
        public Vector2 Move
        {
            get
            {
                // Whichever scheme is pushing harder wins, so a resting joystick never
                // cancels keyboard input and vice versa.
                var v = TouchMove.sqrMagnitude >= _keyboardMove.sqrMagnitude ? TouchMove : _keyboardMove;
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        /// <summary>
        /// Camera look delta in degrees for this frame. Reading it clears the accumulated
        /// touch swipe, so exactly one consumer (the camera rig) may call it per frame --
        /// clearing on a timer instead would race the camera's LateUpdate.
        /// </summary>
        public Vector2 ConsumeLookDelta()
        {
            var v = TouchLookDelta * (TouchLookSensitivity * 0.01f)
                    + _mouseLookDelta * (MouseLookSensitivity * 0.01f);
            TouchLookDelta = Vector2.zero;
            _mouseLookDelta = Vector2.zero;
            return v;
        }

        public bool SprintHeld => TouchSprintHeld || _keyboardSprint || SprintLocked;

        /// <summary>Flips the run lock. Called by the RUN button.</summary>
        public void ToggleSprintLock() => SprintLocked = !SprintLocked;

        /// <summary>
        /// Drops the run lock. Called when sprinting stops meaning anything -- getting into a
        /// vehicle, dying, or a menu opening -- so the player does not step back out of a car
        /// already sprinting because of a tap from two minutes ago.
        /// </summary>
        public void ClearSprintLock() => SprintLocked = false;

        /// <summary>True every frame the trigger is down. Automatic fire reads this.</summary>
        public bool FireHeld => TouchFireHeld || _keyboardFire;

        /// <summary>Held while the aim control is down.</summary>
        public bool AimHeld => TouchAimHeld || _keyboardAim;

        /// <summary>
        /// Run lock. Tapping RUN latches sprint on until it is tapped again.
        ///
        /// Holding a button down for the length of a street is the single most tiring thing a
        /// touch layout can ask for, and it competes with the same thumb that steers. The lock
        /// is deliberately a separate piece of state from <see cref="TouchSprintHeld"/> rather
        /// than a fake continuous press, so a held keyboard Shift and a latched button can
        /// coexist without either clearing the other.
        /// </summary>
        [HideInInspector] public bool SprintLocked;

        // ---------------------------------------------------------------- driving

        /// <summary>
        /// Vehicle throttle, -1 (reverse) to 1 (forward), smoothed.
        ///
        /// <b>One stick drives the car.</b> Push forward to accelerate, pull back to reverse,
        /// and how far you push is how much throttle you get -- the same stick that steers.
        /// There are no gas or reverse buttons: a thumb cannot hold a pedal and steer with the
        /// same hand, which is why the pedal pair was the weakest part of the driving feel.
        ///
        /// The pedal buttons are still honoured if something is holding them, so an older HUD
        /// layout or a rebuild that still has them keeps working rather than going dead.
        /// </summary>
        public float Throttle => _throttle;

        /// <summary>Throttle before smoothing. For diagnostics and the HUD readout.</summary>
        public float RawThrottle { get; private set; }

        /// <summary>Steering, -1 to 1. The movement stick's X axis doubles as the wheel.</summary>
        public float Steer => Mathf.Clamp(DeadzonedStick().x, -1f, 1f);

        /// <summary>
        /// The drive stick with its deadzone removed, rescaled so the first responsive degree
        /// of travel still maps to near-zero input.
        ///
        /// Radial, not per-axis: a square deadzone lets a diagonal push through at a magnitude
        /// a straight push would have swallowed, which makes the car twitch on the diagonals.
        /// </summary>
        Vector2 DeadzonedStick()
        {
            Vector2 raw = Move;
            float magnitude = raw.magnitude;

            if (magnitude <= DriveDeadzone) return Vector2.zero;

            float scaled = Mathf.InverseLerp(DriveDeadzone, 1f, magnitude);
            return raw / magnitude * Mathf.Clamp01(scaled);
        }

        /// <summary>Where the throttle wants to be this frame, before smoothing.</summary>
        float TargetThrottle()
        {
            // A held pedal still wins, so a HUD that still has them is not broken by this.
            if (TouchGasHeld) return 1f;
            if (TouchBrakeHeld) return -1f;

            return Mathf.Clamp(DeadzonedStick().y, -1f, 1f);
        }

        void TickThrottle(float dt)
        {
            float target = TargetThrottle();
            RawThrottle = target;

            // Crossing zero is a reversal -- brake, then reverse -- and wants the quicker rate.
            bool reversing = target * _throttle < 0f;
            float seconds = reversing ? ThrottleReversalSmoothing : ThrottleSmoothing;

            _throttle = seconds <= 0.0001f
                ? target
                : Mathf.MoveTowards(_throttle, target, dt / seconds);
        }

        public bool HandbrakeHeld => TouchHandbrakeHeld || _keyboardHandbrake;

        /// <summary>
        /// Altitude for aircraft: +1 climb, -1 descend.
        ///
        /// Deliberately reuses the on-screen gas and brake buttons. In the air the stick is
        /// already carrying movement, so those two buttons are free -- and "altitude buttons"
        /// is precisely the control scheme chosen for helicopters. Reusing them means the
        /// touch HUD needs no new controls and the thumb does not have to move.
        /// </summary>
        public float Climb
        {
            get
            {
                float v = 0f;
                if (TouchGasHeld || _keyboardClimb) v += 1f;
                if (TouchBrakeHeld || _keyboardDescend) v -= 1f;
                return v;
            }
        }

        bool _keyboardClimb;
        bool _keyboardDescend;

        public bool ConsumeHorn() { bool v = _hornQueued; _hornQueued = false; return v; }
        public void QueueHorn() { _hornQueued = true; }

        public bool IsUsingTouch { get; private set; }

        /// <summary>True once per press. Reading it clears it.</summary>
        public bool ConsumeJump() { bool v = _jumpQueued; _jumpQueued = false; return v; }
        public bool ConsumeAttack() { bool v = _attackQueued; _attackQueued = false; return v; }

        /// <summary>
        /// True once per press of RELOAD.
        ///
        /// Before this there was no way to reload deliberately at all: WeaponController.TryReload
        /// had exactly one caller, the empty-magazine fallback inside PlayerCombat.Attack, so a
        /// half-empty magazine could only be topped up by firing it dry first.
        /// </summary>
        public bool ConsumeReload() { bool v = _reloadQueued; _reloadQueued = false; return v; }

        /// <summary>
        /// Unconditional Interact. Only for the one consumer that owns the button outright --
        /// getting out of a vehicle, where nothing else can be competing for the press.
        /// Everything in the world must go through <see cref="ConsumeInteract(Component)"/>.
        /// </summary>
        public bool ConsumeInteract() { bool v = _interactQueued; _interactQueued = false; return v; }

        // ------------------------------------------------------- interact arbitration
        //
        // One Interact button, six things in the world that answer to it: doors, contacts,
        // shopkeepers, weapon pickups, for-sale properties and parked vehicles. They used to
        // race -- whoever's Update ran first called ConsumeInteract(), cleared the flag and
        // acted, and the rest silently got nothing. Script execution order decided it, so a
        // press made standing 0.6 m from a pistol and 2.0 m from a mission contact accepted
        // the mission.
        //
        // Now each candidate *claims* the button every frame with its distance and a priority
        // tier, and only the winner may consume the press. Claims gathered during one frame's
        // Updates are promoted to the winner at the top of the next -- this component runs at
        // execution order -200, so "the top of the next frame" is before any of them run again.
        // The one-frame delay is invisible: a candidate has to be in range for a frame before
        // the player can press anything.

        /// <summary>Parked vehicles. Lowest, so a door or a person beside a car wins.</summary>
        public const int InteractPriorityVehicle = 10;
        /// <summary>Things lying on the ground: weapon pickups, property For Sale signs.</summary>
        public const int InteractPriorityItem = 20;
        /// <summary>Doors, mission contacts, shopkeepers -- a deliberate approach.</summary>
        public const int InteractPriorityContact = 30;

        Component _bestClaim;
        float _bestClaimDistance;
        int _bestClaimPriority;
        Component _interactWinner;

        /// <summary>Who currently owns the Interact button, or null.</summary>
        public Component InteractWinner => _interactWinner;

        /// <summary>
        /// Offer to handle the next Interact press. Call every frame while in range, before
        /// asking to consume. Higher priority wins; within a tier, the nearest wins.
        /// </summary>
        public void ClaimInteract(Component claimant, float distance, int priority)
        {
            if (claimant == null) return;

            if (_bestClaim != null)
            {
                if (priority < _bestClaimPriority) return;
                if (priority == _bestClaimPriority && distance >= _bestClaimDistance) return;
            }

            _bestClaim = claimant;
            _bestClaimDistance = distance;
            _bestClaimPriority = priority;
        }

        /// <summary>
        /// True once, for the claimant that won the arbitration. Everyone else gets false and
        /// the press is left intact for the winner however the Update order falls.
        /// </summary>
        public bool ConsumeInteract(Component claimant)
        {
            if (claimant == null || _interactWinner != claimant) return false;
            return ConsumeInteract();
        }

        void ResolveInteractClaims()
        {
            _interactWinner = _bestClaim;
            _bestClaim = null;
            _bestClaimPriority = int.MinValue;
            _bestClaimDistance = float.MaxValue;
        }

        public void QueueJump() { _jumpQueued = true; }
        public void QueueInteract() { _interactQueued = true; }
        public void QueueAttack() { _attackQueued = true; }
        public void QueueReload() { _reloadQueued = true; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Update()
        {
            // First thing in the frame, at execution order -200: last frame's claims become
            // this frame's winner. Ahead of the focus check below, so losing focus cannot
            // strand a stale winner holding the button.
            ResolveInteractClaims();

            _keyboardMove = Vector2.zero;
            _mouseLookDelta = Vector2.zero;
            _keyboardSprint = false;
            _keyboardFire = false;
            _keyboardAim = false;

            // A key held while the app loses focus would otherwise latch on and walk the
            // player away unattended -- on mobile that means moving during a phone call.
            if (!Application.isFocused)
            {
                TouchMove = Vector2.zero;
                TouchSprintHeld = false;
                SprintLocked = false;
                TouchFireHeld = false;
                TouchAimHeld = false;
                TouchGasHeld = false;
                TouchBrakeHeld = false;
                TouchHandbrakeHeld = false;
                _keyboardHandbrake = false;

                // Still tick it, so the car coasts to a stop rather than freezing at whatever
                // throttle was applied at the moment the call came in.
                TickThrottle(Time.unscaledDeltaTime);
                return;
            }

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                float x = 0f, y = 0f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
                _keyboardMove = new Vector2(x, y);

                _keyboardSprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

                if (kb.spaceKey.wasPressedThisFrame) _jumpQueued = true;
                if (kb.eKey.wasPressedThisFrame) _interactQueued = true;
                if (kb.fKey.wasPressedThisFrame) _attackQueued = true;
                if (kb.rKey.wasPressedThisFrame) _reloadQueued = true;
                if (kb.hKey.wasPressedThisFrame) _hornQueued = true;

                // Held F is automatic fire on the desktop build, so the same code path the
                // thumb exercises is reachable without a touchscreen.
                _keyboardFire = kb.fKey.isPressed;

                // Space doubles as the handbrake: jumping is meaningless behind the wheel.
                _keyboardHandbrake = kb.spaceKey.isPressed;

                // Space climbs and left-ctrl descends in the air. Space is already "up" for
                // jumping on foot, so the meaning carries over rather than competing.
                _keyboardClimb = kb.spaceKey.isPressed;
                _keyboardDescend = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                _mouseLookDelta = mouse.delta.ReadValue();
                // Right-drag already aims the camera on desktop, and in a third-person
                // shooter holding that button is what "aim" means -- so it raises the
                // stance as well rather than needing a second key nobody would find.
                _keyboardAim = true;
            }

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
                IsUsingTouch = true;
            else if (kb != null && kb.anyKey.wasPressedThisFrame)
                IsUsingTouch = false;
#endif

            // Last, so it sees this frame's stick and keyboard values.
            TickThrottle(Time.unscaledDeltaTime);
        }

    }
}
