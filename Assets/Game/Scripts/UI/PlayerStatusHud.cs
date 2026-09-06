using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Health, armour, wanted stars, ammo, and the end-of-run banner.
    ///
    /// Polls rather than subscribing to every value: bars are read every frame anyway, and a
    /// HUD that rebuilds itself from current state cannot drift out of sync the way an
    /// event-driven one does when an event is missed during a respawn.
    /// </summary>
    public class PlayerStatusHud : MonoBehaviour
    {
        [Header("Vitals")]
        public Image HealthFill;
        public Image ArmorFill;

        [Header("Wanted")]
        [Tooltip("Star images, dimmed when unearned.")]
        public Image[] Stars = new Image[0];
        public Color StarEarned = new Color(1f, 0.82f, 0.25f);
        public Color StarEmpty = new Color(1f, 1f, 1f, 0.16f);
        [Tooltip("The newest star pulses while heat is still climbing.")]
        public float StarPulseSpeed = 4.5f;

        [Header("Weapon")]
        public CanvasGroup AmmoGroup;
        public Text AmmoLabel;
        [Tooltip("Optional. Shows the active weapon's own pictogram beside the count.")]
        public Image WeaponIcon;
        [Tooltip("Optional. Name of the active weapon, above the count.")]
        public Text WeaponName;
        [Tooltip("Optional. One pictogram per WeaponDefinition.Stance value "
                 + "(0 pistol, 1 alt pistol, 2 rifle, 3 assault rifle, 4 bazooka). "
                 + "Indexed by stance, clamped, so a shorter array degrades to the last entry.")]
        public Sprite[] WeaponIconSprites = new Sprite[0];

        [Header("End of run")]
        public CanvasGroup EndBanner;
        public Text EndTitle;
        public Text EndSubtitle;
        public Color BustedColor = new Color(0.42f, 0.66f, 1f);
        public Color WastedColor = new Color(0.95f, 0.32f, 0.28f);

        [Header("Colours")]
        public Color HealthGood = new Color(0.42f, 0.83f, 0.44f);
        public Color HealthLow = new Color(0.93f, 0.29f, 0.26f);
        public Color ArmorColor = new Color(0.55f, 0.72f, 0.95f);

        Health _health;
        PlayerCombat _combat;
        HeatSystem _heat;
        GameStateManager _game;

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                _health = player.GetComponent<Health>();
                _combat = player.GetComponent<PlayerCombat>();
            }

            _heat = HeatSystem.Instance;
            _game = GameStateManager.Instance;

            if (_game != null) _game.StateChanged += OnStateChanged;
            if (EndBanner != null) EndBanner.alpha = 0f;
            if (ArmorFill != null) ArmorFill.color = ArmorColor;
        }

        void OnDestroy()
        {
            if (_game != null) _game.StateChanged -= OnStateChanged;
        }

        void Update()
        {
            _heat ??= HeatSystem.Instance;
            _game ??= GameStateManager.Instance;

            UpdateVitals();
            UpdateWanted();
            UpdateAmmo();
        }

        void UpdateVitals()
        {
            if (_health == null) return;

            if (HealthFill != null)
            {
                float h = _health.HealthFraction;
                HealthFill.fillAmount = h;
                HealthFill.color = Color.Lerp(HealthLow, HealthGood, Mathf.SmoothStep(0f, 1f, h));
            }

            if (ArmorFill != null)
            {
                float a = _health.ArmorFraction;
                ArmorFill.fillAmount = a;
                // Hide the armour bar entirely at zero rather than showing an empty track.
                var c = ArmorColor;
                c.a = a > 0.001f ? 1f : 0f;
                ArmorFill.color = c;
            }
        }

        void UpdateWanted()
        {
            if (Stars.Length == 0) return;

            int level = _heat != null ? _heat.WantedLevel : 0;

            // Pulse the most recently earned star while heat is still being added, so the
            // player can tell the difference between "wanted" and "actively being seen".
            bool climbing = _heat != null && _heat.TimeSinceOffence < 1.5f;
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * StarPulseSpeed));

            for (int i = 0; i < Stars.Length; i++)
            {
                if (Stars[i] == null) continue;

                bool earned = i < level;
                Color c = earned ? StarEarned : StarEmpty;

                if (earned && climbing && i == level - 1) c.a *= pulse;
                Stars[i].color = c;
            }
        }

        /// <summary>
        /// Magazine / reserve for the weapon actually in hand.
        ///
        /// <b>This used to read <c>PlayerCombat.Ammo</c>, which is the pre-Section-2 field.</b>
        /// Once a WeaponController is present it owns the ammunition and PlayerCombat.Attack
        /// returns through the controller branch without ever touching <c>Ammo</c> -- so the
        /// readout sat frozen at its starting 34 for the whole session no matter how much was
        /// fired. <see cref="PlayerCombat.LoadedRounds"/> is the accessor that reads through
        /// the controller when there is one and falls back to the legacy field when there
        /// is not.
        /// </summary>
        void UpdateAmmo()
        {
            bool armed = _combat != null && _combat.IsArmed;

            if (AmmoGroup != null)
                AmmoGroup.alpha = Mathf.MoveTowards(AmmoGroup.alpha, armed ? 1f : 0f,
                                                    8f * Time.unscaledDeltaTime);

            if (!armed) return;

            var weapons = _combat.Weapons;

            if (AmmoLabel != null)
            {
                // Magazine and reserve, not one number: which of the two is short decides
                // whether the player reloads or goes looking for ammunition, and a single
                // total hides that. Reserve is dimmed by size, not colour, so it stays
                // readable on the light chip.
                AmmoLabel.text = weapons != null
                    ? weapons.Magazine + " / " + weapons.Reserve
                    : _combat.LoadedRounds.ToString();
            }

            if (WeaponName != null)
            {
                string id = weapons != null ? weapons.ActiveId : "pistol";
                if (_weaponNameCache != id)
                {
                    _weaponNameCache = id;
                    WeaponName.text = string.IsNullOrEmpty(id) ? "" : id.ToUpperInvariant();
                }
            }

            if (WeaponIcon != null && WeaponIconSprites != null && WeaponIconSprites.Length > 0)
            {
                var def = weapons != null ? weapons.ActiveDefinition : null;
                int stance = def != null ? Mathf.RoundToInt(def.Stance) : 0;
                var sprite = WeaponIconSprites[Mathf.Clamp(stance, 0, WeaponIconSprites.Length - 1)];
                if (sprite != null && WeaponIcon.sprite != sprite) WeaponIcon.sprite = sprite;
            }
        }

        string _weaponNameCache;

        void OnStateChanged(GameState state)
        {
            if (EndBanner == null) return;

            if (state == GameState.Playing)
            {
                EndBanner.alpha = 0f;
                return;
            }

            EndBanner.alpha = 1f;

            bool busted = state == GameState.Busted;
            if (EndTitle != null)
            {
                EndTitle.text = busted ? "BUSTED" : "WASTED";
                EndTitle.color = busted ? BustedColor : WastedColor;
            }
            if (EndSubtitle != null)
                EndSubtitle.text = busted
                    ? "Weapons confiscated. Released outside the station."
                    : "Patched up at the hospital.";
        }
    }
}
