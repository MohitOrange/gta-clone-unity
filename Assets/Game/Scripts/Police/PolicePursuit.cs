using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Drives a police cruiser at the player.
    ///
    /// Steers directly rather than following the lane graph: a pursuit that politely stayed in
    /// lane and stopped at red lights would not read as a pursuit. It still drives through
    /// <see cref="Vehicle.SetInput"/>, so it obeys the same physics as everything else and can
    /// be crashed, rammed and wrecked.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class PolicePursuit : MonoBehaviour
    {
        [Header("Chase")]
        public float ChaseSpeedKph = 78f;
        public float MaxSteerAngleForFullLock = 30f;
        [Tooltip("Ease off inside this distance so the cruiser does not simply ram the player.")]
        public float StandoffDistance = 11f;
        [Tooltip("Stop entirely and put officers out at this range.")]
        public float DeployDistance = 15f;

        [Header("Deployment")]
        public GameObject OfficerPrefab;
        [Tooltip("Officers put out per cruiser.")]
        public int OfficersPerCar = 1;
        [Tooltip("Only deploy once the player is out of a vehicle -- no point chasing on foot otherwise.")]
        public bool RequirePlayerOnFoot = true;

        [Header("Obstacles")]
        public float SensorLength = 9f;
        public float SensorRadius = 1.1f;
        public LayerMask ObstacleMask = ~0;

        [Header("Recovery")]
        public float StuckSpeedKph = 3f;
        public float StuckTimeout = 5f;
        [Tooltip("How hard to reverse out when stuck.")]
        public float ReverseTime = 1.4f;

        Vehicle _vehicle;
        Transform _player;
        PlayerVehicleController _playerDriving;

        bool _deployed;
        float _stuckTimer;
        float _reverseTimer;

        public bool HasDeployed => _deployed;

        void Awake() => _vehicle = GetComponent<Vehicle>();

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) { enabled = false; return; }

            _player = player.transform;
            _playerDriving = player.GetComponent<PlayerVehicleController>();
        }

        void FixedUpdate()
        {
            if (_player == null || _vehicle.IsWrecked) { _vehicle.ClearInput(); return; }

            var game = GameStateManager.Instance;
            if (game != null && !game.IsPlaying) { _vehicle.ClearInput(); return; }

            Vector3 toPlayer = _player.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            if (_reverseTimer > 0f) { TickReverse(toPlayer); return; }

            float steer = ComputeSteer(toPlayer);
            float desiredKph = ComputeSpeed(distance);

            ApplyDrive(steer, desiredKph);
            TryDeploy(distance);
            CheckStuck(desiredKph);
        }

        float ComputeSteer(Vector3 toPlayer)
        {
            float angle = Vector3.SignedAngle(transform.forward, toPlayer.normalized, Vector3.up);
            return Mathf.Clamp(angle / MaxSteerAngleForFullLock, -1f, 1f);
        }

        float ComputeSpeed(float distance)
        {
            if (distance <= DeployDistance) return 0f;

            // Taper down as the cruiser closes so it pulls alongside rather than through.
            if (distance < StandoffDistance * 2f)
                return Mathf.Lerp(12f, ChaseSpeedKph, distance / (StandoffDistance * 2f));

            if (BlockedAhead()) return ChaseSpeedKph * 0.35f;
            return ChaseSpeedKph;
        }

        bool BlockedAhead()
        {
            Vector3 origin = transform.position + Vector3.up * 0.7f + transform.forward * 1.8f;
            if (!Physics.SphereCast(origin, SensorRadius, transform.forward, out RaycastHit hit,
                                    SensorLength, ObstacleMask, QueryTriggerInteraction.Ignore))
                return false;

            if (hit.collider.transform.IsChildOf(transform)) return false;

            // The player is the target, not an obstacle.
            if (_player != null && hit.collider.transform.IsChildOf(_player)) return false;

            return hit.collider.GetComponentInParent<Vehicle>() != null
                   || hit.collider.GetComponentInParent<Pedestrian>() != null;
        }

        void ApplyDrive(float steer, float desiredKph)
        {
            float current = _vehicle.SpeedKph;
            float throttle;

            if (desiredKph <= 0.01f) throttle = current > 2f ? -1f : 0f;
            else throttle = Mathf.Clamp((desiredKph - current) / 14f, -1f, 1f);

            _vehicle.SetInput(throttle, steer, false);
        }

        void TickReverse(Vector3 toPlayer)
        {
            _reverseTimer -= Time.fixedDeltaTime;

            // Reverse away while steering opposite, which is what actually frees a stuck car.
            float steer = -ComputeSteer(toPlayer);
            _vehicle.SetInput(-1f, steer, false);

            if (_reverseTimer <= 0f) _stuckTimer = 0f;
        }

        void CheckStuck(float desiredKph)
        {
            if (desiredKph <= 0.01f || _vehicle.SpeedKph > StuckSpeedKph)
            {
                _stuckTimer = 0f;
                return;
            }

            _stuckTimer += Time.fixedDeltaTime;
            if (_stuckTimer < StuckTimeout) return;

            _stuckTimer = 0f;
            _reverseTimer = ReverseTime;
        }

        void TryDeploy(float distance)
        {
            if (_deployed || OfficerPrefab == null) return;
            if (distance > DeployDistance) return;
            if (RequirePlayerOnFoot && _playerDriving != null && _playerDriving.IsDriving) return;

            _deployed = true;

            for (int i = 0; i < OfficersPerCar; i++)
            {
                // Put them out beside the car, not inside its own collider.
                Vector3 side = transform.right * (i % 2 == 0 ? -2.2f : 2.2f);
                Vector3 spawn = transform.position + side + Vector3.up * 0.2f;

                var rotation = Quaternion.LookRotation(transform.forward, Vector3.up);

                // Through the dispatcher's pool, so a stood-down officer is reused rather than
                // rebuilt. Falls back to a plain Instantiate if there is no dispatcher.
                var dispatcher = PoliceDispatcher.Instance;
                var officer = dispatcher != null
                    ? dispatcher.TakeOfficer(spawn, rotation)
                    : Instantiate(OfficerPrefab, spawn, rotation);
                if (officer == null) continue;

                officer.name = "Officer_" + name + "_" + i;

                // A recycled officer wakes with the health it was returned with.
                var health = officer.GetComponent<Health>();
                if (health != null && health.CurrentHealth < health.MaxHealth) health.Revive(1f);

                dispatcher?.RegisterOfficer(officer);
            }
        }

        /// <summary>Allow a fresh deployment after the unit is recycled.</summary>
        public void ResetDeployment() => _deployed = false;
    }
}
