using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The outfits that exist in the game, read at runtime off the shops that sell them.
    ///
    /// <b>Deliberately not a second list.</b> Every outfit is already authored once, as a
    /// <see cref="ShopItem"/> on the Threads shop (see InteriorBuilder), carrying its id, its
    /// display name, its price, its level gate and -- crucially -- the colour
    /// <see cref="PlayerSkinSwapper"/> tints the character with. The lobby's character select
    /// needs exactly that data, and copying it into a second table is how the shop and the
    /// wardrobe end up disagreeing about what an outfit costs.
    ///
    /// It also closes a real gap. <c>PlayerProgress.ActiveSkin</c> is saved and restored, but
    /// the id alone is not enough to repaint anybody -- the colour lived only on the shop item,
    /// so a skin bought in one session came back as the stock model in the next. With a lookup
    /// from id to item, restoring a save can re-apply the outfit.
    /// </summary>
    public static class SkinLibrary
    {
        static readonly List<ShopItem> _skins = new List<ShopItem>();
        static bool _built;

        /// <summary>
        /// Statics outlive a Play mode session when domain reloading is off, and the cached
        /// items are scene objects that will not exist in the next one.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => Invalidate();

        /// <summary>Every outfit for sale anywhere, cheapest first.</summary>
        public static IReadOnlyList<ShopItem> Skins
        {
            get { Build(); return _skins; }
        }

        public static ShopItem Find(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return null;

            Build();
            for (int i = 0; i < _skins.Count; i++)
                if (_skins[i].Id == skinId) return _skins[i];

            return null;
        }

        /// <summary>The tint an outfit id paints the character with.</summary>
        public static bool TryGetColour(string skinId, out Color colour)
        {
            var item = Find(skinId);
            colour = item != null ? item.Colour : Color.white;
            return item != null;
        }

        /// <summary>Forces the next read to re-scan. Call if shops are built or rebuilt.</summary>
        public static void Invalidate()
        {
            _built = false;
            _skins.Clear();
        }

        static void Build()
        {
            if (_built) return;
            _built = true;
            _skins.Clear();

            // Include inactive: the shops live inside interiors parked off the map, and an
            // interior that has never been entered may have been switched off.
            var shops = Object.FindObjectsByType<Shop>(FindObjectsInactive.Include);

            foreach (var shop in shops)
            {
                if (shop == null || shop.Stock == null) continue;

                foreach (var item in shop.Stock)
                {
                    if (item == null || item.Category != ShopCategory.Skin) continue;
                    if (string.IsNullOrEmpty(item.Id)) continue;
                    if (Contains(item.Id)) continue;

                    _skins.Add(item);
                }
            }

            // Price order, so the wardrobe reads as a progression rather than as whatever
            // order FindObjectsByType happened to return the shops in.
            _skins.Sort((a, b) => a.Price.CompareTo(b.Price));
        }

        static bool Contains(string id)
        {
            for (int i = 0; i < _skins.Count; i++)
                if (_skins[i].Id == id) return true;
            return false;
        }
    }
}
