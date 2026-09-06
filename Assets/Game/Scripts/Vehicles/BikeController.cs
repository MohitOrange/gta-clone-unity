using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Two-wheeled vehicle.
    ///
    /// A bike on two WheelColliders is statically unstable and will simply fall over, so the
    /// upright torque here is not a cheat -- it stands in for the rider's balance, which is
    /// what actually keeps a real motorcycle up. Lean is then applied visually and as a small
    /// cornering force, giving the handling its character without simulating counter-steer.
    /// </summary>
    public class BikeController : Vehicle
    {
        [Header("Wheels")]
        public WheelCollider FrontWheel;
        public WheelCollider RearWheel;
        public Transform FrontMesh;
        public Transform RearMesh;
        [Tooltip("Body transform that visually leans into corners.")]
        public Transform LeanBody;

        [Header("Drivetrain")]
        public float MotorTorque = 900f;
        public float BrakeTorque = 2100f;
        public float HandbrakeTorque = 2600f;
        public float MaxSteerAngle = 38f;
        public float SteerFalloffSpeed = 16f;
        public float TopSpeedKph = 145f;

        [Header("Balance")]
        [Tooltip("How hard the rider fights to stay upright.")]
        public float UprightTorque = 45f;
        public float UprightDamping = 6f;
        [Tooltip("Maximum visual lean angle into a corner.")]
        public float MaxLeanAngle = 32f;
        public float LeanSmoothing = 6f;
        public Vector3 CentreOfMass = new Vector3(0f, -0.35f, 0f);

        float _lean;

        protected override void Awake()
        {
            base.Awake();
            Body.centerOfMass = CentreOfMass;
        }

        void FixedUpdate()
        {
            float falloff = 1f / (1f + Mathf.Abs(ForwardSpeed) / Mathf.Max(1f, SteerFalloffSpeed));
            if (FrontWheel != null) FrontWheel.steerAngle = Steer * MaxSteerAngle * falloff;

            ApplyDrive();
            ApplyBalance();
            UpdateMeshes();
        }

        void ApplyDrive()
        {
            bool atTopSpeed = SpeedKph >= TopSpeedKph;
            bool braking = Throttle != 0f && Mathf.Sign(Throttle) != Mathf.Sign(ForwardSpeed)
                           && Mathf.Abs(ForwardSpeed) > 0.6f;

            float motor = braking || atTopSpeed ? 0f : Throttle * MotorTorque;
            float brake = braking ? BrakeTorque : 0f;
            if (Mathf.Approximately(Throttle, 0f)) brake = BrakeTorque * 0.1f;

            if (RearWheel != null)
            {
                RearWheel.motorTorque = motor;
                RearWheel.brakeTorque = Handbrake ? HandbrakeTorque : brake;
            }
            if (FrontWheel != null)
            {
                // Front brake only under normal braking; locking it on the handbrake would
                // pitch the bike over its own front axle.
                FrontWheel.brakeTorque = Handbrake ? 0f : brake * 0.8f;
            }
        }

        void ApplyBalance()
        {
            // Roll error measured against world up, corrected with a damped spring.
            Vector3 up = transform.up;
            float roll = Vector3.SignedAngle(Vector3.up, up, transform.forward);

            float targetLean = -Steer * MaxLeanAngle * Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / 8f);
            _lean = Mathf.Lerp(_lean, targetLean, 1f - Mathf.Exp(-LeanSmoothing * Time.fixedDeltaTime));

            float error = roll - _lean;
            float angularRoll = Vector3.Dot(Body.angularVelocity, transform.forward);

            Body.AddTorque(transform.forward * (-error * UprightTorque - angularRoll * UprightDamping),
                           ForceMode.Force);

            if (LeanBody != null)
                LeanBody.localRotation = Quaternion.Euler(0f, 0f, _lean * 0.55f);
        }

        void UpdateMeshes()
        {
            Pose(FrontWheel, FrontMesh);
            Pose(RearWheel, RearMesh);
        }

        static void Pose(WheelCollider wc, Transform mesh)
        {
            if (wc == null || mesh == null) return;
            wc.GetWorldPose(out Vector3 p, out Quaternion r);
            mesh.SetPositionAndRotation(p, r * CarController.TyreMeshCorrection);
        }

        public override void OnExit(GameObject occupant)
        {
            base.OnExit(occupant);
            if (RearWheel != null) { RearWheel.motorTorque = 0f; RearWheel.brakeTorque = HandbrakeTorque; }
            if (FrontWheel != null) FrontWheel.brakeTorque = HandbrakeTorque;
        }
    }
}
