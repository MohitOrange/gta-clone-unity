using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BuildingKind = MiniGTA.EditorTools.EnvironmentCatalog.BuildingKind;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Lays a road grid, sidewalks, buildings and a park onto the flat city pad carved by
    /// <see cref="TerrainBuilder"/>.
    ///
    /// Every piece is a primitive box sharing one of a handful of materials and marked
    /// static, so Unity's static batching collapses the whole district into a few draw
    /// calls -- the thing that actually decides whether this runs on a mid-range phone.
    /// </summary>
    public static class CityBuilder
    {
        // Grid geometry, metres.
        public const float RoadWidth = 14f;
        public const float BlockSize = 62f;
        public const float Pitch = BlockSize + RoadWidth;

        /// <summary>
        /// Blocks per side. Phase 11 took this from 5 to 9 -- 25 city blocks to 81, and a
        /// grid span of 380 m to 684 m.
        ///
        /// Raising it is safe as long as <see cref="TerrainBuilder.CityExtents"/> still
        /// covers half the span (see the note there) and the terrain is rebuilt afterwards.
        /// Everything that needs a position in the city asks <see cref="BlockCentre"/> or
        /// <see cref="JunctionCentre"/> rather than carrying its own copy of this arithmetic,
        /// so no other file has to change when it moves. See DECISIONS.md D25.
        /// </summary>
        public const int Blocks = 9;

        public const float SidewalkHeight = 0.18f;
        public const float SidewalkInset = 3.0f;
        public const float RoadSurfaceY = 0.06f;

        // ------------------------------------------------------- grid geometry (shared)

        /// <summary>Total width of the block grid including its bounding roads, metres.</summary>
        public static float GridSpan => Blocks * Pitch;

        /// <summary>South-west corner of the grid, on the outer edge of the outermost road.</summary>
        public static Vector2 GridOrigin => new Vector2(
            TerrainBuilder.CityCenter.x - GridSpan * 0.5f,
            TerrainBuilder.CityCenter.y - GridSpan * 0.5f);

        /// <summary>Street level: the top of the sidewalk slab.</summary>
        public static float StreetY => TerrainBuilder.PlateauHeight + SidewalkHeight;

        /// <summary>Road level: the top of the carriageway.</summary>
        public static float RoadY => TerrainBuilder.PlateauHeight + RoadSurfaceY;

        /// <summary>
        /// Centre of block (bx, bz) at street level. Indices run 0 .. Blocks-1 from the
        /// south-west.
        ///
        /// <b>Use this instead of writing a literal world coordinate.</b> Before Phase 11 the
        /// shop doors, mission markers and the police station spawn were all absolute metres,
        /// chosen by hand off a 5x5 grid; expanding the map moved every block out from under
        /// them and they would have ended up inside walls and carriageways. They are now all
        /// expressed as a block index plus an offset, so they follow the grid.
        /// </summary>
        public static Vector3 BlockCentre(int bx, int bz)
        {
            var o = GridOrigin;
            return new Vector3(
                o.x + RoadWidth + bx * Pitch + BlockSize * 0.5f,
                StreetY,
                o.y + RoadWidth + bz * Pitch + BlockSize * 0.5f);
        }

        /// <summary>
        /// A point on block (bx, bz)'s kerb, on the given edge, at street level.
        /// <paramref name="along"/> runs -0.5 .. +0.5 across that edge.
        /// </summary>
        public static Vector3 BlockEdge(int bx, int bz, Heading edge, float along = 0f)
        {
            Vector3 c = BlockCentre(bx, bz);
            Vector3 outward = edge.ToVector();
            Vector3 side = new Vector3(outward.z, 0f, -outward.x);
            return c + outward * (BlockSize * 0.5f) + side * (BlockSize * along);
        }

        /// <summary>Centre of the road junction at grid indices (i, j), at road level.</summary>
        public static Vector3 JunctionCentre(int i, int j)
        {
            var o = GridOrigin;
            return new Vector3(
                o.x + i * Pitch + RoadWidth * 0.5f,
                RoadY,
                o.y + j * Pitch + RoadWidth * 0.5f);
        }

        /// <summary>The block index at the middle of the grid. Downtown.</summary>
        public static int CentreBlock => Blocks / 2;

        const string MatDir = "Assets/Game/Materials";

        // Two green blocks now, not one: at 81 blocks a single park is lost in the grid.
        // Offset from the centre so downtown keeps its towers.
        static readonly Vector2Int[] ParkBlocks =
        {
            new Vector2Int(CentreBlock - 2, CentreBlock + 1),
            new Vector2Int(CentreBlock + 2, CentreBlock - 2),
        };

        static bool IsParkBlock(int bx, int bz)
        {
            foreach (var p in ParkBlocks) if (p.x == bx && p.y == bz) return true;
            return false;
        }

        /// <summary>
        /// The park the "Clear the Lot" job is set in. Exposed so MissionBuilder points at a
        /// block that is actually green rather than carrying its own copy of the index --
        /// which is how that job came to be briefed as "a crew squatting in the park" while
        /// its site sat in the middle of an office block.
        /// </summary>
        public static Vector2Int MissionParkBlock => ParkBlocks[0];

        /// <summary>
        /// Which part of town a block is in. Drives what gets built on it, so the skyline
        /// changes as you walk out from the middle instead of being towers everywhere.
        /// </summary>
        enum District { Downtown, Midtown, Suburb }

        static District DistrictOf(int bx, int bz)
        {
            int ring = Mathf.Max(Mathf.Abs(bx - CentreBlock), Mathf.Abs(bz - CentreBlock));
            if (ring <= 1) return District.Downtown;   // the middle 3x3
            if (ring <= 3) return District.Midtown;
            return District.Suburb;                    // the outer ring
        }

        /// <summary>Where the player is dropped in. Set during Build.</summary>
        public static Vector3 SpawnPoint { get; private set; }

        [MenuItem("Tools/Mini GTA/4. Build City", priority = 111)]
        public static GameObject BuildCity()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[City] No Terrain in scene. Run 'Build Terrain' first.");
                return null;
            }

            var old = GameObject.Find("City");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("City");
            var roads = NewChild(root, "Roads");
            var walks = NewChild(root, "Sidewalks");
            var builds = NewChild(root, "Buildings");
            var park = NewChild(root, "Park");
            var props = NewChild(root, "Props");

            _nightLights = new List<Renderer>();
            _packBuildings = 0;
            _boxBuildings = 0;
            _landmarks = 0;
            EnvironmentCatalog.ClearFootprintCache();

            var mats = LoadMaterials();
            var rng = new System.Random(20260826);

            float gridSpan = GridSpan;
            float originX = GridOrigin.x;
            float originZ = GridOrigin.y;
            float y = TerrainBuilder.PlateauHeight;

            BuildRoads(roads, mats, originX, originZ, gridSpan, y);
            BuildBlocks(builds, walks, park, props, mats, rng, originX, originZ, y);

            // Spawn in the middle of the westernmost avenue, halfway along it. An open road
            // rather than a sidewalk: it keeps the camera clear of building walls on the first
            // frame, and walking west from here reaches the beach in a few seconds.
            float spawnX = originX + RoadWidth * 0.5f;
            float spawnZ = originZ + gridSpan * 0.5f;
            SpawnPoint = new Vector3(spawnX, y + RoadSurfaceY + 0.3f, spawnZ);

            MarkStaticRecursive(root);

            // One owner for every emissive window shell in the city. See NightLights.
            if (_nightLights.Count > 0)
            {
                var lights = NewChild(root, "NightLights").AddComponent<NightLights>();
                lights.Lights = _nightLights.ToArray();
                foreach (var r in _nightLights) r.enabled = false;
            }

            int total = _packBuildings + _boxBuildings;
            float packShare = total > 0 ? 100f * _packBuildings / total : 0f;

            Debug.Log("[City] Built " + Blocks + "x" + Blocks + " block grid, pitch " + Pitch
                      + "m, span " + gridSpan + "m. Spawn at " + SpawnPoint
                      + "\n  Buildings: " + _packBuildings + " from asset packs ("
                      + packShare.ToString("F1") + "%), " + _boxBuildings + " primitive fallbacks, "
                      + _landmarks + " landmarks, " + _nightLights.Count + " night-light shells."
                      + "\n  Props: " + props.GetComponentsInChildren<Renderer>(true).Length + " renderers.");

            if (_boxBuildings > 0)
            {
                // Phase 11's whole point is that the packs are the norm. A non-zero count here
                // means a prefab path went stale or a lot size stopped matching any model.
                Debug.LogWarning("[City] " + _boxBuildings + " lot(s) fell back to a primitive box. "
                                 + "Expected zero -- check EnvironmentCatalog.Buildings paths and "
                                 + "the lot sizes in BuildLot.");
            }
            return root;
        }

        /// <summary>
        /// Gives every already-placed building a collider if it has none.
        ///
        /// <b>Why this exists as well as the generation fix.</b>
        /// <see cref="EnvironmentCatalog.NormaliseColliders"/> now guarantees a solid prop is
        /// solid, so anything built from here on is correct. But the city in City.unity was
        /// generated before that, and re-running <see cref="BuildCity"/> to pick the fix up
        /// would re-roll every lot from the RNG -- moving buildings that missions, pickups,
        /// interiors and spawn points were placed against. Repairing in place changes the one
        /// thing that is wrong and nothing else.
        ///
        /// Idempotent: a building that already blocks the player is skipped, so running this
        /// twice is not two colliders.
        /// </summary>
        [MenuItem("Tools/Mini GTA/4b. Repair Building Colliders", priority = 112)]
        public static void RepairBuildingColliders()
        {
            var scene = EditorSceneManager.GetActiveScene();
            int examined = 0, fixedUp = 0, alreadySolid = 0, noGeometry = 0;

            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                // The two names BuildLot gives a building, and nothing else: a blanket pass
                // over every renderer would make road decals and lane paint solid.
                if (t.name != "Bld" && !t.name.StartsWith("Bld_Pack")) continue;
                examined++;

                var go = t.gameObject;
                if (HasBlockingCollider(go)) { alreadySolid++; continue; }

                if (EnvironmentCatalog.EnsureSolid(go)) { fixedUp++; EditorUtility.SetDirty(go); }
                else noGeometry++;
            }

            if (fixedUp > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log("[City] Building collider repair: " + examined + " examined, "
                      + alreadySolid + " already solid, " + fixedUp + " given a fitted box"
                      + (noGeometry > 0 ? ", " + noGeometry + " skipped (no mesh to fit)" : "")
                      + ".\n  Buildings are static level geometry: a collider, never a Rigidbody.");

            if (noGeometry > 0)
                Debug.LogWarning("[City] " + noGeometry + " building(s) have no mesh to fit a "
                                 + "collider to and are still walk-through.");
        }

        static bool HasBlockingCollider(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c.enabled && !c.isTrigger) return true;
            return false;
        }

        // Collected while building, consumed once at the end.
        static List<Renderer> _nightLights = new List<Renderer>();
        static int _packBuildings;
        static int _boxBuildings;
        static int _landmarks;

        // Reused by the lot fitter so choosing a building does not allocate 324 times.
        static readonly List<int> _candidates = new List<int>();

        /// <summary>Cap on one-off buildings across the whole city.</summary>
        const int LandmarkBudget = 12;

        /// <summary>
        /// Can this model stand on a lot of this size, and at what scale?
        ///
        /// Tries the footprint both ways round and keeps the orientation that allows the
        /// larger scale, because a 25 x 15 m house fits a 16 x 26 m plot only sideways.
        /// The scale is then clamped into the model's own allowed range: below its minimum a
        /// building's windows and doors stop reading at human height, and above its maximum
        /// it is visibly bigger than the artist drew.
        /// </summary>
        static bool Fits(EnvironmentCatalog.BuildingModel model, float lotWidth, float lotDepth,
                         out float scale, out bool sideways)
        {
            scale = 0f;
            sideways = false;

            if (model.Kind == BuildingKind.Landmark && _landmarks >= LandmarkBudget) return false;
            if (!EnvironmentCatalog.Footprint(model.Path, out Vector3 size, out _)) return false;
            if (size.x < 0.01f || size.z < 0.01f) return false;

            float straight = Mathf.Min(lotWidth / size.x, lotDepth / size.z);
            float turned = Mathf.Min(lotWidth / size.z, lotDepth / size.x);

            sideways = turned > straight;
            float best = Mathf.Max(straight, turned);

            if (best < model.MinScale) return false;
            scale = Mathf.Min(best, model.MaxScale);
            return true;
        }

        /// <summary>
        /// What kind of building this district puts on a plot.
        ///
        /// Weights rather than a hard rule, so a corner shop can turn up downtown and a block
        /// of flats can turn up in the suburbs. A district built from one kind reads as a
        /// texture; a district built mostly from one kind reads as a place.
        /// </summary>
        static BuildingKind PickKind(District district, System.Random rng)
        {
            double r = rng.NextDouble();
            switch (district)
            {
                case District.Downtown:
                    if (r < 0.58) return BuildingKind.Tower;
                    if (r < 0.90) return BuildingKind.Block;
                    if (r < 0.96) return BuildingKind.Shop;
                    return BuildingKind.Landmark;

                case District.Midtown:
                    if (r < 0.40) return BuildingKind.Block;
                    if (r < 0.78) return BuildingKind.Shop;
                    if (r < 0.94) return BuildingKind.Tower;
                    return BuildingKind.Landmark;

                default:
                    if (r < 0.60) return BuildingKind.House;
                    if (r < 0.90) return BuildingKind.Shop;
                    if (r < 0.98) return BuildingKind.Block;
                    return BuildingKind.Landmark;
            }
        }

        // ------------------------------------------------------------------- roads

        static void BuildRoads(GameObject parent, Mats m, float ox, float oz, float span, float y)
        {
            for (int i = 0; i <= Blocks; i++)
            {
                float offset = i * Pitch + RoadWidth * 0.5f;

                // Roads running north-south.
                Box(parent, "Road_NS_" + i, m.Road,
                    new Vector3(ox + offset, y + RoadSurfaceY, oz + span * 0.5f),
                    new Vector3(RoadWidth, 0.12f, span + RoadWidth), false);

                // Roads running east-west.
                Box(parent, "Road_EW_" + i, m.Road,
                    new Vector3(ox + span * 0.5f, y + RoadSurfaceY, oz + offset),
                    new Vector3(span + RoadWidth, 0.12f, RoadWidth), false);
            }

            BuildLaneMarkings(parent, m, ox, oz, span, y);
        }

        /// <summary>
        /// Dashed centre lines. Cosmetic, but without them the roads read as grey strips
        /// rather than streets, and Phase 2 traffic needs the lane centres marked anyway.
        ///
        /// <b>All the dashes on one road line are a single mesh, not a box each.</b> At the
        /// Phase 1 grid size a box per dash was around 650 renderers, which was tolerable.
        /// The Phase 11 grid is 3.24x the area and 10 road lines per axis, which would have
        /// been about 2,100 renderers spent on paint -- more than the entire rest of the city
        /// put together, and the single largest line item in the scene. Batching them per road
        /// gives 20 renderers for the same pixels. See DECISIONS.md D27.
        /// </summary>
        static void BuildLaneMarkings(GameObject parent, Mats m, float ox, float oz, float span, float y)
        {
            var marks = NewChild(parent, "LaneMarkings");

            const float dash = 3.0f;
            const float gap = 3.5f;
            const float halfWidth = 0.16f;
            float step = dash + gap;
            float top = y + RoadSurfaceY + 0.07f;

            for (int i = 0; i <= Blocks; i++)
            {
                float offset = i * Pitch + RoadWidth * 0.5f;

                BuildDashStrip(marks, m.LaneLine, "LaneLine_NS_" + i, top,
                               new Vector3(ox + offset, 0f, oz), Vector3.forward,
                               span, dash, step, halfWidth);

                BuildDashStrip(marks, m.LaneLine, "LaneLine_EW_" + i, top,
                               new Vector3(ox, 0f, oz + offset), Vector3.right,
                               span, dash, step, halfWidth);
            }
        }

        /// <summary>
        /// One road's worth of dashes as a single flat mesh: two triangles per dash, all in
        /// one renderer. Built in local space around <paramref name="start"/> so the object's
        /// transform stays at scale 1 and static batching can fold it in with its neighbours.
        /// </summary>
        static void BuildDashStrip(GameObject parent, Material mat, string name, float top,
                                   Vector3 start, Vector3 along, float length,
                                   float dash, float step, float halfWidth)
        {
            Vector3 across = new Vector3(along.z, 0f, -along.x);

            int count = Mathf.FloorToInt(length / step) + 1;
            var verts = new List<Vector3>(count * 4);
            var tris = new List<int>(count * 6);
            var uvs = new List<Vector2>(count * 4);

            for (int d = 0; d < count; d++)
            {
                float s = d * step;
                if (s + dash > length) break;

                Vector3 a = along * s + across * -halfWidth;
                Vector3 b = along * s + across * halfWidth;
                Vector3 c = along * (s + dash) + across * halfWidth;
                Vector3 e = along * (s + dash) + across * -halfWidth;

                int v = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(e);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));

                // Wound counter-clockwise seen from above, so the paint faces the sky.
                tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
                tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
            }

            if (verts.Count == 0) return;

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(EnvironmentCatalog.DetailPrefix + name);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = new Vector3(start.x, top, start.z);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // ------------------------------------------------------------------ blocks

        static void BuildBlocks(GameObject builds, GameObject walks, GameObject park,
                                GameObject props, Mats m, System.Random rng,
                                float ox, float oz, float y)
        {
            for (int bx = 0; bx < Blocks; bx++)
            {
                for (int bz = 0; bz < Blocks; bz++)
                {
                    float cx = ox + RoadWidth + bx * Pitch + BlockSize * 0.5f;
                    float cz = oz + RoadWidth + bz * Pitch + BlockSize * 0.5f;

                    // Sidewalk slab covers the whole block; buildings sit inset from its edge.
                    Box(walks, "Walk_" + bx + "_" + bz, m.Sidewalk,
                        new Vector3(cx, y + SidewalkHeight * 0.5f, cz),
                        new Vector3(BlockSize, SidewalkHeight, BlockSize), true);

                    if (IsParkBlock(bx, bz))
                        BuildPark(park, m, rng, cx, cz, y + SidewalkHeight);
                    else
                        BuildLot(builds, m, rng, cx, cz, y + SidewalkHeight, DistrictOf(bx, bz));

                    BuildStreetFurniture(props, rng, cx, cz, y + SidewalkHeight);
                }
            }
        }

        /// <summary>
        /// Dresses the kerb of one block: lampposts, a bench or a bus stop, bins and bushes.
        ///
        /// Placement is on the sidewalk band between the building line and the kerb, and it
        /// walks the block's perimeter rather than scattering over the whole slab, so nothing
        /// ends up embedded in a wall or standing in the middle of a carriageway.
        /// </summary>
        static void BuildStreetFurniture(GameObject parent, System.Random rng,
                                         float cx, float cz, float y)
        {
            // Mid-way between the block edge and the building inset: clear of both.
            float band = BlockSize * 0.5f - SidewalkInset * 0.5f;

            // Four edges, a handful of stations along each. Corners are skipped -- a lamppost
            // on a corner is where the crosswalk paint and the traffic signal already are.
            for (int edge = 0; edge < 4; edge++)
            {
                // Outward normal of this edge, and the direction along it.
                Vector3 outward = edge switch
                {
                    0 => Vector3.forward,
                    1 => Vector3.right,
                    2 => Vector3.back,
                    _ => Vector3.left,
                };
                Vector3 along = new Vector3(outward.z, 0f, -outward.x);

                const int stations = 4;
                for (int s = 0; s < stations; s++)
                {
                    // -0.3 .. +0.3 of the block, so the corners stay clear.
                    float t = Mathf.Lerp(-0.30f, 0.30f, (s + 0.5f) / stations);
                    Vector3 at = new Vector3(cx, y, cz) + outward * band + along * (BlockSize * t);

                    // Face the road.
                    float yaw = Quaternion.LookRotation(outward, Vector3.up).eulerAngles.y;

                    double roll = rng.NextDouble();
                    if (roll < 0.34)
                    {
                        var furniture = EnvironmentCatalog.StreetFurniture;
                        EnvironmentCatalog.Place(parent, furniture[rng.Next(furniture.Length)],
                                                 at, yaw + 180f, rng);
                    }
                    else if (roll < 0.55)
                    {
                        var clutter = EnvironmentCatalog.StreetClutter;
                        EnvironmentCatalog.Place(parent, clutter[rng.Next(clutter.Length)],
                                                 at, (float)rng.NextDouble() * 360f, rng);
                    }
                    else if (roll < 0.75)
                    {
                        var bushes = EnvironmentCatalog.CityBushes;
                        EnvironmentCatalog.Place(parent, bushes[rng.Next(bushes.Length)],
                                                 at, (float)rng.NextDouble() * 360f, rng);
                    }
                    // The remaining quarter is left empty. A prop at every station reads as a
                    // pattern; gaps are what make it read as a street.
                }
            }
        }

        /// <summary>
        /// Fills one city block with buildings from the asset packs.
        ///
        /// <b>Phase 11 inverted this.</b> It used to build a primitive box on every lot and
        /// upgrade roughly a third of them to a modelled building (D11), because the only pack
        /// available shipped four models at 4.5-8.6k triangles. With SimplePoly City's 40
        /// models at 132-1,568 triangles a modelled building costs about what a box costs, so
        /// the pack is now the default and the box is a fallback that should essentially never
        /// fire. <see cref="_boxBuildings"/> is logged at the end precisely so a regression
        /// here is visible rather than silent.
        ///
        /// Lot size follows the district: downtown is quartered into big plots that suit a
        /// tower, the outskirts are cut into ninths that suit a house. Fitting a 17 m house
        /// into a 28 m plot is what made Phase 9's pack buildings look marooned in the middle
        /// of their lots.
        /// </summary>
        static void BuildLot(GameObject parent, Mats m, System.Random rng,
                             float cx, float cz, float y, District district)
        {
            float usable = BlockSize - SidewalkInset * 2f;

            // Cells per side. Suburbs get a finer grain because their buildings are smaller.
            int n = district == District.Suburb ? 3 : 2;

            // Unequal splits so a block does not read as a spreadsheet. Sums to 1.
            float[] cuts = RandomCuts(n, rng);

            float originOffset = -usable * 0.5f;
            float runZ = 0f;

            for (int gz = 0; gz < n; gz++)
            {
                float depth = usable * cuts[gz];
                float runX = 0f;

                for (int gx = 0; gx < n; gx++)
                {
                    float width = usable * cuts[(gx + gz) % n];   // rotate so rows differ

                    // Occasional gap: a courtyard, a yard, a car park. Without these the
                    // block line is unbroken and the city reads as extruded wallpaper.
                    bool skip = rng.NextDouble() < (district == District.Downtown ? 0.10 : 0.16);

                    if (!skip)
                    {
                        float inset = Mathf.Lerp(1.2f, 3.2f, (float)rng.NextDouble());
                        float w = Mathf.Max(5f, width - inset);
                        float d = Mathf.Max(5f, depth - inset);

                        var at = new Vector3(
                            cx + originOffset + runX + width * 0.5f,
                            y,
                            cz + originOffset + runZ + depth * 0.5f);

                        if (!PlacePackBuilding(parent, rng, at, w, d, district))
                            PlaceBoxBuilding(parent, m, rng, at, w, d);
                    }

                    runX += width;
                }
                runZ += depth;
            }
        }

        /// <summary>N fractions that sum to 1, none of them tiny.</summary>
        static float[] RandomCuts(int n, System.Random rng)
        {
            var cuts = new float[n];
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                cuts[i] = Mathf.Lerp(0.8f, 1.2f, (float)rng.NextDouble());
                total += cuts[i];
            }
            for (int i = 0; i < n; i++) cuts[i] /= total;
            return cuts;
        }

        /// <summary>
        /// The last-resort primitive block, kept only so a missing prefab degrades to a
        /// building-shaped object rather than a hole in the street. Phase 11 expects this to
        /// fire zero times; the count is logged so it cannot fail quietly.
        /// </summary>
        static void PlaceBoxBuilding(GameObject parent, Mats m, System.Random rng,
                                     Vector3 at, float w, float d)
        {
            float h = Mathf.Lerp(9f, 30f, Mathf.Pow((float)rng.NextDouble(), 1.7f));
            var mat = m.Buildings[rng.Next(m.Buildings.Length)];

            Box(parent, "Bld", mat, new Vector3(at.x, at.y + h * 0.5f, at.z),
                new Vector3(w, h, d), true);
            _boxBuildings++;

            // A darker parapet band gives the roofline a silhouette instead of a flat cut.
            // Parented to the block root, never to the building: Box sets localScale, so a
            // child of an already-scaled box would inherit that scale and multiply out to
            // a slab hundreds of metres wide.
            if (h > 14f)
            {
                Box(parent, "Parapet", m.Parapet,
                    new Vector3(at.x, at.y + h + 0.55f, at.z),
                    new Vector3(w + 0.6f, 1.1f, d + 0.6f), false);
            }
        }

        /// <summary>
        /// Drops one Cartoon City building onto a lot, scaled to fit its footprint.
        ///
        /// The scale is uniform and derived from whichever of the two horizontal axes is the
        /// tighter fit, so a 30 x 18 m tower in a 22 m lot shrinks rather than being squashed
        /// into a shape the artist never drew. Yaw is quantised to 90 degrees so the footprint
        /// stays square to the lot and cannot overhang the pavement.
        ///
        /// Returns false when the lot is too small to take one at a sensible scale, leaving the
        /// caller to fall back to a primitive block.
        /// </summary>
        static bool PlacePackBuilding(GameObject parent, System.Random rng,
                                      Vector3 at, float lotWidth, float lotDepth,
                                      District district)
        {
            var kind = PickKind(district, rng);

            // Collect every model of that kind the lot can actually take, then choose among
            // them at random. Choosing a model first and testing it second -- which is what
            // Phase 9 did -- means a lot that happens to draw a big model gets nothing at all,
            // which is how a third of the city ended up as primitive boxes.
            _candidates.Clear();
            for (int i = 0; i < EnvironmentCatalog.Buildings.Length; i++)
            {
                var candidate = EnvironmentCatalog.Buildings[i];
                if (candidate.Kind != kind) continue;
                if (Fits(candidate, lotWidth, lotDepth, out _, out _)) _candidates.Add(i);
            }

            // Nothing of the preferred kind fits this plot: widen to anything that does,
            // smallest-first, rather than falling back to a box.
            if (_candidates.Count == 0)
            {
                for (int i = 0; i < EnvironmentCatalog.Buildings.Length; i++)
                {
                    if (EnvironmentCatalog.Buildings[i].Kind == BuildingKind.Landmark) continue;
                    if (Fits(EnvironmentCatalog.Buildings[i], lotWidth, lotDepth, out _, out _))
                        _candidates.Add(i);
                }
            }
            if (_candidates.Count == 0) return false;

            var model = EnvironmentCatalog.Buildings[_candidates[rng.Next(_candidates.Count)]];
            if (!Fits(model, lotWidth, lotDepth, out float scale, out bool sideways)) return false;

            if (model.Kind == BuildingKind.Landmark) _landmarks++;

            // Two of the four quarter-turns preserve the orientation that fits; pick between
            // them so the street does not have every building facing the same way.
            int quarterTurns = (sideways ? 1 : 0) + (rng.Next(2) * 2);
            float yaw = quarterTurns * 90f;

            var prop = new EnvironmentCatalog.Prop(model.Path, scale, scale, solid: true);
            var go = EnvironmentCatalog.Place(parent, prop, at, yaw, rng);
            if (go == null) return false;

            go.name = "Bld_Pack";
            _packBuildings++;

            if (!string.IsNullOrEmpty(model.NightLightPath))
            {
                // The overlay shares the building's transform exactly; it is a shell of lit
                // windows designed to sit just inside the facade.
                var shell = new EnvironmentCatalog.Prop(model.NightLightPath, scale, scale, solid: false);
                var lit = EnvironmentCatalog.Place(parent, shell, at, yaw, rng);
                if (lit != null)
                {
                    // Not "Detail_": these have to stay visible at any distance the building
                    // itself is visible at, or the skyline goes dark in patches at night.
                    lit.name = "NightLight";
                    _nightLights.AddRange(lit.GetComponentsInChildren<Renderer>(true));
                }
            }

            return true;
        }

        static void BuildPark(GameObject parent, Mats m, System.Random rng, float cx, float cz, float y)
        {
            float size = BlockSize - SidewalkInset * 2f;

            Box(parent, "ParkGround", m.Grass,
                new Vector3(cx, y + 0.06f, cz),
                new Vector3(size, 0.12f, size), false);

            // Crossing paths.
            Box(parent, "Path_NS", m.Sidewalk,
                new Vector3(cx, y + 0.10f, cz), new Vector3(3.2f, 0.14f, size), false);
            Box(parent, "Path_EW", m.Sidewalk,
                new Vector3(cx, y + 0.10f, cz), new Vector3(size, 0.14f, 3.2f), false);

            // Anything standing in the park stands on the grass slab, not on the sidewalk
            // underneath it. Twelve centimetres does not sound like much, but it is the
            // difference between a trunk meeting the grass and a trunk emerging from it.
            float grassTop = y + 0.12f;

            // Modelled trees from the atmospheric pack, one renderer each, in place of the
            // cylinder-plus-sphere pair the park used to use -- half the renderers for a tree
            // that actually looks like one.
            for (int i = 0; i < 14; i++)
            {
                float px = cx + Mathf.Lerp(-size * 0.42f, size * 0.42f, (float)rng.NextDouble());
                float pz = cz + Mathf.Lerp(-size * 0.42f, size * 0.42f, (float)rng.NextDouble());

                if (Mathf.Abs(px - cx) < 3f || Mathf.Abs(pz - cz) < 3f) continue;   // keep paths clear

                var trees = EnvironmentCatalog.ParkTrees;
                EnvironmentCatalog.Place(parent, trees[rng.Next(trees.Length)],
                                         new Vector3(px, grassTop, pz),
                                         (float)rng.NextDouble() * 360f, rng);
            }

            // Ground clutter between the trees. Collider-free, so it is distance-culled.
            for (int i = 0; i < 22; i++)
            {
                float px = cx + Mathf.Lerp(-size * 0.45f, size * 0.45f, (float)rng.NextDouble());
                float pz = cz + Mathf.Lerp(-size * 0.45f, size * 0.45f, (float)rng.NextDouble());
                if (Mathf.Abs(px - cx) < 2.4f || Mathf.Abs(pz - cz) < 2.4f) continue;

                var clutter = rng.NextDouble() < 0.6 ? EnvironmentCatalog.Grass
                                                     : EnvironmentCatalog.CityBushes;
                EnvironmentCatalog.Place(parent, clutter[rng.Next(clutter.Length)],
                                         new Vector3(px, grassTop, pz),
                                         (float)rng.NextDouble() * 360f, rng);
            }
        }

        // ------------------------------------------------------------- primitives

        /// <summary>
        /// Places a scaled cube in world space. The parent must be unscaled -- size is applied
        /// as localScale, so a scaled parent would multiply into it.
        /// </summary>
        static GameObject Box(GameObject parent, string name, Material mat,
                              Vector3 center, Vector3 size, bool collide)
        {
            Vector3 parentScale = parent.transform.lossyScale;
            if (Mathf.Abs(parentScale.x - 1f) > 0.001f
                || Mathf.Abs(parentScale.y - 1f) > 0.001f
                || Mathf.Abs(parentScale.z - 1f) > 0.001f)
            {
                Debug.LogError("[City] Box parent '" + parent.name + "' has scale " + parentScale
                               + "; children would inherit it. Parent to an unscaled container.");
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.position = center;
            go.transform.localScale = size;

            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Flat decals (roads, markings) get no collider: the terrain underneath already
            // blocks the player, and every extra collider costs broadphase time.
            var col = go.GetComponent<BoxCollider>();
            if (!collide) Object.DestroyImmediate(col);

            return go;
        }

        static GameObject NewChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static void MarkStaticRecursive(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.BatchingStatic
                    | StaticEditorFlags.OccluderStatic
                    | StaticEditorFlags.OccludeeStatic
                    | StaticEditorFlags.ContributeGI);
        }

        // ------------------------------------------------------------- materials

        class Mats
        {
            public Material Road, LaneLine, Sidewalk, Parapet, Grass;
            public Material[] Buildings;
        }

        static Mats LoadMaterials()
        {
            Directory.CreateDirectory(MatDir);

            var m = new Mats
            {
                Road = Mat("Road", new Color(0.13f, 0.13f, 0.145f), 0.18f),
                LaneLine = Mat("LaneLine", new Color(0.88f, 0.86f, 0.72f), 0.10f),
                Sidewalk = Mat("Sidewalk", new Color(0.58f, 0.57f, 0.55f), 0.08f),
                Parapet = Mat("Parapet", new Color(0.28f, 0.28f, 0.30f), 0.10f),
                Grass = Mat("ParkGrass", new Color(0.28f, 0.42f, 0.20f), 0.05f),
            };

            m.Buildings = new[]
            {
                Mat("Bld_Sand",   new Color(0.72f, 0.68f, 0.60f), 0.22f),
                Mat("Bld_Slate",  new Color(0.44f, 0.47f, 0.52f), 0.30f),
                Mat("Bld_Brick",  new Color(0.52f, 0.34f, 0.28f), 0.14f),
                Mat("Bld_Cream",  new Color(0.80f, 0.76f, 0.66f), 0.20f),
                Mat("Bld_Teal",   new Color(0.33f, 0.45f, 0.47f), 0.34f),
                Mat("Bld_Glass",  new Color(0.36f, 0.46f, 0.55f), 0.66f),
            };

            AssetDatabase.SaveAssets();
            return m;
        }

        static Material Mat(string name, Color color, float smoothness)
        {
            string path = MatDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
