using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A building the player can buy. Once owned it becomes a save point and a fast-travel
    /// destination.
    ///
    /// Registers itself in a static list so the fast-travel UI can enumerate destinations
    /// without searching the scene, and so a property bought in one place immediately shows up
    /// as a destination everywhere else.
    /// </summary>
    public class Property : MonoBehaviour
    {
        static readonly List<Property> Registry = new List<Property>();

        /// <summary>Every property in the world, owned or not.</summary>
        public static IReadOnlyList<Property> All => Registry;

        /// <summary>Only the ones the player has bought -- the valid fast-travel targets.</summary>
        public static IEnumerable<Property> Owned
        {
            get
            {
                foreach (var p in Registry)
                    if (p != null && p.IsOwned) yield return p;
            }
        }

        [Header("Identity")]
        public string PropertyId = "property.unnamed";
        public string DisplayName = "Safehouse";
        public int Price = 5000;

        [Header("Points")]
        [Tooltip("Where fast travel drops the player. Usually the front door.")]
        public Transform ArrivalPoint;

        [Header("Sale sign")]
        public Renderer SignRenderer;
        public Color ForSaleColour = new Color(0.95f, 0.55f, 0.2f);
        public Color OwnedColour = new Color(0.35f, 0.9f, 0.5f);

        [Header("Purchase")]
        public float InteractRadius = 4f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        Transform _player;
        MaterialPropertyBlock _mpb;
        bool _wasOwned;

        public bool IsOwned =>
            PlayerProgress.Instance != null && PlayerProgress.Instance.OwnsProperty(PropertyId);

        public string PurchasePrompt => "$" + Price.ToString("N0") + " to buy";

        public Vector3 ArrivalPosition =>
            ArrivalPoint != null ? ArrivalPoint.position : transform.position;

        void OnEnable() => Registry.Add(this);
        void OnDisable() => Registry.Remove(this);

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _player = player.transform;

            _wasOwned = IsOwned;
            TintSign();
        }

        void Update()
        {
            if (_player == null) return;

            if (IsOwned != _wasOwned)
            {
                _wasOwned = IsOwned;
                TintSign();
            }

            if (IsOwned) return;   // nothing left to interact with once bought

            var interiors = InteriorManager.Instance;
            if (interiors != null && interiors.IsInside) return;

            var driving = _player.GetComponent<PlayerVehicleController>();
            if (driving != null && driving.IsDriving) return;

            float distance = Vector3.Distance(transform.position, _player.position);
            if (distance > InteractRadius) return;

            var hud = MissionHud.Instance != null ? MissionHud.Instance.Hud : null;
            if (hud != null) hud.InteractTargetInRange = true;

            // The explicit "stand down if a doorway is nearby" check that used to be here is
            // gone: InputHub now arbitrates, and a doorway claims the Contact tier against
            // this sign's Item tier, so the door still wins at the entrance -- without this
            // class having to know that Doorway exists.
            var hub = InputHub.Instance;
            if (hub == null) return;

            hub.ClaimInteract(this, distance, InputHub.InteractPriorityItem);
            if (!hub.ConsumeInteract(this)) return;

            TryPurchase();
        }

        public bool TryPurchase()
        {
            var progress = PlayerProgress.Instance;
            if (progress == null) return false;

            if (IsOwned)
            {
                MissionHud.Instance?.ShowToast("You already own " + DisplayName);
                return false;
            }

            if (!progress.TrySpend(Price))
            {
                MissionHud.Instance?.ShowToast("Not enough money - " + PurchasePrompt);
                return false;
            }

            progress.GiveProperty(PropertyId);
            TintSign();

            MissionHud.Instance?.ShowToast(DisplayName + " purchased - fast travel unlocked");
            SaveSystem.Instance?.Save();
            return true;
        }

        void TintSign()
        {
            if (SignRenderer == null) return;

            Color colour = IsOwned ? OwnedColour : ForSaleColour;

            _mpb ??= new MaterialPropertyBlock();
            SignRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, colour);
            _mpb.SetColor(EmissionColorId, colour * 2.2f);
            SignRenderer.SetPropertyBlock(_mpb);
        }

        /// <summary>Move the player here. Refuses while wanted, so it is not an escape hatch.</summary>
        public static bool FastTravelTo(Property destination, out string message)
        {
            message = "";

            if (destination == null || !destination.IsOwned)
            {
                message = "You do not own that";
                return false;
            }

            var heat = HeatSystem.Instance;
            if (heat != null && heat.IsWanted)
            {
                message = "Not while the police are looking for you";
                return false;
            }

            var interiors = InteriorManager.Instance;
            if (interiors != null && interiors.IsInside) interiors.Exit();

            var player = GameObject.FindWithTag("Player");
            if (player == null) { message = "No player"; return false; }

            var driving = player.GetComponent<PlayerVehicleController>();
            if (driving != null && driving.IsDriving) driving.Exit();

            var controller = player.GetComponent<PlayerController>();
            if (controller != null) controller.Warp(destination.ArrivalPosition);
            else player.transform.position = destination.ArrivalPosition;

            Camera.main?.GetComponent<ThirdPersonCamera>()?.AlignBehindTarget();

            SaveSystem.Instance?.Save();
            message = "Travelled to " + destination.DisplayName;
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, InteractRadius);
            if (ArrivalPoint != null) Gizmos.DrawWireSphere(ArrivalPoint.position, 1f);
        }
    }
}
