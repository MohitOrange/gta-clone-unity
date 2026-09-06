using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Builds the harbour: a beached ship to explore, a bridge, and piers over the water.
    ///
    /// An editor-time builder rather than runtime spawning, matching TrafficBuilder and
    /// NpcBuilder. These are fixed landmarks -- they belong in the scene, get baked into
    /// static batching and occlusion, and cost nothing at runtime. Spawning them on load
    /// would pay for them every session to place them in the same spot every time.
    ///
    /// <b>No art is generated.</b> Per CLAUDE.md §6, this only places prefabs that are already
    /// in the project: the Pirates pack's ship hulls and the Palmov Island bridge and pier.
    /// The one thing it does add is collision, because those prefabs ship as visual meshes
    /// with nothing to stand on, and item 6.2's agreed scope is "static explorable set-piece
    /// with basic collision" -- explorable being the operative word.
    /// </summary>
    public static class HarborBuilder
    {
        const string RootName = "Harbor";

        // Prefabs already in the project. Checked rather than assumed: an asset pack can be
        // removed, and a builder that silently places nothing is the BUG-019 shape of bug.
        const string ShipPrefab = "Assets/Scenes/CatBorg Studio/3D Pirates Lowpoly Pack/Prefabs/99_Ship_L1.prefab";
        const string WreckPrefab = "Assets/Palmov Island/Low Poly Atmospheric Locations Pack/Prefabs/Vehicles/destroyed ship.prefab";
        const string BridgePrefab = "Assets/Palmov Island/Low Poly Atmospheric Locations Pack/Prefabs/Environment/wooden bridge.prefab";
        const string PierPrefab = "Assets/Palmov Island/Low Poly Atmospheric Locations Pack/Prefabs/Environment/wooden pier.prefab";

        [MenuItem("Mini GTA/World/Build harbour", priority = 40)]
        public static void Build()
        {
            var water = Object.FindAnyObjectByType<WaterVolume>();
            if (water == null)
            {
                Debug.LogError("[Harbor] no WaterVolume in the scene; nothing to build against");
                return;
            }

            float sea = water.SeaLevel;
            Debug.Log("[Harbor] sea level = " + sea.ToString("F2"));

            var existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Debug.Log("[Harbor] removing the previous harbour so this is idempotent");
                Object.DestroyImmediate(existing);
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build harbour");

            // The shoreline is found rather than typed in, so the harbour still lands on the
            // coast if the terrain is ever regenerated.
            if (!TryFindShore(sea, out Vector3 shore, out Vector3 seaward))
            {
                Debug.LogError("[Harbor] could not find a shoreline; is the terrain present?");
                return;
            }
            Debug.Log("[Harbor] shore at " + shore.ToString("F1")
                      + "  seaward " + seaward.ToString("F2"));

            // Scales are not decoration.
            //
            // The source prefabs are small: 99_Ship_L1 measures 7.6 x 7.0 x 4.8 metres out of
            // the box, and the pier 5.3. A player capsule is about 1.8 m tall, so at native
            // scale the "explorable ship" is a rowboat you would step over, and 6.2 asks for
            // something to walk around inside. These multipliers bring the ship to roughly 34 m
            // -- small-galleon proportions -- and the piers to something a person can walk out
            // along. The audit prints final world sizes so this stays checkable rather than
            // asserted.
            int placed = 0;
            placed += Place(ShipPrefab, root.transform, "Ship_Explorable",
                            shore + seaward * 60f, Quaternion.LookRotation(-seaward), sea, 4.5f);
            placed += Place(WreckPrefab, root.transform, "Ship_Wreck",
                            shore + seaward * 130f + Perp(seaward) * 80f,
                            Quaternion.LookRotation(Perp(seaward)), sea, 2.2f);

            // Hulls have to sit *in* the water, not on it.
            //
            // These prefabs pivot at the bottom of the hull, so placing one at sea level puts
            // the entire vessel on the surface like a bath toy -- clearly visible in the first
            // capture, hovering with daylight under the keel. Draft is applied from measured
            // bounds rather than a fixed offset, because the two ships are different sizes and
            // are scaled differently.
            Draft(root.transform.Find("Ship_Explorable"), sea, 2.6f);
            Draft(root.transform.Find("Ship_Wreck"), sea, 5.5f);
            // Piers and the shore bridge are placed BELOW the waterline, not above it.
            //
            // These prefabs pivot at the bottom of their own bounds and model no submerged
            // pilings, so the posts simply stop where the pivot is. Placed at sea + 0.6 the
            // post ends hung 0.6 m clear of the water with daylight under them, and the
            // bridge at sea + 1.4 was 2.3 m above the seabed underneath it -- visible from the
            // beach as a structure standing on nothing. Sinking the base half a metre puts the
            // cut ends under the surface, where the water hides them, which is the same trick
            // Draft() already uses for the two hulls above.
            const float Piling = 0.5f;
            placed += Place(PierPrefab, root.transform, "Pier_Main",
                            shore + seaward * 22f, Quaternion.LookRotation(seaward), sea - Piling, 3f);
            placed += Place(PierPrefab, root.transform, "Pier_Side",
                            shore + seaward * 20f + Perp(seaward) * 46f,
                            Quaternion.LookRotation(seaward), sea - Piling, 3f);
            placed += Place(BridgePrefab, root.transform, "Bridge_Shore",
                            shore + seaward * 8f + Perp(seaward) * -42f,
                            Quaternion.LookRotation(Perp(seaward)), sea - Piling, 2.5f);

            Debug.Log("[Harbor] placed " + placed + " set-pieces");

            int colliders = AddCollision(root);
            Debug.Log("[Harbor] added " + colliders + " colliders so the set-pieces are walkable");

            MarkStatic(root);

            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            Debug.Log("[Harbor] scene saved");
        }

        static Vector3 Perp(Vector3 v) => Vector3.Cross(Vector3.up, v).normalized;

        /// <summary>
        /// Drops an object so the bottom of its visible bounds sits <paramref name="depth"/>
        /// metres below the waterline.
        /// </summary>
        static void Draft(Transform t, float sea, float depth)
        {
            if (t == null) return;

            var renderers = t.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            float wanted = sea - depth;
            float delta = wanted - b.min.y;

            t.position += Vector3.up * delta;
            Debug.Log("[Harbor]   " + t.name + " drafted " + delta.ToString("F2")
                      + " m so the keel sits " + depth.ToString("F1") + " m under");
        }

        /// <summary>
        /// Walks outward from the map centre until the ground drops below sea level, which is
        /// the shoreline, and reports which way is out to sea.
        /// </summary>
        static bool TryFindShore(float sea, out Vector3 shore, out Vector3 seaward)
        {
            shore = Vector3.zero;
            seaward = Vector3.forward;

            var terrain = Terrain.activeTerrain;
            Vector3 centre = terrain != null
                ? terrain.transform.position
                  + new Vector3(terrain.terrainData.size.x * 0.5f, 0f,
                                terrain.terrainData.size.z * 0.5f)
                : Vector3.zero;

            // Sixteen directions is enough to find a coast without being a search.
            for (int i = 0; i < 16; i++)
            {
                float a = i * (Mathf.PI * 2f / 16f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

                Vector3 lastAbove = Vector3.zero;
                bool haveAbove = false;

                for (float d = 40f; d < 1200f; d += 8f)
                {
                    Vector3 probe = centre + dir * d;
                    if (!Ground(probe, out Vector3 hit)) continue;

                    if (hit.y > sea + 0.5f) { lastAbove = hit; haveAbove = true; continue; }

                    if (haveAbove)
                    {
                        shore = lastAbove;
                        seaward = dir;
                        return true;
                    }
                    break;
                }
            }
            return false;
        }

        static bool Ground(Vector3 at, out Vector3 hit)
        {
            hit = at;
            if (Physics.Raycast(new Vector3(at.x, 400f, at.z), Vector3.down,
                                out RaycastHit rh, 800f, ~0, QueryTriggerInteraction.Ignore))
            {
                hit = rh.point;
                return true;
            }
            return false;
        }

        static int Place(string path, Transform parent, string name,
                         Vector3 position, Quaternion rotation, float y, float scale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning("[Harbor] prefab missing, skipped: " + path);
                return 0;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(new Vector3(position.x, y, position.z), rotation);
            if (!Mathf.Approximately(scale, 1f))
                go.transform.localScale = Vector3.one * scale;

            Debug.Log("[Harbor]   " + name + " at " + go.transform.position.ToString("F1"));
            return 1;
        }

        /// <summary>
        /// Gives every visible mesh something to stand on.
        ///
        /// The source prefabs are scenery: they have renderers and no colliders, so without
        /// this the ship is a hologram the player walks through. Non-convex MeshColliders are
        /// correct here precisely because these are static -- a convex hull would fill in the
        /// decks and railings and make the ship a solid block rather than something to walk
        /// around inside.
        /// </summary>
        static int AddCollision(GameObject root)
        {
            int added = 0;

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                if (filter.GetComponent<Collider>() != null) continue;

                var mc = filter.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                mc.convex = false;
                added++;
            }
            return added;
        }

        static void MarkStatic(GameObject root)
        {
            // NavigationStatic is deliberately absent: it is deprecated in Unity 6, and
            // navmesh source selection is done through NavMeshBuilder now.
            var flags = StaticEditorFlags.BatchingStatic
                        | StaticEditorFlags.OccluderStatic
                        | StaticEditorFlags.OccludeeStatic;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        /// <summary>Reports what is actually in the scene, for verification.</summary>
        [MenuItem("Mini GTA/World/Audit harbour", priority = 41)]
        public static void Audit()
        {
            var root = GameObject.Find(RootName);
            if (root == null) { Debug.LogWarning("[Harbor] no harbour in the scene"); return; }

            var water = Object.FindAnyObjectByType<WaterVolume>();
            float sea = water != null ? water.SeaLevel : 0f;

            var pieces = new List<Transform>();
            foreach (Transform child in root.transform) pieces.Add(child);

            Debug.Log("[Harbor] audit: " + pieces.Count + " set-pieces, sea level " + sea.ToString("F2"));

            foreach (var p in pieces)
            {
                var renderers = p.GetComponentsInChildren<Renderer>(true);
                var colliders = p.GetComponentsInChildren<Collider>(true);

                Bounds b = new Bounds(p.position, Vector3.zero);
                foreach (var r in renderers) b.Encapsulate(r.bounds);

                Debug.Log("[Harbor]   " + p.name
                          + "  pos " + p.position.ToString("F1")
                          + "  size " + b.size.ToString("F1")
                          + "  renderers " + renderers.Length
                          + "  colliders " + colliders.Length
                          + (colliders.Length == 0 ? "   <-- NOT WALKABLE" : ""));
            }
        }
    }
}
