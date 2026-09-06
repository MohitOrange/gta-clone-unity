using UnityEngine;

namespace MiniGTA
{
    /// <summary>How low the player is carrying themselves.</summary>
    public enum Stance { Standing, Crouched, Prone }

    /// <summary>
    /// Crouch and prone.
    ///
    /// <b>Read this before judging how it looks.</b> Neither pose has an animation, and that is
    /// not an oversight in this script. The two imported humanoid packs between them ship
    /// idles, a military idle, combat aims, eight-way walk/run/sprint, turns, jump, damage,
    /// death, talk and a grenade throw (Kevin Iglesias), and eighteen standing martial-arts
    /// strikes (EEJANAI). There is no crouch, no kneel, no crawl and no prone clip in either.
    /// The nearest thing to a low pose is a death clip, and dressing a crouch in a death
    /// animation would look like a corpse sliding along the pavement.
    ///
    /// So this implements the half that can be implemented honestly: the collider, the eye
    /// height and the speed are all real and all measurable. The character continues to stand
    /// up straight while doing it. That is deliberately not disguised -- the alternative was to
    /// sink the mesh into the ground to fake a lower silhouette, which puts the legs through
    /// the pavement and looks broken rather than absent. Same treatment as the seated-pose gap
    /// in ASSET_GAPS.md: record it, do not approximate it.
    ///
    /// What a fix needs: one crouch idle, one crouch walk, and one prone idle plus a crawl,
    /// humanoid and retargetable to the shared rig. The state machine, the collider maths, the
    /// speed multipliers and the buttons are all here and would not change.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerStance : MonoBehaviour
    {
        [Header("Capsule")]
        [Tooltip("Fraction of standing height while crouched.")]
        [Range(0.2f, 1f)] public float CrouchHeightFraction = 0.60f;
        [Tooltip("Fraction of standing height while prone.")]
        [Range(0.1f, 1f)] public float ProneHeightFraction = 0.30f;

        [Header("Speed")]
        [Tooltip("Movement speed multiplier while crouched.")]
        [Range(0.1f, 1f)] public float CrouchSpeedFactor = 0.45f;
        [Tooltip("Movement speed multiplier while prone. A crawl.")]
        [Range(0.05f, 1f)] public float ProneSpeedFactor = 0.20f;

        [Header("Feel")]
        [Tooltip("How fast the capsule resizes. Instant resizing pops the camera.")]
        public float TransitionSpeed = 8f;

        CharacterController _cc;
        PlayerController _player;
        Health _health;

        float _standHeight;
        Vector3 _standCentre;

        public Stance Current { get; private set; } = Stance.Standing;

        /// <summary>Speed multiplier for the current stance. Read by PlayerController.</summary>
        public float SpeedFactor => Current switch
        {
            Stance.Crouched => CrouchSpeedFactor,
            Stance.Prone => ProneSpeedFactor,
            _ => 1f,
        };

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _player = GetComponent<PlayerController>();
            _health = GetComponent<Health>();

            _standHeight = _cc.height;
            _standCentre = _cc.center;
        }

        void Update()
        {
            var hub = InputHub.Instance;
            if (hub == null) return;

            // Dead, swimming or driving: stance is meaningless and a shrunken capsule while
            // swimming would change the submersion test that decides swim entry.
            bool allowed = (_health == null || !_health.IsDead)
                           && (_player == null || !_player.IsSwimming);

            if (!allowed)
            {
                hub.ConsumeCrouch();
                hub.ConsumeProne();
                if (Current != Stance.Standing) Current = Stance.Standing;
            }
            else
            {
                // Each button toggles its own stance, and going straight from crouched to prone
                // works without standing up in between -- which is what the buttons look like
                // they should do.
                if (hub.ConsumeCrouch())
                    Current = Current == Stance.Crouched ? Stance.Standing : Stance.Crouched;

                if (hub.ConsumeProne())
                    Current = Current == Stance.Prone ? Stance.Standing : Stance.Prone;
            }

            ApplyCapsule();
        }

        /// <summary>
        /// Resizes the capsule towards the current stance.
        ///
        /// The centre moves with the height so the capsule keeps its feet on the ground rather
        /// than shrinking about its middle and leaving the player hovering: a CharacterController
        /// is positioned by its centre, so halving the height without halving the centre lifts
        /// the whole capsule by a quarter of its height.
        /// </summary>
        void ApplyCapsule()
        {
            float target = Current switch
            {
                Stance.Crouched => _standHeight * CrouchHeightFraction,
                Stance.Prone => _standHeight * ProneHeightFraction,
                _ => _standHeight,
            };

            if (Mathf.Approximately(_cc.height, target)) return;

            float height = Mathf.MoveTowards(_cc.height, target,
                                             _standHeight * TransitionSpeed * Time.deltaTime);

            // Standing back up has to be blocked when there is something overhead, or the
            // capsule grows through a ceiling and the player is ejected out of the world.
            if (height > _cc.height && !HasHeadroom(height)) return;

            _cc.height = height;
            _cc.center = new Vector3(_standCentre.x,
                                     _standCentre.y - (_standHeight - height) * 0.5f,
                                     _standCentre.z);
        }

        /// <summary>Is there room to grow back to this height?</summary>
        bool HasHeadroom(float height)
        {
            float radius = _cc.radius * 0.95f;
            Vector3 bottom = transform.position + Vector3.up * (_cc.center.y - _cc.height * 0.5f + radius);
            Vector3 top = transform.position + Vector3.up * (_standCentre.y - _standHeight * 0.5f + height - radius);

            return !Physics.CheckCapsule(bottom, top, radius,
                                         ~(1 << LayerMask.NameToLayer("Ignore Raycast")),
                                         QueryTriggerInteraction.Ignore);
        }

        /// <summary>Forces the player upright. Used when getting into a vehicle.</summary>
        public void StandUp() => Current = Stance.Standing;
    }
}
