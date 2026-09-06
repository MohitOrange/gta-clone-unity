using System;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A fake ad network that behaves like a real one.
    ///
    /// It shows a real full-screen overlay with a real countdown and a skip button that only
    /// appears part-way through, because the point is to playtest the *flow* -- does the game
    /// pause correctly, does the reward land, does dismissing early correctly pay nothing --
    /// long before there is an AdMob account. A mock that instantly returned success would
    /// test none of that.
    /// </summary>
    public class MockAdService : IAdService
    {
        readonly AdService _host;

        bool _adsRemoved;

        public string ProviderName => "mock";
        public bool IsInitialised { get; private set; }

        // The mock always has inventory; that is the point of it.
        public bool IsRewardedReady => IsInitialised;
        public bool IsInterstitialReady => IsInitialised && !_adsRemoved;

        public MockAdService(AdService host) => _host = host;

        public void Initialise()
        {
            IsInitialised = true;
            Debug.Log("[Ads/mock] Initialised. No account needed; ads are simulated with a "
                      + "real countdown so the flow is testable.");
        }

        public void ShowRewarded(AdPlacement placement, Action<AdResult> onComplete)
        {
            var overlay = AdOverlayUI.Instance;
            if (overlay == null)
            {
                // No overlay in the scene: fail rather than silently paying out, so a missing
                // prefab shows up as "ads do not work" instead of "free money".
                Debug.LogWarning("[Ads/mock] No AdOverlayUI in the scene; rewarded unavailable.");
                onComplete?.Invoke(AdResult.Unavailable);
                return;
            }

            overlay.Play(new AdOverlayRequest
            {
                Headline = HeadlineFor(placement),
                Subline = "Watch to the end to claim your reward",
                Duration = 6f,
                SkippableAfter = 3f,
                Skippable = true,
                // Skipping a rewarded ad is allowed but pays nothing.
                RewardOnSkip = false,
                Placement = placement,
            }, onComplete);
        }

        public void ShowInterstitial(AdPlacement placement, Action<AdResult> onClosed)
        {
            var overlay = AdOverlayUI.Instance;
            if (overlay == null)
            {
                onClosed?.Invoke(AdResult.Unavailable);
                return;
            }

            overlay.Play(new AdOverlayRequest
            {
                Headline = "Advertisement",
                Subline = "",
                Duration = 4f,
                SkippableAfter = 2f,
                Skippable = true,
                // An interstitial has nothing to reward; closing it is the only outcome.
                RewardOnSkip = true,
                Placement = placement,
            }, onClosed);
        }

        public void ShowBanner() => AdOverlayUI.Instance?.SetBannerVisible(!_adsRemoved);
        public void HideBanner() => AdOverlayUI.Instance?.SetBannerVisible(false);

        public void SetAdsRemoved(bool removed)
        {
            _adsRemoved = removed;
            if (removed) HideBanner();
        }

        static string HeadlineFor(AdPlacement placement) => placement switch
        {
            AdPlacement.DoubleCash => "Double your payout",
            AdPlacement.Revive => "Get back up",
            AdPlacement.ClearWanted => "Lose the heat",
            _ => "Rewarded video",
        };
    }
}
