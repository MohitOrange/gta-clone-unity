using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// AI driver that follows the lane graph, obeys signals and avoids rear-ending whatever is
    /// in front of it.
    ///
    /// Drives through <see cref="Vehicle.SetInput"/> -- the same entry point the player's thumbs
    /// use -- so AI cars are subject to identical physics, collide properly, and can be
    /// commandeered later without swapping controllers.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class TrafficCar : MonoBehaviour
    {
        [Header("Driving")]
        public float CruiseSpeedKph = 42f;
        [Tooltip("Speed cap while within an intersection box, so turns are not taken flat out.")]
        public float TurnSpeedKph = 18f;
        [Tooltip("How far ahead the driver aims. Larger is smoother but cuts corners.")]
        public float LookAhead = 6.5f;
        public float NodeArriveRadius = 4.5f;
        public float MaxSteerAngleForFullLock = 34f;

        [Header("Signals")]
        [Tooltip("Distance before a stop line at which the driver starts braking for red.")]
        public float StopLineBrakeDistance = 16f;
        [Tooltip("Stop this far short of the stop-line node.")]
        public float StopLineOffset = 1.2f;
        [Tooltip("A yellow is run if the car cannot stop comfortably in the time remaining.")]
        public float YellowCommitTime = 1.1f;

        [Header("Traffic avoidance")]
        public float SensorLength = 11f;
        public float SensorRadius = 1.1f;
        public LayerMask ObstacleMask = ~0;

        [Header("Recovery")]
        [Tooltip("If stuck below this speed for this long, respawn elsewhere on the network.")]
        public float StuckSpeedKph = 2f;
        public float StuckTimeout = 9f;

        Vehicle _vehicle;
        RoadNetwork _network;

        int _currentNode = -1;
        int _targetNode = -1;
        float _stuckTimer;

        public bool IsStopped { get; private set; }

        void Awake() => _vehicle = GetComponent<Vehicle>();

        void Start()
        {
            _network = RoadNetwork.Instance;
            if (_network == null)
            {
                enabled = false;
                return;
            }

            if (_targetNode < 0) SnapToNearestLane();
        }

        /// <summary>
        /// Place this car on a specific lane node. Used by the spawner.
        /// Returns false if the node was invalid, so the caller can try another rather than
        /// silently leaving the car wherever it was.
        /// </summary>
        public bool PlaceOnNode(int nodeIndex)
        {
            _network ??= RoadNetwork.Instance;
            var node = _network?.GetNode(nodeIndex);
            if (node == null) return false;

            _currentNode = nodeIndex;
            _targetNode = PickNext(nodeIndex);
            _stuckTimer = 0f;

            // Only a small drop: the suspension has ~0.24m of travel, so spawning high enough
            // to out-reach it means the car free-falls onto its wheels instead of settling.
            transform.position = node.Position + Vector3.up * 0.3f;
            transform.rotation = Quaternion.LookRotation(node.Heading.ToVector(), Vector3.up);

            // Teleporting a rigidbody leaves stale physics state behind unless it is pushed
            // through explicitly.
            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = transform.position;
                body.rotation = transform.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            return true;
        }

        void SnapToNearestLane()
        {
            int near = _network.NearestNode(transform.position);
            if (near >= 0)
            {
                _currentNode = near;
                _targetNode = PickNext(near);
            }
        }

        void FixedUpdate()
        {
            if (_network == null || _vehicle.IsWrecked) return;

            var target = _network.GetNode(_targetNode);
            if (target == null) { SnapToNearestLane(); return; }

            Vector3 toTarget = target.Position - transform.position;
            toTarget.y = 0f;

            if (toTarget.magnitude <= NodeArriveRadius)
            {
                _currentNode = _targetNode;
                _targetNode = PickNext(_currentNode);
                target = _network.GetNode(_targetNode);
                if (target == null) return;

                toTarget = target.Position - transform.position;
                toTarget.y = 0f;
            }

            float steer = ComputeSteer(toTarget);
            float desiredKph = ComputeDesiredSpeed(target, toTarget.magnitude, steer);

            ApplyDrive(steer, desiredKph);
            CheckStuck();
        }

        float ComputeSteer(Vector3 toTarget)
        {
            // Aim at a point along the segment rather than the node itself, which stops the
            // car sawing left-right as it converges on a waypoint.
            Vector3 aim = toTarget.normalized * Mathf.Min(LookAhead, toTarget.magnitude);
            float angle = Vector3.SignedAngle(transform.forward, aim, Vector3.up);
            return Mathf.Clamp(angle / MaxSteerAngleForFullLock, -1f, 1f);
        }

        float ComputeDesiredSpeed(LaneNode target, float distance, float steer)
        {
            float desired = CruiseSpeedKph;

            // Ease off through corners.
            if (Mathf.Abs(steer) > 0.25f)
                desired = Mathf.Lerp(CruiseSpeedKph, TurnSpeedKph, Mathf.Abs(steer));

            if (MustStopForSignal(target, distance)) return 0f;
            if (BlockedAhead(out float gap)) desired = Mathf.Min(desired, gap < 4f ? 0f : desired * 0.4f);

            return desired;
        }

        bool MustStopForSignal(LaneNode target, float distance)
        {
            if (!target.IsStopLine) return false;
            if (distance > StopLineBrakeDistance) return false;

            var junction = _network.GetIntersection(target.IntersectionIndex);
            if (junction == null) return false;

            LightState state = junction.StateFor(target.Heading);
            if (state == LightState.Green) return false;

            if (state == LightState.Yellow)
            {
                // Commit to crossing if stopping would leave the car sitting in the junction.
                float speed = Mathf.Max(1f, _vehicle.SpeedKph / 3.6f);
                float timeToLine = distance / speed;
                if (timeToLine < YellowCommitTime) return false;
            }

            return distance > StopLineOffset || _vehicle.SpeedKph > 1f;
        }

        bool BlockedAhead(out float gap)
        {
            gap = float.MaxValue;

            Vector3 origin = transform.position + Vector3.up * 0.7f + transform.forward * 1.6f;
            if (!Physics.SphereCast(origin, SensorRadius, transform.forward, out RaycastHit hit,
                                    SensorLength, ObstacleMask, QueryTriggerInteraction.Ignore))
                return false;

            // Ignore our own colliders and the road surface.
            if (hit.collider.transform.IsChildOf(transform)) return false;

            bool isTraffic = hit.collider.GetComponentInParent<Vehicle>() != null
                             || hit.collider.GetComponentInParent<Pedestrian>() != null;
            if (!isTraffic) return false;

            gap = hit.distance;
            return true;
        }

        void ApplyDrive(float steer, float desiredKph)
        {
            float current = _vehicle.SpeedKph;
            IsStopped = desiredKph <= 0.01f;

            float throttle;
            if (IsStopped)
            {
                throttle = current > 1.5f ? -1f : 0f;   // brake, then hold
            }
            else
            {
                float error = desiredKph - current;
                throttle = Mathf.Clamp(error / 12f, -1f, 1f);
            }

            _vehicle.SetInput(throttle, steer, false);
        }

        void CheckStuck()
        {
            if (_vehicle.SpeedKph > StuckSpeedKph || IsStopped)
            {
                _stuckTimer = 0f;
                return;
            }

            _stuckTimer += Time.fixedDeltaTime;
            if (_stuckTimer < StuckTimeout) return;

            _stuckTimer = 0f;
            TrafficSpawner.Instance?.Recycle(this);
        }

        int PickNext(int from)
        {
            var node = _network.GetNode(from);
            if (node == null || node.Next.Length == 0) return -1;
            return node.Next[Random.Range(0, node.Next.Length)];
        }
    }
}
