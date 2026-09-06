using UnityEngine;

namespace MiniGTA
{
    public enum ShopCategory
    {
        /// <summary>Restores something immediately and is consumed. Repeatable.</summary>
        Consumable,
        /// <summary>A weapon or permanent capability. Bought once.</summary>
        Equipment,
        /// <summary>Changes the player's appearance. Bought once, then selectable.</summary>
        Skin,
        /// <summary>Applies to the vehicle currently in the garage bay.</summary>
        VehicleUpgrade,
        /// <summary>A building the player can own.</summary>
        Property,
    }

    public enum ShopEffect
    {
        None,
        RestoreHealth,
        GiveArmor,
        GiveAmmo,
        GivePistol,
        ExtendAmmoCapacity,
        VehicleSpeed,
        VehicleArmor,
        VehiclePaint,
        PlayerSkin,
        BuyProperty,
    }

    /// <summary>
    /// One line of stock.
    ///
    /// A plain serialisable class rather than a ScriptableObject so shop inventories are
    /// authored in the same builder script that places the shops, and so the whole economy is
    /// reviewable as code instead of as a folder of assets you have to click through.
    /// </summary>
    [System.Serializable]
    public class ShopItem
    {
        [Tooltip("Stable id, written into the save when the item is owned.")]
        public string Id = "item.unnamed";
        public string DisplayName = "Item";
        [TextArea(1, 3)] public string Description = "";

        public ShopCategory Category = ShopCategory.Consumable;
        public ShopEffect Effect = ShopEffect.None;

        public int Price = 100;

        [Tooltip("How much the effect gives: hit points, rounds, upgrade step, and so on.")]
        public float Amount = 1f;

        [Tooltip("Paint colour, for VehiclePaint items.")]
        public Color Colour = Color.white;

        [Tooltip("Minimum player level before this appears in stock.")]
        public int RequiredLevel = 1;

        /// <summary>True for things you can buy repeatedly (ammo, health).</summary>
        public bool IsRepeatable => Category == ShopCategory.Consumable
                                    || Category == ShopCategory.VehicleUpgrade;
    }
}
