using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Decides when to offer a rewarded ad, and pays out when one is watched.
    ///
    /// All three offers live here rather than inside the systems they affect, so the mission
    /// manager knows nothing about advertising and the ad service knows nothing about missions.
    /// The payout always happens in the callback, never optimistically before the ad finishes.
    ///
    /// Interstitials are hooked here too, at the two natural breakpoints the brief asked for:
    /// after a mission ends and on the game-over screen. Both go through
    /// <see cref="AdService.TryShowInterstitial"/>, which enforces the frequency cap.
    /// </summary>
    public class AdRewards : MonoBehaviour
    {
        [Header("Double cash")]
        [Tooltip("Multiplier applied to the mission reward when the ad is watched.")]
        public int CashMultiplier = 2;
        [Tooltip("Seconds the offer stays on screen before it lapses.")]
        public float OfferTimeout = 8f;

        [Header("Revive")]
        [Range(0.1f, 1f)] public float ReviveHealthFraction = 0.75f;

        [Header("Clear wanted")]
        [Tooltip("Minimum stars before the clear-wanted offer is worth showing.")]
        public int MinStarsForClearOffer = 2;
        [Tooltip("Seconds between clear-wanted offers, so it does not nag.")]
        public float ClearWantedCooldown = 60f;

        MissionManager _missions;
        GameStateManager _game;
        HeatSystem _heat;

        int _pendingCashReward;
        float _lastClearOffer = -999f;

        void Start()
        {
            _missions = MissionManager.Instance;
            _game = GameStateManager.Instance;
            _heat = HeatSystem.Instance;

            if (_missions != null) _missions.MissionEnded += OnMissionEnded;
            if (_game != null) _game.StateChanged += OnGameStateChanged;
        }

        void OnDestroy()
        {
            if (_missions != null) _missions.MissionEnded -= OnMissionEnded;
            if (_game != null) _game.StateChanged -= OnGameStateChanged;
        }

        // ------------------------------------------------------------- double cash

        void OnMissionEnded(MissionBase mission, bool success, string headline, string detail)
        {
            if (!success)
            {
                // A failed mission still counts as a breakpoint for an interstitial.
                AdService.Instance?.TryShowInterstitial(AdPlacement.MissionComplete);
                return;
            }

            _pendingCashReward = mission.RewardMoney;

            var prompt = AdOfferPrompt.Instance;
            var ads = AdService.Instance;

            if (prompt == null || ads == null || !ads.Provider_.IsRewardedReady)
            {
                AdService.Instance?.TryShowInterstitial(AdPlacement.MissionComplete);
                return;
            }

            prompt.Offer(
                "Double your payout?",
                "Watch a short video to earn $" + (_pendingCashReward * (CashMultiplier - 1)) + " extra",
                "WATCH", "NO THANKS", OfferTimeout,
                onAccept: () => ads.ShowRewarded(AdPlacement.DoubleCash,
                    onReward: GrantDoubleCash,
                    onFailed: () => MissionHud.Instance?.ShowToast("No video available")),
                // Declining is the moment to show the interstitial instead: the player has
                // already been asked once, so this is not two ads back to back.
                onDecline: () => ads.TryShowInterstitial(AdPlacement.MissionComplete));
        }

        void GrantDoubleCash()
        {
            int bonus = _pendingCashReward * (CashMultiplier - 1);
            _pendingCashReward = 0;

            if (bonus <= 0) return;

            PlayerProgress.Instance?.AddMoney(bonus);
            MissionHud.Instance?.ShowToast("Bonus paid: $" + bonus.ToString("N0"));
            SaveSystem.Instance?.Save();
        }

        // ----------------------------------------------------------------- revive

        void OnGameStateChanged(GameState state)
        {
            if (state == GameState.Playing) return;

            var prompt = AdOfferPrompt.Instance;
            var ads = AdService.Instance;

            // Busted is not revivable -- being arrested is not a death you shake off.
            bool canRevive = state == GameState.Wasted;

            if (!canRevive || prompt == null || ads == null || !ads.Provider_.IsRewardedReady)
            {
                ads?.TryShowInterstitial(AdPlacement.GameOver);
                return;
            }

            // Hold the respawn open long enough for the player to read the offer.
            _game.PauseRespawnCountdown(OfferTimeout + 2f);

            prompt.Offer(
                "Get back up?",
                "Watch a short video to revive where you fell",
                "REVIVE", "GIVE UP", OfferTimeout,
                onAccept: () => ads.ShowRewarded(AdPlacement.Revive,
                    onReward: () =>
                    {
                        if (_game.ReviveInPlace(ReviveHealthFraction))
                            MissionHud.Instance?.ShowToast("Back on your feet");
                    },
                    onFailed: () => MissionHud.Instance?.ShowToast("No video available")),
                onDecline: () => ads.TryShowInterstitial(AdPlacement.GameOver));
        }

        // ----------------------------------------------------------- clear wanted

        /// <summary>
        /// Offer to wipe the wanted level. Called from the HUD button, so the player asks for
        /// it rather than being interrupted mid-chase.
        /// </summary>
        public void OfferClearWanted()
        {
            _heat ??= HeatSystem.Instance;
            var ads = AdService.Instance;
            var prompt = AdOfferPrompt.Instance;

            if (_heat == null || ads == null || prompt == null) return;

            if (_heat.WantedLevel < MinStarsForClearOffer)
            {
                MissionHud.Instance?.ShowToast("Not wanted enough for that");
                return;
            }

            if (Time.unscaledTime - _lastClearOffer < ClearWantedCooldown)
            {
                MissionHud.Instance?.ShowToast("Try again shortly");
                return;
            }

            if (!ads.Provider_.IsRewardedReady)
            {
                MissionHud.Instance?.ShowToast("No video available");
                return;
            }

            _lastClearOffer = Time.unscaledTime;

            prompt.Offer(
                "Lose the heat?",
                "Watch a short video to clear your wanted level",
                "WATCH", "CANCEL", OfferTimeout,
                onAccept: () => ads.ShowRewarded(AdPlacement.ClearWanted,
                    onReward: () =>
                    {
                        HeatSystem.Instance?.Clear();
                        PoliceDispatcher.Instance?.StandDown();
                        MissionHud.Instance?.ShowToast("You lost them");
                    },
                    onFailed: () => MissionHud.Instance?.ShowToast("No video available")),
                onDecline: null);
        }

        /// <summary>True when the clear-wanted button should be on screen.</summary>
        public bool ClearWantedAvailable
        {
            get
            {
                _heat ??= HeatSystem.Instance;
                return _heat != null && _heat.WantedLevel >= MinStarsForClearOffer;
            }
        }
    }
}
