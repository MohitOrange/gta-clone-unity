using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// On-foot locomotion: walk, sprint, jump, fall and swim, driven entirely by
    /// <see cref="InputHub"/> so touch and keyboard behave identically.
    ///
    /// Movement is camera-relative (push the stick "up" and you run away from the camera,
    /// which is what every third-person game trains players to expect) and the body turns
    /// to face travel rather than strafing.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        public float WalkSpeed = 2.2f;
        public float RunSpeed = 5.8f;
        public float SwimSpeed = 2.4f;

        [Header("Acceleration")]
        [Tooltip("How quickly ground speed reaches the target. Higher is snappier.")]
        public float GroundAcceleration = 14f;
        [Tooltip("Air control as a fraction of ground acceleration.")]
        [Range(0f, 1f)] public float AirControl = 0.35f;
        [Tooltip("Section 7. While the weapon stance is up, face the camera so the stick "
                 + "strafes instead of turning -- which is what makes the directional "
                 + "locomotion clips reachable at all. Turn off to face travel always.")]
        public bool StrafeWhileAiming = true;

        public float TurnSmoothTime = 0.08f;

        [Header("Jump / gravity")]
        public float JumpHeight = 1.25f;
        public float Gravity = -22f;
        [Tooltip("Grace period after walking off a ledge during which a jump still works.")]
        public float CoyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing still fires on touchdown.")]
        public float JumpBuffer = 0.15f;

        [Header("Swimming")]
        [Tooltip("Height up the capsule at which submersion flips the player into swim mode.")]
        [Range(0f, 1f)] public float SwimEntryHeight = 0.62f;
        [Tooltip("How much LOWER on the body the exit probe sits. Exiting must be harder than " +
                 "entering, or the player flickers between wading and swimming in wave chop.")]
        [Range(0f, 0.5f)] public float SwimExitMargin = 0.18f;
        [Tooltip("How strongly the swimmer is pulled to the surface.")]
        public float Buoyancy = 6f;
        [Tooltip("How far the character origin (the feet) rests below the surface while floating. " +
                 "Must be deep enough to keep the chest under water, otherwise the swim state " +
                 "cancels itself the instant buoyancy lifts the body.")]
        public float SwimFloatDepth = 1.25f;

        [Header("Refs")]
        public Transform CameraTarget;
        public HudContext Hud;

        CharacterController _cc;
        PlayerStance _stance;
        PlayerAnimation _anim;
        Transform _cam;

        Vector3 _horizontalVelocity;
        float _verticalVelocity;
        float _turnVelocity;
        float _lastGroundedTime = -99f;
        float _lastJumpPressTime = -99f;
        bool _isSwimming;

        public bool IsGrounded { get; private set; }
        public bool IsSwimming => _isSwimming;
        public float PlanarSpeed => _horizontalVelocity.magnitude;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _stance = GetComponent<PlayerStance>();
            _anim = GetComponentInChildren<PlayerAnimation>();
            if (Camera.main != null) _cam = Camera.main.transform;
        }

        void Update()
        {
            var hub = InputHub.Instance;
            if (hub == null) return;

            if (_cam == null && Camera.main != null) _cam = Camera.main.transform;

            if (hub.ConsumeJump()) _lastJumpPressTime = Time.time;

            UpdateSwimState();

            if (_isSwimming) TickSwimming(hub);
            else TickGrounded(hub);

            PushAnimation();
            PushHudContext();
        }

        // ---------------------------------------------------------------- on foot

        void TickGrounded(InputHub hub)
        {
            IsGrounded = _cc.isGrounded;
            if (IsGrounded) _lastGroundedTime = Time.time;

            Vector3 wish = CameraRelative(hub.Move);
            float targetSpeed = hub.SprintHeld ? RunSpeed : WalkSpeed;

            // Crouching and going prone slow you down. Applied here rather than inside
            // PlayerStance so there is still exactly one line that decides ground speed.
            if (_stance != null) targetSpeed *= _stance.SpeedFactor;
            Vector3 targetVelocity = wish * (targetSpeed * hub.Move.magnitude);

            float accel = GroundAcceleration * (IsGrounded ? 1f : AirControl);
            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity, targetVelocity, accel * Time.deltaTime);

            // Stick to slopes: a small constant downward bias stops the controller from
            // stair-stepping into the air on every downhill frame.
            if (IsGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;

            bool canJump = Time.time - _lastGroundedTime <= CoyoteTime;
            bool wantsJump = Time.time - _lastJumpPressTime <= JumpBuffer;
            if (canJump && wantsJump)
            {
                _verticalVelocity = Mathf.Sqrt(-2f * Gravity * JumpHeight);
                _lastJumpPressTime = -99f;
                _lastGroundedTime = -99f;
                IsGrounded = false;
                if (_anim != null) _anim.TriggerJump();
            }

            _verticalVelocity += Gravity * Time.deltaTime;

            FaceTravel(wish, hub.Move.magnitude);

            Vector3 motion = _horizontalVelocity + Vector3.up * _verticalVelocity;
            _cc.Move(motion * Time.deltaTime);
        }

        // ---------------------------------------------------------------- swimming

        void TickSwimming(InputHub hub)
        {
            IsGrounded = false;

            Vector3 wish = CameraRelative(hub.Move);
            float speed = SwimSpeed * (hub.SprintHeld ? 1.45f : 1f);
            Vector3 targetVelocity = wish * (speed * hub.Move.magnitude);

            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity, targetVelocity, GroundAcceleration * 0.5f * Time.deltaTime);

            // Seek the surface rather than snapping to it, so entering water has weight.
            float floatLine = SurfaceHeight() - SwimFloatDepth;
            float error = floatLine - transform.position.y;
            _verticalVelocity = Mathf.Lerp(_verticalVelocity, error * Buoyancy, 1f - Mathf.Exp(-6f * Time.deltaTime));
            _verticalVelocity = Mathf.Clamp(_verticalVelocity, -4f, 4f);

            FaceTravel(wish, hub.Move.magnitude);

            Vector3 motion = _horizontalVelocity + Vector3.up * _verticalVelocity;
            _cc.Move(motion * Time.deltaTime);
        }

        void UpdateSwimState()
        {
            var water = WaterVolume.Instance;
            if (water == null) { SetSwimming(false); return; }

            if (_isSwimming)
            {
                // Exit probe sits BELOW the entry probe, so the body has to rise clear of the
                // water before the player stands up. A probe above the entry point would make
                // leaving easier than arriving and the state would oscillate every frame.
                float exitHeight = Mathf.Max(0f, SwimEntryHeight - SwimExitMargin);
                Vector3 exitProbe = transform.position + Vector3.up * (_cc.height * exitHeight);
                if (!water.IsSubmerged(exitProbe)) SetSwimming(false);
            }
            else
            {
                // Test at chest height: wading through shallows should not trigger a swim.
                Vector3 chest = transform.position + Vector3.up * (_cc.height * SwimEntryHeight);
                if (water.IsSubmerged(chest)) SetSwimming(true);
            }
        }

        void SetSwimming(bool value)
        {
            if (_isSwimming == value) return;
            _isSwimming = value;
            if (value) _verticalVelocity = Mathf.Max(_verticalVelocity, -2f);
        }

        float SurfaceHeight()
        {
            var water = WaterVolume.Instance;
            return water != null ? water.SurfaceHeightAt(transform.position) : 0f;
        }

        // ---------------------------------------------------------------- shared

        /// <summary>Maps stick input into world space relative to where the camera looks.</summary>
        Vector3 CameraRelative(Vector2 move)
        {
            if (move.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 fwd = _cam != null ? _cam.forward : Vector3.forward;
            Vector3 right = _cam != null ? _cam.right : Vector3.right;

            fwd.y = 0f; right.y = 0f;
            fwd.Normalize(); right.Normalize();

            return (fwd * move.y + right * move.x).normalized;
        }

        /// <summary>
        /// Turns the body toward where it is travelling -- unless the weapon is up.
        ///
        /// <b>Why the exception exists.</b> Section 7 wired the pack's 21 directional clips into
        /// a 2D locomotion blend so that strafing and backpedalling stop playing the forward
        /// cycle sideways. Then measuring it showed MoveX pinned at 0.00 and MoveY at 1.00 no
        /// matter which way the stick went: this controller rotates the body to face travel, so
        /// local-space velocity is *always* straight ahead and the game had no strafing for the
        /// blend to serve. The clips were reachable only in principle.
        ///
        /// While the weapon stance is up the body faces the camera instead, so the stick moves
        /// the character around a fixed facing and the directional set is what plays. That is
        /// also the genre convention -- you keep pointing at the thing you are shooting.
        ///
        /// <b>This is a feel change, and it is one boolean.</b> Set StrafeWhileAiming false and
        /// the character faces travel at all times exactly as before; the blend then simply
        /// never leaves its forward quadrant, which is where it was.
        /// </summary>
        void FaceTravel(Vector3 wish, float inputMagnitude)
        {
            if (StrafeWhileAiming && _anim != null && _anim.InAimStance)
            {
                FaceCamera();
                return;
            }

            if (inputMagnitude < 0.05f || wish.sqrMagnitude < 0.0001f) return;

            float target = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(
                transform.eulerAngles.y, target, ref _turnVelocity, TurnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        /// <summary>Face the camera, so movement becomes strafing rather than turning.</summary>
        void FaceCamera()
        {
            if (_cam == null) return;

            Vector3 look = _cam.forward;
            look.y = 0f;
            if (look.sqrMagnitude < 0.0001f) return;

            float target = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(
                transform.eulerAngles.y, target, ref _turnVelocity, TurnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        float _lastFacing;

        void PushAnimation()
        {
            if (_anim == null) return;

            // Normalised so the blend tree reads 0 idle / 0.5 walk / 1 run regardless of
            // what the speed constants are tuned to.
            float speed01 = 0f;
            float planar = _horizontalVelocity.magnitude;
            if (planar > 0.05f)
            {
                speed01 = planar <= WalkSpeed
                    ? Mathf.InverseLerp(0f, WalkSpeed, planar) * 0.5f
                    : 0.5f + Mathf.InverseLerp(WalkSpeed, RunSpeed, planar) * 0.5f;
            }

            _anim.SetLocomotion(speed01, IsGrounded, _isSwimming, _verticalVelocity);

            // Section 7. Where the body is going relative to where it is facing, so the 2D
            // blend can pick a strafe or a backpedal instead of playing the forward cycle
            // sideways. Normalised: the tree wants a direction on the unit circle, and Speed
            // above already carries how fast.
            Vector3 local = transform.InverseTransformDirection(_horizontalVelocity);
            Vector2 dir = new Vector2(local.x, local.z);
            _anim.SetMoveDirection(dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up);

            // Turn-in-place: only meaningful while standing still. Signed degrees per second,
            // scaled into roughly -1..1 so the blend does not need to know the turn rate.
            float turnRate = Mathf.DeltaAngle(_lastFacing, transform.eulerAngles.y)
                           / Mathf.Max(0.0001f, Time.deltaTime);
            _lastFacing = transform.eulerAngles.y;
            _anim.SetTurn(planar < 0.15f ? Mathf.Clamp(turnRate / 180f, -1f, 1f) : 0f);
        }

        /// <summary>
        /// Publishes swimming-vs-on-foot, and nothing else.
        ///
        /// <b>This used to make PlayerContext.OnFootArmed unreachable.</b> Two components write
        /// the same enum from orthogonal facts: this one owns whether the player is in water,
        /// and PlayerCombat owns whether a weapon is up. The line here was an unconditional
        /// assignment of OnFoot, so whichever component's Update ran second in the frame won --
        /// and it was this one. PlayerCombat set OnFootArmed, this overwrote it with OnFoot, and
        /// the armed context never survived a single frame.
        ///
        /// It went unnoticed because HudContext.Apply treated OnFoot and OnFootArmed
        /// identically. The moment a control was made armed-only -- the aim and reload buttons --
        /// it became a control that could never appear.
        ///
        /// So: assert Swimming while swimming, clear it on leaving the water, and otherwise
        /// leave the on-foot variant alone for PlayerCombat to choose between.
        /// </summary>
        void PushHudContext()
        {
            if (Hud == null) return;
            if (Hud.Context == PlayerContext.Driving) return;   // vehicles own the HUD in Phase 2

            if (_isSwimming) { Hud.Context = PlayerContext.Swimming; return; }

            // Just climbed out. Drop to plain OnFoot; PlayerCombat raises it back to
            // OnFootArmed on its next tick if a weapon is in hand.
            if (Hud.Context == PlayerContext.Swimming) Hud.Context = PlayerContext.OnFoot;
        }

        /// <summary>Teleport helper used by the world builder and later by mission scripts.</summary>
        public void Warp(Vector3 position)
        {
            _cc.enabled = false;
            transform.position = position;
            _cc.enabled = true;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }
    }
}
