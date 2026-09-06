using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Ocean boat driven by buoyancy sample points rather than wheels.
    ///
    /// Each float point pushes up in proportion to how deep it sits below the wave surface,
    /// which produces pitch and roll from the wave shape for free -- the hull rocks because
    /// the bow and stern are at different wave heights, not because anything animates it.
    /// Uses the same <see cref="WaterVolume"/> the swimming code reads, so the boat sits on
    /// exactly the surface the player sees.
    /// </summary>
    public class BoatController : Vehicle
    {
        [Header("Buoyancy")]
        [Tooltip("Hull sample points. Four corners is the minimum for pitch and roll.")]
        public Transform[] FloatPoints = new Transform[0];
        [Tooltip("Total upward force at full submersion, as a multiple of the hull's own weight. " +
                 "Must exceed 1 or the boat sinks; the hull settles where buoyancy balances weight, " +
                 "so 2.2 floats it with roughly half its draft under water.")]
        public float BuoyancyStrength = 2.2f;
        [Tooltip("Submersion depth at which buoyancy saturates.")]
        public float MaxSubmersion = 1.2f;

        [Header("Drag")]
        public float WaterDrag = 1.4f;
        public float WaterAngularDrag = 2.6f;
        public float AirDrag = 0.08f;
        public float AirAngularDrag = 0.35f;

        [Header("Drivetrain")]
        public float ThrustForce = 5200f;
        public float ReverseFactor = 0.45f;
        public float TurnTorque = 2600f;
        [Tooltip("A rudder only bites when water is moving past it, so turning needs way on.")]
        public float MinSteerSpeed = 0.8f;
        public float TopSpeedKph = 70f;

        [Header("Feel")]
        [Tooltip("Sideways grip. Higher values stop the hull sliding across the water.")]
        public float LateralGrip = 2.4f;
        public Vector3 CentreOfMass = new Vector3(0f, -0.35f, 0f);

        int _submergedPoints;

        /// <summary>True when at least one hull point is in the water.</summary>
        public bool IsAfloat => _submergedPoints > 0;

        protected override void Awake()
        {
            base.Awake();
            Body.centerOfMass = CentreOfMass;
        }

        void FixedUpdate()
        {
            var water = WaterVolume.Instance;
            if (water == null) return;

            ApplyBuoyancy(water);
            ApplyDragForState();

            if (_submergedPoints > 0)
            {
                ApplyThrust();
                ApplySteering();
                ApplyLateralGrip();
            }
        }

        void ApplyBuoyancy(WaterVolume water)
        {
            _submergedPoints = 0;
            if (FloatPoints.Length == 0) return;

            float perPoint = BuoyancyStrength * Body.mass * -Physics.gravity.y / FloatPoints.Length;

            foreach (var p in FloatPoints)
            {
                if (p == null) continue;

                float surface = water.SurfaceHeightAt(p.position);
                float depth = surface - p.position.y;
                if (depth <= 0f) continue;

                _submergedPoints++;

                float t = Mathf.Clamp01(depth / MaxSubmersion);
                Body.AddForceAtPosition(Vector3.up * (perPoint * t), p.position, ForceMode.Force);
            }
        }

        void ApplyDragForState()
        {
            bool wet = _submergedPoints > 0;
            Body.linearDamping = wet ? WaterDrag : AirDrag;
            Body.angularDamping = wet ? WaterAngularDrag : AirAngularDrag;
        }

        void ApplyThrust()
        {
            if (SpeedKph >= TopSpeedKph && Throttle > 0f) return;

            float scale = Throttle >= 0f ? 1f : ReverseFactor;
            float submersion = _submergedPoints / (float)Mathf.Max(1, FloatPoints.Length);

            Body.AddForce(transform.forward * (Throttle * ThrustForce * scale * submersion),
                          ForceMode.Force);
        }

        void ApplySteering()
        {
            float way = Mathf.Abs(ForwardSpeed);
            if (way < MinSteerSpeed) return;

            float authority = Mathf.Clamp01(way / 6f);
            // Reverse the rudder when going astern, as a real boat does.
            float direction = ForwardSpeed >= 0f ? 1f : -1f;

            Body.AddTorque(Vector3.up * (Steer * TurnTorque * authority * direction), ForceMode.Force);
        }

        void ApplyLateralGrip()
        {
            // Kill sideways velocity so the hull tracks its heading instead of drifting.
            Vector3 lateral = Vector3.Project(Body.linearVelocity, transform.right);
            Body.AddForce(-lateral * LateralGrip, ForceMode.Acceleration);
        }

        void OnDrawGizmosSelected()
        {
            if (FloatPoints == null) return;
            Gizmos.color = new Color(0.2f, 0.7f, 1f);
            foreach (var p in FloatPoints)
                if (p != null) Gizmos.DrawWireSphere(p.position, 0.28f);
        }
    }
}
