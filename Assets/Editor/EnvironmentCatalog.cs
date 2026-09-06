using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// The prop tables the world builders draw from, and the one function that puts a pack
    /// prefab into the world correctly.
    ///
    /// "Correctly" is doing real work here. The two environment packs disagree about almost
    /// everything: Cartoon City ships MeshColliders on 5-8k triangle buildings, Palmov Island
    /// ships tidy BoxColliders; pivots are on the ground in most prefabs and 16 cm below it in
    /// one; scales range from a 20-triangle tree to a 22-metre palm. <see cref="Place"/> is
    /// where all of that is normalised, so no builder has to know which pack a prop came from.
    /// </summary>
    public static class EnvironmentCatalog
    {
        const string City = "Assets/ithappy/Cartoon_City_Free/Prefabs";
        const string Palmov = "Assets/Palmov Island/Low Poly Atmospheric Locations Pack/Prefabs";
        const string Simple = "Assets/SimplePoly City - Low Poly Assets/Prefab";

        /// <summary>
        /// Prefix that marks decoration <see cref="PerformanceSetup"/> is allowed to
        /// distance-cull. Everything carrying it is built collider-free by <see cref="Place"/>,
        /// which is what makes culling it safe -- you cannot walk into what is not there.
        /// </summary>
        public const string DetailPrefix = "Detail_";

        /// <summary>One placeable prop: where it lives and how big it is allowed to be.</summary>
        public struct Prop
        {
            public string Path;
            public float MinScale;
            public float MaxScale;
            /// <summary>True keeps a collider and keeps it drawn at all distances.</summary>
            public bool Solid;

            public Prop(string path, float min, float max, bool solid)
            {
                Path = path; MinScale = min; MaxScale = max; Solid = solid;
            }
        }

        static Prop C(string sub, float min, float max, bool solid) =>
            new Prop(City + "/" + sub + ".prefab", min, max, solid);

        static Prop P(string sub, float min, float max, bool solid) =>
            new Prop(Palmov + "/" + sub + ".prefab", min, max, solid);

        // --------------------------------------------------------------- buildings

        /// <summary>
        /// What a building is for. <see cref="CityBuilder"/> picks a district's mix from these
        /// so the skyline changes as you walk out from the centre instead of being one texture
        /// of towers everywhere.
        /// </summary>
        public enum BuildingKind
        {
            /// <summary>Detached, two storeys, garden-sized footprint. Outskirts.</summary>
            House,
            /// <summary>Single-unit commercial frontage. Anywhere, but the midtown staple.</summary>
            Shop,
            /// <summary>Mid-rise apartment or office. Midtown and inner suburbs.</summary>
            Block,
            /// <summary>High-rise. Downtown only.</summary>
            Tower,
            /// <summary>One-off with a distinctive silhouette. Rationed, one per district.</summary>
            Landmark,
        }

        /// <summary>
        /// One building model: where it lives, what it is for, and how far it may be scaled.
        ///
        /// <b>Phase 11 replaced the four-model table this used to be.</b> Phase 9 had only the
        /// four Cartoon City buildings at 4.5-8.6k triangles each, which is why it could not
        /// afford to put one on every lot and mixed them into primitive boxes at roughly a
        /// third of lots (D11). SimplePoly City changes that arithmetic completely: 40 models,
        /// <b>132 to 1,568 triangles</b>, one renderer and one material each. A modelled
        /// building is now within a rounding error of a primitive box, so there is no longer
        /// any reason to place a box. See DECISIONS.md D24.
        ///
        /// The scale range is per-model rather than global because the packs disagree about
        /// size: a SimplePoly shop is drawn at roughly true scale, while the Cartoon City
        /// towers are drawn large and look wrong above about 0.8.
        /// </summary>
        public struct BuildingModel
        {
            public string Path;
            public string NightLightPath;   // null when the pack ships none
            public BuildingKind Kind;
            public float MinScale;
            public float MaxScale;

            public BuildingModel(string path, BuildingKind kind, float min, float max,
                                 string nightLight = null)
            {
                Path = path; Kind = kind; MinScale = min; MaxScale = max; NightLightPath = nightLight;
            }
        }

        static BuildingModel S(string sub, BuildingKind k, float min, float max) =>
            new BuildingModel(Simple + "/Buildings/" + sub + ".prefab", k, min, max);

        static BuildingModel PH(string sub, BuildingKind k, float min, float max) =>
            new BuildingModel(Palmov + "/Houses/" + sub + ".prefab", k, min, max);

        /// <summary>
        /// Every building the city can draw from. Footprints in the comments are measured, not
        /// estimated -- see PHASE11.md for the full table.
        /// </summary>
        public static readonly BuildingModel[] Buildings =
        {
            // --- SimplePoly City: 40 models, 132-1,568 tris, 1 renderer each -------------
            // Towers. 13x12 footprint, 17-25 m tall.
            S("Building Sky_big_color01",    BuildingKind.Tower, 0.85f, 1.45f),
            S("Building Sky_big_color02",    BuildingKind.Tower, 0.85f, 1.45f),
            S("Building Sky_big_color03",    BuildingKind.Tower, 0.85f, 1.45f),
            S("Building Sky_small_color01",  BuildingKind.Tower, 0.85f, 1.35f),
            S("Building Sky_small_color02",  BuildingKind.Tower, 0.85f, 1.35f),
            S("Building Sky_small_color03",  BuildingKind.Tower, 0.85f, 1.35f),

            // Mid-rise. 16x13, 19 m.
            S("Building_Residential_color01", BuildingKind.Block, 0.85f, 1.30f),
            S("Building_Residential_color02", BuildingKind.Block, 0.85f, 1.30f),
            S("Building_Residential_color03", BuildingKind.Block, 0.85f, 1.30f),
            S("Building_Clothing",            BuildingKind.Block, 0.85f, 1.25f),
            S("Building_Fruits  Shop",        BuildingKind.Block, 0.85f, 1.25f),   // sic: two spaces in the pack
            S("Building_Bar",                 BuildingKind.Block, 0.90f, 1.40f),
            S("Building_Restaurant",          BuildingKind.Block, 0.90f, 1.40f),

            // Commercial frontages. 11-24 m wide, 6.5-9.8 m tall.
            S("Building_Auto Service",  BuildingKind.Shop, 0.85f, 1.20f),
            S("Building_Bakery",        BuildingKind.Shop, 0.85f, 1.20f),
            S("Building_Books Shop",    BuildingKind.Shop, 0.85f, 1.20f),
            S("Building_Chicken Shop",  BuildingKind.Shop, 0.85f, 1.25f),
            S("Building_Coffee Shop",   BuildingKind.Shop, 0.85f, 1.15f),
            S("Building_Drug Store",    BuildingKind.Shop, 0.85f, 1.25f),
            S("Building_Fast Food",     BuildingKind.Shop, 0.85f, 1.25f),
            S("Building_Gift Shop",     BuildingKind.Shop, 0.85f, 1.25f),
            S("Building_Music Store",   BuildingKind.Shop, 0.85f, 1.25f),
            S("Building_Pizza",         BuildingKind.Shop, 0.85f, 1.20f),
            S("Building_Shoes Shop",    BuildingKind.Shop, 0.85f, 1.20f),
            S("Building_Super Market",  BuildingKind.Shop, 0.85f, 1.15f),

            // Detached housing. 17-25 m wide, 8-10 m tall.
            S("Building_House_01_color01", BuildingKind.House, 0.80f, 1.10f),
            S("Building_House_01_color02", BuildingKind.House, 0.80f, 1.10f),
            S("Building_House_01_color03", BuildingKind.House, 0.80f, 1.10f),
            S("Building_House_02_color01", BuildingKind.House, 0.80f, 1.10f),
            S("Building_House_02_color02", BuildingKind.House, 0.80f, 1.10f),
            S("Building_House_02_color03", BuildingKind.House, 0.80f, 1.10f),
            S("Building_House_03_color01", BuildingKind.House, 0.80f, 1.10f),
            S("Building_House_03_color02", BuildingKind.House, 0.80f, 1.10f),
            S("Building_House_03_color03", BuildingKind.House, 0.80f, 1.10f),
            S("Building_House_04_color01", BuildingKind.House, 0.75f, 1.00f),
            S("Building_House_04_color02", BuildingKind.House, 0.75f, 1.00f),
            S("Building_House_04_color03", BuildingKind.House, 0.75f, 1.00f),

            // Landmarks. Rationed by CityBuilder -- a stadium on every block is not a skyline.
            S("Building_Factory",     BuildingKind.Landmark, 0.80f, 1.05f),
            S("Building_Gas Station", BuildingKind.Landmark, 0.85f, 1.05f),
            S("Building_Stadium",     BuildingKind.Landmark, 0.55f, 0.75f),   // 35x45 m, 6.7k tris

            // --- Palmov Island houses: one shared material, so these batch together --------
            PH("Buildings/building",   BuildingKind.Block, 0.85f, 1.20f),
            PH("Buildings/building 2", BuildingKind.Block, 0.85f, 1.30f),
            PH("Buildings/building 3", BuildingKind.Block, 0.85f, 1.30f),
            PH("Buildings/building 4", BuildingKind.Block, 0.85f, 1.30f),
            // Palmov's "Wooden winter houses" are deliberately NOT here. They are log cabins
            // with snow-laden roofs, and this city is a beach town with palm trees on the
            // seafront -- placed in the suburbs they read as a bug, not as variety.
            PH("japanes temple",   BuildingKind.Landmark, 0.85f, 1.15f),   // sic: pack spelling
            PH("railway station",  BuildingKind.Landmark, 0.85f, 1.10f),

            // --- Cartoon City: kept for the night-lit skyline ------------------------------
            // These are the only buildings in any pack that ship an emissive window overlay,
            // which is what NightLights drives after dark. Dropping them would leave the
            // whole city dark at night, so they stay in the downtown mix despite costing
            // 4.5-8.6k triangles each -- roughly six SimplePoly towers apiece.
            new BuildingModel(City + "/Buildings/Eco_Building_Grid.prefab", BuildingKind.Tower,
                              0.55f, 0.80f, City + "/Buildings/Eco_Building_Grid_NightLight.prefab"),
            new BuildingModel(City + "/Buildings/Eco_Building_Terrace.prefab", BuildingKind.Tower,
                              0.55f, 0.80f, City + "/Buildings/Eco_Building_Terrace_NightLight.prefab"),
            new BuildingModel(City + "/Buildings/Regular_Building_TwistedTower_Large.prefab",
                              BuildingKind.Tower, 0.55f, 0.80f,
                              City + "/Buildings/Regular_Building_TwistedTower_Large_NightLight.prefab"),
            new BuildingModel(City + "/Buildings/Eco_Building_Slope.prefab", BuildingKind.Tower,
                              0.55f, 0.80f),
        };

        // ------------------------------------------------------------------- props

        /// <summary>Things on a pavement that a car should hit. Kept, never culled.</summary>
        public static readonly Prop[] StreetFurniture =
        {
            P("Environment/lamppost",            1.0f, 1.0f, true),
            P("Environment/Benchs/bench",        1.0f, 1.0f, true),
            P("Environment/Benchs/bench 2",      1.0f, 1.0f, true),
            C("Props/Bus_Stop_02",               1.0f, 1.0f, true),
        };

        /// <summary>Small clutter. Collider-free by design, so it can be distance-culled.</summary>
        public static readonly Prop[] StreetClutter =
        {
            C("Props/Trash_Can_06", 0.85f, 1.05f, false),
            C("Props/Trash_Can_08", 0.85f, 1.05f, false),
            C("Props/Trash_02",     0.9f,  1.1f,  false),
            C("Props/Trash_04",     0.9f,  1.1f,  false),
            P("Environment/trash can", 0.9f, 1.2f, false),
        };

        public static readonly Prop[] CityBushes =
        {
            C("Vegetation/Bush_06", 0.7f, 1.1f, false),
            C("Vegetation/Bush_07", 0.7f, 1.1f, false),
            C("Vegetation/Bush_10", 0.8f, 1.2f, false),
            P("Plants/shrub",       0.8f, 1.4f, false),
        };

        /// <summary>Park trees. Solid: a tree you can walk through reads as a bug.</summary>
        public static readonly Prop[] ParkTrees =
        {
            P("Trees/tree",              0.9f, 1.5f, true),
            P("Trees/tree 2",            1.0f, 1.6f, true),
            P("Trees/sakura tree",       0.7f, 1.1f, true),
            P("Trees/Autumn trees/autumn tree",       0.8f, 1.2f, true),
            P("Trees/Autumn trees/autumn tree 2",     0.8f, 1.2f, true),
            P("Trees/Fir trees/fir tree medium",   0.8f, 1.2f, true),
        };

        public static readonly Prop[] CoastTrees =
        {
            P("Trees/Palm trees/palm tree large",  0.9f, 1.4f, true),
            P("Trees/Palm trees/palm tree small",  1.0f, 1.6f, true),
            P("Trees/Palm trees/palm tree bent",   0.9f, 1.3f, true),
            P("Trees/Palm trees/palm tree tilted", 0.9f, 1.3f, true),
        };

        public static readonly Prop[] MountainTrees =
        {
            P("Trees/Fir trees/fir tree large",  0.9f, 1.3f, true),
            P("Trees/Fir trees/fir tree medium", 0.9f, 1.3f, true),
            P("Trees/Fir trees/fir tree small",  0.9f, 1.4f, true),
            P("Trees/Fir trees/fir tree bent",   0.9f, 1.2f, true),
            P("Trees/Fir trees/pine tree large", 0.8f, 1.2f, true),
        };

        public static readonly Prop[] Rocks =
        {
            P("Stones/Stones large gray blue/stone large gray blue",   0.8f, 1.6f, true),
            P("Stones/Stones large gray blue/stone large gray blue 2", 0.8f, 1.4f, true),
            P("Stones/Stones large brown/stone large brown",       0.8f, 1.4f, true),
            P("Stones/Stones large/stone large",             0.7f, 1.2f, true),
        };

        public static readonly Prop[] SmallRocks =
        {
            P("Stones/Stones small/stone small",    0.7f, 1.5f, false),
            P("Stones/Stones small/stone small 2",  0.7f, 1.5f, false),
            P("Stones/Stones small/stone small 3",  0.7f, 1.5f, false),
            P("Stones/Stones small/stone small 4",  0.7f, 1.5f, false),
        };

        public static readonly Prop[] Grass =
        {
            P("Plants/Grass/grass",          0.8f, 1.6f, false),
            P("Plants/Grass/grass 2",        0.8f, 1.6f, false),
            P("Plants/Grass/grass 3",        0.8f, 1.6f, false),
            P("Plants/Grass yellow/grass yellow",   0.8f, 1.6f, false),
            P("Plants/bush plant",     0.7f, 1.2f, false),
        };

        // ------------------------------------------------------------------ place

        /// <summary>
        /// Instantiates a prop, normalised for this project.
        ///
        /// Returns null when the prefab is missing, so a builder degrades to fewer props rather
        /// than to an exception halfway through generating the world.
        /// </summary>
        public static GameObject Place(GameObject parent, Prop prop, Vector3 position,
                                       float yaw, System.Random rng, float extraScale = 1f)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(prop.Path);
            if (source == null)
            {
                Debug.LogWarning("[Env] Missing prop " + prop.Path);
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            go.transform.SetParent(parent.transform, true);

            float scale = Mathf.Lerp(prop.MinScale, prop.MaxScale, (float)rng.NextDouble()) * extraScale;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;

            NormaliseColliders(go, prop.Solid, boxifyAbove: 400);

            if (!prop.Solid) go.name = DetailPrefix + go.name;

            MarkStatic(go);
            return go;
        }

        /// <summary>
        /// Makes a pack prefab's collision affordable, or removes it.
        ///
        /// Cartoon City ships MeshColliders, including on its buildings -- a 5,746 triangle
        /// static mesh collider per tower, ninety times over. A box round the same volume
        /// answers the same gameplay question ("can I walk through this?") for a fraction of
        /// the broadphase and bake cost.
        ///
        /// <b>A solid prop is guaranteed to end up with a collider.</b> This used to only
        /// rewrite colliders that were already there, which quietly meant <c>solid: true</c>
        /// promised nothing: a prefab shipping with none kept none. Of the 53 models in
        /// <see cref="Buildings"/>, 44 ship bare -- every SimplePoly City building and every
        /// Palmov house -- so 392 of the 417 buildings in the city were walk-through. See
        /// <see cref="EnsureSolid"/>.
        /// </summary>
        public static void NormaliseColliders(GameObject go, bool solid, int boxifyAbove)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
            {
                if (!solid) { Object.DestroyImmediate(col); continue; }

                // A trigger does not block anything, so it does not count as collision.
                // Left enabled -- something may be reading it -- but no longer mistaken for
                // the reason this prop is solid.
                if (col.isTrigger) continue;

                var mc = col as MeshCollider;
                if (mc == null) continue;
                if (mc.sharedMesh == null) { Object.DestroyImmediate(mc); continue; }
                if (mc.sharedMesh.triangles.Length / 3 <= boxifyAbove) continue;

                var mesh = mc.sharedMesh;
                var owner = mc.gameObject;
                Object.DestroyImmediate(mc);

                var box = owner.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
            }

            if (solid) EnsureSolid(go);
        }

        /// <summary>
        /// Gives a prop that blocks nothing a box that does, fitted to its own geometry.
        ///
        /// Returns true only when it had to add one, so callers can count what was broken.
        ///
        /// A single box for a whole building, rather than one per child mesh: it is the same
        /// answer <see cref="NormaliseColliders"/> already gives a heavy MeshCollider, it is
        /// one broadphase entry instead of a dozen, and for a low-poly block the difference
        /// from the true silhouette is a corner the player cannot walk into. Buildings here
        /// are not enterable -- <c>InteriorBuilder</c> owns interiors as separate rooms -- so
        /// a solid volume is the correct shape, not an approximation of one.
        ///
        /// <b>No Rigidbody.</b> Static level geometry with a Rigidbody would fall under
        /// gravity; a collider alone is what makes a wall a wall.
        /// </summary>
        public static bool EnsureSolid(GameObject go)
        {
            foreach (var existing in go.GetComponentsInChildren<Collider>(true))
                if (existing.enabled && !existing.isTrigger) return false;

            // Measured in the prop root's own local space, so the box inherits whatever scale
            // Place applied and a rotated child still contributes its real extent.
            var toLocal = go.transform.worldToLocalMatrix;
            bool any = false;
            var local = new Bounds();

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;

                // mesh.bounds, not renderer.bounds: the mesh's own AABB is available without
                // the renderer being enabled, and is not already baked into world space.
                var b = mesh.bounds;
                var m = toLocal * mf.transform.localToWorldMatrix;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);

                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }

            if (!any) return false;

            var fitted = go.AddComponent<BoxCollider>();
            fitted.center = local.center;
            fitted.size = local.size;
            fitted.isTrigger = false;
            return true;
        }

        public static void MarkStatic(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.BatchingStatic
                    | StaticEditorFlags.OccluderStatic
                    | StaticEditorFlags.OccludeeStatic);
        }

        // Measuring means loading the prefab and walking every MeshFilter in it. The lot
        // fitter asks about every candidate building for every lot in the city -- with a
        // 9x9 grid that is tens of thousands of queries against the same fifty prefabs.
        static readonly System.Collections.Generic.Dictionary<string, Bounds> _footprints =
            new System.Collections.Generic.Dictionary<string, Bounds>();

        /// <summary>Cached <see cref="MeasureFootprint"/>. Call this, not the uncached one.</summary>
        public static bool Footprint(string path, out Vector3 size, out Vector3 centre)
        {
            if (_footprints.TryGetValue(path, out var cached))
            {
                size = cached.size;
                centre = cached.center;
                return size.sqrMagnitude > 0.0001f;
            }

            bool ok = MeasureFootprint(path, out size, out centre);
            _footprints[path] = ok ? new Bounds(centre, size) : new Bounds(Vector3.zero, Vector3.zero);
            return ok;
        }

        /// <summary>Drops the cache. Call before a rebuild if prefabs may have been reimported.</summary>
        public static void ClearFootprintCache() => _footprints.Clear();

        /// <summary>Measures a prefab's footprint and height, in its own unscaled space.</summary>
        public static bool MeasureFootprint(string path, out Vector3 size, out Vector3 centre)
        {
            size = Vector3.zero;
            centre = Vector3.zero;

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) return false;

            var b = new Bounds();
            bool first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 toRoot = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Vector3 c = toRoot.MultiplyPoint3x4(mb.center);
                Vector3 e = toRoot.MultiplyVector(mb.extents);
                var part = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
                if (first) { b = part; first = false; } else b.Encapsulate(part);
            }

            if (first) return false;
            size = b.size;
            centre = b.center;
            return true;
        }
    }
}
