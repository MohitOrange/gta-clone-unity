using System;
using UnityEngine;

// Define MINIGTA_ADMOB in Project Settings > Player > Scripting Define Symbols once the
// Google Mobile Ads Unity plugin is installed. Until then this file compiles to a stub that
// reports "unavailable", so the project always builds whether or not the SDK is present.
#if MINIGTA_ADMOB
using GoogleMobileAds.Api;
#endif

namespace MiniGTA
{
    /// <summary>
    /// Google Mobile Ads (AdMob) adapter.
    ///
    /// The brief asked for a Capacitor AdMob plugin; this project is Unity, so the equivalent
    /// is the official Google Mobile Ads Unity plugin. Same account, same unit ids, same
    /// consent requirements -- only the binding differs.
    ///
    /// Everything provider-specific is confined to this one file behind
    /// <see cref="IAdService"/>. Game code never references it, so a build with no SDK
    /// installed still compiles and runs on the mock.
    ///
    /// To go live:
    ///   1. Install the plugin (Package Manager, from the googleads-mobile-unity releases).
    ///   2. Add MINIGTA_ADMOB to Scripting Define Symbols.
    ///   3. Put your real unit ids in the fields below (or leave the test ids while developing).
    ///   4. Set AdService.Provider to GoogleMobileAds.
    ///   5. Enter your AdMob App ID in Assets > Google Mobile Ads > Settings.
    /// </summary>
    public class GoogleMobileAdsService : IAdService
    {
        // Google's official test unit ids. These always fill and are safe to ship in a dev
        // build; using real ids on a test device is what gets an account suspended.
        const string AndroidTestRewarded = "ca-app-pub-3940256099942544/5224354917";
        const string AndroidTestInterstitial = "ca-app-pub-3940256099942544/1033173712";
        const string AndroidTestBanner = "ca-app-pub-3940256099942544/6300978111";

        readonly AdService _host;
        bool _adsRemoved;

        public string ProviderName => "admob";

        public GoogleMobileAdsService(AdService host) => _host = host;

#if MINIGTA_ADMOB
        RewardedAd _rewarded;
        InterstitialAd _interstitial;
        BannerView _banner;

        public bool IsInitialised { get; private set; }
        public bool IsRewardedReady => _rewarded != null && _rewarded.CanShowAd();
        public bool IsInterstitialReady =>
            !_adsRemoved && _interstitial != null && _interstitial.CanShowAd();

        public void Initialise()
        {
            MobileAds.Initialize(_ =>
            {
                IsInitialised = true;
                LoadRewarded();
                LoadInterstitial();
                Debug.Log("[Ads/admob] SDK initialised.");
            });
        }

        void LoadRewarded()
        {
            _rewarded?.Destroy();

            RewardedAd.Load(AndroidTestRewarded, new AdRequest(), (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogWarning("[Ads/admob] Rewarded load failed: " + error);
                    return;
                }

                _rewarded = ad;
                // Always queue the next one as soon as this one closes, or the second
                // rewarded offer of a session silently has no inventory.
                _rewarded.OnAdFullScreenContentClosed += LoadRewarded;
            });
        }

        void LoadInterstitial()
        {
            _interstitial?.Destroy();

            InterstitialAd.Load(AndroidTestInterstitial, new AdRequest(), (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogWarning("[Ads/admob] Interstitial load failed: " + error);
                    return;
                }

                _interstitial = ad;
                _interstitial.OnAdFullScreenContentClosed += LoadInterstitial;
            });
        }

        public void ShowRewarded(AdPlacement placement, Action<AdResult> onComplete)
        {
            if (!IsRewardedReady) { onComplete?.Invoke(AdResult.Unavailable); return; }

            bool earned = false;

            _rewarded.OnAdFullScreenContentClosed += () =>
                onComplete?.Invoke(earned ? AdResult.Rewarded : AdResult.Dismissed);

            _rewarded.OnAdFullScreenContentFailed += _ =>
                onComplete?.Invoke(AdResult.Unavailable);

            _rewarded.Show(_ => earned = true);
        }

        public void ShowInterstitial(AdPlacement placement, Action<AdResult> onClosed)
        {
            if (!IsInterstitialReady) { onClosed?.Invoke(AdResult.Unavailable); return; }

            _interstitial.OnAdFullScreenContentClosed += () => onClosed?.Invoke(AdResult.Dismissed);
            _interstitial.OnAdFullScreenContentFailed += _ => onClosed?.Invoke(AdResult.Unavailable);
            _interstitial.Show();
        }

        public void ShowBanner()
        {
            if (_adsRemoved) return;

            _banner ??= new BannerView(AndroidTestBanner, AdSize.Banner, AdPosition.Bottom);
            _banner.LoadAd(new AdRequest());
            _banner.Show();
        }

        public void HideBanner() => _banner?.Hide();

        public void SetAdsRemoved(bool removed)
        {
            _adsRemoved = removed;
            if (!removed) return;

            _banner?.Destroy();
            _banner = null;
            _interstitial?.Destroy();
            _interstitial = null;
        }
#else
        // --- Stub used until the SDK is installed --------------------------------------
        public bool IsInitialised => true;
        public bool IsRewardedReady => false;
        public bool IsInterstitialReady => false;

        public void Initialise() =>
            Debug.LogWarning("[Ads/admob] Google Mobile Ads SDK is not installed. Install the "
                             + "plugin and define MINIGTA_ADMOB, or set AdService.Provider to "
                             + "Mock for playtesting.");

        public void ShowRewarded(AdPlacement placement, Action<AdResult> onComplete)
            => onComplete?.Invoke(AdResult.Unavailable);

        public void ShowInterstitial(AdPlacement placement, Action<AdResult> onClosed)
            => onClosed?.Invoke(AdResult.Unavailable);

        public void ShowBanner() { }
        public void HideBanner() { }
        public void SetAdsRemoved(bool removed) => _adsRemoved = removed;
#endif
    }
}
