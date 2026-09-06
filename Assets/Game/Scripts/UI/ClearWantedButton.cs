using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// The "lose the heat" button. Appears only while the player is wanted enough for the
    /// offer to be worth anything.
    ///
    /// Player-initiated on purpose: interrupting a chase with an unsolicited ad offer is the
    /// single most resented thing a mobile game can do. This waits to be asked.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class ClearWantedButton : MonoBehaviour
    {
        public Button Button;
        public AdRewards Rewards;
        public float FadeSpeed = 8f;

        CanvasGroup _group;

        void Awake() => _group = GetComponent<CanvasGroup>();

        void Start()
        {
            if (Rewards == null) Rewards = FindAnyObjectByType<AdRewards>();
            if (Button != null) Button.onClick.AddListener(OnPressed);

            _group.alpha = 0f;
        }

        void Update()
        {
            bool show = Rewards != null
                        && Rewards.ClearWantedAvailable
                        && (AdService.Instance == null || !AdService.Instance.AdShowing)
                        && (AdOfferPrompt.Instance == null || !AdOfferPrompt.Instance.IsShowing);

            _group.alpha = Mathf.MoveTowards(_group.alpha, show ? 1f : 0f,
                                             FadeSpeed * Time.unscaledDeltaTime);

            // Stop accepting taps the moment it starts fading, not when it reaches zero.
            _group.blocksRaycasts = show;
            _group.interactable = show;
        }

        void OnPressed() => Rewards?.OfferClearWanted();
    }
}
