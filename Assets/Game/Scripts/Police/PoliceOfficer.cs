using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A foot patrolman: closes on the player, opens fire once the wanted level justifies it,
    /// and makes the arrest at contact.
    ///
    /// Shooting is gated behind a star threshold on purpose. At one star the police are a
    /// nuisance you can outrun; only once you have earned it do they start shooting, which is
    /// what makes the star count mean something rather than just changing how many cars appear.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PoliceOfficer : MonoBehaviour
    {
        enum State { Approach, Engage, Arresting }

        [Header("Movement")]
        public float MoveSpeed = 4.6f;
        public float TurnSpeed = 9f;
        public float Gravity = -18f;
        [Tooltip("Stop closing once this near, so officers do not shove the player around.")]
        public float PreferredRange = 6f;

        [Header("Arrest")]
        [Tooltip("Distance at which an arrest can begin.")]
        public float ArrestRadius = 2.4f;
        [Tooltip("Seconds of contact required to make the arrest stick.")]
        public float ArrestTime = 1.5f;
        [Tooltip("Above this speed the player is resisting and cannot be cuffed.")]
        public float MaxArrestSpeed = 2.5f;
        [Tooltip("A subdued suspect inside this range is closed on for the cuffs rather than " +
                 "shot at. Without it, officers hold at firing range and can never arrest.")]
        public float ArrestApproachRange = 12f;

        [Header("Firearm")]
        [Tooltip("Minimum wanted stars before officers shoot rather than just chase.")]
        public int MinStarsToShoot = 3;
        public float FireRange = 24f;
        public float FireCooldown = 1.15f;
        public float Damage = 11f;
        public float Accuracy = 0.72f;
        public LayerMask HitMask = ~0;

        CharacterController _cc;
        Health _health;
        PlayerAnimation _anim;
        RagdollLite _ragdoll;

        Transform _player;
        Health _playerHealth;
        PlayerController _playerController;

        State _state = State.Approach;
        float _fireTimer;
        float _arrestTimer;
        float _verticalVelocity;

        public float ArrestProgress => Mathf.Clamp01(_arrestTimer / Mathf.Max(0.01f, ArrestTime));

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            _anim = GetComponentInChildren<PlayerAnimation>();
            _ragdoll = GetComponent<RagdollLite>();
        }

        void OnEnable() { if (_health != null) _health.Died += OnDied; }
        void OnDisable() { if (_health != null) _health.Died -= OnDied; }

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) { enabled = false; return; }

            _player = player.transform;
            _playerHealth = player.GetComponent<Health>();
            _playerController = player.GetComponent<PlayerController>();
        }

        void Update()
        {
            if (_player == null || (_health != null && _health.IsDead)) return;

            // Nothing to do once the player is down or already busted.
            var game = GameStateManager.Instance;
            if (game != null && !game.IsPlaying) { Move(Vector3.zero); return; }

            Vector3 toPlayer = _player.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            UpdateState(distance);

            switch (_state)
            {
                case State.Approach: TickApproach(toPlayer, distance); break;
                case State.Engage: TickEngage(toPlayer, distance); break;
                case State.Arresting: TickArrest(toPlayer, distance); break;
            }
        }

        void UpdateState(float distance)
        {
            bool subdued = PlayerIsSubdued();

            if (subdued && distance <= ArrestRadius) { _state = State.Arresting; return; }
            if (_state == State.Arresting) _arrestTimer = 0f;

            // A suspect who has stopped running gets walked up to and cuffed, whatever the star
            // count. Only someone actively fleeing or fighting gets shot at.
            if (subdued && distance <= ArrestApproachRange) { _state = State.Approach; return; }

            int stars = HeatSystem.Instance != null ? HeatSystem.Instance.WantedLevel : 0;
            _state = stars >= MinStarsToShoot && distance <= FireRange ? State.Engage : State.Approach;
        }

        bool PlayerIsSubdued()
        {
            if (_playerHealth != null && _playerHealth.IsDead) return true;
            if (_playerController == null) return true;
            return _playerController.PlanarSpeed <= MaxArrestSpeed;
        }

        void TickApproach(Vector3 toPlayer, float distance)
        {
            _arrestTimer = 0f;
            Move(distance > ArrestRadius ? toPlayer.normalized : Vector3.zero);
        }

        void TickEngage(Vector3 toPlayer, float distance)
        {
            _arrestTimer = 0f;

            // Hold at a comfortable distance rather than walking into the player's face.
            Vector3 move = distance > PreferredRange ? toPlayer.normalized : Vector3.zero;
            Move(move, faceOverride: toPlayer.normalized);

            _fireTimer -= Time.deltaTime;
            if (_fireTimer > 0f) return;
            _fireTimer = FireCooldown;

            Fire();
        }

        void TickArrest(Vector3 toPlayer, float distance)
        {
            Move(Vector3.zero, faceOverride: toPlayer.normalized);

            _arrestTimer += Time.deltaTime;
            if (_arrestTimer < ArrestTime) return;

            _arrestTimer = 0f;
            GameStateManager.Instance?.Bust();
        }

        void Fire()
        {
            if (_anim != null) _anim.TriggerShoot();

            Vector3 origin = transform.position + Vector3.up * 1.45f;
            Vector3 target = _player.position + Vector3.up * 1.1f;
            Vector3 direction = (target - origin).normalized;

            // Accuracy as a cone: a perfect shot every time would be unplayable.
            float spread = Mathf.Lerp(9f, 0.6f, Mathf.Clamp01(Accuracy));
            direction = Quaternion.Euler(
                Random.Range(-spread, spread), Random.Range(-spread, spread), 0f) * direction;

            bool didHit = Physics.Raycast(origin, direction, out RaycastHit hit, FireRange,
                                          HitMask, QueryTriggerInteraction.Ignore);

            // The shared effect, same as the player and HostileNpc use.
            WeaponVfx.Ensure().Shot(origin,
                                    didHit ? hit.point : origin + direction * FireRange,
                                    didHit,
                                    didHit ? hit.normal : Vector3.zero);

            if (!didHit) return;

            var victim = hit.collider.GetComponentInParent<Health>();
            if (victim == null || victim == _health) return;

            victim.Apply(new DamageInfo(Damage, hit.point, direction, DamageSource.Bullet, gameObject));
        }

        void Move(Vector3 direction, Vector3 faceOverride = default)
        {
            bool moving = direction.sqrMagnitude > 0.0001f;

            Vector3 face = faceOverride.sqrMagnitude > 0.0001f ? faceOverride : direction;
            if (face.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(face, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look,
                                                      1f - Mathf.Exp(-TurnSpeed * Time.deltaTime));
            }

            _verticalVelocity = _cc.isGrounded ? -1f : _verticalVelocity + Gravity * Time.deltaTime;

            Vector3 motion = direction * MoveSpeed + Vector3.up * _verticalVelocity;
            _cc.Move(motion * Time.deltaTime);

            if (_anim != null) _anim.SetLocomotion(moving ? 1f : 0f, true, false, 0f);
        }

        void OnDied(DamageInfo info)
        {
            if (_ragdoll != null)
                _ragdoll.Collapse(info.Direction, Mathf.Clamp(info.Amount * 0.08f, 1.5f, 8f));
            else
                enabled = false;
        }
    }
}
