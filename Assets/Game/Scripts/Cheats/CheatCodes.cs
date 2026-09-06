using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The code table. Item 9 asks for fifty or more; this declares 56.
    ///
    /// <b>Every code does something real.</b> None of these is a stub -- CLAUDE.md §3 forbids
    /// placeholder logic in finished code, and a cheat that silently does nothing is worse
    /// than a missing one, because it looks like the system is broken rather than the code
    /// being absent. Where a cheat has nothing to act on (no player yet, no vehicle, no
    /// weapon) it returns false and the caller reports <see cref="CheatResult.Failed"/>,
    /// which is a different and honest answer.
    ///
    /// Naming is uppercase and underscore-separated so the codes are typeable and greppable.
    /// </summary>
    static class CheatCodes
    {
        // ---------------------------------------------------------------- lookup helpers
        //
        // Every cheat starts by finding something to act on, and any of them can legitimately
        // fail because the thing does not exist yet. These return null rather than throwing so
        // the failure is a false, not an exception.

        static GameObject Player => GameObject.FindWithTag("Player");

        static T OnPlayer<T>() where T : Component
        {
            var p = Player;
            return p == null ? null : p.GetComponentInChildren<T>();
        }

        static CrowdDirector Crowd => Object.FindAnyObjectByType<CrowdDirector>();
        static DayNightCycle Sky => Object.FindAnyObjectByType<DayNightCycle>();

        internal static void RegisterAll()
        {
            // ------------------------------------------------------------------ player
            Add("HEAL", CheatCategory.Player, "Restore health to full", () =>
            {
                var h = OnPlayer<Health>();
                if (h == null) return false;
                h.Heal(h.MaxHealth);
                return true;
            });

            Add("ARMOR", CheatCategory.Player, "Full armour", () =>
            {
                var h = OnPlayer<Health>();
                if (h == null) return false;
                h.AddArmor(h.MaxArmor);
                return true;
            });

            Add("GOD_ON", CheatCategory.Player, "Invulnerable", () => SetGod(true));
            Add("GOD_OFF", CheatCategory.Player, "Mortal again", () => SetGod(false));

            Add("KILL_ME", CheatCategory.Player, "Kill the player", () =>
            {
                var h = OnPlayer<Health>();
                if (h == null || h.IsDead) return false;
                h.Apply(new DamageInfo(h.MaxHealth + h.MaxArmor + 1f,
                                       h.transform.position, Vector3.forward,
                                       DamageSource.Unknown, null));
                return true;
            });

            Add("REVIVE", CheatCategory.Player, "Revive at full health", () =>
            {
                var h = OnPlayer<Health>();
                if (h == null || !h.IsDead) return false;
                h.Revive();
                return true;
            });

            Add("TANK", CheatCategory.Player, "Ten times maximum health", () =>
            {
                var h = OnPlayer<Health>();
                if (h == null) return false;
                h.MaxHealth = 1000f;
                h.Heal(h.MaxHealth);
                return true;
            });

            Add("GLASS", CheatCategory.Player, "One hit point", () =>
            {
                var h = OnPlayer<Health>();
                if (h == null) return false;
                h.MaxHealth = 1f;
                h.Heal(1f);
                return true;
            });

            Add("SPEED_UP", CheatCategory.Player, "Double running speed", () => Scale(2f));
            Add("SPEED_DOWN", CheatCategory.Player, "Halve running speed", () => Scale(0.5f));
            Add("SPEED_RESET", CheatCategory.Player, "Default movement speeds", () =>
            {
                var pc = OnPlayer<PlayerController>();
                if (pc == null) return false;
                pc.WalkSpeed = 2.2f; pc.RunSpeed = 5.8f; pc.SwimSpeed = 2.4f;
                pc.JumpHeight = 1.25f;
                return true;
            });

            Add("MOON_JUMP", CheatCategory.Player, "Very high jump", () =>
            {
                var pc = OnPlayer<PlayerController>();
                if (pc == null) return false;
                pc.JumpHeight = 6f;
                return true;
            });

            // ------------------------------------------------------------------ weapons
            Add("GUN_PISTOL", CheatCategory.Weapons, "Give the pistol", () => Give("pistol", 96));
            Add("GUN_RIFLE", CheatCategory.Weapons, "Give the rifle", () => Give("rifle", 180));
            Add("GUN_SHOTGUN", CheatCategory.Weapons, "Give the shotgun", () => Give("shotgun", 64));
            Add("GUN_SNIPER", CheatCategory.Weapons, "Give the sniper", () => Give("sniper", 40));
            Add("MELEE_KNIFE", CheatCategory.Weapons, "Give the knife", () => Give("knife", 0));
            Add("MELEE_BAT", CheatCategory.Weapons, "Give the bat", () => Give("bat", 0));
            Add("MELEE_AXE", CheatCategory.Weapons, "Give the axe", () => Give("axe", 0));

            Add("AMMO", CheatCategory.Weapons, "Refill reserve ammunition", () =>
            {
                var w = OnPlayer<WeaponController>();
                if (w == null || !w.HasWeapon) return false;
                w.RefillReserve();
                return true;
            });

            Add("ARSENAL", CheatCategory.Weapons, "Every weapon in the library", () =>
            {
                var w = OnPlayer<WeaponController>();
                if (w == null) return false;
                foreach (var id in new[] { "pistol", "rifle", "shotgun", "sniper",
                                           "knife", "bat", "axe" })
                    w.GiveWeapon(id, 200);
                return true;
            });

            Add("DISARM", CheatCategory.Weapons, "Lock the weapon out", () => Lock(true));
            Add("REARM", CheatCategory.Weapons, "Unlock the weapon", () => Lock(false));

            Add("SWAP", CheatCategory.Weapons, "Cycle to the next weapon", () =>
            {
                var w = OnPlayer<WeaponController>();
                return w != null && w.TryCycle();
            });

            // ------------------------------------------------------------------ money
            Add("CASH_1K", CheatCategory.Money, "Add 1,000", () => Money(1000));
            Add("CASH_10K", CheatCategory.Money, "Add 10,000", () => Money(10000));
            Add("CASH_100K", CheatCategory.Money, "Add 100,000", () => Money(100000));
            Add("BROKE", CheatCategory.Money, "Take all money", () =>
            {
                var p = PlayerProgress.Instance;
                if (p == null) return false;
                p.TrySpend(p.Money);
                return true;
            });

            Add("XP_500", CheatCategory.Money, "Add 500 XP", () => Xp(500));
            Add("XP_5K", CheatCategory.Money, "Add 5,000 XP", () => Xp(5000));

            // ------------------------------------------------------------------ police
            Add("WANTED_UP", CheatCategory.Police, "Raise the wanted level", () =>
            {
                var h = HeatSystem.Instance;
                if (h == null) return false;
                h.AddHeat(60f, "cheat");
                return true;
            });

            Add("WANTED_MAX", CheatCategory.Police, "Maximum wanted level", () =>
            {
                var h = HeatSystem.Instance;
                if (h == null) return false;
                // Enough to cross the top threshold from any starting point.
                for (int i = 0; i < h.MaxWantedLevel + 1; i++) h.AddHeat(120f, "cheat");
                return true;
            });

            Add("WANTED_CLEAR", CheatCategory.Police, "Clear the wanted level", () =>
            {
                var h = HeatSystem.Instance;
                if (h == null) return false;
                h.Clear();
                return true;
            });

            Add("HEAT_FREEZE", CheatCategory.Police, "Stop heat decaying", () => Decay(0f));
            Add("HEAT_MELT", CheatCategory.Police, "Very fast heat decay", () => Decay(500f));
            Add("HEAT_NORMAL", CheatCategory.Police, "Default heat decay", () => Decay(3.5f));

            // ------------------------------------------------------------------ world
            Add("TIME_DAWN", CheatCategory.World, "Set time to dawn", () => Clock(0.25f));
            Add("TIME_NOON", CheatCategory.World, "Set time to noon", () => Clock(0.5f));
            Add("TIME_DUSK", CheatCategory.World, "Set time to dusk", () => Clock(0.75f));
            Add("TIME_NIGHT", CheatCategory.World, "Set time to midnight", () => Clock(0f));
            Add("TIME_STOP", CheatCategory.World, "Freeze the day cycle", () => Advance(false));
            Add("TIME_GO", CheatCategory.World, "Resume the day cycle", () => Advance(true));

            Add("RAIN_ON", CheatCategory.World, "Force rain", () => Rain(true));
            Add("RAIN_OFF", CheatCategory.World, "Stop rain", () => Rain(false));

            Add("GRAVITY_LOW", CheatCategory.World, "Moon gravity", () => Gravity(-3f));
            Add("GRAVITY_HIGH", CheatCategory.World, "Triple gravity", () => Gravity(-58.8f));
            Add("GRAVITY_NORMAL", CheatCategory.World, "Earth gravity", () => Gravity(-9.81f));

            Add("SLOWMO", CheatCategory.World, "Quarter speed", () => TimeScale(0.25f));
            Add("FASTMO", CheatCategory.World, "Double speed", () => TimeScale(2f));
            Add("TIMESCALE_RESET", CheatCategory.World, "Normal speed", () => TimeScale(1f));

            Add("TIDE_UP", CheatCategory.World, "Raise the sea by 5 m", () => Tide(5f));
            Add("TIDE_DOWN", CheatCategory.World, "Lower the sea by 5 m", () => Tide(-5f));
            Add("STORM_SEA", CheatCategory.World, "Big waves", () =>
            {
                var w = WaterVolume.Instance;
                if (w == null) return false;
                w.WaveAmplitude = 2.2f; w.WaveSpeed = 1.8f;
                return true;
            });
            Add("CALM_SEA", CheatCategory.World, "Flat water", () =>
            {
                var w = WaterVolume.Instance;
                if (w == null) return false;
                w.WaveAmplitude = 0.05f; w.WaveSpeed = 0.3f;
                return true;
            });

            // ------------------------------------------------------------------ vehicles
            Add("REPAIR", CheatCategory.Vehicles, "Repair the current vehicle", () =>
            {
                var v = CurrentVehicle();
                if (v == null) return false;
                var health = v.GetComponent<Health>();
                if (health != null) health.Heal(health.MaxHealth);
                return true;
            });

            Add("FLIP", CheatCategory.Vehicles, "Upright the current vehicle", () =>
            {
                var v = CurrentVehicle();
                if (v == null) return false;
                var t = v.transform;
                t.rotation = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
                t.position += Vector3.up * 1.2f;
                var rb = v.GetComponent<Rigidbody>();
                if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                return true;
            });

            Add("BOOST", CheatCategory.Vehicles, "Shove the vehicle forward", () =>
            {
                var v = CurrentVehicle();
                var rb = v == null ? null : v.GetComponent<Rigidbody>();
                if (rb == null) return false;
                rb.AddForce(v.transform.forward * 40f, ForceMode.VelocityChange);
                return true;
            });

            // ------------------------------------------------------------------ crowd
            Add("NPC_MAX", CheatCategory.Crowd, "Stress-test crowd (1000)", () =>
            {
                var c = Crowd;
                return c != null && c.TrySetPopulation(CrowdDirector.StressTestPopulation);
            });

            Add("NPC_NORMAL", CheatCategory.Crowd, "Shipping crowd (300)", () =>
            {
                var c = Crowd;
                return c != null && c.TrySetPopulation(CrowdDirector.ShippingMaxPopulation);
            });

            Add("NPC_NONE", CheatCategory.Crowd, "Stop growing the crowd", () =>
            {
                var c = Crowd;
                return c != null && c.TrySetPopulation(0);
            });

            Add("PANIC", CheatCategory.Crowd, "Everyone nearby flees", () =>
            {
                var p = Player;
                if (p == null) return false;
                return Pedestrian.AlarmNear(p.transform.position, 60f, p.transform.position) > 0;
            });

            // ------------------------------------------------------------------ debug
            Add("TIER_LOW", CheatCategory.Debug, "Low quality tier", () => Tier(QualityTier.Low));
            Add("TIER_MED", CheatCategory.Debug, "Medium quality tier", () => Tier(QualityTier.Medium));
            Add("TIER_HIGH", CheatCategory.Debug, "High quality tier", () => Tier(QualityTier.High));

            Add("FPS_UNCAP", CheatCategory.Debug, "Remove the frame cap", () =>
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                return true;
            });

            Add("CROWD_STATS", CheatCategory.Debug, "Log crowd LOD bands", () =>
            {
                var c = Crowd;
                if (c == null) return false;
                Debug.Log("[Cheat] population=" + c.Population
                          + " full=" + c.FullCount + " simple=" + c.SimpleCount
                          + " frozen=" + c.FrozenCount);
                return true;
            });

            Add("CHEAT_LIST", CheatCategory.Debug, "Log every cheat code", () =>
            {
                foreach (var c in CheatRegistry.All)
                    Debug.Log("[Cheat] " + c.Category + "  " + c.Code + " -- " + c.Description);
                return true;
            });
        }

        // ---------------------------------------------------------------- implementations

        static void Add(string code, CheatCategory category, string description,
                        System.Func<bool> apply)
            => CheatRegistry.Register(new Cheat(code, category, description, apply));

        static bool SetGod(bool on)
        {
            var h = OnPlayer<Health>();
            if (h == null) return false;
            h.Invulnerable = on;
            return true;
        }

        static bool Scale(float factor)
        {
            var pc = OnPlayer<PlayerController>();
            if (pc == null) return false;
            pc.WalkSpeed *= factor;
            pc.RunSpeed *= factor;
            pc.SwimSpeed *= factor;
            return true;
        }

        static bool Give(string id, int rounds)
        {
            var w = OnPlayer<WeaponController>();
            if (w == null) return false;
            w.GiveWeapon(id, rounds);
            return true;
        }

        static bool Lock(bool locked)
        {
            var w = OnPlayer<WeaponController>();
            if (w == null) return false;
            w.SetLocked(locked);
            return true;
        }

        static bool Money(int amount)
        {
            var p = PlayerProgress.Instance;
            if (p == null) return false;
            p.AddMoney(amount);
            return true;
        }

        static bool Xp(int amount)
        {
            var p = PlayerProgress.Instance;
            if (p == null) return false;
            p.AddXp(amount);
            return true;
        }

        static bool Decay(float perSecond)
        {
            var h = HeatSystem.Instance;
            if (h == null) return false;
            h.DecayPerSecond = perSecond;
            return true;
        }

        static bool Clock(float t)
        {
            var s = Sky;
            if (s == null) return false;
            s.TimeOfDay = Mathf.Repeat(t, 1f);
            return true;
        }

        static bool Advance(bool on)
        {
            var s = Sky;
            if (s == null) return false;
            s.Advance = on;
            return true;
        }

        static bool Rain(bool on)
        {
            var s = Sky;
            if (s == null) return false;
            s.EnableRain = on;
            return true;
        }

        static bool Gravity(float y)
        {
            Physics.gravity = new Vector3(0f, y, 0f);
            return true;
        }

        static bool TimeScale(float scale)
        {
            // Deliberately refuses to zero the timescale. MenuState owns that, and a cheat
            // that sets it to 0 leaves the game frozen with no menu open and no way back --
            // the exact trap that produced two false negatives during BUG-012.
            Time.timeScale = Mathf.Clamp(scale, 0.05f, 8f);
            return true;
        }

        static bool Tide(float delta)
        {
            var w = WaterVolume.Instance;
            if (w == null) return false;
            w.SeaLevel += delta;
            return true;
        }

        static bool Tier(QualityTier tier)
        {
            var t = PerformanceTuner.Instance;
            if (t == null) return false;
            t.Apply(tier);
            return true;
        }

        static GameObject CurrentVehicle()
        {
            var pvc = OnPlayer<PlayerVehicleController>();
            if (pvc == null || !pvc.IsDriving) return null;

            // The player is parented into the car while driving, so the vehicle is the nearest
            // ancestor that actually carries a Vehicle component.
            var v = pvc.GetComponentInParent<Vehicle>();
            return v != null ? v.gameObject : null;
        }
    }
}
