using System;
using UnityEngine;

namespace MiniGTA
{
    public enum AdProvider
    {
        /// <summary>Fake ads with a real countdown. Playtestable with no ad account.</summary>
        Mock,
        /// <summary>Google Mobile Ads. Requires the SDK and real unit ids.</summary>
        GoogleMobileAds,
        /// <summary>No ads at all. Useful for a paid build or for recording footage.</summary>
        None,
    }

    /// <summary>
    /// The single entry point game code uses to ask for an ad, plus the frequency rules.
    ///
    /// Capping lives here rather than in the provider so it applies identically no matter who
    /// is serving. An interstitial the player sees twice in thirty seconds is worse for
    /// retention than one they never see, so the cap is enforced before the provider is ever
    /// asked.
    /// </summary>
    public class AdService : MonoBehaviour
    {
        static AdService _instance;

        public static AdService Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<AdService>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Provider")]
        [Tooltip("Which implementation to use. Swapping this is the only change needed to go " +
                 "from playtesting to a live ad account.")]
        public AdProvider Provider = AdProvider.Mock;

        [Header("Frequency caps")]
        [Tooltip("Minimum seconds between interstitials.")]
        public float InterstitialCooldown = 180f;
        [Tooltip("Skip the very first interstitial opportunity, so a new player is not hit " +
                 "with an ad before they have played anything.")]
        public int GraceInterstitials = 1;

        [Header("Launch")]
        [Tooltip("Show an interstitial shortly after the game starts.")]
        public bool ShowOnLaunch = true;
        public float LaunchDelay = 2.5f;

        [Header("Debug")]
        public bool LogAdEvents = true;

        IAdService _provider;
        float _lastInterstitialTime = -9999f;
        int _interstitialOpportunities;

        /// <summary>Fired whenever an ad opens or closes, so gameplay can pause around it.</summary>
        public event Action<bool> AdVisibilityChanged;

        public IAdService Provider_ => _provider;
        public string ProviderName => _provider != null ? _provider.ProviderName : "none";

        /// <summary>True while an ad is on screen.</summary>
        public bool AdShowing { get; private set; }

        public bool AdsRemoved =>
            PlayerProgress.Instance != null && PlayerProgress.Instance.AdsRemoved;

        /// <summary>Seconds until an interstitial is allowed again, 0 if ready.</summary>
        public float InterstitialCooldownRemaining =>
            Mathf.Max(0f, InterstitialCooldown - (Time.unscaledTime - _lastInterstitialTime));

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;

            _provider = CreateProvider();
            _provider.Initialise();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            if (ShowOnLaunch) Invoke(nameof(LaunchInterstitial), LaunchDelay);
        }

        void LaunchInterstitial() => TryShowInterstitial(AdPlacement.AppLaunch);

        IAdService CreateProvider()
        {
            switch (Provider)
            {
                case AdProvider.GoogleMobileAds:
                    return new GoogleMobileAdsService(this);
                case AdProvider.None:
                    return new NullAdService();
                default:
                    return new MockAdService(this);
            }
        }

        // ------------------------------------------------------------------ rewarded

        /// <summary>
        /// Offer a rewarded ad. <paramref name="onReward"/> runs only if it was watched to the
        /// end; <paramref name="onFailed"/> runs on dismissal or unavailability.
        ///
        /// Rewarded ads ignore the interstitial cap and the Remove Ads purchase: the player
        /// asked for this one, and it pays them.
        /// </summary>
        public void ShowRewarded(AdPlacement placement, Action onReward, Action onFailed = null)
        {
            if (_provider == null || !_provider.IsRewardedReady)
            {
                Log("rewarded unavailable at " + placement);
                onFailed?.Invoke();
                return;
            }

            SetAdShowing(true);

            _provider.ShowRewarded(placement, resultCode =>
            {
                SetAdShowing(false);
                Log("rewarded " + placement + " -> " + resultCode);

                if (resultCode == AdResult.Rewarded) onReward?.Invoke();
                else onFailed?.Invoke();
            });
        }

        // -------------------------------------------------------------- interstitial

        /// <summary>
        /// Show an interstitial if the rules allow. Returns false when it was suppressed --
        /// callers should carry on regardless rather than waiting.
        /// </summary>
        public bool TryShowInterstitial(AdPlacement placement, Action onClosed = null)
        {
            if (!CanShowInterstitial(out string reason))
            {
                Log("interstitial at " + placement + " suppressed: " + reason);
                onClosed?.Invoke();
                return false;
            }

            _lastInterstitialTime = Time.unscaledTime;
            SetAdShowing(true);

            _provider.ShowInterstitial(placement, resultCode =>
            {
                SetAdShowing(false);
                Log("interstitial " + placement + " -> " + resultCode);
                onClosed?.Invoke();
            });

            return true;
        }

        bool CanShowInterstitial(out string reason)
        {
            reason = "";

            if (_provider == null) { reason = "no provider"; return false; }
            if (AdsRemoved) { reason = "ads removed"; return false; }
            if (AdShowing) { reason = "an ad is already showing"; return false; }
            if (!_provider.IsInterstitialReady) { reason = "not ready"; return false; }

            // Count the opportunity even when we skip it, so the grace period actually elapses.
            _interstitialOpportunities++;
            if (_interstitialOpportunities <= GraceInterstitials)
            {
                reason = "grace period (" + _interstitialOpportunities + "/" + GraceInterstitials + ")";
                return false;
            }

            float remaining = InterstitialCooldownRemaining;
            if (remaining > 0f)
            {
                reason = "capped, " + remaining.ToString("0") + "s remaining";
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------------- banner

        /// <summary>Banners are menu-only, and never appear for a player who paid to remove ads.</summary>
        public void ShowBanner()
        {
            if (_provider == null || AdsRemoved) return;
            _provider.ShowBanner();
        }

        public void HideBanner() => _provider?.HideBanner();

        // -------------------------------------------------------------------- state

        void SetAdShowing(bool showing)
        {
            if (AdShowing == showing) return;
            AdShowing = showing;
            AdVisibilityChanged?.Invoke(showing);
        }

        /// <summary>Called when the Remove Ads entitlement changes.</summary>
        public void OnAdsRemovedChanged(bool removed)
        {
            _provider?.SetAdsRemoved(removed);
            if (removed) HideBanner();
        }

        void Log(string message)
        {
            if (LogAdEvents) Debug.Log("[Ads/" + ProviderName + "] " + message);
        }
    }

    /// <summary>Serves nothing. Every call reports unavailable, immediately.</summary>
    public class NullAdService : IAdService
    {
        public string ProviderName => "none";
        public bool IsInitialised => true;
        public bool IsRewardedReady => false;
        public bool IsInterstitialReady => false;

        public void Initialise() { }
        public void ShowRewarded(AdPlacement placement, Action<AdResult> onComplete)
            => onComplete?.Invoke(AdResult.Unavailable);
        public void ShowInterstitial(AdPlacement placement, Action<AdResult> onClosed)
            => onClosed?.Invoke(AdResult.Unavailable);
        public void ShowBanner() { }
        public void HideBanner() { }
        public void SetAdsRemoved(bool removed) { }
    }
}
