using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A business with stock. Owns the rules for what can be bought and what buying it does.
    ///
    /// Every purchase funnels through <see cref="TryBuy"/>, so the "can I afford it / do I
    /// already own it / does it apply to anything" checks and the money deduction live in one
    /// place. The UI only renders what this reports and calls this to act -- it never touches
    /// the wallet itself.
    /// </summary>
    public class Shop : MonoBehaviour
    {
        [Header("Identity")]
        public string ShopId = "shop.unnamed";
        public string DisplayName = "Shop";
        [TextArea(1, 2)] public string Greeting = "Take a look.";

        [Header("Stock")]
        public List<ShopItem> Stock = new List<ShopItem>();

        /// <summary>Why a given item cannot be bought right now, or empty if it can.</summary>
        public string BlockedReason(ShopItem item)
        {
            if (item == null) return "No stock";

            var progress = PlayerProgress.Instance;
            if (progress == null) return "Unavailable";

            if (progress.Level < item.RequiredLevel) return "Level " + item.RequiredLevel;

            if (!item.IsRepeatable && progress.Owns(item.Id)) return "Owned";

            if (item.Category == ShopCategory.Property)
                return progress.OwnsProperty(item.Id) ? "Owned" : AffordCheck(item, progress);

            if (item.Category == ShopCategory.VehicleUpgrade)
            {
                var garage = Garage.Instance;
                if (garage == null || !garage.HasVehicles) return "No vehicle";
                if (!garage.CanFit(item.Effect)) return "Maxed";
            }

            // Health and armour are wasted at full, so refuse rather than take the money.
            if (item.Effect == ShopEffect.RestoreHealth || item.Effect == ShopEffect.GiveArmor)
            {
                var health = FindPlayerHealth();
                if (health != null)
                {
                    bool full = item.Effect == ShopEffect.RestoreHealth
                        ? health.CurrentHealth >= health.MaxHealth
                        : health.CurrentArmor >= health.MaxArmor;
                    if (full) return "Full";
                }
            }

            return AffordCheck(item, progress);
        }

        static string AffordCheck(ShopItem item, PlayerProgress progress) =>
            progress.Money < PriceOf(item) ? "Too expensive" : "";

        public bool CanBuy(ShopItem item) => string.IsNullOrEmpty(BlockedReason(item));

        /// <summary>Price including any escalation for repeat upgrades.</summary>
        public static int PriceOf(ShopItem item)
        {
            if (item == null) return 0;
            if (item.Category != ShopCategory.VehicleUpgrade) return item.Price;

            var garage = Garage.Instance;
            var vehicle = garage != null ? garage.Selected : null;
            if (vehicle == null) return item.Price;

            int level = item.Effect switch
            {
                ShopEffect.VehicleSpeed => vehicle.SpeedLevel,
                ShopEffect.VehicleArmor => vehicle.ArmorLevel,
                _ => 0,
            };

            return garage.UpgradeCost(level, item.Price);
        }

        /// <summary>Attempt a purchase. Returns a message describing what happened.</summary>
        public bool TryBuy(ShopItem item, out string message)
        {
            string blocked = BlockedReason(item);
            if (!string.IsNullOrEmpty(blocked))
            {
                message = blocked;
                return false;
            }

            var progress = PlayerProgress.Instance;
            int price = PriceOf(item);

            if (!progress.TrySpend(price))
            {
                message = "Too expensive";
                return false;
            }

            ApplyEffect(item, out message);

            // Non-consumables are recorded so they show as owned and survive a save.
            if (!item.IsRepeatable) progress.GiveItem(item.Id);

            SaveSystem.Instance?.Save();
            return true;
        }

        void ApplyEffect(ShopItem item, out string message)
        {
            message = item.DisplayName + " purchased";

            var player = GameObject.FindWithTag("Player");
            var health = player != null ? player.GetComponent<Health>() : null;
            var combat = player != null ? player.GetComponent<PlayerCombat>() : null;

            switch (item.Effect)
            {
                case ShopEffect.RestoreHealth:
                    health?.Heal(item.Amount);
                    message = "Patched up";
                    break;

                case ShopEffect.GiveArmor:
                    health?.AddArmor(item.Amount);
                    message = "Armour fitted";
                    break;

                case ShopEffect.GiveAmmo:
                    if (combat != null)
                        combat.Ammo = Mathf.Min(combat.MaxAmmo, combat.Ammo + Mathf.RoundToInt(item.Amount));
                    message = Mathf.RoundToInt(item.Amount) + " rounds";
                    break;

                case ShopEffect.GivePistol:
                    combat?.GivePistol(Mathf.RoundToInt(item.Amount));
                    message = "Pistol acquired";
                    break;

                case ShopEffect.ExtendAmmoCapacity:
                    if (combat != null) combat.MaxAmmo += Mathf.RoundToInt(item.Amount);
                    message = "Ammo capacity increased";
                    break;

                case ShopEffect.VehicleSpeed:
                case ShopEffect.VehicleArmor:
                case ShopEffect.VehiclePaint:
                    Garage.Instance?.Fit(item.Effect, item.Colour);
                    message = item.DisplayName + " fitted";
                    break;

                case ShopEffect.PlayerSkin:
                    PlayerProgress.Instance?.SetSkin(item.Id);
                    PlayerSkinSwapper.Instance?.Apply(item.Id, item.Colour);
                    message = "Now wearing " + item.DisplayName;
                    break;

                case ShopEffect.BuyProperty:
                    PlayerProgress.Instance?.GiveProperty(item.Id);
                    message = item.DisplayName + " is yours";
                    break;
            }
        }

        static Health FindPlayerHealth()
        {
            var player = GameObject.FindWithTag("Player");
            return player != null ? player.GetComponent<Health>() : null;
        }

        /// <summary>Stock the player is allowed to see, filtered by level.</summary>
        public IEnumerable<ShopItem> VisibleStock()
        {
            var progress = PlayerProgress.Instance;
            int level = progress != null ? progress.Level : 1;

            foreach (var item in Stock)
            {
                if (item == null) continue;
                // Show one level ahead so the player can see what is coming.
                if (item.RequiredLevel > level + 1) continue;
                yield return item;
            }
        }
    }
}
