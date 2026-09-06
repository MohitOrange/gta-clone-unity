using System;

namespace MiniGTA
{
    /// <summary>Where an ad was asked for. Used for capping rules and analytics.</summary>
    public enum AdPlacement
    {
        AppLaunch,
        MissionComplete,
        GameOver,
        DoubleCash,
        Revive,
        ClearWanted,
        Menu,
    }

    /// <summary>How a rewarded ad ended.</summary>
    public enum AdResult
    {
        /// <summary>Watched to the end. The reward is owed.</summary>
        Rewarded,
        /// <summary>Closed early. No reward.</summary>
        Dismissed,
        /// <summary>Nothing was available, or the provider failed.</summary>
        Unavailable,
    }

    /// <summary>
    /// Everything the game is allowed to know about advertising.
    ///
    /// Game code calls this and nothing else -- no provider type, no SDK namespace, no
    /// initialisation order. That is the whole point: swapping the mock for Google Mobile Ads
    /// (or removing ads entirely for a paid build) is a one-line change in
    /// <see cref="AdService"/> and touches no gameplay.
    ///
    /// Every method must be safe to call at any time, including before initialisation and on
    /// a platform with no ad support. Implementations report failure through the callback
    /// rather than throwing, so a missing ad can never break a mission flow.
    /// </summary>
    public interface IAdService
    {
        string ProviderName { get; }

        /// <summary>True once the provider is ready to serve. Never blocks.</summary>
        bool IsInitialised { get; }

        /// <summary>True when a rewarded ad could be shown right now.</summary>
        bool IsRewardedReady { get; }

        /// <summary>True when an interstitial could be shown right now.</summary>
        bool IsInterstitialReady { get; }

        void Initialise();

        /// <summary>
        /// Show a rewarded ad. The callback always fires exactly once, with
        /// <see cref="AdResult.Unavailable"/> if nothing could be shown.
        /// </summary>
        void ShowRewarded(AdPlacement placement, Action<AdResult> onComplete);

        /// <summary>
        /// Show an interstitial. The callback fires once when the ad closes, or immediately
        /// if none was shown.
        /// </summary>
        void ShowInterstitial(AdPlacement placement, Action<AdResult> onClosed);

        /// <summary>Show the banner. Menus only -- never over live gameplay.</summary>
        void ShowBanner();

        void HideBanner();

        /// <summary>
        /// Stop serving interstitials and banners. Rewarded ads stay available, because they
        /// are opt-in and grant something the player wants.
        /// </summary>
        void SetAdsRemoved(bool removed);
    }
}
