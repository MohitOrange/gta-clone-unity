using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The person being escorted. Trails the player at a set distance and stops when close
    /// enough, so they read as following rather than shoving.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class EscortClient : MonoBehaviour
    {
        public Transform Leader;
        public float MoveSpeed = 3.6f;
        public float FollowDistance = 3.2f;
        public float TurnSpeed = 8f;
        public float Gravity = -18f;

        [Tooltip("Break into a run if this far behind, so they can catch up.")]
        public float SprintDistance = 9f;
        public float SprintMultiplier = 1.6f;

        CharacterController _cc;
        PlayerAnimation _anim;
        Health _health;
        RagdollLite _ragdoll;
        float _verticalVelocity;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _anim = GetComponentInChildren<PlayerAnimation>();
            _health = GetComponent<Health>();
            _ragdoll = GetComponent<RagdollLite>();
        }

        void OnEnable() { if (_health != null) _health.Died += OnDied; }
        void OnDisable() { if (_health != null) _health.Died -= OnDied; }

        void Update()
        {
            if (Leader == null || (_health != null && _health.IsDead)) return;

            Vector3 to = Leader.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;

            bool shouldMove = distance > FollowDistance;
            bool sprinting = distance > SprintDistance;

            Vector3 direction = shouldMove ? to.normalized : Vector3.zero;
            float speed = MoveSpeed * (sprinting ? SprintMultiplier : 1f);

            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look,
                                                      1f - Mathf.Exp(-TurnSpeed * Time.deltaTime));
            }

            _verticalVelocity = _cc.isGrounded ? -1f : _verticalVelocity + Gravity * Time.deltaTime;
            _cc.Move((direction * speed + Vector3.up * _verticalVelocity) * Time.deltaTime);

            if (_anim != null)
                _anim.SetLocomotion(shouldMove ? (sprinting ? 1f : 0.5f) : 0f, true, false, 0f);
        }

        void OnDied(DamageInfo info)
        {
            if (_ragdoll != null) _ragdoll.Collapse(info.Direction, 3f);
            enabled = false;
        }
    }
}
