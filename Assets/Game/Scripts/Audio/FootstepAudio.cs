using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Footsteps for the player, timed off distance travelled rather than off the animation.
    ///
    /// Driving them from a distance accumulator means the step rate tracks the actual speed
    /// for free -- walking, running and the acceleration between them all land correctly --
    /// without needing animation events baked into retargeted Mixamo clips.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class FootstepAudio : MonoBehaviour
    {
        [Tooltip("Metres of ground covered per footstep.")]
        public float StrideLength = 1.55f;
        [Tooltip("Below this speed the player is shuffling, not walking.")]
        public float MinSpeed = 0.6f;

        [Range(0f, 1f)] public float Volume = 0.45f;

        PlayerController _player;
        float _distance;

        void Awake() => _player = GetComponent<PlayerController>();

        void Update()
        {
            // Swimming and falling have no ground to hit.
            if (!_player.IsGrounded || _player.IsSwimming) { _distance = 0f; return; }

            float speed = _player.PlanarSpeed;
            if (speed < MinSpeed) { _distance = 0f; return; }

            _distance += speed * Time.deltaTime;
            if (_distance < StrideLength) return;

            _distance -= StrideLength;

            // Running lands harder than walking, and no two steps are identical.
            float weight = Mathf.InverseLerp(MinSpeed, 5.8f, speed);
            AudioManager.Instance?.Play(Sfx.Footstep, transform.position,
                                        Volume * Mathf.Lerp(0.6f, 1f, weight),
                                        Random.Range(0.85f, 1.15f));
        }
    }
}
