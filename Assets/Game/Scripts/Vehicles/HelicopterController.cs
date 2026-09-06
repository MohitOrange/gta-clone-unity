using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A helicopter flown with one stick and two altitude buttons, with the nose steering
    /// itself.
    ///
    /// <b>Why not a real flight model.</b> A faithful helicopter needs collective, cyclic,
    /// anti-torque pedals and a throttle -- four axes, two of them coupled, on a device with
    /// one thumb free. That is a simulator's control scheme and it is unflyable on a phone.
    /// The agreed scheme is a single stick for where you want to go, two buttons for how high,
    /// and yaw handled automatically, so the whole aircraft is three inputs and the player
    /// never has to think about the tail rotor.
    ///
    /// The physics is still real, not scripted: the rotor produces lift along the aircraft's
    /// own up axis, and the machine moves sideways <i>because</i> it tilts, exactly as a
    /// helicopter does. Tilting is what the stick controls. That keeps the feel honest --
    /// banking into a turn, sagging when you level off, drifting on after you let go -- while
    /// the input stays simple enough for a thumb.
    /// </summary>
    public class HelicopterController : Vehicle
    {
        [Header("Rotor")]
        [Tooltip("Lift at full collective, as a multiple of the aircraft's own weight. Must "
                 + "exceed 1 or it can never leave the ground.")]
        public float LiftToWeight = 2.1f;
        [Tooltip("Seconds for the rotor to reach full speed from cold. Nothing flies until it "
                 + "has, which is what stops a stolen helicopter leaping off the pad.")]
        public float SpoolUpSeconds = 3.5f;
        [Tooltip("Fraction of rotor speed below which there is no meaningful lift.")]
        [Range(0f, 1f)] public float MinFlyingRotor = 0.55f;

        [Header("Attitude")]
        [Tooltip("Maximum tilt from the stick, in degrees. More tilt means faster travel and "
                 + "a more aggressive-looking aircraft.")]
        public float MaxTiltDegrees = 22f;
        [Tooltip("How briskly the airframe chases the tilt the stick is asking for.")]
        public float TiltResponse = 2.6f;
        [Tooltip("How briskly the nose swings round to face the way it is moving.")]
        public float AutoYawResponse = 1.8f;
        [Tooltip("Ground speed below which the nose stops chasing, so a hover does not spin.")]
        public float AutoYawMinSpeed = 2.5f;

        [Header("Handling")]
        public float MaxClimbRate = 9f;
        public float MaxSpeedKph = 145f;
        [Tooltip("Sideways and backwards drag. Higher values make it feel less like a puck.")]
        public float LateralDrag = 0.9f;
        [Tooltip("Extra lift very close to the ground, as real rotor downwash produces.")]
        public float GroundEffectHeight = 6f;
        public float GroundEffectBoost = 0.25f;

        Vector2 _cyclic;
        float _collective;
        float _rotor;

        /// <summary>Rotor speed, 0 to 1. Public so a HUD or audio can read it.</summary>
        public float RotorSpeed => _rotor;

        /// <summary>Whether the rotor is turning fast enough to fly.</summary>
        public bool CanFly => _rotor >= MinFlyingRotor && !IsWrecked;

        /// <summary>Metres above whatever is directly below. Negative if nothing is.</summary>
        public float AltitudeAboveGround { get; private set; } = -1f;

        protected override void Awake()
        {
            base.Awake();
            Kind = VehicleKind.Helicopter;

            // A helicopter hangs from its rotor, so the mass sits below the hub. Without this
            // it handles like a brick balanced on a pole and flips at the first input.
            Body.centerOfMass = new Vector3(0f, -0.6f, 0f);
        }

        public override void SetFlightInput(Vector2 cyclic, float collective)
        {
            _cyclic = Vector2.ClampMagnitude(cyclic, 1f);
            _collective = Mathf.Clamp(collective, -1f, 1f);
        }

        public override void OnExit(GameObject occupant)
        {
            base.OnExit(occupant);

            // Left alone, it settles rather than holding whatever the pilot last asked for.
            _cyclic = Vector2.zero;
            _collective = 0f;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            Spool(dt);
            Measure();

            if (!CanFly)
            {
                // A dead or spinning-down rotor does not hold the aircraft up. Gravity is left
                // to do its work, which is also the wrecked-helicopter behaviour.
                return;
            }

            ApplyLift(dt);
            ApplyTilt(dt);
            ApplyAutoYaw(dt);
            ApplyDrag(dt);
            ClampSpeed();
        }

        /// <summary>
        /// Rotor spins up while occupied and winds down when abandoned or wrecked.
        /// </summary>
        void Spool(float dt)
        {
            bool wants = IsOccupied && !IsWrecked;
            float target = wants ? 1f : 0f;
            float rate = 1f / Mathf.Max(0.01f, SpoolUpSeconds);

            // Winding down is slower than spinning up: a rotor has a lot of inertia, and an
            // engine cut mid-air should give the player a moment, not drop them instantly.
            _rotor = Mathf.MoveTowards(_rotor, target, rate * dt * (wants ? 1f : 0.4f));
        }

        void Measure()
        {
            AltitudeAboveGround = Physics.Raycast(transform.position + Vector3.up * 0.5f,
                                                  Vector3.down, out RaycastHit hit, 200f,
                                                  ~0, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : -1f;
        }

        /// <summary>
        /// Lift along the aircraft's own up axis, trimmed to hold altitude at neutral stick.
        /// </summary>
        void ApplyLift(float dt)
        {
            // Hover trim: at neutral collective the rotor exactly cancels gravity, so letting
            // go holds height instead of sinking. Without this the player is forever tapping
            // climb just to stay level, which reads as a broken aircraft rather than a
            // demanding one.
            float hover = Body.mass * -Physics.gravity.y;
            float commanded = hover * (1f + _collective * (LiftToWeight - 1f));

            if (AltitudeAboveGround >= 0f && AltitudeAboveGround < GroundEffectHeight)
            {
                float t = 1f - AltitudeAboveGround / GroundEffectHeight;
                commanded *= 1f + GroundEffectBoost * t;
            }

            Body.AddForce(transform.up * (commanded * _rotor), ForceMode.Force);

            // Climb rate is limited directly rather than by force alone, so the aircraft has a
            // ceiling on how fast it can leave and does not accelerate upward without end.
            var v = Body.linearVelocity;
            v.y = Mathf.Clamp(v.y, -MaxClimbRate * 1.4f, MaxClimbRate);
            Body.linearVelocity = v;
        }

        /// <summary>
        /// Tilts toward the stick. Movement is a consequence of the tilt, not a separate push.
        /// </summary>
        void ApplyTilt(float dt)
        {
            float pitch = -_cyclic.y * MaxTiltDegrees;
            float roll = -_cyclic.x * MaxTiltDegrees;

            Quaternion want = Quaternion.Euler(pitch, transform.eulerAngles.y, roll);
            Quaternion next = Quaternion.Slerp(Body.rotation, want, 1f - Mathf.Exp(-TiltResponse * dt));

            Body.MoveRotation(next);
        }

        /// <summary>
        /// Swings the nose round to face the direction of travel.
        ///
        /// This is the whole reason the player never touches a pedal. It only engages above a
        /// walking pace, because a hovering helicopter has no direction of travel to face and
        /// would otherwise pirouette on the spot chasing its own noise.
        /// </summary>
        void ApplyAutoYaw(float dt)
        {
            Vector3 flat = Body.linearVelocity;
            flat.y = 0f;
            if (flat.magnitude < AutoYawMinSpeed) return;

            Quaternion facing = Quaternion.LookRotation(flat.normalized, Vector3.up);
            Quaternion next = Quaternion.Slerp(Body.rotation, facing,
                                               1f - Mathf.Exp(-AutoYawResponse * dt));

            // Yaw only: the tilt is owned by ApplyTilt and must not be undone here.
            Vector3 e = next.eulerAngles;
            Vector3 cur = Body.rotation.eulerAngles;
            Body.MoveRotation(Quaternion.Euler(cur.x, e.y, cur.z));
        }

        void ApplyDrag(float dt)
        {
            // Resist sideways and rearward drift so the aircraft has a clear nose-first
            // preference, without adding drag along the direction it is trying to go.
            Vector3 local = transform.InverseTransformDirection(Body.linearVelocity);
            local.x = Mathf.MoveTowards(local.x, 0f, LateralDrag * dt * 10f);
            if (local.z < 0f) local.z = Mathf.MoveTowards(local.z, 0f, LateralDrag * dt * 6f);
            Body.linearVelocity = transform.TransformDirection(local);
        }

        void ClampSpeed()
        {
            float max = MaxSpeedKph / 3.6f;
            Vector3 flat = Body.linearVelocity;
            float y = flat.y;
            flat.y = 0f;

            if (flat.magnitude > max) flat = flat.normalized * max;

            flat.y = y;
            Body.linearVelocity = flat;
        }
    }
}
