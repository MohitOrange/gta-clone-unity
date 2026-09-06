using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>Things a level can unlock. Ids are stable strings so saves survive reordering.</summary>
    public static class Unlocks
    {
        public const string Bike = "vehicle.bike";
        public const string Boat = "vehicle.boat";
        public const string ExtendedAmmo = "weapon.extended_ammo";
        public const string NorthDistrict = "area.north";
        public const string BodyArmor = "gear.armor";
    }

    /// <summary>
    /// Money, experience, level and unlocks.
    ///
    /// Deliberately the only thing that persists between sessions. Everything else -- where
    /// the traffic is, which pedestrians are alive, the wanted level -- is disposable world
    /// state that should be rebuilt fresh, so a save file stays small and never encodes a
    /// broken world.
    /// </summary>
    public class PlayerProgress : MonoBehaviour
    {
        static PlayerProgress _instance;

        public static PlayerProgress Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<PlayerProgress>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Starting state")]
        public int StartingMoney = 250;

        [Header("Curve")]
        [Tooltip("Base XP step. Total to reach level N is Base*(N-1) + Quadratic*(N-1)^2.")]
        public int XpCurveBase = 120;
        public int XpCurveQuadratic = 40;
        public int MaxLevel = 10;

        [Header("Unlocks by level")]
        [Tooltip("What becomes available on reaching each level. Index 0 is level 1.")]
        public string[] UnlockPerLevel =
        {
            "",                        // level 1: cars only
            Unlocks.Bike,              // level 2
            Unlocks.Boat,              // level 3
            Unlocks.ExtendedAmmo,      // level 4
            Unlocks.BodyArmor,         // level 5
            Unlocks.NorthDistrict,     // level 6
        };

        readonly HashSet<string> _unlocked = new HashSet<string>();
        readonly HashSet<string> _completedMissions = new HashSet<string>();

        /// <summary>Things bought from a shop: weapons, skins, capability upgrades.</summary>
        readonly HashSet<string> _ownedItems = new HashSet<string>();

        /// <summary>Properties the player has purchased, by id.</summary>
        readonly HashSet<string> _ownedProperties = new HashSet<string>();

        public int Money { get; private set; }
        public int Xp { get; private set; }
        public int Level { get; private set; } = 1;

        public IReadOnlyCollection<string> Unlocked => _unlocked;
        public IReadOnlyCollection<string> CompletedMissions => _completedMissions;
        public IReadOnlyCollection<string> OwnedItems => _ownedItems;
        public IReadOnlyCollection<string> OwnedProperties => _ownedProperties;

        /// <summary>Skin id the player is currently wearing, or empty for the default.</summary>
        public string ActiveSkin { get; private set; } = "";

        /// <summary>Shown when the player has never named themselves.</summary>
        public const string DefaultProfileName = "Rookie";

        /// <summary>
        /// Long enough for a real handle, short enough to fit the lobby's name chip at every
        /// aspect ratio without the label overrunning the art behind it.
        /// </summary>
        public const int MaxProfileNameLength = 16;

        /// <summary>
        /// The player's chosen name, shown in the lobby and stored in the save file (v4).
        ///
        /// Deliberately not cleared by <see cref="ResetProgress"/>: it identifies the person
        /// holding the phone, not the run they are on, so starting a new game does not make
        /// them type it again.
        /// </summary>
        public string ProfileName { get; private set; } = DefaultProfileName;

        public event System.Action<string> ProfileNameChanged;

        /// <summary>
        /// Set by the Remove Ads purchase. Suppresses interstitials and banners; rewarded ads
        /// stay available because they are opt-in and pay the player.
        /// </summary>
        public bool AdsRemoved { get; private set; }

        public event System.Action<bool> AdsRemovedChanged;

        public event System.Action<string> ItemPurchased;
        public event System.Action<string> SkinChanged;

        public event System.Action<int> MoneyChanged;
        public event System.Action<int, int> XpChanged;      // xp, xpForNextLevel
        public event System.Action<int, string> LeveledUp;   // level, unlock id ("" if none)

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
            Money = StartingMoney;
            ApplyUnlocksForLevel();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        // ------------------------------------------------------------------- curve

        /// <summary>Total accumulated XP required to be at the given level.</summary>
        public int XpRequiredFor(int level)
        {
            int n = Mathf.Max(0, level - 1);
            return XpCurveBase * n + XpCurveQuadratic * n * n;
        }

        /// <summary>XP still needed for the next level, or 0 at max level.</summary>
        public int XpToNextLevel =>
            Level >= MaxLevel ? 0 : Mathf.Max(0, XpRequiredFor(Level + 1) - Xp);

        /// <summary>Progress through the current level, 0..1.</summary>
        public float LevelProgress
        {
            get
            {
                if (Level >= MaxLevel) return 1f;
                int floor = XpRequiredFor(Level);
                int ceiling = XpRequiredFor(Level + 1);
                if (ceiling <= floor) return 1f;
                return Mathf.Clamp01((Xp - floor) / (float)(ceiling - floor));
            }
        }

        // ------------------------------------------------------------------ awards

        public void AddMoney(int amount)
        {
            if (amount == 0) return;
            Money = Mathf.Max(0, Money + amount);
            MoneyChanged?.Invoke(Money);
        }

        public bool TrySpend(int amount)
        {
            if (amount <= 0 || Money < amount) return false;
            AddMoney(-amount);
            return true;
        }

        public void AddXp(int amount)
        {
            if (amount <= 0) return;
            Xp += amount;

            // Loop rather than single-step: a big reward can carry more than one level.
            while (Level < MaxLevel && Xp >= XpRequiredFor(Level + 1))
            {
                Level++;
                string unlock = ApplyUnlocksForLevel();
                LeveledUp?.Invoke(Level, unlock);
            }

            XpChanged?.Invoke(Xp, XpToNextLevel);
        }

        /// <summary>Grants everything up to the current level. Returns this level's unlock id.</summary>
        string ApplyUnlocksForLevel()
        {
            string newest = "";

            for (int i = 0; i < Mathf.Min(Level, UnlockPerLevel.Length); i++)
            {
                string id = UnlockPerLevel[i];
                if (string.IsNullOrEmpty(id)) continue;

                _unlocked.Add(id);
                if (i == Level - 1) newest = id;
            }

            return newest;
        }

        public bool IsUnlocked(string id) => string.IsNullOrEmpty(id) || _unlocked.Contains(id);

        // ---------------------------------------------------------------- missions

        public bool HasCompleted(string missionId) =>
            !string.IsNullOrEmpty(missionId) && _completedMissions.Contains(missionId);

        public void MarkCompleted(string missionId)
        {
            if (!string.IsNullOrEmpty(missionId)) _completedMissions.Add(missionId);
        }

        // ------------------------------------------------------------- ownership

        public bool Owns(string itemId) =>
            !string.IsNullOrEmpty(itemId) && _ownedItems.Contains(itemId);

        public void GiveItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !_ownedItems.Add(itemId)) return;
            ItemPurchased?.Invoke(itemId);
        }

        public bool OwnsProperty(string propertyId) =>
            !string.IsNullOrEmpty(propertyId) && _ownedProperties.Contains(propertyId);

        public void GiveProperty(string propertyId)
        {
            if (string.IsNullOrEmpty(propertyId)) return;
            _ownedProperties.Add(propertyId);
        }

        /// <summary>Grant or revoke the Remove Ads entitlement.</summary>
        public void SetAdsRemoved(bool removed)
        {
            if (AdsRemoved == removed) return;
            AdsRemoved = removed;

            AdsRemovedChanged?.Invoke(removed);
            AdService.Instance?.OnAdsRemovedChanged(removed);
        }

        public void SetSkin(string skinId)
        {
            if (ActiveSkin == skinId) return;
            ActiveSkin = skinId ?? "";
            SkinChanged?.Invoke(ActiveSkin);
        }

        /// <summary>Rename the profile. Rejects nothing -- it cleans instead, so the UI never
        /// has to decide what a bad name is.</summary>
        public void SetProfileName(string name)
        {
            string clean = CleanProfileName(name);
            if (ProfileName == clean) return;

            ProfileName = clean;
            ProfileNameChanged?.Invoke(ProfileName);
        }

        /// <summary>
        /// Trims, caps the length, and turns nothing at all into the default name.
        ///
        /// Static because loading a v3 save has to run the same rule on an absent field as the
        /// rename field runs on an empty box.
        /// </summary>
        public static string CleanProfileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return DefaultProfileName;

            string trimmed = name.Trim();
            return trimmed.Length <= MaxProfileNameLength
                ? trimmed
                : trimmed.Substring(0, MaxProfileNameLength);
        }

        // -------------------------------------------------------------- save/load

        public SaveData CaptureSave()
        {
            var data = new SaveData
            {
                Version = SaveData.CurrentVersion,
                Money = Money,
                Xp = Xp,
                Level = Level,
            };

            data.Unlocked = new List<string>(_unlocked).ToArray();
            data.CompletedMissions = new List<string>(_completedMissions).ToArray();
            data.OwnedItems = new List<string>(_ownedItems).ToArray();
            data.OwnedProperties = new List<string>(_ownedProperties).ToArray();
            data.ActiveSkin = ActiveSkin;
            data.ProfileName = ProfileName;
            data.AdsRemoved = AdsRemoved;
            return data;
        }

        public void RestoreFrom(SaveData data)
        {
            if (data == null) return;

            Money = data.Money;
            Xp = data.Xp;
            Level = Mathf.Clamp(data.Level, 1, MaxLevel);

            _unlocked.Clear();
            if (data.Unlocked != null)
                foreach (var id in data.Unlocked) _unlocked.Add(id);

            _completedMissions.Clear();
            if (data.CompletedMissions != null)
                foreach (var id in data.CompletedMissions) _completedMissions.Add(id);

            _ownedItems.Clear();
            if (data.OwnedItems != null)
                foreach (var id in data.OwnedItems) _ownedItems.Add(id);

            _ownedProperties.Clear();
            if (data.OwnedProperties != null)
                foreach (var id in data.OwnedProperties) _ownedProperties.Add(id);

            ActiveSkin = data.ActiveSkin ?? "";
            SkinChanged?.Invoke(ActiveSkin);

            // A v3 save has no ProfileName at all. CleanProfileName turns the empty string
            // into the default, so an old file gets a name instead of a blank chip.
            ProfileName = CleanProfileName(data.ProfileName);
            ProfileNameChanged?.Invoke(ProfileName);

            AdsRemoved = data.AdsRemoved;
            AdsRemovedChanged?.Invoke(AdsRemoved);
            AdService.Instance?.OnAdsRemovedChanged(AdsRemoved);

            // Re-derive level unlocks in case the table changed since the save was written.
            ApplyUnlocksForLevel();

            MoneyChanged?.Invoke(Money);
            XpChanged?.Invoke(Xp, XpToNextLevel);
        }

        /// <summary>Wipe back to a new game. Used by the save system's reset.</summary>
        public void ResetProgress()
        {
            Money = StartingMoney;
            Xp = 0;
            Level = 1;
            _unlocked.Clear();
            _completedMissions.Clear();
            _ownedItems.Clear();
            _ownedProperties.Clear();
            ActiveSkin = "";
            AdsRemoved = false;
            ApplyUnlocksForLevel();

            MoneyChanged?.Invoke(Money);
            XpChanged?.Invoke(Xp, XpToNextLevel);
        }
    }
}
