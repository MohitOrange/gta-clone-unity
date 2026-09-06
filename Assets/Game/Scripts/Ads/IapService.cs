using System;
using UnityEngine;

namespace MiniGTA
{
    public enum PurchaseResult { Success, Cancelled, Failed, Unavailable }

    /// <summary>
    /// In-app purchase hook. **Deliberately not a real store integration.**
    ///
    /// Real billing cannot be built here: it needs a Google Play Console entry, a signed
    /// upload, a configured product id, and server-side receipt validation -- none of which
    /// exist inside this project. Shipping a fake that *looks* like it charges money would be
    /// worse than an obvious stub, so this one announces itself loudly and, in a real build,
    /// refuses rather than granting anything.
    ///
    /// The shape is what matters: <see cref="Purchase"/> and <see cref="Restore"/> are the two
    /// calls any store implementation needs, and the UI is already wired to them. Swapping in
    /// Unity IAP or Google Play Billing means replacing the body of this class only.
    /// </summary>
    public class IapService : MonoBehaviour
    {
        public const string RemoveAdsProductId = "com.minigta.city.removeads";

        static IapService _instance;

        public static IapService Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<IapService>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Catalogue")]
        [Tooltip("Display price only. The real price comes from the store at runtime.")]
        public string RemoveAdsPrice = "$2.99";

        [Header("Development")]
        [Tooltip("Grant purchases without a store, for testing the entitlement flow. " +
                 "MUST be false in any build that reaches a real user.")]
        public bool SimulatePurchasesInEditor = true;

        [Tooltip("Seconds the fake store dialog takes, so the UI's pending state is testable.")]
        public float SimulatedDelay = 1.2f;

        public bool IsPurchasePending { get; private set; }

        public bool RemoveAdsOwned =>
            PlayerProgress.Instance != null && PlayerProgress.Instance.AdsRemoved;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        /// <summary>Begin a purchase. The callback always fires exactly once.</summary>
        public void Purchase(string productId, Action<PurchaseResult> onComplete)
        {
            if (IsPurchasePending)
            {
                onComplete?.Invoke(PurchaseResult.Failed);
                return;
            }

            if (RemoveAdsOwned && productId == RemoveAdsProductId)
            {
                onComplete?.Invoke(PurchaseResult.Success);
                return;
            }

            bool canSimulate = SimulatePurchasesInEditor && Application.isEditor;

            if (!canSimulate)
            {
                Debug.LogWarning("[IAP] No billing backend is wired up. Set up Google Play "
                                 + "Billing (or Unity IAP) and replace IapService.Purchase. "
                                 + "Refusing rather than granting '" + productId + "'.");
                onComplete?.Invoke(PurchaseResult.Unavailable);
                return;
            }

            StartCoroutine(SimulatePurchase(productId, onComplete));
        }

        System.Collections.IEnumerator SimulatePurchase(string productId,
                                                       Action<PurchaseResult> onComplete)
        {
            IsPurchasePending = true;
            Debug.Log("[IAP] SIMULATED purchase of '" + productId + "' - editor only, no money moved.");

            yield return new WaitForSecondsRealtime(SimulatedDelay);

            IsPurchasePending = false;
            Grant(productId);
            onComplete?.Invoke(PurchaseResult.Success);
        }

        /// <summary>
        /// Re-apply entitlements the player already owns. A real implementation queries the
        /// store; this one reads the save, which is why a reinstall would lose it.
        /// </summary>
        public void Restore(Action<PurchaseResult> onComplete)
        {
            bool owned = RemoveAdsOwned;

            Debug.Log("[IAP] Restore requested. Locally recorded entitlement: " + owned
                      + ". A real build must query the store here, not the save file.");

            onComplete?.Invoke(owned ? PurchaseResult.Success : PurchaseResult.Unavailable);
        }

        void Grant(string productId)
        {
            if (productId != RemoveAdsProductId) return;

            PlayerProgress.Instance?.SetAdsRemoved(true);
            SaveSystem.Instance?.Save();
        }

        /// <summary>Development helper: hand the entitlement back so the flow can be retested.</summary>
        public void RevokeForTesting()
        {
            PlayerProgress.Instance?.SetAdsRemoved(false);
            SaveSystem.Instance?.Save();
        }
    }
}
