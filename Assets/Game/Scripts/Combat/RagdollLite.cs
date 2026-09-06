using System.Collections;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Death handling in two stages: the rig plays its death animation, then the body goes
    /// physical and topples as a single rigid capsule carrying the momentum of whatever
    /// killed it.
    ///
    /// A real ragdoll needs a dozen bodies and joints per character, which on a phone running
    /// a dozen NPCs is not affordable. Freezing the skinned pose and toppling one capsule
    /// reads convincingly at gameplay distance for a fraction of the cost.
    ///
    /// <b>BUG-012.</b> This class used to disable the Animator on the first frame of the
    /// collapse. That was correct when it was written -- there was no death animation, so
    /// stopping the animator was how the corpse kept the pose the hit reaction left it in.
    /// Once Death01 was wired up, that same line became the bug: the animator was switched
    /// off in the same frame the Dead parameter was raised, so the state machine never got a
    /// frame in which to evaluate the AnyState -> Death transition. Every diagnostic looked
    /// healthy -- the parameter really was true, the state and transition really were in the
    /// controller -- because the machine driving them had been turned off. The rig simply
    /// froze on its last evaluated clip, which read as "the death animation never plays".
    ///
    /// So the animator now stays alive for the length of the death clip and is frozen only
    /// at the point physics takes over. Rigs with no death state keep the old behaviour.
    /// </summary>
    public class RagdollLite : MonoBehaviour
    {
        [Header("Body")]
        [Tooltip("Capsule dimensions for the toppled body, in metres.")]
        public float Height = 1.75f;
        public float Radius = 0.3f;
        public float Mass = 72f;

        [Header("Death animation")]
        [Tooltip("Let the rig play its Death state before the body goes physical. "
                 + "Rigs whose controller has no Dead parameter collapse immediately.")]
        public bool PlayDeathAnimation = true;
        [Range(0.1f, 1f)]
        [Tooltip("How far into the death clip physics takes over. Under 1 so the body is "
                 + "still settling when it goes physical, rather than snapping from a pose "
                 + "that has already finished.")]
        public float FreezeAtClipFraction = 0.85f;
        [Tooltip("Fallback hold, used only if the death state reports no length.")]
        public float DeathAnimationSeconds = 0.75f;

        [Header("Impulse")]
        [Tooltip("How hard a killing blow shoves the body.")]
        public float ImpulseScale = 3.2f;
        [Tooltip("Upward component, so bodies lift slightly rather than sliding flat.")]
        public float UpwardBias = 0.35f;
        [Tooltip("Random spin applied on collapse.")]
        public float TorqueScale = 2.4f;

        [Header("Cleanup")]
        [Tooltip("Seconds before the corpse is removed. 0 keeps it forever.")]
        public float DespawnAfter = 25f;

        Rigidbody _body;
        CapsuleCollider _capsule;
        bool _collapsed;

        public bool HasCollapsed => _collapsed;

        /// <summary>
        /// Switch the character from animated to dead. Safe to call more than once.
        /// </summary>
        public void Collapse(Vector3 impulseDirection, float force)
        {
            if (_collapsed) return;
            _collapsed = true;

            // Stop everything that would fight for control of the transform. This component
            // excludes itself so it can still run the death coroutine, and the Animator is
            // deliberately not touched here -- see the class summary.
            foreach (var behaviour in GetComponents<MonoBehaviour>())
                if (behaviour != this) behaviour.enabled = false;

            var animator = GetComponentInChildren<Animator>();

            if (PlayDeathAnimation && TryPlayDeath(animator))
            {
                // The CharacterController stays enabled through the animation so the dying
                // body still has a collider and the player cannot walk through it.
                StartCoroutine(GoPhysicalAfterDeathClip(animator, impulseDirection, force));
                return;
            }

            GoPhysical(animator, impulseDirection, force);
        }

        /// <summary>
        /// Raises Dead on the rig. Returns false if this rig has no death state to play, in
        /// which case the caller should collapse immediately rather than wait for an
        /// animation that will never arrive.
        /// </summary>
        bool TryPlayDeath(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;

            bool hasDead = false;
            foreach (var p in animator.parameters)
            {
                if (p.nameHash != PlayerAnimation.DeadHash) continue;
                hasDead = p.type == AnimatorControllerParameterType.Bool;
                break;
            }
            if (!hasDead) return false;

            // A corpse that dies off-screen must still animate, or it pops into its death
            // pose the moment the camera finds it.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            animator.SetBool(PlayerAnimation.DeadHash, true);
            return true;
        }

        /// <summary>
        /// Holds off physics until the death clip has essentially played out.
        ///
        /// The hold is read from the death state rather than typed in as a constant. The
        /// clip in the pack is 0.733s, but Death02 and Death03 are not the same length, and
        /// a hand-authored replacement would be a third -- a hardcoded wait would freeze
        /// some deaths mid-fall and let others finish and stand there before toppling.
        /// </summary>
        IEnumerator GoPhysicalAfterDeathClip(Animator animator, Vector3 impulseDirection, float force)
        {
            // Let the transition into Death finish first, or the length read below is still
            // the length of the clip being transitioned out of.
            float guard = 0f;
            while (animator != null && animator.isActiveAndEnabled
                   && animator.IsInTransition(0) && guard < 0.5f)
            {
                guard += Time.deltaTime;
                yield return null;
            }

            float length = animator != null && animator.isActiveAndEnabled
                ? animator.GetCurrentAnimatorStateInfo(0).length
                : 0f;

            float hold = length > 0.01f
                ? length * Mathf.Clamp01(FreezeAtClipFraction)
                : Mathf.Max(0f, DeathAnimationSeconds);

            yield return new WaitForSeconds(hold);
            GoPhysical(animator, impulseDirection, force);
        }

        /// <summary>
        /// Freezes the pose and hands the body to physics.
        /// </summary>
        void GoPhysical(Animator animator, Vector3 impulseDirection, float force)
        {
            if (_body != null) return;   // already physical

            if (animator != null)
            {
                // Culling must go off first: a culled animator would not have written the pose
                // we are about to freeze, leaving the corpse in bind pose.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = false;
            }

            var controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            _capsule = gameObject.AddComponent<CapsuleCollider>();
            _capsule.height = Height;
            _capsule.radius = Radius;
            _capsule.center = new Vector3(0f, Height * 0.5f, 0f);
            // Lay the capsule along Z so it tips over rather than standing like a post.
            _capsule.direction = 2;

            _body = gameObject.AddComponent<Rigidbody>();
            _body.mass = Mass;
            _body.linearDamping = 0.4f;
            _body.angularDamping = 0.6f;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.interpolation = RigidbodyInterpolation.Interpolate;

            Vector3 impulse = (impulseDirection.normalized + Vector3.up * UpwardBias).normalized
                              * (force * ImpulseScale);
            _body.AddForce(impulse, ForceMode.Impulse);

            _body.AddTorque(Random.insideUnitSphere * (TorqueScale * force), ForceMode.Impulse);

            if (DespawnAfter > 0f) Destroy(gameObject, DespawnAfter);
        }
    }
}
