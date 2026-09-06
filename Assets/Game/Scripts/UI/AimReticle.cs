using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The centre-screen crosshair, shown only while the weapon is actually up.
    ///
    /// <b>Why it is tied to the aim stance and not to being armed.</b> Bullets leave along the
    /// camera's forward vector (see <see cref="PlayerCombat"/>), so the centre of the screen is
    /// genuinely where a shot goes -- but only when the player has raised the weapon. A
    /// crosshair painted on at all times would promise that precision while the character is
    /// jogging with the gun at their hip, which is the sort of small lie that makes a game feel
    /// inaccurate rather than making it feel aimed.
    ///
    /// <see cref="PlayerAnimation.InAimStance"/> is the same signal that drives the AimPose
    /// animator layer, so the reticle and the pose can never disagree: if the character is
    /// visibly aiming, the crosshair is up, and vice versa.
    /// </summary>
    public class AimReticle : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Faded in and out as a group. Left null, the component finds one on itself.")]
        public CanvasGroup Group;

        [Header("Feel")]
        [Tooltip("Alpha units per second. Fast enough to feel instant, slow enough not to pop.")]
        public float FadeSpeed = 14f;

        [Tooltip("How far the arms sit from centre when the weapon is down, as a multiplier on "
                 + "their resting offset. The reticle tightens as it appears, which reads as "
                 + "the weapon steadying.")]
        public float BloomWhenIdle = 1.7f;

        [Tooltip("The four arms. Their authored anchoredPosition is treated as the tight, "
                 + "fully-aimed position.")]
        public RectTransform[] Arms = System.Array.Empty<RectTransform>();

        PlayerAnimation _anim;
        Vector2[] _tight;
        float _bloom = 1f;

        void Awake()
        {
            if (Group == null) Group = GetComponent<CanvasGroup>();

            _tight = new Vector2[Arms.Length];
            for (int i = 0; i < Arms.Length; i++)
                if (Arms[i] != null) _tight[i] = Arms[i].anchoredPosition;

            if (Group != null) Group.alpha = 0f;
        }

        void Update()
        {
            if (_anim == null)
            {
                // The player is rebuilt by the scene assembler and respawned on death, so this
                // is resolved lazily rather than cached once in Awake.
                var player = GameObject.FindWithTag("Player");
                if (player != null) _anim = player.GetComponentInChildren<PlayerAnimation>();
                if (_anim == null)
                {
                    if (Group != null) Group.alpha = 0f;
                    return;
                }
            }

            bool aiming = _anim.InAimStance;

            if (Group != null)
                Group.alpha = Mathf.MoveTowards(Group.alpha, aiming ? 1f : 0f,
                                                FadeSpeed * Time.unscaledDeltaTime);

            _bloom = Mathf.MoveTowards(_bloom, aiming ? 1f : BloomWhenIdle,
                                       FadeSpeed * Time.unscaledDeltaTime);

            for (int i = 0; i < Arms.Length; i++)
                if (Arms[i] != null) Arms[i].anchoredPosition = _tight[i] * _bloom;
        }
    }
}
