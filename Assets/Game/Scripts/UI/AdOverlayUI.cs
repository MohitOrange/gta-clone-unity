using System;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>What the mock provider wants shown.</summary>
    public struct AdOverlayRequest
    {
        public string Headline;
        public string Subline;
        public float Duration;
        public float SkippableAfter;
        public bool Skippable;
        /// <summary>True for interstitials, where closing early is still a normal close.</summary>
        public bool RewardOnSkip;
        public AdPlacement Placement;
    }

    /// <summary>
    /// The fake ad screen, and the banner.
    ///
    /// Runs on unscaled time and sets <see cref="Time.timeScale"/> to zero while showing,
    /// because a real full-screen ad suspends the game underneath it. Testing against an
    /// overlay that let the world keep running would hide every bug caused by the real thing
    /// pausing it -- police still shooting during an ad, timers still ticking on a mission.
    /// </summary>
    public class AdOverlayUI : MonoBehaviour
    {
        static AdOverlayUI _instance;

        public static AdOverlayUI Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<AdOverlayUI>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Overlay")]
        public CanvasGroup Panel;
        public Text HeadlineLabel;
        public Text SublineLabel;
        public Text CountdownLabel;
        public Image ProgressFill;
        public Button SkipButton;
        public Text SkipLabel;

        [Header("Banner")]
        public CanvasGroup Banner;
        public Text BannerLabel;

        [Header("Behaviour")]
        [Tooltip("Freeze the game while an ad is on screen, as a real full-screen ad does.")]
        public bool PauseGameWhileShowing = true;

        Action<AdResult> _onComplete;
        AdOverlayRequest _request;
        float _elapsed;
        bool _playing;
        float _restoreTimeScale = 1f;

        public bool IsPlaying => _playing;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            if (SkipButton != null) SkipButton.onClick.AddListener(Skip);
            SetPanelVisible(false);
            SetBannerVisible(false);
        }

        public void Play(AdOverlayRequest request, Action<AdResult> onComplete)
        {
            if (_playing)
            {
                // Never stack ads. Refuse rather than interrupting the one in flight.
                onComplete?.Invoke(AdResult.Unavailable);
                return;
            }

            _request = request;
            _onComplete = onComplete;
            _elapsed = 0f;
            _playing = true;

            if (HeadlineLabel != null) HeadlineLabel.text = request.Headline;
            if (SublineLabel != null) SublineLabel.text = request.Subline;

            SetPanelVisible(true);

            if (PauseGameWhileShowing)
            {
                _restoreTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
        }

        void Update()
        {
            if (!_playing) return;

            // Unscaled: the game is frozen underneath, but the ad still has to count down.
            _elapsed += Time.unscaledDeltaTime;

            float remaining = Mathf.Max(0f, _request.Duration - _elapsed);
            if (CountdownLabel != null) CountdownLabel.text = Mathf.CeilToInt(remaining) + "s";
            if (ProgressFill != null)
                ProgressFill.fillAmount = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _request.Duration));

            bool canSkip = _request.Skippable && _elapsed >= _request.SkippableAfter;
            if (SkipButton != null) SkipButton.gameObject.SetActive(canSkip);
            if (SkipLabel != null && !canSkip)
                SkipLabel.text = "Skip in " + Mathf.CeilToInt(_request.SkippableAfter - _elapsed);

            if (_elapsed >= _request.Duration) Finish(AdResult.Rewarded);
        }

        /// <summary>Closed early. Interstitials treat this as a normal close; rewarded do not.</summary>
        void Skip() => Finish(_request.RewardOnSkip ? AdResult.Rewarded : AdResult.Dismissed);

        void Finish(AdResult resultCode)
        {
            if (!_playing) return;
            _playing = false;

            if (PauseGameWhileShowing) Time.timeScale = _restoreTimeScale;

            SetPanelVisible(false);

            var callback = _onComplete;
            _onComplete = null;
            callback?.Invoke(resultCode);
        }

        void SetPanelVisible(bool visible)
        {
            if (Panel == null) return;
            Panel.alpha = visible ? 1f : 0f;
            Panel.blocksRaycasts = visible;
            Panel.interactable = visible;
        }

        public void SetBannerVisible(bool visible)
        {
            if (Banner == null) return;
            Banner.alpha = visible ? 1f : 0f;
            Banner.blocksRaycasts = false;

            if (visible && BannerLabel != null)
                BannerLabel.text = "Advertisement  -  your banner here";
        }
    }
}
