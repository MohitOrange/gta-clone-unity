using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// The shop / garage / fast-travel panel.
    ///
    /// Rows are built at runtime from a single hidden template rather than authored one by one,
    /// so a shop's inventory can change without touching the UI, and the same panel serves a
    /// weapons counter, a mechanic's bay and a travel desk.
    ///
    /// The panel is modal: it blocks raycasts so a thumb on a buy button cannot also drive the
    /// joystick underneath it, and it releases any held input on open so the player does not
    /// keep sprinting while browsing.
    /// </summary>
    public class ShopUI : MonoBehaviour
    {
        static ShopUI _instance;

        public static ShopUI Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<ShopUI>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Panel")]
        public CanvasGroup Panel;
        public Text TitleLabel;
        public Text SubtitleLabel;
        public Text MoneyLabel;

        [Header("Rows")]
        [Tooltip("Hidden template cloned once per line of stock.")]
        public RectTransform RowTemplate;
        public RectTransform RowContainer;
        [Tooltip("Vertical spacing between rows, in canvas units.")]
        public float RowHeight = 74f;

        [Header("Buttons")]
        public Button CloseButton;

        [Header("Garage")]
        public Button CycleVehicleButton;
        public Text VehicleLabel;

        [Header("Colours")]
        public Color Affordable = new Color(0.55f, 0.92f, 0.55f);
        public Color Blocked = new Color(0.95f, 0.45f, 0.4f);
        public Color Owned = new Color(0.6f, 0.65f, 0.72f);

        readonly List<RectTransform> _rows = new List<RectTransform>();

        Shop _shop;
        bool _fastTravelMode;

        public bool IsOpen { get; private set; }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            if (RowTemplate != null) RowTemplate.gameObject.SetActive(false);
            if (CloseButton != null) CloseButton.onClick.AddListener(Close);
            if (CycleVehicleButton != null) CycleVehicleButton.onClick.AddListener(CycleVehicle);

            SetVisible(false);
        }

        void Update()
        {
            if (!IsOpen) return;

            var progress = PlayerProgress.Instance;
            if (MoneyLabel != null && progress != null)
                MoneyLabel.text = "$" + progress.Money.ToString("N0");

            // Walking away closes the counter, so the player is never trapped in a panel.
            var interiors = InteriorManager.Instance;
            if (interiors != null && !interiors.IsInside && !_fastTravelMode) Close();
        }

        // -------------------------------------------------------------------- open

        public void Open(Shop shop)
        {
            if (shop == null) return;

            _shop = shop;
            _fastTravelMode = false;

            if (TitleLabel != null) TitleLabel.text = shop.DisplayName.ToUpperInvariant();
            if (SubtitleLabel != null) SubtitleLabel.text = shop.Greeting;

            bool garage = InteriorManager.Instance != null
                          && InteriorManager.Instance.Current != null
                          && InteriorManager.Instance.Current.IsGarage;

            if (CycleVehicleButton != null) CycleVehicleButton.gameObject.SetActive(garage);
            if (VehicleLabel != null) VehicleLabel.gameObject.SetActive(garage);

            SetVisible(true);
            Rebuild();
        }

        public void OpenFastTravel()
        {
            _shop = null;
            _fastTravelMode = true;

            if (TitleLabel != null) TitleLabel.text = "FAST TRAVEL";
            if (SubtitleLabel != null) SubtitleLabel.text = "Where to?";

            if (CycleVehicleButton != null) CycleVehicleButton.gameObject.SetActive(false);
            if (VehicleLabel != null) VehicleLabel.gameObject.SetActive(false);

            SetVisible(true);
            Rebuild();
        }

        public void Close()
        {
            _shop = null;
            _fastTravelMode = false;
            SetVisible(false);
        }

        void SetVisible(bool visible)
        {
            IsOpen = visible;

            if (Panel != null)
            {
                Panel.alpha = visible ? 1f : 0f;
                Panel.blocksRaycasts = visible;
                Panel.interactable = visible;
            }

            if (!visible) return;

            // Drop anything the thumb was holding, or the player browses while sprinting.
            var hub = InputHub.Instance;
            if (hub != null)
            {
                hub.TouchMove = Vector2.zero;
                hub.TouchSprintHeld = false;
                hub.TouchGasHeld = false;
                hub.TouchBrakeHeld = false;
            }
        }

        // ----------------------------------------------------------------- content

        void Rebuild()
        {
            ClearRows();

            if (_fastTravelMode) BuildFastTravelRows();
            else BuildShopRows();

            UpdateVehicleLabel();
        }

        void BuildShopRows()
        {
            if (_shop == null) return;

            int index = 0;
            foreach (var item in _shop.VisibleStock())
            {
                int price = Shop.PriceOf(item);
                string blocked = _shop.BlockedReason(item);
                bool canBuy = string.IsNullOrEmpty(blocked);

                var captured = item;
                AddRow(index++, item.DisplayName, item.Description,
                       "$" + price.ToString("N0"),
                       canBuy ? "" : blocked,
                       canBuy,
                       () => Buy(captured));
            }

            if (index == 0) AddRow(0, "Nothing in stock", "", "", "", false, null);
        }

        void BuildFastTravelRows()
        {
            int index = 0;
            foreach (var property in Property.Owned)
            {
                var captured = property;
                AddRow(index++, property.DisplayName, "Owned property", "", "", true,
                       () => Travel(captured));
            }

            if (index == 0)
                AddRow(0, "No properties owned", "Buy a safehouse to unlock fast travel",
                       "", "", false, null);
        }

        void AddRow(int index, string name, string description, string price,
                    string blockedReason, bool enabled, System.Action onClick)
        {
            if (RowTemplate == null || RowContainer == null) return;

            var row = Instantiate(RowTemplate, RowContainer);
            row.gameObject.SetActive(true);
            row.anchoredPosition = new Vector2(0f, -index * RowHeight);
            _rows.Add(row);

            SetChildText(row, "Name", name);
            SetChildText(row, "Description", description);
            SetChildText(row, "Price", price);

            var status = FindChildText(row, "Status");
            if (status != null)
            {
                status.text = enabled ? "BUY" : blockedReason;
                status.color = enabled ? Affordable
                             : blockedReason == "Owned" ? Owned : Blocked;
            }

            var button = row.GetComponent<Button>();
            if (button == null) return;

            button.interactable = enabled;
            button.onClick.RemoveAllListeners();
            if (enabled && onClick != null) button.onClick.AddListener(() => onClick());
        }

        static void SetChildText(RectTransform row, string childName, string value)
        {
            var text = FindChildText(row, childName);
            if (text != null) text.text = value;
        }

        static Text FindChildText(RectTransform row, string childName)
        {
            var child = row.Find(childName);
            return child != null ? child.GetComponent<Text>() : null;
        }

        void ClearRows()
        {
            foreach (var row in _rows)
                if (row != null) Destroy(row.gameObject);
            _rows.Clear();
        }

        // ------------------------------------------------------------------ actions

        void Buy(ShopItem item)
        {
            if (_shop == null) return;

            _shop.TryBuy(item, out string message);
            MissionHud.Instance?.ShowToast(message);

            // Prices and availability shift after every purchase, so redraw the whole list.
            Rebuild();
        }

        void Travel(Property destination)
        {
            Close();

            Property.FastTravelTo(destination, out string message);
            MissionHud.Instance?.ShowToast(message);
        }

        void CycleVehicle()
        {
            Garage.Instance?.SelectNext();
            Rebuild();
        }

        void UpdateVehicleLabel()
        {
            if (VehicleLabel == null) return;

            var garage = Garage.Instance;
            var vehicle = garage != null ? garage.Selected : null;

            VehicleLabel.text = vehicle == null
                ? "No vehicle in the bay"
                : vehicle.DisplayName + "   -   Engine " + vehicle.SpeedLevel
                  + " / Armour " + vehicle.ArmorLevel
                  + (vehicle.HasCustomColour ? " / Custom paint" : "");
        }
    }
}
