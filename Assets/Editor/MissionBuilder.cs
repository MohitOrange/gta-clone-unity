using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Places the mission contacts and their jobs around the city.
    ///
    /// Positions are derived from the road grid rather than hand-picked, so re-rolling the
    /// city does not leave contacts standing inside buildings. Rewards are tuned so the first
    /// job alone levels the player to 2, which is what makes the second contact light up and
    /// demonstrates the progression loop in one run.
    /// </summary>
    public static class MissionBuilder
    {
        const string PrefabDir = "Assets/Game/Prefabs";
        const string ControllerPath = "Assets/Game/Animator/PlayerLocomotion.controller";

        static readonly string[] ContactModels =
        {
            "Assets/characters/Arissa.fbx",
            "Assets/characters/Ch08_nonPBR.fbx",
            "Assets/characters/Ch22_nonPBR.fbx",
            "Assets/characters/Ch31_nonPBR.fbx",
        };

        [MenuItem("Tools/Mini GTA/14. Build Missions", priority = 134)]
        public static void Build()
        {
            var old = GameObject.Find("Missions");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("Missions");
            var jobs = NewChild(root, "Jobs");
            var contacts = NewChild(root, "Contacts");

            var officer = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/PoliceOfficer.prefab");
            var car = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Car_3.prefab");
            var civilianModel = AssetDatabase.LoadAssetAtPath<GameObject>(ContactModels[0]);

            float y = TerrainBuilder.PlateauHeight + CityBuilder.SidewalkHeight;
            float roadY = TerrainBuilder.PlateauHeight + CityBuilder.RoadSurfaceY;

            // --- 1. Courier run -------------------------------------------------------
            var delivery = jobs.AddComponent<DeliveryMission>();
            delivery.MissionId = "job.courier";
            delivery.Title = "Courier Run";
            delivery.Briefing = "Four drops, one clock. Don't get out of the car.";
            delivery.RequiredLevel = 1;
            delivery.RewardMoney = 300;
            delivery.RewardXp = 180;      // enough on its own to reach level 2
            delivery.TimeLimit = 165f;
            delivery.CargoName = "the package";
            delivery.Checkpoints = new[]
            {
                RoadRel(-1, 1, roadY), RoadRel(1, 1, roadY), RoadRel(2, -1, roadY), RoadRel(0, -2, roadY),
            };
            MakeContact(contacts, "Contact_Courier", ContactModels[0],
                        Kerb(C - 3, C, Heading.West, 0.1f, y), delivery);

            // --- 2. Clear the lot -----------------------------------------------------
            var elimination = jobs.AddComponent<EliminationMission>();
            elimination.MissionId = "job.clearlot";
            elimination.Title = "Clear the Lot";
            elimination.Briefing = "A crew is squatting in the park. Move them on.";
            elimination.RequiredLevel = 2;
            elimination.RewardMoney = 450;
            elimination.RewardXp = 240;
            elimination.TimeLimit = 0f;
            elimination.EnemyPrefab = officer;
            elimination.EnemyCount = 4;
            elimination.EnemyHealth = 60f;
            elimination.SiteCentre = Block(ParkBlock.x, ParkBlock.y, y);   // the park
            elimination.SiteRadius = 13f;
            MakeContact(contacts, "Contact_Enforcer", ContactModels[1],
                        Kerb(C + 1, C - 1, Heading.East, -0.2f, y), elimination);

            // --- 3. Safe passage ------------------------------------------------------
            var escort = jobs.AddComponent<EscortMission>();
            escort.MissionId = "job.escort";
            escort.Title = "Safe Passage";
            escort.Briefing = "Walk my accountant across town. People want him quiet.";
            escort.RequiredLevel = 2;
            escort.RewardMoney = 520;
            escort.RewardXp = 300;
            escort.TimeLimit = 0f;
            escort.ClientPrefab = officer;   // same humanoid rig, stripped of police behaviour
            escort.ClientStart = Kerb(C, C + 1, Heading.South, 0.15f, y);
            escort.Destination = RoadRel(-1, 0, roadY);
            escort.EnemyPrefab = officer;
            escort.EnemiesPerAmbush = 2;
            escort.AmbushPoints = new[] { RoadRel(0, 1, roadY), RoadRel(-1, 0, roadY) };
            MakeContact(contacts, "Contact_Fixer", ContactModels[2],
                        Kerb(C, C + 1, Heading.South, -0.1f, y), escort);

            // --- 4. Grand theft -------------------------------------------------------
            var heist = jobs.AddComponent<HeistMission>();
            heist.MissionId = "job.heist";
            heist.Title = "Grand Theft";
            heist.Briefing = "There's a car in a guarded lot. Bring it to me, quietly or not.";
            heist.RequiredLevel = 3;
            heist.RewardMoney = 900;
            heist.RewardXp = 420;
            heist.TimeLimit = 0f;
            heist.TargetVehiclePrefab = car;
            heist.TargetName = "the Sedan";
            heist.PickupPoint = RoadRel(2, 0, roadY);
            heist.PickupYaw = 90f;
            heist.GuardPrefab = officer;
            heist.GuardCount = 2;
            heist.DropOff = RoadRel(2, 2, roadY);
            heist.AlarmHeat = 60f;
            MakeContact(contacts, "Contact_Boss", ContactModels[3],
                        Kerb(C + 2, C + 2, Heading.North, 0.2f, y), heist);

            if (officer == null)
                Debug.LogWarning("[Missions] PoliceOfficer prefab missing; enemies and the escort "
                                 + "client will not spawn. Run 'Build Character Prefabs' first.");
            if (car == null)
                Debug.LogWarning("[Missions] Car_3 prefab missing; the heist has no target vehicle.");

            Debug.Log("[Missions] Built 4 jobs and 4 contacts.");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>World position of road intersection (i, j) on the city grid.</summary>
        /// <summary>Centre block index of the grid. Every mission site is placed against it.</summary>
        static int C => CityBuilder.CentreBlock;

        /// <summary>The park block the "Clear the Lot" job cleans out.</summary>
        static Vector2Int ParkBlock => CityBuilder.MissionParkBlock;

        static Vector3 Road(int i, int j, float y)
        {
            Vector3 p = RoadNetworkBuilder.IntersectionCentre(i, j);
            return new Vector3(p.x, y, p.z);
        }

        /// <summary>
        /// A junction, addressed relative to the middle of the grid.
        ///
        /// Mission checkpoints used to be absolute junction indices chosen for a 5x5 grid.
        /// On the Phase 11 9x9 grid those same indices all sit in the south-west corner, so
        /// the courier run would have been four drops in one quarter of the map instead of a
        /// route across it. Relative indices keep a route centred whatever the grid size.
        /// </summary>
        static Vector3 RoadRel(int di, int dj, float y) => Road(C + di, C + dj, y);

        /// <summary>World position of the centre of city block (bx, bz).</summary>
        static Vector3 Block(int bx, int bz, float y)
        {
            Vector3 p = CityBuilder.BlockCentre(bx, bz);
            return new Vector3(p.x, y, p.z);
        }

        /// <summary>
        /// A spot on the pavement outside block (bx, bz), where a mission contact stands.
        ///
        /// Phase 11 replaced four absolute world coordinates here. They had been picked by
        /// hand off the 5x5 grid, and expanding the map to 9x9 moved every block out from
        /// under them -- two of the four would have ended up inside a building and one in the
        /// middle of a carriageway. Anything positioned in the city is now expressed as a
        /// block index so it follows the grid whatever size it is.
        /// </summary>
        static Vector3 Kerb(int bx, int bz, Heading edge, float along, float y)
        {
            Vector3 p = CityBuilder.BlockEdge(bx, bz, edge, along);
            // A metre and a half out from the building line, clear of the wall but still on
            // the pavement rather than in the gutter.
            return new Vector3(p.x, y, p.z) + edge.ToVector() * 1.5f;
        }

        static void MakeContact(GameObject parent, string name, string modelPath,
                                Vector3 position, MissionBase mission)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = position;

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (fbx != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                model.name = "Body";
                model.transform.SetParent(go.transform, false);

                var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;

                go.AddComponent<PlayerAnimation>();
            }

            // Contacts are scenery, not obstacles -- a trigger so the player can walk through
            // them rather than getting wedged against a quest-giver on a narrow pavement.
            var trigger = go.AddComponent<CapsuleCollider>();
            trigger.isTrigger = true;
            trigger.height = 1.8f;
            trigger.radius = 0.4f;
            trigger.center = new Vector3(0f, 0.9f, 0f);

            var beacon = BuildBeacon(go);

            var giver = go.AddComponent<MissionGiver>();
            giver.Mission = mission;
            giver.Beacon = beacon.transform;
            giver.BeaconRenderer = beacon.GetComponent<Renderer>();
        }

        /// <summary>Spinning marker above a contact's head.</summary>
        static GameObject BuildBeacon(GameObject parent)
        {
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beacon.name = "Beacon";
            beacon.transform.SetParent(parent.transform, false);
            beacon.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            // Tipped on its corners so it reads as a diamond from any angle.
            beacon.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            beacon.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);

            Object.DestroyImmediate(beacon.GetComponent<BoxCollider>());

            beacon.GetComponent<Renderer>().sharedMaterial = BeaconMaterial();
            return beacon;
        }

        static Material BeaconMaterial()
        {
            const string path = "Assets/Game/Materials/MissionBeacon.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(1f, 0.82f, 0.25f));
            mat.SetFloat("_Smoothness", 0.7f);
            // Emission keyword must be on in the shared material or per-beacon property block
            // colours are ignored entirely.
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", new Color(1f, 0.82f, 0.25f) * 2.6f);
            mat.enableInstancing = true;

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static GameObject NewChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }
    }
}
