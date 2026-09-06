using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Orbiting third-person follow camera driven by the right-thumb look pad (or
    /// right-mouse drag on desktop).
    ///
    /// Runs in LateUpdate so it always sees the player's final position for the frame --
    /// following in Update produces a one-frame lag that reads as jitter on device.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform Target;
        [Tooltip("Offset from the target's feet to the orbit pivot, roughly shoulder height.")]
        public Vector3 PivotOffset = new Vector3(0f, 1.55f, 0f);

        [Header("Orbit")]
        public float Distance = 5.0f;
        public float MinPitch = -35f;
        public float MaxPitch = 70f;
        public float StartYaw = 0f;
        public float StartPitch = 12f;
        [Tooltip("Nudge the camera off-centre so the player does not block the crosshair.")]
        public float ShoulderOffset = 0.45f;

        [Header("Smoothing")]
        [Tooltip("Seconds for the pivot to catch up to the target. 0 is rigid.")]
        public float FollowSmoothTime = 0.06f;
        [Tooltip("Seconds for rotation to settle after the thumb lifts.")]
        public float RotationSmoothTime = 0.04f;

        [Header("Collision")]
        public LayerMask CollisionMask = ~0;
        [Tooltip("Keeps the near plane clear of whatever the camera pulls up against.")]
        public float CollisionRadius = 0.28f;
        [Tooltip("How fast the camera returns to full distance once unblocked.")]
        public float ReturnSpeed = 6f;

        [Header("Look")]
        [Tooltip("Drag up looks down. Some players cannot play any other way.")]
        public bool InvertY;

        float _yaw;
        float _pitch;
        float _smoothYaw;
        float _smoothPitch;
        float _yawVelocity;
        float _pitchVelocity;
        float _currentDistance;
        Vector3 _pivot;
        Vector3 _pivotVelocity;

        void Start()
        {
            _yaw = _smoothYaw = StartYaw;
            _pitch = _smoothPitch = StartPitch;
            _currentDistance = Distance;

            if (Target != null) _pivot = Target.position + PivotOffset;
            ApplyImmediate();
        }

        void LateUpdate()
        {
            if (Target == null) return;

            var hub = InputHub.Instance;
            if (hub != null)
            {
                Vector2 look = hub.ConsumeLookDelta();
                _yaw += look.x;
                _pitch -= InvertY ? -look.y : look.y;   // drag up looks up, unless inverted
                _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
            }

            _smoothYaw = Mathf.SmoothDampAngle(_smoothYaw, _yaw, ref _yawVelocity, RotationSmoothTime);
            _smoothPitch = Mathf.SmoothDampAngle(_smoothPitch, _pitch, ref _pitchVelocity, RotationSmoothTime);

            Vector3 desiredPivot = Target.position + PivotOffset;
            _pivot = FollowSmoothTime <= 0f
                ? desiredPivot
                : Vector3.SmoothDamp(_pivot, desiredPivot, ref _pivotVelocity, FollowSmoothTime);

            Quaternion rot = Quaternion.Euler(_smoothPitch, _smoothYaw, 0f);
            Vector3 back = rot * Vector3.back;
            Vector3 side = rot * Vector3.right * ShoulderOffset;

            // Pull in instantly when something blocks the view, ease back out when it clears.
            float wanted = Distance;
            Vector3 origin = _pivot + side;
            if (Physics.SphereCast(origin, CollisionRadius, back, out RaycastHit hit,
                                   Distance, CollisionMask, QueryTriggerInteraction.Ignore))
            {
                wanted = Mathf.Max(0.6f, hit.distance);
            }

            _currentDistance = wanted < _currentDistance
                ? wanted
                : Mathf.MoveTowards(_currentDistance, wanted, ReturnSpeed * Time.deltaTime);

            transform.position = origin + back * _currentDistance;
            transform.rotation = rot;
        }

        void ApplyImmediate()
        {
            Quaternion rot = Quaternion.Euler(_smoothPitch, _smoothYaw, 0f);
            transform.position = _pivot + rot * Vector3.right * ShoulderOffset + rot * Vector3.back * _currentDistance;
            transform.rotation = rot;
        }

        /// <summary>
        /// Re-aim the rig at a vehicle with its own framing. The on-foot values are captured
        /// on first use so <see cref="ClearTarget"/> can restore them exactly, rather than
        /// hard-coding a "walking" distance in two places.
        /// </summary>
        public void SetTarget(Transform target, float distance, Vector3 pivotOffset)
        {
            CaptureFootDefaults();

            Target = target;
            Distance = distance;
            PivotOffset = pivotOffset;

            AlignBehindTarget();
        }

        /// <summary>Return to following the player on foot.</summary>
        public void ClearTarget(Transform playerTransform)
        {
            CaptureFootDefaults();

            Target = playerTransform;
            Distance = _footDistance;
            PivotOffset = _footPivot;

            AlignBehindTarget();
        }

        void CaptureFootDefaults()
        {
            if (_footDefaultsCaptured) return;
            _footDistance = Distance;
            _footPivot = PivotOffset;
            _footDefaultsCaptured = true;
        }

        bool _footDefaultsCaptured;
        float _footDistance;
        Vector3 _footPivot;

        /// <summary>Snap the orbit behind the target, e.g. after a teleport or cutscene.</summary>
        public void AlignBehindTarget()
        {
            if (Target == null) return;
            _yaw = _smoothYaw = Target.eulerAngles.y;
            _pitch = _smoothPitch = StartPitch;
            _pivot = Target.position + PivotOffset;
            _currentDistance = Distance;
            ApplyImmediate();
        }
    }
}
