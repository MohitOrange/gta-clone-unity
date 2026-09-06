using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Four-wheeled vehicle built on WheelColliders.
    ///
    /// Handling is deliberately arcade: enough grip loss to drift on the handbrake, but a
    /// stable centre of mass and a downforce term so a phone-sized frame rate never flips the
    /// car. Terrain awareness comes free from the physics -- climbing costs speed because
    /// gravity opposes drive torque -- with an explicit slope assist so hills feel deliberate
    /// rather than merely sluggish.
    /// </summary>
    public class CarController : Vehicle
    {
        [System.Serializable]
        public class Wheel
        {
            public WheelCollider Collider;
            public Transform Mesh;
            public bool Steers;
            public bool Drives;
            public bool Brakes = true;

            [Tooltip("Rotation applied on top of the collider's pose, to line the mesh's own "
                   + "axes up with the collider's. Identity for a modelled wheel; 90 degrees "
                   + "about Z for a Unity cylinder primitive.")]
            public Vector3 MeshRotationEuler = new Vector3(0f, 0f, 90f);

            public Quaternion MeshRotationOffset => Quaternion.Euler(MeshRotationEuler);
        }

        [Header("Wheels")]
        public Wheel[] Wheels = new Wheel[0];

        [Header("Drivetrain")]
        public float MotorTorque = 1600f;
        public float BrakeTorque = 3200f;
        public float HandbrakeTorque = 4200f;
        public float MaxSteerAngle = 32f;
        [Tooltip("Steering authority shrinks with speed so the car is not twitchy at pace.")]
        public float SteerFalloffSpeed = 22f;
        public float TopSpeedKph = 130f;

        [Header("Stability")]
        [Tooltip("Local-space centre of mass. Low and slightly rearward keeps the car planted.")]
        public Vector3 CentreOfMass = new Vector3(0f, -0.45f, -0.1f);
        [Tooltip("Downforce per (m/s)^2. Cheap substitute for real aero that stops launches.")]
        public float Downforce = 12f;
        [Tooltip("Extra grip lost on the rear axle while the handbrake is down. 1 = normal grip.")]
        [Range(0.1f, 1f)] public float HandbrakeGrip = 0.42f;

        [Header("Slopes")]
        [Tooltip("Fraction of drive torque retained at a 30-degree climb.")]
        [Range(0.2f, 1f)] public float ClimbTorqueRetention = 0.62f;

        float[] _baseStiffness;

        protected override void Awake()
        {
            base.Awake();
            Body.centerOfMass = CentreOfMass;

            _baseStiffness = new float[Wheels.Length];
            for (int i = 0; i < Wheels.Length; i++)
            {
                if (Wheels[i]?.Collider == null) continue;
                _baseStiffness[i] = Wheels[i].Collider.sidewaysFriction.stiffness;
            }
        }

        void FixedUpdate()
        {
            ApplySteering();
            ApplyDrive();
            ApplyHandbrake();

            // Downforce scales with speed squared, like real aero.
            float speed = Body.linearVelocity.magnitude;
            Body.AddForce(-transform.up * (Downforce * speed * speed * 0.01f), ForceMode.Force);

            UpdateWheelMeshes();
        }

        void ApplySteering()
        {
            float falloff = 1f / (1f + Mathf.Abs(ForwardSpeed) / Mathf.Max(1f, SteerFalloffSpeed));
            float angle = Steer * MaxSteerAngle * falloff;

            foreach (var w in Wheels)
                if (w?.Collider != null && w.Steers)
                    w.Collider.steerAngle = angle;
        }

        void ApplyDrive()
        {
            bool atTopSpeed = SpeedKph >= TopSpeedKph;

            // Uphill costs torque. Dot of forward against gravity-up gives the climb angle.
            float climb = Mathf.Clamp01(Vector3.Dot(transform.forward, Vector3.up));
            float slopeFactor = Mathf.Lerp(1f, ClimbTorqueRetention, climb / 0.5f);

            // Pressing the opposite direction to travel is braking, not reverse, until stopped.
            bool braking = Throttle != 0f && Mathf.Sign(Throttle) != Mathf.Sign(ForwardSpeed)
                           && Mathf.Abs(ForwardSpeed) > 0.6f;

            float motor = braking || atTopSpeed ? 0f : Throttle * MotorTorque * slopeFactor;
            float brake = braking ? BrakeTorque : 0f;

            // No input and rolling: light drag so the car coasts to a stop instead of forever.
            if (Mathf.Approximately(Throttle, 0f)) brake = BrakeTorque * 0.12f;

            foreach (var w in Wheels)
            {
                if (w?.Collider == null) continue;
                if (w.Drives) w.Collider.motorTorque = motor;
                if (w.Brakes) w.Collider.brakeTorque = brake;
            }
        }

        void ApplyHandbrake()
        {
            for (int i = 0; i < Wheels.Length; i++)
            {
                var w = Wheels[i];
                if (w?.Collider == null) continue;

                // Only the rear axle loses grip -- that asymmetry is what makes it a drift
                // rather than a straight-line skid.
                bool rear = !w.Steers;

                var friction = w.Collider.sidewaysFriction;
                friction.stiffness = Handbrake && rear
                    ? _baseStiffness[i] * HandbrakeGrip
                    : _baseStiffness[i];
                w.Collider.sidewaysFriction = friction;

                if (Handbrake && rear)
                {
                    w.Collider.brakeTorque = HandbrakeTorque;
                    w.Collider.motorTorque = 0f;
                }
            }
        }

        /// <summary>
        /// Unity's cylinder primitive stands on its Y axis, but a wheel spins about X. The
        /// extra 90 degrees converts one to the other -- without it GetWorldPose overwrites the
        /// build-time correction every frame and the tyres render as upright barrels.
        ///
        /// This is the correction for a <b>primitive cylinder</b> only, which is what the
        /// procedural bike and boat still use. A wheel that was modelled as a wheel already has
        /// its axle on X and needs <see cref="Quaternion.identity"/> instead -- applying this to
        /// one lays it flat, which is the same bug in mirror image. Hence the per-wheel field
        /// below rather than one constant for the whole project.
        /// </summary>
        internal static readonly Quaternion TyreMeshCorrection = Quaternion.Euler(0f, 0f, 90f);

        void UpdateWheelMeshes()
        {
            foreach (var w in Wheels)
            {
                if (w?.Collider == null || w.Mesh == null) continue;
                w.Collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
                w.Mesh.SetPositionAndRotation(pos, rot * w.MeshRotationOffset);
            }
        }

        public override void OnExit(GameObject occupant)
        {
            base.OnExit(occupant);
            // Leave it parked, not rolling away downhill.
            foreach (var w in Wheels)
                if (w?.Collider != null)
                {
                    w.Collider.motorTorque = 0f;
                    w.Collider.brakeTorque = HandbrakeTorque;
                }
        }
    }
}
