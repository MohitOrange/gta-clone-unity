using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A mission enemy: closes on its target and shoots.
    ///
    /// Separate from <see cref="PoliceOfficer"/> despite the overlap, because police behaviour
    /// is gated on the wanted level and includes arrest -- neither of which applies to a hired
    /// gun. Sharing one class would mean a pile of "if this is a mission enemy" branches
    /// through logic that is otherwise about the law.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class HostileNpc : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Who to attack. Defaults to the player.")]
        public Transform Target;
        [Tooltip("Give up and idle beyond this range.")]
        public float LoseInterestRange = 90f;

        [Header("Movement")]
        public float MoveSpeed = 4.2f;
        public float TurnSpeed = 8f;
        public float Gravity = -18f;
        [Tooltip("Hold at this range and shoot rather than closing all the way in.")]
        public float PreferredRange = 8f;

        [Header("Firearm")]
        public float FireRange = 26f;
        public float FireCooldown = 1.3f;
        public float Damage = 9f;
        [Range(0f, 1f)] public float Accuracy = 0.6f;
        public LayerMask HitMask = ~0;

        [Header("Melee")]
        [Tooltip("Inside this range the enemy stops shooting and swings instead.")]
        public float MeleeRange = 2.2f;
        [Tooltip("Radius of the swing volume. Matches PlayerCombat so both read the same.")]
        public float MeleeRadius = 0.85f;
        public float MeleeDamage = 12f;
        public float MeleeCooldown = 0.9f;

        CharacterController _cc;
        Health _health;
        PlayerAnimation _anim;
        RagdollLite _ragdoll;

        float _fireTimer;
        float _meleeTimer;
        float _verticalVelocity;

        readonly Collider[] _meleeHits = new Collider[12];

        /// <summary>Raised when this enemy dies, so a mission can count kills.</summary>
        public event System.Action<HostileNpc> Died;

        public bool IsDead => _health != null && _health.IsDead;

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
            if (Target != null) return;

            var player = GameObject.FindWithTag("Player");
            if (player != null) Target = player.transform;
        }

        void Update()
        {
            if (Target == null || IsDead) return;

            var game = GameStateManager.Instance;
            if (game != null && !game.IsPlaying) { Move(Vector3.zero); return; }

            Vector3 toTarget = Target.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            if (distance > LoseInterestRange) { Move(Vector3.zero); return; }

            Vector3 direction = toTarget.normalized;
            Move(distance > PreferredRange ? direction : Vector3.zero, direction);

            // Point blank is a swing, not a shot. A rifle fired into someone standing on top
            // of you reads as a bug, and the raycast is unreliable at that range anyway --
            // the muzzle origin can already be inside the target's collider, so the ray
            // starts past it and hits whatever is behind.
            if (distance <= MeleeRange)
            {
                _meleeTimer -= Time.deltaTime;
                if (_meleeTimer > 0f) return;

                _meleeTimer = MeleeCooldown;
                Melee();
                return;
            }

            if (distance > FireRange) return;

            _fireTimer -= Time.deltaTime;
            if (_fireTimer > 0f) return;

            _fireTimer = FireCooldown;
            Fire();
        }

        /// <summary>
        /// A swing, using the same volume and facing test as <see cref="PlayerCombat"/>.
        ///
        /// <b>BUG-014.</b> This class had no melee at all -- it shot at every range, including
        /// zero. Section 2.9 asks for the player and every armed NPC to run the same weapon
        /// mechanic, so this deliberately mirrors PlayerCombat.Melee rather than inventing a
        /// second set of rules: the hit sphere sits half a range ahead of the chest, and the
        /// facing gate uses the same forward vector and the same 0.2 dot threshold. Both were
        /// wrong together in BUG-008 and are now right together.
        ///
        /// It damages any Health in the arc, not just the current Target, which is what makes
        /// NPC-vs-NPC damage work: a swing aimed at the player catches a pedestrian standing
        /// in it, exactly as the player's swing does.
        /// </summary>
        void Melee()
        {
            if (_anim != null) _anim.TriggerPunch();

            Vector3 origin = transform.position + Vector3.up * 1.1f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : transform.forward;

            int count = Physics.OverlapSphereNonAlloc(
                origin + forward * (MeleeRange * 0.5f), MeleeRadius, _meleeHits,
                HitMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var victim = _meleeHits[i].GetComponentInParent<Health>();
                if (victim == null || victim == _health || victim.IsDead) continue;

                Vector3 toVictim = victim.transform.position - transform.position;
                toVictim.y = 0f;
                if (Vector3.Dot(toVictim.normalized, forward) < 0.2f) continue;

                victim.Apply(new DamageInfo(MeleeDamage, victim.transform.position + Vector3.up,
                                            forward, DamageSource.Melee, gameObject));
            }
        }

        void Fire()
        {
            if (_anim != null) _anim.TriggerShoot();

            Vector3 origin = transform.position + Vector3.up * 1.45f;
            Vector3 aim = Target.position + Vector3.up * 1.1f;
            Vector3 direction = (aim - origin).normalized;

            float spread = Mathf.Lerp(11f, 1f, Mathf.Clamp01(Accuracy));
            direction = Quaternion.Euler(
                Random.Range(-spread, spread), Random.Range(-spread, spread), 0f) * direction;

            bool didHit = Physics.Raycast(origin, direction, out RaycastHit hit, FireRange,
                                          HitMask, QueryTriggerInteraction.Ignore);

            // The same shared effect the player uses, per 2.9 -- an NPC muzzle flash that
            // did not match the player's would be the tell that they are different systems.
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
            _cc.Move((direction * MoveSpeed + Vector3.up * _verticalVelocity) * Time.deltaTime);

            if (_anim != null) _anim.SetLocomotion(moving ? 1f : 0f, true, false, 0f);
        }

        void OnDied(DamageInfo info)
        {
            Died?.Invoke(this);

            if (_ragdoll != null)
                _ragdoll.Collapse(info.Direction, Mathf.Clamp(info.Amount * 0.08f, 1.5f, 8f));
            else
                enabled = false;
        }
    }
}
