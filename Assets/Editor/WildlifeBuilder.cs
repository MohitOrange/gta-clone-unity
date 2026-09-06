using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Scatters wildlife and wild vegetation over the parts of the map the city builder does
    /// not touch: the park, the coastline and the mountains.
    ///
    /// Placement is procedural and seeded from the same terrain constants the terrain itself
    /// came from, exactly like <see cref="CityBuilder"/> -- nothing here is hand-placed, and
    /// reshaping the map reshapes this too. Candidate points are rejected against the live
    /// terrain (height band, slope, distance from the city pad) rather than against a list of
    /// coordinates that would go stale the moment MapSize changed.
    /// </summary>
    public static class WildlifeBuilder
    {
        const string AnimalDir = "Assets/ithappy/Animals_FREE/Prefabs";

        /// <summary>
        /// Which animal belongs where, and how big a patch it wanders.
        ///
        /// The tiger is deliberately absent from anywhere the player routinely walks: it is a
        /// 3 m predator with a run cycle, and ambient decoration that mauls nobody but looks
        /// like it should is worse than no tiger.
        /// </summary>
        struct Species
        {
            public string Prefab;
            public int Count;
            public float RoamRadius;
            public float WalkSpeed;
            public float RunSpeed;
            public Zone Where;

            public Species(string prefab, int count, float roam, float walk, float run, Zone where)
            {
                Prefab = prefab; Count = count; RoamRadius = roam;
                WalkSpeed = walk; RunSpeed = run; Where = where;
            }
        }

        enum Zone { City, Park, Coast, Mountain }

        /// <summary>
        /// The roster.
        ///
        /// <b>City strays exist because the first pass forgot the player.</b> Phase 9 placed all
        /// 43 animals in the park, on the coast and up the mountains -- correct zones, and
        /// between 88 m and half a map away from where anyone actually spawns and walks. The
        /// scene contained 43 animals and a player saw none of them. Strays on the pavements are
        /// what make the wildlife something you encounter rather than something the scene census
        /// can prove is there.
        /// </summary>
        /// <summary>
        /// Counts are per 25 city blocks -- the grid size these numbers were tuned against --
        /// and scaled to whatever the grid is now. Phase 11 tripled the city's area; leaving
        /// the absolute counts alone would have thinned the strays from one per block to one
        /// per four, which is the same "technically present, never seen" failure the City zone
        /// was added to fix. Park, coast and mountain scale with the map for the same reason.
        /// </summary>
        static float DensityScale =>
            Mathf.Clamp((CityBuilder.Blocks * CityBuilder.Blocks) / 25f, 1f, 3.5f);

        static Species[] BuildRoster()
        {
            float k = DensityScale;
            int N(int perTwentyFive) => Mathf.Max(1, Mathf.RoundToInt(perTwentyFive * k));

            return new[]
            {
                new Species("Dog_001",     N(8), 10f, 1.3f, 5.2f, Zone.City),
                new Species("Kitty_001",   N(7),  7f, 0.9f, 4.4f, Zone.City),
                new Species("Chicken_001", N(5),  5f, 0.7f, 2.6f, Zone.City),

                new Species("Dog_001",     N(4), 11f, 1.3f, 5.2f, Zone.Park),
                new Species("Kitty_001",   N(4),  7f, 0.9f, 4.4f, Zone.Park),
                new Species("Chicken_001", N(5),  5f, 0.7f, 2.6f, Zone.Park),

                new Species("Pinguin_001", N(8),  8f, 0.8f, 2.4f, Zone.Coast),
                new Species("Chicken_001", N(4),  6f, 0.7f, 2.6f, Zone.Coast),

                new Species("Deer_001",    N(7), 22f, 1.5f, 7.0f, Zone.Mountain),
                new Species("Horse_001",   N(5), 26f, 1.7f, 8.0f, Zone.Mountain),
                new Species("Tiger_001",   N(2), 20f, 1.2f, 7.5f, Zone.Mountain),
            };
        }

        [MenuItem("Tools/Mini GTA/17. Build Wildlife and Wild Vegetation", priority = 137)]
        public static void Build()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[Wildlife] No Terrain in scene. Run 'Build Terrain' first.");
                return;
            }

            var old = GameObject.Find("Wildlife");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("Wildlife");
            var animals = new GameObject("Animals");
            animals.transform.SetParent(root.transform, false);
            var flora = new GameObject("WildFlora");
            flora.transform.SetParent(root.transform, false);

            var rng = new System.Random(90901);
            ResetCityBlockDeck();

            int placed = 0;
            foreach (var species in BuildRoster())
                placed += PlaceSpecies(animals, terrain, rng, species);

            int plants = PlaceWildFlora(flora, terrain, rng);

            Debug.Log("[Wildlife] " + placed + " animals (density x"
                      + DensityScale.ToString("F2") + ") across city, park, coast and mountains; "
                      + plants + " trees, rocks and grass clumps.");
        }

        // ---------------------------------------------------------------- animals

        static int PlaceSpecies(GameObject parent, Terrain terrain, System.Random rng, Species species)
        {
            string path = AnimalDir + "/" + species.Prefab;
            // The pack nests prefabs one folder per animal; try both layouts.
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab")
                      ?? FindPrefab(species.Prefab);
            if (source == null)
            {
                Debug.LogWarning("[Wildlife] Missing animal prefab " + species.Prefab);
                return 0;
            }

            int placed = 0;
            for (int i = 0; i < species.Count; i++)
            {
                if (!FindGround(terrain, rng, species.Where, out Vector3 at)) continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
                go.name = species.Prefab + "_" + i;
                go.transform.SetParent(parent.transform, true);
                go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));

                StripPackDemoScripts(go);

                // Let Unity decide off-screen cost. AmbientAnimal's own distance check is a
                // cap for things that are on screen but too far to read; deciding "is it
                // visible" is the renderer's job, and it does it for free.
                foreach (var an in go.GetComponentsInChildren<Animator>(true))
                    an.cullingMode = AnimatorCullingMode.CullCompletely;

                var brain = go.AddComponent<AmbientAnimal>();
                brain.Home = at;
                brain.RoamRadius = species.RoamRadius;
                brain.WalkSpeed = species.WalkSpeed;
                brain.RunSpeed = species.RunSpeed;

                // The pack's own collider is left exactly as shipped: one per animal, so a deer
                // stops a bullet and a car instead of being scenery a pistol shoots through.
                placed++;
            }

            return placed;
        }

        /// <summary>
        /// Removes the animal pack's own demo controllers from a spawned animal.
        ///
        /// The prefabs ship with the pack's playable-creature scripts attached, and one of them
        /// polls <c>Input.GetAxis</c>. This project is set to Input System only, where that call
        /// <b>throws</b> -- so every animal threw an InvalidOperationException every frame, 43
        /// of them, before this. They would also fight <see cref="AmbientAnimal"/> for the same
        /// transform even if they did not throw.
        ///
        /// Matched by namespace rather than by type name, so the pack adding a fourth demo
        /// script does not quietly reintroduce the problem.
        ///
        /// Removal has to respect <c>RequireComponent</c>: these two scripts declare a
        /// dependency on each other's order, and Unity refuses to delete the one that is
        /// required while the requirer is still attached. Hence the repeated passes, each
        /// taking only the components nothing else still needs.
        /// </summary>
        static void StripPackDemoScripts(GameObject go)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                var remaining = go.GetComponentsInChildren<MonoBehaviour>(true)
                    .Where(mb => mb != null && mb.GetType().Namespace != null
                              && mb.GetType().Namespace.StartsWith("ithappy"))
                    .ToList();

                if (remaining.Count == 0) return;

                bool removedAny = false;
                foreach (var mb in remaining)
                {
                    if (mb == null || IsRequiredByAnother(mb, remaining)) continue;
                    Object.DestroyImmediate(mb, true);
                    removedAny = true;
                }

                if (!removedAny) break;
            }

            int left = go.GetComponentsInChildren<MonoBehaviour>(true)
                .Count(mb => mb != null && mb.GetType().Namespace != null
                          && mb.GetType().Namespace.StartsWith("ithappy"));
            if (left > 0)
                Debug.LogWarning("[Wildlife] " + left + " pack demo script(s) could not be removed "
                                 + "from " + go.name + "; they will throw on legacy Input.", go);
        }

        /// <summary>True if any other component in the pool declares RequireComponent on this one.</summary>
        static bool IsRequiredByAnother(MonoBehaviour candidate, List<MonoBehaviour> pool)
        {
            var type = candidate.GetType();
            foreach (var other in pool)
            {
                if (other == null || other == candidate) continue;
                foreach (RequireComponent rc in other.GetType()
                             .GetCustomAttributes(typeof(RequireComponent), true))
                {
                    if (rc.m_Type0 == type || rc.m_Type1 == type || rc.m_Type2 == type) return true;
                }
            }
            return false;
        }

        static GameObject FindPrefab(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab",
                     new[] { "Assets/ithappy/Animals_FREE" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        // ------------------------------------------------------------------ flora

        static int PlaceWildFlora(GameObject parent, Terrain terrain, System.Random rng)
        {
            int placed = 0;

            placed += Scatter(parent, terrain, rng, Zone.Coast, EnvironmentCatalog.CoastTrees, 34);
            placed += Scatter(parent, terrain, rng, Zone.Coast, EnvironmentCatalog.Grass, 40);
            placed += Scatter(parent, terrain, rng, Zone.Coast, EnvironmentCatalog.SmallRocks, 26);

            placed += Scatter(parent, terrain, rng, Zone.Mountain, EnvironmentCatalog.MountainTrees, 48);
            placed += Scatter(parent, terrain, rng, Zone.Mountain, EnvironmentCatalog.Rocks, 20);
            placed += Scatter(parent, terrain, rng, Zone.Mountain, EnvironmentCatalog.SmallRocks, 30);

            return placed;
        }

        static int Scatter(GameObject parent, Terrain terrain, System.Random rng, Zone zone,
                           EnvironmentCatalog.Prop[] table, int count)
        {
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                if (!FindGround(terrain, rng, zone, out Vector3 at)) continue;

                var go = EnvironmentCatalog.Place(parent, table[rng.Next(table.Length)],
                                                  at, (float)rng.NextDouble() * 360f, rng);
                if (go != null) placed++;
            }
            return placed;
        }

        // ----------------------------------------------------------------- ground

        /// <summary>
        /// Rejection-samples a point that actually belongs to the zone.
        ///
        /// Zones are defined by what the terrain is doing at a point, not by a bounding box:
        /// "coast" is land just above the waterline, "mountain" is anything high and not too
        /// steep. That way reshaping the terrain constants moves the wildlife with it, and
        /// nothing ends up standing on a cliff face or floating over the sea.
        /// </summary>
        static bool FindGround(Terrain terrain, System.Random rng, Zone zone, out Vector3 point)
        {
            point = Vector3.zero;

            float map = TerrainBuilder.MapSize;
            float citySpan = CityBuilder.Blocks * CityBuilder.Pitch;

            // The park is a flat slab the city builder already levelled, so it needs no
            // rejection sampling -- any point inside it is valid ground.
            if (zone == Zone.Park)
            {
                if (!ParkCentre(out Vector3 centre)) return false;
                float half = CityBuilder.BlockSize * 0.5f - CityBuilder.SidewalkInset - 2f;
                point = new Vector3(
                    centre.x + Mathf.Lerp(-half, half, (float)rng.NextDouble()),
                    centre.y,
                    centre.z + Mathf.Lerp(-half, half, (float)rng.NextDouble()));
                return true;
            }

            // City strays stand on the pavement band between the building line and the kerb --
            // the same strip the street furniture uses, so they are on a flat slab of known
            // height and never in a carriageway.
            if (zone == Zone.City)
            {
                float gridSpan = CityBuilder.Blocks * CityBuilder.Pitch;
                float ox = TerrainBuilder.CityCenter.x - gridSpan * 0.5f;
                float oz = TerrainBuilder.CityCenter.y - gridSpan * 0.5f;

                // Blocks are dealt from a shuffled deck rather than drawn at random, so twenty
                // strays land on twenty different blocks. Drawing with replacement left whole
                // quarters of the city empty and put the nearest animal 190 m from the spawn --
                // which is the same "technically placed, never seen" failure this zone exists
                // to fix.
                int block = NextCityBlock(rng);
                int bx = block % CityBuilder.Blocks;
                int bz = block / CityBuilder.Blocks;
                float cx = ox + CityBuilder.RoadWidth + bx * CityBuilder.Pitch + CityBuilder.BlockSize * 0.5f;
                float cz = oz + CityBuilder.RoadWidth + bz * CityBuilder.Pitch + CityBuilder.BlockSize * 0.5f;

                float band = CityBuilder.BlockSize * 0.5f - CityBuilder.SidewalkInset * 0.5f;
                float along = Mathf.Lerp(-0.34f, 0.34f, (float)rng.NextDouble()) * CityBuilder.BlockSize;

                Vector3 outward = rng.Next(4) switch
                {
                    0 => Vector3.forward,
                    1 => Vector3.right,
                    2 => Vector3.back,
                    _ => Vector3.left,
                };
                Vector3 sideways = new Vector3(outward.z, 0f, -outward.x);

                point = new Vector3(cx, TerrainBuilder.PlateauHeight + CityBuilder.SidewalkHeight, cz)
                      + outward * band + sideways * along;
                return true;
            }

            for (int attempt = 0; attempt < 60; attempt++)
            {
                float x = (float)rng.NextDouble() * map;
                float z = (float)rng.NextDouble() * map;

                // Never inside the city pad: that is the city builder's ground, and a deer in
                // a carriageway is a traffic bug, not decoration.
                if (Mathf.Abs(x - TerrainBuilder.CityCenter.x) < citySpan * 0.5f + 25f
                 && Mathf.Abs(z - TerrainBuilder.CityCenter.y) < citySpan * 0.5f + 25f)
                    continue;

                float h = TerrainBuilder.SampleHeight(terrain, x, z);
                float slope = Slope(terrain, x, z);

                bool ok = zone switch
                {
                    // Just above the tideline, on ground flat enough to stand on.
                    Zone.Coast => h > TerrainBuilder.SeaLevel + 0.6f
                               && h < TerrainBuilder.SeaLevel + 7f
                               && slope < 22f,
                    // High ground, but not a cliff face.
                    Zone.Mountain => h > TerrainBuilder.PlateauHeight + 12f
                                  && slope < 32f,
                    _ => false,
                };

                if (!ok) continue;

                point = new Vector3(x, h, z);
                return true;
            }

            return false;
        }

        /// <summary>Block indices in a shuffled order, refilled whenever the deck runs out.</summary>
        static readonly List<int> _cityBlockDeck = new List<int>();

        static void ResetCityBlockDeck() => _cityBlockDeck.Clear();

        static int NextCityBlock(System.Random rng)
        {
            if (_cityBlockDeck.Count == 0)
            {
                int total = CityBuilder.Blocks * CityBuilder.Blocks;
                for (int i = 0; i < total; i++) _cityBlockDeck.Add(i);

                for (int i = _cityBlockDeck.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (_cityBlockDeck[i], _cityBlockDeck[j]) = (_cityBlockDeck[j], _cityBlockDeck[i]);
                }
            }

            int last = _cityBlockDeck.Count - 1;
            int block = _cityBlockDeck[last];
            _cityBlockDeck.RemoveAt(last);
            return block;
        }

        static float Slope(Terrain terrain, float x, float z)
        {
            var data = terrain.terrainData;
            Vector3 pos = terrain.transform.position;
            float u = Mathf.Clamp01((x - pos.x) / data.size.x);
            float v = Mathf.Clamp01((z - pos.z) / data.size.z);
            return data.GetSteepness(u, v);
        }

        static bool ParkCentre(out Vector3 centre)
        {
            centre = Vector3.zero;
            var park = GameObject.Find("City/Park");
            if (park == null) return false;

            var renderers = park.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;

            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            centre = new Vector3(b.center.x, b.max.y, b.center.z);
            return true;
        }
    }
}
