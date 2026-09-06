using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Populates the world: the AI traffic spawner, crosswalk pedestrians, and the vehicles
    /// parked within reach of the player's spawn.
    /// </summary>
    public static class TrafficBuilder
    {
        const string PrefabDir = "Assets/Game/Prefabs";

        [MenuItem("Tools/Mini GTA/12. Populate Traffic", priority = 132)]
        public static void Populate()
        {
            var network = Object.FindAnyObjectByType<RoadNetwork>();
            if (network == null)
            {
                Debug.LogError("[Traffic] No RoadNetwork. Run 'Build Road Network' first.");
                return;
            }

            var old = GameObject.Find("Traffic");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("Traffic");

            BuildSpawner(root);
            int peds = BuildPedestrians(root);
            int parked = BuildParkedVehicles(root);

            Debug.Log("[Traffic] Spawner configured, " + peds + " pedestrians, "
                      + parked + " vehicles parked for the player.");
        }

        // ----------------------------------------------------------------- spawner

        static void BuildSpawner(GameObject root)
        {
            var go = new GameObject("TrafficSpawner");
            go.transform.SetParent(root.transform, false);

            var spawner = go.AddComponent<TrafficSpawner>();

            var cars = new List<GameObject>();
            for (int i = 0; i < 6; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Car_" + i + ".prefab");
                if (prefab != null) cars.Add(prefab);
            }

            spawner.CarPrefabs = cars.ToArray();
            spawner.PolicePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Car_Police.prefab");
            spawner.TrafficCount = 12;
            spawner.PoliceCount = 2;

            var player = GameObject.FindWithTag("Player");
            if (player != null) spawner.Player = player.transform;

            if (cars.Count == 0)
                Debug.LogWarning("[Traffic] No car prefabs found. Run 'Build Vehicle Prefabs' first.");
        }

        // ------------------------------------------------------------- pedestrians

        static int BuildPedestrians(GameObject root)
        {
            var parent = new GameObject("Pedestrians");
            parent.transform.SetParent(root.transform, false);

            // One shared roster, so the crowd, the police and the shopkeepers cannot drift onto
            // different rigs. See CharacterCatalog for why every entry has to be humanoid.
            var looks = CharacterCatalog.Crowd;
            var controller = CharacterCatalog.LoadController();

            int lanes = CityBuilder.Blocks + 1;
            float half = CityBuilder.RoadWidth * 0.5f;
            var rng = new System.Random(7788);
            int count = 0;

            // Only the lit junctions have signals to obey, so that is where crossings go.
            for (int j = 1; j < lanes - 1; j++)
            {
                for (int i = 1; i < lanes - 1; i++)
                {
                    // Two per junction would crowd the frame budget; sample about a third.
                    if (rng.NextDouble() > 0.4) continue;

                    Vector3 centre = RoadNetworkBuilder.IntersectionCentre(i, j);
                    float y = TerrainBuilder.PlateauHeight + CityBuilder.SidewalkHeight + 0.05f;

                    // The northern crosswalk spans the north-south carriageway, so the walk
                    // is east-west and it is north-south traffic that must be stopped.
                    Vector3 a = new Vector3(centre.x - (half + 3.5f), y, centre.z + half + 1.4f);
                    Vector3 b = new Vector3(centre.x + (half + 3.5f), y, centre.z + half + 1.4f);

                    var ped = BuildPedestrian(parent, looks[rng.Next(looks.Length)], controller,
                                              a, b, j * lanes + i, Heading.East, count);
                    if (ped != null) count++;
                }
            }

            // Sidewalk wanderers, one per block, roaming the pavement around it.
            float blockHalf = CityBuilder.BlockSize * 0.5f - 2f;
            float gridSpan = CityBuilder.Blocks * CityBuilder.Pitch;
            float ox = TerrainBuilder.CityCenter.x - gridSpan * 0.5f;
            float oz = TerrainBuilder.CityCenter.y - gridSpan * 0.5f;
            float walkY = TerrainBuilder.PlateauHeight + CityBuilder.SidewalkHeight + 0.05f;

            for (int bx = 0; bx < CityBuilder.Blocks; bx++)
            {
                for (int bz = 0; bz < CityBuilder.Blocks; bz++)
                {
                    if (rng.NextDouble() > 0.45) continue;

                    float cx = ox + CityBuilder.RoadWidth + bx * CityBuilder.Pitch + CityBuilder.BlockSize * 0.5f;
                    float cz = oz + CityBuilder.RoadWidth + bz * CityBuilder.Pitch + CityBuilder.BlockSize * 0.5f;

                    var wanderer = BuildWanderer(parent, looks[rng.Next(looks.Length)], controller,
                                                 new Vector3(cx, walkY, cz),
                                                 new Vector2(blockHalf, blockHalf), count);
                    if (wanderer != null) count++;
                }
            }

            return count;
        }

        /// <summary>
        /// The shared body of every street civilian: controller, health, ragdoll, model.
        ///
        /// Crossers and wanderers differ only in the route they are given, so they are built by
        /// one function and configured by two. Before Phase 9 they were two near-identical
        /// copies that had already drifted -- an easy place for a fix to land in one and not
        /// the other.
        /// </summary>
        static GameObject BuildCivilian(GameObject parent, CharacterCatalog.Look look,
                                        RuntimeAnimatorController controller,
                                        string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = position;

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.28f;
            cc.center = new Vector3(0f, 0.88f, 0f);
            cc.stepOffset = 0.35f;

            // Off-screen pedestrians stop costing animation time entirely.
            var model = CharacterCatalog.AttachBody(go, look, controller,
                                                    AnimatorCullingMode.CullCompletely);
            if (model == null)
            {
                Object.DestroyImmediate(go);
                return null;
            }

            // Damageable, flinches, and topples when killed.
            var health = go.AddComponent<Health>();
            health.MaxHealth = 70f;
            health.MaxArmor = 0f;

            go.AddComponent<DamageReaction>();
            go.AddComponent<RagdollLite>();

            return go;
        }

        static GameObject BuildPedestrian(GameObject parent, CharacterCatalog.Look look,
                                          RuntimeAnimatorController controller,
                                          Vector3 a, Vector3 b, int intersection,
                                          Heading walkHeading, int index)
        {
            var go = BuildCivilian(parent, look, controller, "Pedestrian_" + index, a);
            if (go == null) return null;

            var ped = go.AddComponent<Pedestrian>();
            ped.Route = PedestrianRoute.Crossing;
            ped.PointA = a;
            ped.PointB = b;
            ped.IntersectionIndex = intersection;
            ped.WalkHeading = walkHeading;

            return go;
        }

        /// <summary>
        /// Civilians who roam a block's sidewalk rather than shuttling across a crossing.
        /// These are the ones that make the streets feel occupied between junctions.
        /// </summary>
        static GameObject BuildWanderer(GameObject parent, CharacterCatalog.Look look,
                                        RuntimeAnimatorController controller,
                                        Vector3 centre, Vector2 extents, int index)
        {
            var go = BuildCivilian(parent, look, controller, "Civilian_" + index, centre);
            if (go == null) return null;

            var ped = go.AddComponent<Pedestrian>();
            ped.Route = PedestrianRoute.Wander;
            ped.PointA = centre;
            ped.WanderExtents = extents;
            ped.IntersectionIndex = -1;

            return go;
        }

        // ---------------------------------------------------------------- parked

        static int BuildParkedVehicles(GameObject root)
        {
            var parent = new GameObject("Parked");
            parent.transform.SetParent(root.transform, false);

            int count = 0;
            Vector3 spawn = CityBuilder.SpawnPoint;
            if (spawn == Vector3.zero)
            {
                // The city was not rebuilt this session, so fall back to the west avenue --
                // the same place BuildCity spawns the player. Derived from the grid rather
                // than written out, because the grid moved in Phase 11 and a literal here
                // would have parked the player's starter car half a kilometre out to sea.
                Vector3 j = CityBuilder.JunctionCentre(0, CityBuilder.Blocks / 2);
                spawn = new Vector3(j.x, TerrainBuilder.PlateauHeight + 0.4f, j.z);
            }

            // A car and a bike within a few seconds' walk of where the player starts.
            count += Place(parent, "Car_0", spawn + new Vector3(0f, 0.6f, 14f), 0f) ? 1 : 0;
            count += Place(parent, "Bike", spawn + new Vector3(-2.4f, 0.5f, -9f), 180f) ? 1 : 0;
            count += Place(parent, "Car_3", spawn + new Vector3(0f, 0.6f, -26f), 180f) ? 1 : 0;

            // A boat sitting off the beach, west of the city in water deep enough to float.
            // The shoreline is a fixed fraction of the map (see TerrainBuilder.WorldHeight),
            // so this is expressed against MapSize and follows the coast when the map grows.
            var boatPos = new Vector3(TerrainBuilder.MapSize * 0.178f,
                                      TerrainBuilder.SeaLevel + 0.2f,
                                      TerrainBuilder.CityCenter.y);
            count += Place(parent, "Boat", boatPos, 270f) ? 1 : 0;

            return count;
        }

        static bool Place(GameObject parent, string prefabName, Vector3 position, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + prefabName + ".prefab");
            if (prefab == null)
            {
                Debug.LogWarning("[Traffic] Missing prefab " + prefabName);
                return false;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return true;
        }
    }
}
