using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Generates the island: a flat coastal city pad, a beach sloping into the sea to the
    /// west, and mountains sealing the other three edges as a natural map boundary.
    ///
    /// Everything is procedural so the world can be re-rolled by changing constants, and so
    /// the repo carries no multi-megabyte heightmap or texture binaries.
    /// </summary>
    public static class TerrainBuilder
    {
        // --- World dimensions (metres) ---
        //
        // Phase 11 took this from 1000 to 1600 m a side -- 2.56x the area. Everything about
        // the island's shape is expressed in normalised (u,v) coordinates below, so the coast,
        // the beach and the mountain ring all scale with it automatically and the layout is
        // unchanged; only the scale is. CityCenter and CityExtents are the two values that are
        // in absolute metres and so had to move with it.
        //
        // HeightmapRes deliberately stayed at 513, which coarsens the terrain from 1.95 to
        // 3.12 m per texel. That is the right trade on a phone: the city pad is flattened
        // anyway, and a 1025 heightmap would have roughly quadrupled terrain patch cost to add
        // detail only the mountains would show. See DECISIONS.md D26.
        public const int MapSize = 1600;
        public const int MapHeight = 160;
        public const int HeightmapRes = 513;   // must be 2^n + 1
        public const int AlphamapRes = 512;

        // --- Elevations (world Y) ---
        public const float SeaLevel = 8f;
        public const float SeabedHeight = 1.5f;

        /// <summary>
        /// How far above the waterline the sand reaches before it gives way to grass.
        ///
        /// This is a height, not a distance, so the beach that results is as wide as the shore
        /// is shallow -- which is what a beach is. 9 m against a PlateauHeight of 15 leaves the
        /// city on grass and the shore in sand with a metre or two of overlap between them.
        /// </summary>
        public const float BeachHeight = 9f;
        public const float PlateauHeight = 15f;
        public const float MountainHeight = 96f;

        // --- City pad, in world XZ ---
        //
        // Held at the same *normalised* spot on the island as before the Phase 11 expansion
        // (0.56, 0.50), so the city still sits just inland of the beach with the mountains
        // behind it: 560/1000 -> 896/1600, 500/1000 -> 800/1600.
        //
        // CityExtents must stay comfortably larger than half the block grid's span, or the
        // outer blocks are built on ground the flattening pass never levelled and buildings
        // sink into a hillside. At Blocks = 9 the grid spans 684 m, so half-span is 342 and
        // this has 18 m of margin. Check this if either number changes.
        public static readonly Vector2 CityCenter = new Vector2(896f, 800f);
        public static readonly Vector2 CityExtents = new Vector2(360f, 360f);

        const string TerrainAssetPath = "Assets/Game/World/IslandTerrain.asset";
        const string LayerDir = "Assets/Game/World/TerrainLayers";

        [MenuItem("Tools/Mini GTA/3. Build Terrain", priority = 110)]
        public static Terrain BuildTerrain()
        {
            Directory.CreateDirectory("Assets/Game/World");
            Directory.CreateDirectory(LayerDir);

            var data = new TerrainData
            {
                heightmapResolution = HeightmapRes,
                alphamapResolution = AlphamapRes,
                baseMapResolution = 512,
                size = new Vector3(MapSize, MapHeight, MapSize),
            };

            data.SetDetailResolution(512, 16);
            WriteHeights(data);

            var layers = BuildLayers();
            data.terrainLayers = layers;
            WriteSplat(data, layers.Length);

            AssetDatabase.DeleteAsset(TerrainAssetPath);
            AssetDatabase.CreateAsset(data, TerrainAssetPath);

            var existing = GameObject.Find("Terrain");
            if (existing != null) Object.DestroyImmediate(existing);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.isStatic = true;
            go.layer = LayerMask.NameToLayer("Default");

            var terrain = go.GetComponent<Terrain>();
            ApplyMobileSettings(terrain);

            AssetDatabase.SaveAssets();
            Debug.Log("[World] Terrain built: " + MapSize + "m x " + MapSize + "m, "
                      + HeightmapRes + " heightmap, " + layers.Length + " layers.");
            return terrain;
        }

        static void ApplyMobileSettings(Terrain t)
        {
            // Terrain is the single biggest fill-rate risk on mobile. These are the knobs
            // that actually matter: aggressive LOD error, short basemap, instanced draws.
            t.heightmapPixelError = 12f;
            t.basemapDistance = 220f;
            t.drawInstanced = true;
            t.allowAutoConnect = true;
            t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t.detailObjectDistance = 90f;
            t.treeDistance = 350f;
            t.treeBillboardDistance = 90f;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
            AssetDatabase.DeleteAsset("Assets/Game/World/TerrainMat.mat");
            AssetDatabase.CreateAsset(mat, "Assets/Game/World/TerrainMat.mat");
            t.materialTemplate = mat;
        }

        // ------------------------------------------------------------------ heights

        static void WriteHeights(TerrainData data)
        {
            int res = HeightmapRes;
            var heights = new float[res, res];

            for (int z = 0; z < res; z++)
            {
                float v = z / (float)(res - 1);
                for (int x = 0; x < res; x++)
                {
                    float u = x / (float)(res - 1);
                    heights[z, x] = Mathf.Clamp01(WorldHeight(u, v) / MapHeight);
                }
            }

            data.SetHeights(0, 0, heights);
        }

        /// <summary>Height in world metres at normalised map coordinates.</summary>
        static float WorldHeight(float u, float v)
        {
            // 1. Coast: sea to the west, land to the east.
            float shore = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.11f, 0.31f, u));
            float h = Mathf.Lerp(SeabedHeight, PlateauHeight, shore);

            // 2. Mountains on the three landward edges. Squared so they rise gently at the
            //    foot and steepen toward the border, which reads as a range rather than a wall.
            float distEast = 1f - u;
            float distNorth = 1f - v;
            float distSouth = v;
            float edge = Mathf.Min(distEast, Mathf.Min(distNorth, distSouth));
            float mount = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.34f, edge));
            h += mount * mount * MountainHeight;

            // 3. Rolling detail, suppressed on the beach so the shoreline stays clean.
            float detail = Fbm(u * 5.5f, v * 5.5f, 4) - 0.5f;
            h += detail * 11f * shore;

            float ridge = Fbm(u * 2.3f + 31.7f, v * 2.3f + 11.3f, 3) - 0.5f;
            h += ridge * 26f * mount;

            // 4. Flatten the city pad last so nothing above can tilt it.
            float flat = CityFlatness(u, v);
            h = Mathf.Lerp(h, PlateauHeight, flat);

            return Mathf.Clamp(h, 0.5f, MapHeight - 2f);
        }

        /// <summary>1 inside the city pad, easing to 0 over a blend margin outside it.</summary>
        static float CityFlatness(float u, float v)
        {
            const float blend = 55f;

            float wx = u * MapSize;
            float wz = v * MapSize;

            float dx = Mathf.Abs(wx - CityCenter.x) - CityExtents.x;
            float dz = Mathf.Abs(wz - CityCenter.y) - CityExtents.y;
            float d = Mathf.Max(dx, dz);          // Chebyshev distance outside the rect

            if (d <= 0f) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / blend));
        }

        static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / Mathf.Max(norm, 0.0001f);
        }

        // ------------------------------------------------------------------ layers

        static TerrainLayer[] BuildLayers()
        {
            var defs = new (string name, Color a, Color b, float tile)[]
            {
                ("Sand",  new Color(0.83f, 0.76f, 0.56f), new Color(0.74f, 0.66f, 0.47f), 12f),
                ("Grass", new Color(0.33f, 0.44f, 0.22f), new Color(0.25f, 0.35f, 0.17f), 10f),
                ("Rock",  new Color(0.42f, 0.40f, 0.38f), new Color(0.30f, 0.29f, 0.28f), 16f),
                ("Dirt",  new Color(0.44f, 0.36f, 0.26f), new Color(0.35f, 0.28f, 0.20f), 12f),
            };

            var layers = new TerrainLayer[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                string texPath = LayerDir + "/" + d.name + "_Albedo.png";
                var tex = MakeNoiseTexture(d.a, d.b, 256, 9.7f * (i + 1));
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);

                var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

                string layerPath = LayerDir + "/" + d.name + ".terrainlayer";
                AssetDatabase.DeleteAsset(layerPath);
                var layer = new TerrainLayer
                {
                    diffuseTexture = imported,
                    tileSize = new Vector2(d.tile, d.tile),
                    tileOffset = Vector2.zero,
                    specular = Color.black,
                    metallic = 0f,
                    smoothness = 0.05f,
                };
                AssetDatabase.CreateAsset(layer, layerPath);
                layers[i] = layer;
            }
            return layers;
        }

        /// <summary>
        /// Two-tone value noise. Not photoreal, but it breaks up the flat-colour look at
        /// zero download cost and reads correctly at the distances a mobile camera uses.
        /// </summary>
        static Texture2D MakeNoiseTexture(Color a, Color b, int size, float seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var px = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = 0f, amp = 0.5f, freq = 4f, norm = 0f;
                    for (int o = 0; o < 4; o++)
                    {
                        // Wrap sampling so the texture tiles without a visible seam.
                        float fx = (x / (float)size) * freq;
                        float fy = (y / (float)size) * freq;
                        n += Mathf.PerlinNoise(fx + seed, fy + seed) * amp;
                        norm += amp;
                        amp *= 0.5f;
                        freq *= 2f;
                    }
                    n /= norm;
                    px[y * size + x] = Color.Lerp(a, b, n);
                }
            }

            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// Repaints the terrain's layer weights in place, leaving the heightmap alone.
        ///
        /// <b>Why this is separate from "3. Build Terrain".</b> That step creates a fresh
        /// TerrainData and rewrites the heightmap, which moves the ground under the city, the
        /// harbour, the road network, the spawn points and every prop placed against them.
        /// Repainting is the only part of it the island actually needed, and it changes nothing
        /// but the splat map.
        ///
        /// <b>What it fixes.</b> The terrain in City.unity was carrying Unity's default
        /// alphamap -- all weight on the first layer -- rather than anything WriteSplat had
        /// produced. Measured over all 262,144 alphamap cells: sand dominant on 100.0% of them,
        /// grass, rock and dirt on 0.0%, and a peak sand weight of 1.00, which the formula
        /// cannot produce because grass is a constant 1 underneath it (the most sand can reach
        /// is 1.6 / 2.6 = 0.62). So the whole 1600 m island was rendering as beach: no grass
        /// inland, no rock on the mountains, and no shoreline transition, because there was
        /// nothing for the sand to transition into.
        /// </summary>
        [MenuItem("Tools/Mini GTA/3b. Repaint Terrain Layers", priority = 111)]
        public static void RepaintLayers()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogError("[Terrain] No Terrain in the scene. Nothing to repaint.");
                return;
            }

            var data = terrain.terrainData;
            if (data.terrainLayers == null || data.terrainLayers.Length < 4)
            {
                Debug.LogError("[Terrain] Expected 4 layers (Sand, Grass, Rock, Dirt), found "
                               + (data.terrainLayers == null ? 0 : data.terrainLayers.Length)
                               + ". Run 'Build Terrain' on an empty scene instead.");
                return;
            }

            WriteSplat(data, data.terrainLayers.Length);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            // Report the result rather than the intent: the previous state looked correct in
            // the builder and was wrong on the ground.
            int res = data.alphamapResolution;
            var map = data.GetAlphamaps(0, 0, res, res);
            var dominant = new int[data.terrainLayers.Length];

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int best = 0;
                    for (int l = 1; l < dominant.Length; l++)
                        if (map[z, x, l] > map[z, x, best]) best = l;
                    dominant[best]++;
                }
            }

            var line = new System.Text.StringBuilder();
            int cells = res * res;
            for (int l = 0; l < dominant.Length; l++)
                line.Append("  ").Append(data.terrainLayers[l].name).Append(' ')
                    .Append((100f * dominant[l] / cells).ToString("F1")).Append('%');

            Debug.Log("[Terrain] Repainted " + cells + " alphamap cells. Dominant layer share:"
                      + line + "\n  Heightmap untouched, so nothing placed on the terrain moved.");
        }

        // ------------------------------------------------------------------ splat

        static void WriteSplat(TerrainData data, int layerCount)
        {
            int res = AlphamapRes;
            var map = new float[res, res, layerCount];

            for (int z = 0; z < res; z++)
            {
                float v = z / (float)(res - 1);
                for (int x = 0; x < res; x++)
                {
                    float u = x / (float)(res - 1);

                    float height = data.GetInterpolatedHeight(u, v);
                    // Terrain normals are in world space; y component falls off with steepness.
                    Vector3 n = data.GetInterpolatedNormal(u, v);
                    float slope = 1f - Mathf.Clamp01(n.y);

                    // Sand runs from the seabed up to BeachTop, then fades out.
                    //
                    // It used to be symmetrical about the waterline -- 1 - |height - SeaLevel|
                    // / 4.5 -- which faded sand out *downwards* as well as upwards and left the
                    // entire seabed painted grass, because grass is a constant 1 underneath.
                    // Everything the player swims over was green. Below the waterline there is
                    // nothing for sand to fade into, so it does not fade: the seabed and the
                    // wet sand at the water's edge are one continuous surface, which is also
                    // what removes the hard line the old formula drew along the shore.
                    float sand = height <= SeaLevel
                        ? 1f
                        : 1f - Mathf.Clamp01((height - SeaLevel) / BeachHeight);

                    float rock = Mathf.Clamp01(Mathf.InverseLerp(0.30f, 0.62f, slope))
                                 + Mathf.Clamp01(Mathf.InverseLerp(46f, 78f, height));
                    float dirt = Mathf.Clamp01(Mathf.InverseLerp(20f, 40f, height)) * 0.7f;
                    float grass = 1f;

                    // Rock is a cliff material. On the beach it was competing with the sand on
                    // the dune slopes and speckling them grey, so it is held back wherever the
                    // sand is strong.
                    rock *= 1f - Mathf.Clamp01(sand);

                    sand = Mathf.Clamp01(sand) * 1.6f;
                    rock = Mathf.Clamp01(rock) * 1.8f;

                    float total = sand + grass + rock + dirt;
                    map[z, x, 0] = sand / total;
                    map[z, x, 1] = grass / total;
                    map[z, x, 2] = rock / total;
                    map[z, x, 3] = dirt / total;
                }
            }

            data.SetAlphamaps(0, 0, map);
        }

        /// <summary>World-space ground height, used by the city builder and spawn logic.</summary>
        public static float SampleHeight(Terrain terrain, float worldX, float worldZ)
        {
            return terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrain.transform.position.y;
        }
    }
}
