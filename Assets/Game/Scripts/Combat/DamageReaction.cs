using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Plays a flinch when this character is hurt, and makes civilians run from whoever did it.
    ///
    /// Split out from <see cref="Health"/> so that damage bookkeeping stays free of animation
    /// and AI concerns -- a destructible object can have Health with no reaction at all.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class DamageReaction : MonoBehaviour
    {
        [Tooltip("Minimum damage worth flinching at, so chip damage does not stutter the pose.")]
        public float FlinchThreshold = 4f;
        [Tooltip("Seconds between flinches, so rapid fire does not lock the character in reaction.")]
        public float FlinchCooldown = 0.6f;

        [Tooltip("Civilians run from the attacker when this character is hurt.")]
        public bool PanicsBystanders = true;
        public float PanicRadius = 16f;

        Health _health;
        PlayerAnimation _anim;
        float _lastFlinch = -99f;

        void Awake()
        {
            _health = GetComponent<Health>();
            _anim = GetComponentInChildren<PlayerAnimation>();
        }

        void OnEnable()
        {
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
            _health.Revived += OnRevived;
        }

        void OnDisable()
        {
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
            _health.Revived -= OnRevived;
        }

        /// <summary>
        /// Puts the character into its death pose.
        ///
        /// <b>BUG-012.</b> Nothing in the project listened to <see cref="Health.Died"/> for
        /// animation purposes, so a killed character kept playing whatever locomotion clip it
        /// was on -- observed live as a pedestrian punched to zero health standing there walking
        /// on the spot. This is the one line that was missing; the Death state and the clip both
        /// now exist in the controller.
        ///
        /// Deliberately not disabling the Animator: RagdollLite may want to blend out of the
        /// death pose, and a disabled Animator freezes the rig in whatever frame it was on.
        /// </summary>
        void OnDied(DamageInfo info)
        {
            if (_anim != null) _anim.SetDead(true);
        }

        void OnRevived()
        {
            if (_anim != null) _anim.SetDead(false);
        }

        void OnDamaged(DamageInfo info)
        {
            if (info.Amount >= FlinchThreshold && Time.time - _lastFlinch >= FlinchCooldown)
            {
                _lastFlinch = Time.time;
                if (_anim != null) _anim.TriggerHit();
            }

            if (!PanicsBystanders) return;

            Vector3 threat = info.Attacker != null ? info.Attacker.transform.position : info.Point;
            Pedestrian.AlarmNear(transform.position, PanicRadius, threat);
        }
    }
}
