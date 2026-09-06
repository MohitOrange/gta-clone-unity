using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Wildlife that wanders a patch of ground, and bolts when something big comes near.
    ///
    /// Deliberately not a <see cref="CharacterController"/> or a rigidbody. There are dozens of
    /// these scattered across the map purely as decoration; paying broadphase and controller
    /// cost for a chicken is the wrong trade on a phone. Movement is a transform write plus one
    /// downward raycast, and the whole thing switches itself off past
    /// <see cref="SimulationRange"/> so animals on the far side of the map cost nothing.
    ///
    /// The animator contract is the one every prefab in the Animals FREE pack already ships:
    /// two floats, <c>Vert</c> (0 idle, 1 moving) and <c>State</c> (0 walk, 1 run).
    /// </summary>
    public class AmbientAnimal : MonoBehaviour
    {
        static readonly int VertHash = Animator.StringToHash("Vert");
        static readonly int StateHash = Animator.StringToHash("State");

        [Header("Roaming")]
        [Tooltip("Centre of the patch. Set at spawn; defaults to where it starts.")]
        public Vector3 Home;
        [Tooltip("Half-extents of the patch, metres.")]
        public float RoamRadius = 9f;
        public float WalkSpeed = 1.1f;
        public float RunSpeed = 4.5f;
        public float TurnSpeed = 220f;

        [Header("Rhythm")]
        public Vector2 PauseSeconds = new Vector2(1.5f, 6f);
        public Vector2 WalkSeconds = new Vector2(3f, 9f);

        [Header("Fleeing")]
        [Tooltip("Runs if the player or a vehicle comes within this distance.")]
        public float FleeRadius = 9f;
        public float FleeSeconds = 3.5f;

        [Header("Performance")]
        [Tooltip("Stops simulating past this distance from the player.")]
        // 140 m was shorter than the draw distance on every quality tier (420 m at the lowest),
        // so an animal could be plainly on screen and frozen mid-stride. The renderer's own
        // culling now handles the off-screen case, leaving this as a cap for things that are
        // visible but too distant to read as moving.
        public float SimulationRange = 260f;
        [Tooltip("Ground is sampled this often, not every frame.")]
        public float GroundSampleInterval = 0.25f;

        [Tooltip("How far the ground may sit above or below the patch this animal was placed on. "
               + "Anything outside this band is treated as a bad probe hit, not as ground.")]
        public float MaxGroundRise = 3f;

        Animator _animator;
        Transform _player;
        Vector3 _target;
        float _stateTimer;
        float _fleeTimer;
        float _groundTimer;
        float _groundY;
        bool _moving;

        // Terrain and city geometry only. Sampling every layer would let an animal stand on
        // another animal, or on the player's head.
        int _groundMask;

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            if (Home == Vector3.zero) Home = transform.position;
            _groundMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
            _groundY = transform.position.y;
            PickNewTarget();
        }

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _player = player.transform;

            // Desynchronise the herd: without this every deer in a group starts and stops on
            // exactly the same frame and the whole group reads as one object.
            _stateTimer = Random.Range(0f, PauseSeconds.y);
        }

        void Update()
        {
            if (_player != null)
            {
                float sqr = (_player.position - transform.position).sqrMagnitude;
                if (sqr > SimulationRange * SimulationRange)
                {
                    if (_animator != null && _animator.enabled) _animator.enabled = false;
                    return;
                }

                if (_animator != null && !_animator.enabled) _animator.enabled = true;
                if (sqr < FleeRadius * FleeRadius) StartFleeing(_player.position);
            }

            float dt = Time.deltaTime;
            bool fleeing = _fleeTimer > 0f;
            if (fleeing) _fleeTimer -= dt;

            _stateTimer -= dt;
            if (!fleeing && _stateTimer <= 0f) ToggleRhythm();

            bool moving = fleeing || _moving;
            float speed = fleeing ? RunSpeed : WalkSpeed;

            if (moving) Step(speed, dt);

            if (_animator != null)
            {
                _animator.SetFloat(VertHash, moving ? 1f : 0f, 0.15f, dt);
                _animator.SetFloat(StateHash, fleeing ? 1f : 0f, 0.2f, dt);
            }
        }

        // ------------------------------------------------------------------ moving

        void Step(float speed, float dt)
        {
            Vector3 flat = _target - transform.position;
            flat.y = 0f;

            if (flat.sqrMagnitude < 0.6f)
            {
                PickNewTarget();
                return;
            }

            Vector3 dir = flat.normalized;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(dir, Vector3.up), TurnSpeed * dt);

            Vector3 next = transform.position + transform.forward * (speed * dt);
            next.y = SampleGround(next, dt);
            transform.position = next;
        }

        /// <summary>Hits from one ground probe. Sized for ground plus a few bodies on top of it.</summary>
        static readonly RaycastHit[] _probe = new RaycastHit[8];

        /// <summary>
        /// Ground height under a point, resampled a few times a second rather than every frame.
        /// A raycast per animal per frame is the one thing here that would actually show up in
        /// a profile; walking speed makes four samples a second indistinguishable from sixty.
        ///
        /// <b>The probe must not land on another animal.</b> The first version of this took the
        /// first thing a downward ray hit, and the ray hits anything solid -- including other
        /// animals. Two animals standing close together each read the other's back as "ground",
        /// each stepped up onto it, and the pair climbed. Over a few minutes of play the whole
        /// population drifted hundreds of metres into the sky, which is why the world looked
        /// empty of wildlife despite the scene holding sixty of them. Hits on anything that is
        /// itself a character are skipped, and the result is clamped near the patch the animal
        /// was placed on, so no feedback loop can survive even if something new turns up.
        /// </summary>
        float SampleGround(Vector3 at, float dt)
        {
            _groundTimer -= dt;
            if (_groundTimer > 0f) return Mathf.Lerp(transform.position.y, _groundY, 12f * dt);

            _groundTimer = GroundSampleInterval;

            int count = Physics.RaycastNonAlloc(at + Vector3.up * 4f, Vector3.down, _probe, 12f,
                                                _groundMask, QueryTriggerInteraction.Ignore);

            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                var col = _probe[i].collider;
                if (col == null) continue;

                // Never stand on another animal, on a pedestrian, or on the player.
                if (col.GetComponentInParent<AmbientAnimal>() != null) continue;
                if (col.GetComponentInParent<CharacterController>() != null) continue;

                if (_probe[i].point.y > best) best = _probe[i].point.y;
            }

            if (!float.IsNegativeInfinity(best))
            {
                // A roaming patch is essentially level, so ground far from where this animal was
                // placed is not ground -- it is something the probe should not have hit.
                _groundY = Mathf.Clamp(best, Home.y - MaxGroundRise, Home.y + MaxGroundRise);
            }

            return Mathf.Lerp(transform.position.y, _groundY, 12f * dt);
        }

        void ToggleRhythm()
        {
            _moving = !_moving;
            if (_moving)
            {
                PickNewTarget();
                _stateTimer = Random.Range(WalkSeconds.x, WalkSeconds.y);
            }
            else
            {
                _stateTimer = Random.Range(PauseSeconds.x, PauseSeconds.y);
            }
        }

        void PickNewTarget()
        {
            Vector2 offset = Random.insideUnitCircle * RoamRadius;
            _target = Home + new Vector3(offset.x, 0f, offset.y);
        }

        /// <summary>Bolt directly away from a threat, staying inside the patch.</summary>
        public void StartFleeing(Vector3 threat)
        {
            _fleeTimer = FleeSeconds;

            Vector3 away = transform.position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;

            Vector3 candidate = transform.position + away.normalized * (RoamRadius * 0.9f);

            // Clamp back toward home so a startled animal does not end up in the sea or halfway
            // across the map, where nothing would ever bring it back.
            Vector3 fromHome = candidate - Home;
            fromHome.y = 0f;
            if (fromHome.magnitude > RoamRadius * 1.6f)
                candidate = Home + fromHome.normalized * (RoamRadius * 1.6f);

            _target = candidate;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(Home == Vector3.zero ? transform.position : Home, RoamRadius);
        }
    }
}
