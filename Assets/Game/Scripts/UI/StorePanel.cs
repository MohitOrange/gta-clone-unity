using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// The Remove Ads storefront: buy, restore, and a development revoke.
    ///
    /// Kept separate from the shop panel because it spends real money rather than in-game
    /// currency, and the two must never look interchangeable to the player.
    /// </summary>
    public class StorePanel : MonoBehaviour
    {
        static StorePanel _instance;

        public static StorePanel Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<StorePanel>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Panel")]
        public CanvasGroup Panel;
        public Text StatusLabel;

        [Header("Buttons")]
        [Tooltip("The HUD store button that opens this panel. Wired at runtime because an "
                 + "editor-time onClick.AddListener is not serialised into the scene.")]
        public Button OpenButton;
        public Button RemoveAdsButton;
        public Text RemoveAdsLabel;
        public Button RestoreButton;
        public Button RevokeButton;
        public Button CloseButton;

        [Header("Colours")]
        public Color Available = new Color(0.55f, 0.92f, 0.55f);
        public Color Owned = new Color(0.6f, 0.65f, 0.72f);
        public Color Busy = new Color(0.95f, 0.82f, 0.4f);

        public bool IsOpen { get; private set; }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            if (OpenButton != null) OpenButton.onClick.AddListener(Toggle);
            if (RemoveAdsButton != null) RemoveAdsButton.onClick.AddListener(BuyRemoveAds);
            if (RestoreButton != null) RestoreButton.onClick.AddListener(Restore);
            if (RevokeButton != null) RevokeButton.onClick.AddListener(Revoke);
            if (CloseButton != null) CloseButton.onClick.AddListener(Close);

            SetVisible(false);
        }

        void Update()
        {
            if (IsOpen) Refresh();
        }

        public void Open()
        {
            SetVisible(true);
            Refresh();

            // Banners are menu-only, so this is exactly where one belongs.
            AdService.Instance?.ShowBanner();
        }

        public void Close()
        {
            SetVisible(false);
            AdService.Instance?.HideBanner();
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        void Refresh()
        {
            var iap = IapService.Instance;
            if (iap == null) return;

            bool owned = iap.RemoveAdsOwned;
            bool pending = iap.IsPurchasePending;

            if (RemoveAdsLabel != null)
            {
                RemoveAdsLabel.text = pending ? "PURCHASING..."
                                   : owned ? "PURCHASED"
                                   : "REMOVE ADS   " + iap.RemoveAdsPrice;
                RemoveAdsLabel.color = pending ? Busy : owned ? Owned : Available;
            }

            if (RemoveAdsButton != null) RemoveAdsButton.interactable = !owned && !pending;
            if (RevokeButton != null) RevokeButton.gameObject.SetActive(owned && Application.isEditor);

            if (StatusLabel == null) return;

            StatusLabel.text = owned
                ? "Interstitials and banners are off. Rewarded videos still available."
                : "Removes interstitials and banners. Rewarded videos are unaffected.";
        }

        void BuyRemoveAds()
        {
            var iap = IapService.Instance;
            if (iap == null) return;

            iap.Purchase(IapService.RemoveAdsProductId, resultCode =>
            {
                switch (resultCode)
                {
                    case PurchaseResult.Success:
                        MissionHud.Instance?.ShowToast("Ads removed - thank you");
                        AdService.Instance?.HideBanner();
                        break;
                    case PurchaseResult.Unavailable:
                        MissionHud.Instance?.ShowToast("Store not available in this build");
                        break;
                    case PurchaseResult.Cancelled:
                        MissionHud.Instance?.ShowToast("Purchase cancelled");
                        break;
                    default:
                        MissionHud.Instance?.ShowToast("Purchase failed");
                        break;
                }

                Refresh();
            });
        }

        void Restore()
        {
            IapService.Instance?.Restore(resultCode =>
            {
                MissionHud.Instance?.ShowToast(resultCode == PurchaseResult.Success
                    ? "Purchases restored"
                    : "Nothing to restore");
                Refresh();
            });
        }

        void Revoke()
        {
            IapService.Instance?.RevokeForTesting();
            MissionHud.Instance?.ShowToast("Entitlement revoked (testing)");
            Refresh();
        }

        void SetVisible(bool visible)
        {
            IsOpen = visible;

            if (Panel == null) return;
            Panel.alpha = visible ? 1f : 0f;
            Panel.blocksRaycasts = visible;
            Panel.interactable = visible;
        }
    }
}
