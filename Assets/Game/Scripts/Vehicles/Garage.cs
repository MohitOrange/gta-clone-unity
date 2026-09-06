using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The player's owned vehicles and the bay they are worked on in.
    ///
    /// Stores vehicles as *data*, not as parked GameObjects. A garage full of live rigidbodies
    /// would cost physics every frame for cars nobody is looking at; instead the selected one
    /// is spawned into the bay on demand and despawned on the way out.
    /// </summary>
    public class Garage : MonoBehaviour
    {
        static Garage _instance;

        public static Garage Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<Garage>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Bay")]
        [Tooltip("Where the selected vehicle is displayed inside the garage interior.")]
        public Transform DisplayBay;
        [Tooltip("Where a vehicle is delivered when the player drives it out.")]
        public Transform DeliveryPoint;

        [Header("Prefabs")]
        [Tooltip("Every vehicle the garage can spawn. Matched by prefab name against the saved " +
                 "PrefabId, which avoids a Resources folder and keeps the reference checkable " +
                 "in the inspector.")]
        public GameObject[] VehiclePrefabs = new GameObject[0];

        [Header("Upgrade pricing")]
        public int MaxUpgradeLevel = 3;

        [Header("Starting garage")]
        [Tooltip("Prefab name the player already owns on a new game, so the upgrade bay is " +
                 "usable before they have bought anything. Empty for an empty garage.")]
        public string StartingVehicleId = "Car_0";
        public string StartingVehicleName = "Sedan";

        readonly List<OwnedVehicleData> _owned = new List<OwnedVehicleData>();

        GameObject _displayed;
        int _selected;

        public IReadOnlyList<OwnedVehicleData> Owned => _owned;
        public int SelectedIndex => _selected;
        public bool HasVehicles => _owned.Count > 0;

        public OwnedVehicleData Selected =>
            _owned.Count == 0 ? null : _owned[Mathf.Clamp(_selected, 0, _owned.Count - 1)];

        public event System.Action Changed;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            // A save load replaces this; it only matters on a brand new game.
            if (_owned.Count == 0 && !string.IsNullOrEmpty(StartingVehicleId))
                Add(StartingVehicleId, StartingVehicleName);
        }

        // ---------------------------------------------------------------- ownership

        /// <summary>Adds a vehicle to the garage. Returns false if it is already owned.</summary>
        public bool Add(string prefabId, string displayName)
        {
            if (string.IsNullOrEmpty(prefabId)) return false;

            foreach (var v in _owned)
                if (v.PrefabId == prefabId) return false;

            _owned.Add(new OwnedVehicleData
            {
                PrefabId = prefabId,
                DisplayName = string.IsNullOrEmpty(displayName) ? prefabId : displayName,
            });

            Changed?.Invoke();
            return true;
        }

        public void Select(int index)
        {
            if (_owned.Count == 0) return;

            _selected = Mathf.Clamp(index, 0, _owned.Count - 1);
            RefreshDisplay();
            Changed?.Invoke();
        }

        public void SelectNext() => Select((_selected + 1) % Mathf.Max(1, _owned.Count));

        // ---------------------------------------------------------------- upgrades

        /// <summary>Cost of the next step of an upgrade. Rises with each level fitted.</summary>
        public int UpgradeCost(int currentLevel, int basePrice) =>
            Mathf.RoundToInt(basePrice * (1f + currentLevel * 0.8f));

        public bool CanFit(ShopEffect effect)
        {
            var v = Selected;
            if (v == null) return false;

            return effect switch
            {
                ShopEffect.VehicleSpeed => v.SpeedLevel < MaxUpgradeLevel,
                ShopEffect.VehicleArmor => v.ArmorLevel < MaxUpgradeLevel,
                ShopEffect.VehiclePaint => true,
                _ => false,
            };
        }

        /// <summary>Fit an upgrade to the selected vehicle. Payment is the caller's job.</summary>
        public bool Fit(ShopEffect effect, Color colour)
        {
            var v = Selected;
            if (v == null || !CanFit(effect)) return false;

            switch (effect)
            {
                case ShopEffect.VehicleSpeed: v.SpeedLevel++; break;
                case ShopEffect.VehicleArmor: v.ArmorLevel++; break;
                case ShopEffect.VehiclePaint:
                    v.HasCustomColour = true;
                    v.ColourR = colour.r; v.ColourG = colour.g; v.ColourB = colour.b;
                    break;
                default: return false;
            }

            RefreshDisplay();
            Changed?.Invoke();
            return true;
        }

        // ----------------------------------------------------------------- display

        /// <summary>Show the selected vehicle in the bay. Called on entering the garage.</summary>
        public void RefreshDisplay()
        {
            ClearDisplay();

            var data = Selected;
            if (data == null || DisplayBay == null) return;

            var prefab = LoadPrefab(data.PrefabId);
            if (prefab == null) return;

            _displayed = Instantiate(prefab, DisplayBay.position, DisplayBay.rotation);
            _displayed.name = "GarageDisplay_" + data.PrefabId;

            // A display model must not drive off, get shot at, or be counted as traffic.
            StripForDisplay(_displayed);
            ApplyUpgradesTo(_displayed, data);
        }

        public void ClearDisplay()
        {
            if (_displayed != null) Destroy(_displayed);
            _displayed = null;
        }

        static void StripForDisplay(GameObject go)
        {
            var traffic = go.GetComponent<TrafficCar>();
            if (traffic != null) Destroy(traffic);

            var pursuit = go.GetComponent<PolicePursuit>();
            if (pursuit != null) Destroy(pursuit);

            var body = go.GetComponent<Rigidbody>();
            // Kinematic so it sits perfectly still on the plinth instead of settling and rolling.
            if (body != null) body.isKinematic = true;
        }

        static void ApplyUpgradesTo(GameObject go, OwnedVehicleData data)
        {
            var upgrades = go.GetComponent<VehicleUpgrades>() ?? go.AddComponent<VehicleUpgrades>();
            upgrades.ReadFrom(data);
        }

        /// <summary>
        /// Puts the selected vehicle on the street outside, fully upgraded, and returns it.
        /// </summary>
        public GameObject DeliverSelected()
        {
            var data = Selected;
            if (data == null || DeliveryPoint == null) return null;

            var prefab = LoadPrefab(data.PrefabId);
            if (prefab == null) return null;

            var go = Instantiate(prefab, DeliveryPoint.position, DeliveryPoint.rotation);
            go.name = "Garage_" + data.PrefabId;

            var traffic = go.GetComponent<TrafficCar>();
            if (traffic != null) Destroy(traffic);

            ApplyUpgradesTo(go, data);
            return go;
        }

        GameObject LoadPrefab(string prefabId)
        {
            if (string.IsNullOrEmpty(prefabId)) return null;

            foreach (var prefab in VehiclePrefabs)
                if (prefab != null && prefab.name == prefabId) return prefab;

            Debug.LogWarning("[Garage] No prefab named '" + prefabId
                             + "' in VehiclePrefabs. A saved vehicle cannot be spawned.");
            return null;
        }

        // -------------------------------------------------------------- save/load

        public OwnedVehicleData[] Capture() => _owned.ToArray();

        public void Restore(OwnedVehicleData[] data)
        {
            _owned.Clear();
            if (data != null) _owned.AddRange(data);

            _selected = 0;
            Changed?.Invoke();
        }
    }
}
