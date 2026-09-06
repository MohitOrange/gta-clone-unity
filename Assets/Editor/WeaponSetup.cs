using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Builds the shared <see cref="WeaponLibrary"/> asset, and works out where a hand socket
    /// has to sit on a given rig.
    ///
    /// The fits are computed from the prefabs' own measured bounds rather than typed in, so a
    /// weapon that is modelled at a different size lands at the right size anyway: each entry
    /// declares the real-world length it should end up at and the scale falls out of that.
    ///
    /// The socket orientation is derived, not guessed. See <see cref="ComputeHandSocket"/>.
    /// </summary>
    public static class WeaponSetup
    {
        const string LibraryPath = "Assets/Game/Data/WeaponLibrary.asset";
        const string PackDir = "Assets/ithappy/Weapons_FREE/Prefabs";

        /// <summary>
        /// What each weapon should measure along its long axis once in a hand, and which of the
        /// model's own axes that long axis is.
        ///
        /// The pack does not use one convention: firearms are modelled down +Z, melee weapons
        /// down +Y, everything at roughly 2.5x life size. Recording the axis per weapon is the
        /// whole reason the socket can stay generic.
        /// </summary>
        struct Fit
        {
            public string Id;
            public string Prefab;
            public float TargetLength;
            public Axis Long;
            public bool HasMuzzle;
            public Vector3 GripNudge;

            // Section 12: the numbers that make one weapon different from another.
            public string Family;
            public float Stance;
            public int Magazine;
            public int Reserve;
            public float Damage;
            public float Range;
            public float Spread;
            public float Cooldown;
            public float Reload;
            public float Equip;

            public Fit(string id, string prefab, float target, Axis axis, bool muzzle,
                       Vector3 nudge = default)
            {
                Id = id; Prefab = prefab; TargetLength = target; Long = axis;
                HasMuzzle = muzzle; GripNudge = nudge;
                Family = "Gun"; Stance = 0f;
                Magazine = 12; Reserve = 96; Damage = 34f; Range = 90f;
                Spread = 1.4f; Cooldown = 0.3f; Reload = 1.6f; Equip = 0.45f;
            }

            /// <summary>Firearm stats. Returns a copy so the table stays a list of literals.</summary>
            public Fit Gun(float stance, int mag, int reserve, float damage, float range,
                           float spread, float cooldown, float reload, float equip)
            {
                Family = "Gun"; Stance = stance;
                Magazine = mag; Reserve = reserve; Damage = damage; Range = range;
                Spread = spread; Cooldown = cooldown; Reload = reload; Equip = equip;
                return this;
            }

            /// <summary>
            /// Melee stats. No magazine, no reserve, no reload -- a knife with twelve rounds
            /// and a 1.6-second reload is what the placeholder data actually said.
            /// </summary>
            public Fit Melee(float damage, float reach, float swing, float equip)
            {
                Family = "Melee"; Stance = 0f;
                Magazine = 0; Reserve = 0; Damage = damage; Range = reach;
                Spread = 0f; Cooldown = swing; Reload = 0f; Equip = equip;
                return this;
            }
        }

        enum Axis { Z, Y }

        /// <summary>
        /// The roster, and Section 12's real numbers.
        ///
        /// <b>All seven used to be identical</b> -- magazine 12, reserve 96, damage 34, range 90,
        /// reload 1.6 s, family "Gun" -- so the knife, the bat and the axe each carried a
        /// twelve-round magazine and a reload time, and a sniper rifle hit exactly as hard as a
        /// pistol at exactly the same range. The structure was right and the data was never
        /// authored.
        ///
        /// Shaped so each weapon is worth choosing:
        ///   pistol   the baseline. Middling everything, largest reserve.
        ///   rifle    reach and rate, less damage per shot than a shotgun up close.
        ///   shotgun  heavy but wide and short -- the spread is what makes it a room weapon,
        ///            since there is no damage falloff system to express range any other way.
        ///   sniper   four rounds, hits for over half a health bar, slow to fire and slower to
        ///            reload, almost no spread.
        ///   melee    no ammunition at all; the reach and swing timing separate them.
        /// </summary>
        static readonly Fit[] Fits =
        {
            //                                                                      stance mag res  dmg   rng   spread cool  reload equip
            new Fit("pistol",  "pistol_001",       0.21f, Axis.Z, true,  new Vector3(0f, -0.01f, 0.01f))
                .Gun(0f,  12, 96,  26f,  60f, 1.6f, 0.28f, 1.40f, 0.40f),
            new Fit("rifle",   "rifle_001",        0.90f, Axis.Z, true)
                .Gun(3f,  30, 150, 22f, 110f, 1.1f, 0.11f, 2.10f, 0.65f),
            new Fit("shotgun", "shotgun_001",      0.95f, Axis.Z, true)
                .Gun(2f,   6,  36, 62f,  28f, 6.5f, 0.85f, 2.60f, 0.70f),
            new Fit("sniper",  "sniper_rifle_001", 1.15f, Axis.Z, true)
                .Gun(2f,   4,  24, 95f, 300f, 0.15f, 1.50f, 3.00f, 0.90f),

            // Melee. Reach in metres, swing as the cooldown between blows.
            new Fit("knife",   "knife_001",        0.28f, Axis.Y, false).Melee(28f, 1.9f, 0.42f, 0.30f),
            new Fit("bat",     "baseball_bat_001", 0.85f, Axis.Y, false).Melee(38f, 2.4f, 0.70f, 0.45f),
            new Fit("axe",     "axe_001",          0.80f, Axis.Y, false).Melee(52f, 2.2f, 0.95f, 0.50f),
        };

        [MenuItem("Tools/Mini GTA/16. Build Weapon Library", priority = 136)]
        public static WeaponLibrary Build()
        {
            Directory.CreateDirectory("Assets/Game/Data");

            var lib = AssetDatabase.LoadAssetAtPath<WeaponLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<WeaponLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            var built = new List<WeaponDefinition>();
            var report = new List<string>();

            foreach (var fit in Fits)
            {
                string path = PackDir + "/" + fit.Prefab + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning("[Weapons] Missing " + path); continue; }

                Bounds local = LocalBounds(prefab);
                float modelled = fit.Long == Axis.Z ? local.size.z : local.size.y;
                if (modelled < 0.0001f) { Debug.LogWarning("[Weapons] Zero-size " + fit.Prefab); continue; }

                float scale = fit.TargetLength / modelled;

                // Rotate the model's long axis onto the socket's forward. The socket already
                // points where the character aims, so nothing else has to know about aiming.
                Vector3 euler = fit.Long == Axis.Y ? new Vector3(90f, 0f, 0f) : Vector3.zero;

                // Muzzle: the far end of the long axis, in the model's own unscaled space.
                Vector3 muzzle = fit.Long == Axis.Z
                    ? new Vector3(local.center.x, local.center.y, local.max.z)
                    : new Vector3(local.center.x, local.max.y, local.center.z);

                built.Add(new WeaponDefinition
                {
                    Id = fit.Id,
                    Prefab = prefab,
                    LocalPosition = fit.GripNudge,
                    LocalEuler = euler,
                    Scale = scale,
                    MuzzleLocal = muzzle,
                    HasMuzzle = fit.HasMuzzle,

                    // Section 12.
                    ClipFamily = fit.Family,
                    Stance = fit.Stance,
                    MagazineSize = fit.Magazine,
                    ReserveCapacity = fit.Reserve,
                    Damage = fit.Damage,
                    Range = fit.Range,
                    Spread = fit.Spread,
                    FireCooldown = fit.Cooldown,
                    ReloadSeconds = fit.Reload,
                    EquipSeconds = fit.Equip,
                });

                report.Add(fit.Id + " (" + fit.Prefab + ") modelled " + modelled.ToString("F3")
                           + " m -> x" + scale.ToString("F3") + " = " + fit.TargetLength.ToString("F2") + " m");
            }

            lib.Weapons = built.ToArray();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();

            Debug.Log("[Weapons] Library rebuilt with " + built.Count + " weapons:\n  "
                      + string.Join("\n  ", report));
            return lib;
        }

        public static WeaponLibrary Load() =>
            AssetDatabase.LoadAssetAtPath<WeaponLibrary>(LibraryPath) ?? Build();

        // ------------------------------------------------------------------ socket

        /// <summary>
        /// Works out where a weapon socket goes on this character's right hand, by posing the
        /// rig with a clip in which it is already holding a pistol correctly.
        ///
        /// Guessing a hand rotation from bone axes does not survive a rig change: the Mixamo
        /// characters and the Apocalyptic character disagree on every hand axis (this rig's
        /// fingers run down local +Y and its bind pose faces -Z). What they do agree on is what
        /// "aiming a pistol" looks like, because the humanoid avatar normalises exactly that.
        /// So the socket is defined as: whatever rotation makes the weapon point where the
        /// character points, in the pose where the character is pointing a weapon.
        ///
        /// Returns false, leaving the outputs at zero, if the rig is not humanoid or the
        /// reference clip is missing -- callers then get an unrotated socket, which is visibly
        /// wrong rather than silently wrong.
        /// </summary>
        public static bool ComputeHandSocket(GameObject characterRoot,
                                             out Vector3 localPosition, out Vector3 localEuler)
        {
            localPosition = Vector3.zero;
            localEuler = Vector3.zero;

            var animator = characterRoot.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning("[Weapons] " + characterRoot.name + " is not humanoid; socket left unrotated.");
                return false;
            }

            var clip = ReferenceAimClip();
            if (clip == null)
            {
                Debug.LogWarning("[Weapons] No pistol-aim reference clip found; socket left unrotated.");
                return false;
            }

            var model = animator.gameObject;
            Quaternion modelRotation = model.transform.rotation;

            clip.SampleAnimation(model, 0f);

            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null)
            {
                animator.Rebind();
                Debug.LogWarning("[Weapons] Rig has no RightHand bone.");
                return false;
            }

            // Where the character is pointing, in world space, in this pose.
            Quaternion aim = Quaternion.LookRotation(modelRotation * Vector3.forward,
                                                     modelRotation * Vector3.up);

            Quaternion socket = Quaternion.Inverse(hand.rotation) * aim;
            localEuler = socket.eulerAngles;

            // Put the grip in the palm rather than on the wrist joint. Half a hand's length
            // along the finger direction, which for a humanoid is hand -> middle/index knuckle.
            var knuckle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal)
                       ?? animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            if (knuckle != null)
                localPosition = hand.InverseTransformVector(
                    (knuckle.position - hand.position) * 0.55f);

            // Undo the sampled pose: leaving a character frozen in a firing stance in the saved
            // scene would look like a bug and would be one the next time it was serialised.
            animator.Rebind();
            return true;
        }

        /// <summary>
        /// The pose the socket is solved from: a character correctly aiming a pistol.
        ///
        /// Phase 9b replaced the animation source, so this moved from Mixamo's `Pistol Idle` to
        /// the Kevin Iglesias `Gun_Aim01`. That matters more than it looks: the socket rotation
        /// is *derived* from whichever clip this returns, so pointing it at a clip where the
        /// character is not aiming would silently mis-aim every weapon in the game. The fallback
        /// order is deliberate -- the aim hold first, the firing pose second.
        /// </summary>
        static AnimationClip ReferenceAimClip()
        {
            const string ki = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Combat/Gun";

            return FromFbx(ki + "/HumanM@Gun_Aim01.fbx")
                ?? FromFbx(ki + "/HumanM@Gun_Aim01_Shoot01.fbx")
                ?? FromFbx(ki + "/HumanM@Gun_Aim02.fbx");
        }

        static AnimationClip FromFbx(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview"));
        }

        // ------------------------------------------------------------------ shared

        /// <summary>
        /// Adds a configured <see cref="WeaponSocket"/> to a character root.
        /// One call for the player, the police, and anything armed later.
        /// </summary>
        public static WeaponSocket AttachSocket(GameObject characterRoot, string armedWeapon,
                                                bool followCombat)
        {
            var socket = characterRoot.GetComponent<WeaponSocket>()
                      ?? characterRoot.AddComponent<WeaponSocket>();

            socket.Library = Load();
            socket.AttachBone = HumanBodyBones.RightHand;
            socket.ArmedWeapon = armedWeapon;
            socket.FollowCombatState = followCombat;
            socket.StartingWeapon = followCombat ? "" : armedWeapon;

            if (ComputeHandSocket(characterRoot, out Vector3 pos, out Vector3 euler))
            {
                socket.SocketPosition = pos;
                socket.SocketEuler = euler;
            }

            return socket;
        }

        // --------------------------------------------------------------- pickups

        /// <summary>
        /// Where the weapons lie in the world, as block index plus kerb edge.
        ///
        /// Chosen rather than scattered: the player now starts with nothing but their fists
        /// (D28), so the first pistol has to be findable without a guide. The first entry is
        /// two blocks from the spawn point on the same avenue, in the player's line of sight
        /// as they set off; the rest are spread across the districts so that re-arming after
        /// a bust does not mean a walk back across the map.
        /// </summary>
        /// <summary>
        /// Where the weapons in the world are, generated rather than hand-listed.
        ///
        /// <b>What was here before.</b> Six entries, all of them pistols. Audited against the
        /// live scene that came out as 6 pickups covering 6 of the 81 city blocks, with the
        /// entire downtown 3x3 empty, rows bz=0/3/6 and columns bx=1/3 empty, nothing at all
        /// on the beach, and six of the seven authored weapons never appearing in the world.
        /// The rifle, shotgun, sniper, knife, bat and axe all had real stats and no way to be
        /// found.
        ///
        /// <b>The rule.</b> One pickup on every other block in both axes -- a 5x5 lattice over
        /// the 9x9 grid -- which reaches the centre block, all four corners and every district
        /// ring, and puts roughly 130 m between neighbours. Even by construction, so it cannot
        /// drift back into a cluster when the grid size changes.
        ///
        /// <b>What is where.</b> The weapon follows the district, so where you are is a hint
        /// about what you will find: suburbs carry melee and sidearms, midtown adds shotguns
        /// and rifles, downtown is where the rifles and the single sniper are. The choice is a
        /// pure function of the block index, so a rebuild reproduces the same map.
        /// </summary>
        static (int bx, int bz, Heading edge, float along, string weapon, int rounds)[] BuildPickupTable()
        {
            var spots = new System.Collections.Generic.List<(int, int, Heading, float, string, int)>();

            // Rotating through the four kerbs rather than always the same one, so a street
            // does not end up with every weapon on its north side.
            var edges = new[] { Heading.North, Heading.East, Heading.South, Heading.West };
            int n = 0;

            for (int bz = 0; bz < CityBuilder.Blocks; bz += 2)
            {
                for (int bx = 0; bx < CityBuilder.Blocks; bx += 2)
                {
                    int centre = CityBuilder.Blocks / 2;
                    int ring = Mathf.Max(Mathf.Abs(bx - centre), Mathf.Abs(bz - centre));

                    string weapon;
                    if (ring <= 1)                       // downtown
                        weapon = (bx + bz) % 4 == 0 ? "rifle" : ((bx + bz) % 4 == 2 ? "shotgun" : "pistol");
                    else if (ring <= 3)                  // midtown
                        weapon = (bx + bz) % 3 == 0 ? "shotgun" : ((bx + bz) % 3 == 1 ? "pistol" : "rifle");
                    else                                 // suburbs
                        weapon = (bx + bz) % 3 == 0 ? "bat" : ((bx + bz) % 3 == 1 ? "knife" : "pistol");

                    var edge = edges[n % edges.Length];
                    // Spread along the kerb as well as around it, so neighbouring blocks do
                    // not line their pickups up down the street.
                    float along = -0.28f + 0.14f * (n % 5);

                    spots.Add((bx, bz, edge, along, weapon, RoundsFor(weapon)));
                    n++;
                }
            }

            // One sniper, in the centre of downtown. The strongest weapon in the game is worth
            // a specific journey rather than being one of twenty-five things on a lattice.
            int mid = CityBuilder.Blocks / 2;
            spots.Add((mid, mid, Heading.East, 0.34f, "sniper", RoundsFor("sniper")));

            return spots.ToArray();
        }

        /// <summary>Two magazines' worth, or a single instance for a melee weapon.</summary>
        static int RoundsFor(string weapon)
        {
            switch (weapon)
            {
                case "pistol": return 24;
                case "rifle": return 60;
                case "shotgun": return 18;
                case "sniper": return 8;
                default: return 1;      // knife, bat, axe -- no ammunition
            }
        }

        [MenuItem("Tools/Mini GTA/17. Place Weapon Pickups", priority = 137)]
        public static int BuildPickups()
        {
            var old = GameObject.Find("WeaponPickups");
            if (old != null) Object.DestroyImmediate(old);

            var lib = Load();
            if (lib == null)
            {
                Debug.LogError("[Weapons] No weapon library; run 'Build Weapon Library' first.");
                return 0;
            }

            var root = new GameObject("WeaponPickups");
            int placed = 0;

            var table = BuildPickupTable();
            foreach (var spot in table)
            {
                var def = lib.Find(spot.weapon);
                if (def == null || def.Prefab == null)
                {
                    Debug.LogWarning("[Weapons] Pickup wants '" + spot.weapon + "' but the library has no such weapon.");
                    continue;
                }

                Vector3 kerb = CityBuilder.BlockEdge(spot.bx, spot.bz, spot.edge, spot.along);
                // Out from the building line onto the open pavement, and up to chest height so
                // it reads against the ground rather than lying flat in it.
                Vector3 at = kerb + spot.edge.ToVector() * 1.8f + Vector3.up * 0.95f;

                var go = new GameObject("Pickup_" + spot.weapon + "_" + spot.bx + "_" + spot.bz);
                go.transform.SetParent(root.transform, false);
                go.transform.position = at;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(def.Prefab);
                visual.name = "Visual";
                visual.transform.SetParent(go.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, 25f);

                // Bigger than it would be in the hand. A 21 cm pistol at grip scale is a
                // speck at walking distance; a pickup has to be spotted before it can be
                // walked to, so it is drawn at roughly two and a half times life size.
                visual.transform.localScale = Vector3.one * (def.Scale * 2.5f);

                // Collider-free: this is a thing you walk up to, not a thing you bump into,
                // and a physics body here would let the player kick the pistol down the street.
                foreach (var col in visual.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(col);

                var pickup = go.AddComponent<WeaponPickup>();
                pickup.WeaponId = spot.weapon;
                pickup.Rounds = spot.rounds;
                pickup.Visual = visual.transform;

                placed++;
            }

            var byType = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var spot in table)
                byType[spot.weapon] = byType.TryGetValue(spot.weapon, out int c) ? c + 1 : 1;

            var breakdown = new System.Text.StringBuilder();
            foreach (var kv in byType) breakdown.Append("  ").Append(kv.Key).Append(' ').Append(kv.Value);

            Debug.Log("[Weapons] Placed " + placed + " weapon pickup(s) on a "
                      + ((CityBuilder.Blocks + 1) / 2) + "x" + ((CityBuilder.Blocks + 1) / 2)
                      + " lattice over the " + CityBuilder.Blocks + "x" + CityBuilder.Blocks
                      + " block grid, plus one sniper downtown."
                      + "\n  By type:" + breakdown
                      + "\n  The player still starts unarmed.");
            return placed;
        }

        /// <summary>Bounds of a prefab's meshes in the prefab root's own space.</summary>
        static Bounds LocalBounds(GameObject prefab)
        {
            var b = new Bounds();
            bool first = true;

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;

                Bounds mb = mf.sharedMesh.bounds;
                // Child parts (a magazine, a sight) sit at an offset from the root; their mesh
                // bounds are in their own space, so they have to be brought into the root's.
                Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Vector3 c = toRoot.MultiplyPoint3x4(mb.center);
                Vector3 e = toRoot.MultiplyVector(mb.extents);
                var part = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);

                if (first) { b = part; first = false; } else b.Encapsulate(part);
            }

            return b;
        }
    }
}
