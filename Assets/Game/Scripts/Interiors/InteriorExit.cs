using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The way out. Walking into it puts the player back on the street where they came in.
    ///
    /// Proximity rather than a physics trigger: the player arrives standing on the entry point,
    /// and a trigger volume large enough to be reliable would often already contain them,
    /// bouncing them straight back outside.
    /// </summary>
    public class InteriorExit : MonoBehaviour
    {
        public float ExitRadius = 1.8f;
        [Tooltip("Ignore the exit for a moment after entering, so you do not walk in and " +
                 "immediately back out again.")]
        public float ArmDelay = 0.8f;

        Transform _player;
        float _armedAt;

        /// <summary>
        /// Start the grace period. Must be called each time the player walks in, not just on
        /// enable: the component is enabled once at scene load, so an OnEnable-based delay has
        /// long expired by the time anyone actually uses the door, and the player is bounced
        /// straight back out the moment they arrive.
        /// </summary>
        public void Arm() => _armedAt = Time.time + ArmDelay;

        void OnEnable() => Arm();

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _player = player.transform;
        }

        void Update()
        {
            if (_player == null || Time.time < _armedAt) return;

            var interiors = InteriorManager.Instance;
            if (interiors == null || !interiors.IsInside) return;

            Vector3 delta = _player.position - transform.position;
            delta.y = 0f;
            if (delta.magnitude > ExitRadius) return;

            interiors.Exit();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, ExitRadius);
        }
    }
}
