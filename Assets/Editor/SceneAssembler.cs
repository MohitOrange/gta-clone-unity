using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Assembles the playable scene: lighting rig, ocean, player + camera, and the complete
    /// on-screen control layout.
    ///
    /// Written as a rebuild-from-scratch step rather than a set of manual scene edits so the
    /// scene can always be regenerated after a script change, and so the layout is reviewable
    /// as code instead of as a binary .unity diff.
    /// </summary>
    public static class SceneAssembler
    {
        const string ScenePath = "Assets/Scenes/City.unity";
        const string UiDir = "Assets/Game/UI";
        // The player's model now comes from CharacterCatalog.Player, so that the player, the
        // police and the crowd cannot drift onto different rigs or different scales.

        [MenuItem("Tools/Mini GTA/6. Assemble Scene", priority = 113)]
        public static void Assemble()
        {
            var scene = EditorSceneManager.GetActiveScene();

            ConfigureRenderPipeline();

            // Clear anything a previous run left behind.
            foreach (var name in new[] { "GameSystems", "Lighting", "Ocean", "Player", "HUD", "EventSystem",
                                         "Main Camera", "Directional Light", "Global Volume",
                                         "RespawnPoints" })
            {
                var existing = GameObject.Find(name);
                if (existing != null) Object.DestroyImmediate(existing);
            }

            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[Scene] No Terrain. Run steps 3 and 4 first.");
                return;
            }

            var systems = BuildSystems();
            BuildLighting();
            BuildOcean();
            var player = BuildPlayer();
            var cam = BuildCamera(player);
            BuildHud(out HudContext hudContext);

            // Wire the cross-references that cannot be set at construction time.
            var pc = player.GetComponent<PlayerController>();
            pc.Hud = hudContext;
            pc.CameraTarget = cam.transform;

            var tpc = cam.GetComponent<ThirdPersonCamera>();
            tpc.Target = player.transform;
            tpc.CollisionMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));

            var driving = player.GetComponent<PlayerVehicleController>();
            driving.Hud = hudContext;
            driving.CameraRig = tpc;

            var redLights = player.GetComponent<RedLightMonitor>();
            redLights.Driver = driving;

            var combat = player.GetComponent<PlayerCombat>();
            combat.Hud = hudContext;
            combat.AimSource = cam.transform;
            // Never let a bullet or a punch resolve against the player's own colliders.
            combat.HitMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));

            var respawn = BuildRespawnPoints(terrain);
            var game = systems.GetComponent<GameStateManager>();
            game.Player = player;
            game.HospitalSpawn = respawn.hospital;
            game.PoliceStationSpawn = respawn.station;

            var dispatcher = systems.GetComponent<PoliceDispatcher>();
            dispatcher.Player = player.transform;

            systems.GetComponent<SaveSystem>().Player = player;

            var missionHud = Object.FindAnyObjectByType<MissionHud>();
            if (missionHud != null) missionHud.Hud = hudContext;

            // Traffic is built by a later step, but re-assembling the scene replaces the Player
            // object, so any existing spawner must be re-pointed at the new one.
            var spawner = Object.FindAnyObjectByType<TrafficSpawner>();
            if (spawner != null) spawner.Player = player.transform;

            PlacePlayerAtSpawn(player, terrain);
            tpc.AlignBehindTarget();

            // The HUD was just rebuilt, so the menus that hang off it have to be too.
            MenuBuilder.Build();

            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log("[Scene] Assembled and saved to " + ScenePath
                      + "\n  Systems: " + systems.name
                      + "\n  Player spawn: " + player.transform.position
                      + "\n  HUD: joystick + look pad + " + "Jump/Sprint/Interact/Attack");
        }

        // ------------------------------------------------------- interface rebuild

        /// <summary>
        /// Rebuilds the canvas and every screen on it, and nothing else.
        ///
        /// <b>Why this exists.</b> The HUD is built by <see cref="Assemble"/>, and Assemble also
        /// destroys and rebuilds the player, the camera, the lighting and the ocean -- so
        /// re-skinning the interface used to mean re-running the whole pipeline and re-checking
        /// everything Phases 9 through 12 established. Three re-skins in, that is the wrong
        /// shape. This step throws away the canvas, builds a new one, and re-points the four
        /// references that live outside it.
        ///
        /// Those four are the entire external surface of the HUD: <c>PlayerController.Hud</c>,
        /// <c>PlayerVehicleController.Hud</c>, <c>PlayerCombat.Hud</c> and
        /// <c>MissionHud.Hud</c>. Everything else on the canvas is found at runtime by type.
        /// If a future screen adds a fifth, it has to be added here too -- a dangling HUD
        /// reference is silent, exactly like the null police prefabs were.
        /// </summary>
        [MenuItem("Tools/Mini GTA/6b. Rebuild Interface Only", priority = 114)]
        public static void RebuildInterface()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var player = GameObject.FindWithTag("Player");
            if (player == null)
            {
                Debug.LogError("[Scene] No Player in the scene. Run step 6 first.");
                return;
            }

            foreach (var name in new[] { "HUD", "EventSystem" })
            {
                var existing = GameObject.Find(name);
                if (existing != null) Object.DestroyImmediate(existing);
            }

            // The lobby's 3D stage is owned by LobbyBuilder and lives outside the canvas, so
            // MenuBuilder's own cleanup does not reach it. Left behind, a second stage would
            // stack on the first.
            var stage = GameObject.Find(LobbyBuilder.StageRootName);
            if (stage != null) Object.DestroyImmediate(stage);

            BuildHud(out HudContext hud);

            var controller = player.GetComponent<PlayerController>();
            if (controller != null) controller.Hud = hud;

            var driving = player.GetComponent<PlayerVehicleController>();
            if (driving != null) driving.Hud = hud;

            var combat = player.GetComponent<PlayerCombat>();
            if (combat != null) combat.Hud = hud;

            // MenuBuilder builds the lobby, the pause menu, settings and the map onto the new
            // canvas, and runs the safe-area and contrast passes at the end.
            MenuBuilder.Build();

            var missionHud = Object.FindAnyObjectByType<MissionHud>(FindObjectsInactive.Include);
            if (missionHud != null) missionHud.Hud = hud;
            else Debug.LogWarning("[Scene] No MissionHud after the rebuild -- objectives will not show.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log("[Scene] Interface rebuilt on the existing world. Face: " + UiTheme.FontSource);
        }

        // ------------------------------------------------------------- pipeline

        static void ConfigureRenderPipeline()
        {
            // The ocean's shoreline blending samples scene depth, which URP only generates
            // when the asset asks for it. Enable on every URP asset in the project so the
            // mobile and PC quality tiers both look right.
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null) continue;

                var so = new SerializedObject(asset);
                var depth = so.FindProperty("m_RequireDepthTexture");
                if (depth != null) depth.boolValue = true;

                // Opaque texture is only needed for refraction, which the water does not use.
                var opaque = so.FindProperty("m_RequireOpaqueTexture");
                if (opaque != null) opaque.boolValue = false;

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();
        }

        // -------------------------------------------------------------- systems

        static GameObject BuildSystems()
        {
            var go = new GameObject("GameSystems");
            go.AddComponent<InputHub>();
            go.AddComponent<HeatSystem>();
            go.AddComponent<GameStateManager>();

            var dispatcher = go.AddComponent<PoliceDispatcher>();
            WirePolicePrefabs(dispatcher);

            // Progression before the save system: SaveSystem.Start loads into PlayerProgress,
            // so the thing being restored has to exist first.
            go.AddComponent<PlayerProgress>();
            go.AddComponent<MissionManager>();
            go.AddComponent<Garage>();
            go.AddComponent<InteriorManager>();

            // Ad and store services before SaveSystem: loading progress restores the Remove Ads
            // entitlement, which calls into AdService.
            go.AddComponent<AdService>();
            go.AddComponent<IapService>();
            go.AddComponent<AdRewards>();

            go.AddComponent<SaveSystem>();

            return go;
        }

        /// <summary>
        /// Where the player wakes up after a bust or a wasting. Placed on the city grid rather
        /// than at the start point so respawning moves you somewhere, which is half the penalty.
        /// </summary>
        static (Transform hospital, Transform station) BuildRespawnPoints(Terrain terrain)
        {
            var root = new GameObject("RespawnPoints");

            Vector3 hospitalPos = PlaceOnGround(terrain, new Vector2(680f, 640f));
            Vector3 stationPos = PlaceOnGround(terrain, new Vector2(450f, 380f));

            var hospital = new GameObject("HospitalSpawn").transform;
            hospital.SetParent(root.transform, false);
            hospital.position = hospitalPos;

            var station = new GameObject("PoliceStationSpawn").transform;
            station.SetParent(root.transform, false);
            station.position = stationPos;

            return (hospital, station);
        }

        static Vector3 PlaceOnGround(Terrain terrain, Vector2 xz)
        {
            float y = terrain != null
                ? TerrainBuilder.SampleHeight(terrain, xz.x, xz.y)
                : TerrainBuilder.PlateauHeight;
            return new Vector3(xz.x, y + 1.0f, xz.y);
        }

        // -------------------------------------------------------------- lighting

        static void BuildLighting()
        {
            var root = new GameObject("Lighting");

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(root.transform, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.intensity = 1.25f;

            var moonGo = new GameObject("Moon");
            moonGo.transform.SetParent(root.transform, false);
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.shadows = LightShadows.None;   // a second shadow-casting light is not worth it on mobile
            moon.intensity = 0.12f;
            moon.color = new Color(0.62f, 0.72f, 1f);

            var cycle = root.AddComponent<DayNightCycle>();
            cycle.Sun = sun;
            cycle.Moon = moon;
            cycle.TimeOfDay = 0.40f;      // late morning: good light for a first look
            cycle.DayLengthSeconds = 300f;
            cycle.Refresh();              // OnEnable already ran with Sun/Moon still null

            BuildWeather(root, cycle);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fog = true;
        }

        /// <summary>
        /// The rain emitter, parented under Lighting because weather is a lighting state here:
        /// the sun damping, the ambient shift and the fog pull-in do more to sell a shower than
        /// the drops do. See DayNightCycle.Wetness and DECISIONS.md D29.
        /// </summary>
        static void BuildWeather(GameObject lightingRoot, DayNightCycle cycle)
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(lightingRoot.transform, false);

            go.AddComponent<ParticleSystem>();
            var weather = go.AddComponent<WeatherSystem>();
            weather.Cycle = cycle;
            weather.ConfigureEmitter(BuildRainMaterial());
        }

        /// <summary>
        /// An unlit alpha-blended material for the drops.
        ///
        /// Unlit on purpose: a lit particle would be shaded by the same sun the rain is
        /// supposed to be blocking, so drops would brighten in sunshine and vanish under
        /// cloud -- exactly backwards.
        ///
        /// <b>Alpha, not additive.</b> Additive was the first choice and it was wrong: it only
        /// ever brightens, so pale drops over an overcast sky and a light road added almost
        /// nothing and the downpour was invisible in the frame despite 1,350 live particles.
        /// Alpha blending lets a drop darken as well as lighten, which is what makes it read
        /// against the sky it is falling out of.
        /// </summary>
        static Material BuildRainMaterial()
        {
            const string path = "Assets/Game/Materials/Rain.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[Scene] No URP particle shader found; rain will use the default material.");
                return null;
            }

            var mat = new Material(shader) { name = "Rain" };
            mat.SetColor("_BaseColor", new Color(0.88f, 0.92f, 0.98f, 0.80f));
            // Surface 1 = Transparent, Blend 0 = Alpha, in URP's particle shader.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)RenderQueue.Transparent;

            Directory.CreateDirectory("Assets/Game/Materials");
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ----------------------------------------------------------------- ocean

        static void BuildOcean()
        {
            var mat = BuildWaterMaterial();

            // Covers the western sea and runs well past the map edge so the horizon reads as
            // open water rather than a visible rectangle.
            const float sizeX = 1400f;
            const float sizeZ = 2200f;
            const int segX = 140;
            const int segZ = 220;

            var mesh = BuildGridMesh(sizeX, sizeZ, segX, segZ);
            string meshPath = "Assets/Game/World/OceanMesh.asset";
            Directory.CreateDirectory("Assets/Game/World");
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject("Ocean");
            // Centred on the map's own middle rather than a literal, and pushed west so the
            // mesh reaches well past the shoreline in every direction. The Phase 11 expansion
            // to 1600 m would otherwise have left open terrain visible past the water's edge.
            go.transform.position = new Vector3(TerrainBuilder.MapSize * 0.5f - 800f,
                                                TerrainBuilder.SeaLevel,
                                                TerrainBuilder.MapSize * 0.5f);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var water = go.AddComponent<WaterVolume>();
            water.SeaLevel = TerrainBuilder.SeaLevel;
            water.WaveAmplitude = 0.5f;
            water.WaveLength = 34f;
            water.WaveSpeed = 0.55f;
        }

        static Material BuildWaterMaterial()
        {
            const string path = "Assets/Game/Materials/Ocean.mat";
            var shader = Shader.Find("MiniGTA/OceanWater");
            if (shader == null)
            {
                Debug.LogError("[Scene] OceanWater shader not found; water will fall back to URP Lit.");
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            Directory.CreateDirectory("Assets/Game/Materials");
            AssetDatabase.DeleteAsset(path);

            var mat = new Material(shader);
            if (mat.HasProperty("_ShallowColor")) mat.SetColor("_ShallowColor", new Color(0.20f, 0.62f, 0.66f));
            if (mat.HasProperty("_DeepColor")) mat.SetColor("_DeepColor", new Color(0.02f, 0.12f, 0.24f));
            if (mat.HasProperty("_WaveAmplitude")) mat.SetFloat("_WaveAmplitude", 0.5f);
            if (mat.HasProperty("_WaveLength")) mat.SetFloat("_WaveLength", 34f);
            if (mat.HasProperty("_WaveSpeed")) mat.SetFloat("_WaveSpeed", 0.55f);
            if (mat.HasProperty("_DepthFade")) mat.SetFloat("_DepthFade", 7f);
            if (mat.HasProperty("_FoamWidth")) mat.SetFloat("_FoamWidth", 1.6f);

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Flat XZ grid centred on the origin, enough vertices for the wave displacement.</summary>
        static Mesh BuildGridMesh(float sizeX, float sizeZ, int segX, int segZ)
        {
            var mesh = new Mesh { name = "OceanGrid", indexFormat = IndexFormat.UInt32 };

            int vx = segX + 1, vz = segZ + 1;
            var verts = new Vector3[vx * vz];
            var uvs = new Vector2[vx * vz];
            var normals = new Vector3[vx * vz];

            for (int z = 0; z < vz; z++)
            {
                float tz = z / (float)segZ;
                for (int x = 0; x < vx; x++)
                {
                    float tx = x / (float)segX;
                    int i = z * vx + x;
                    verts[i] = new Vector3((tx - 0.5f) * sizeX, 0f, (tz - 0.5f) * sizeZ);
                    uvs[i] = new Vector2(tx, tz);
                    normals[i] = Vector3.up;
                }
            }

            var tris = new int[segX * segZ * 6];
            int t = 0;
            for (int z = 0; z < segZ; z++)
            {
                for (int x = 0; x < segX; x++)
                {
                    int i = z * vx + x;
                    tris[t++] = i;
                    tris[t++] = i + vx;
                    tris[t++] = i + 1;
                    tris[t++] = i + 1;
                    tris[t++] = i + vx;
                    tris[t++] = i + vx + 1;
                }
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- player

        static GameObject BuildPlayer()
        {
            var root = new GameObject("Player");
            root.tag = "Player";

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.42f;
            cc.skinWidth = 0.03f;

            // Model instance. Any humanoid FBX works here; swapping the entry in
            // CharacterCatalog is the only change needed to change character, because
            // retargeting goes through the humanoid avatar.
            var model = CharacterCatalog.AttachBody(root, CharacterCatalog.Player,
                CharacterCatalog.LoadController(), AnimatorCullingMode.CullUpdateTransforms);

            if (model == null)
            {
                var placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                placeholder.name = "PlaceholderBody";
                placeholder.transform.SetParent(root.transform, false);
                placeholder.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                placeholder.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
                Object.DestroyImmediate(placeholder.GetComponent<CapsuleCollider>());
                root.AddComponent<PlayerAnimation>();
            }

            root.AddComponent<PlayerController>();
            root.AddComponent<PlayerVehicleController>();
            root.AddComponent<RedLightMonitor>();

            var health = root.AddComponent<Health>();
            health.MaxHealth = 100f;
            health.MaxArmor = 100f;
            health.StartingArmor = 0f;
            // Slow regen so a firefight is survivable without a medkit economy this phase.
            health.RegenPerSecond = 3.5f;
            health.RegenDelay = 7f;

            // The player flinching is fine; the player's own hits should not scare bystanders
            // twice, since PlayerCombat already reports and panics on contact.
            var reaction = root.AddComponent<DamageReaction>();
            reaction.PanicsBystanders = false;

            var combat = root.AddComponent<PlayerCombat>();

            // Phase 11: the player starts with their fists. The pistol is a thing lying in
            // the world that has to be found and picked up (see WeaponPickup and D28), not a
            // flag switched on at build time. MainMenu already cleared these on New Game, so
            // this only ever armed a player who had never saved -- which is to say, a player
            // on their very first run, which is exactly who should not start armed.
            combat.HasPistol = false;
            combat.Mode = WeaponMode.Unarmed;
            combat.Ammo = 0;

            root.AddComponent<PlayerSkinSwapper>();

            // The pistol in the player's hand. FollowCombatState means it appears and
            // disappears with PlayerCombat.IsArmed rather than needing PlayerCombat to know
            // that weapons have meshes at all.
            var socket = WeaponSetup.AttachSocket(root, "pistol", followCombat: true);

            // Section 2. The weapon state machine -- slots, ammunition, and the legality of
            // firing / reloading / switching.
            //
            // <b>It was written and attached to nothing.</b> The class, the slot rules and the
            // locking all existed and compiled, but no GameObject in the project carried the
            // component, so the game ran the Phase-7 two-field weapon and the five cheat codes
            // that reach for a WeaponController returned early every time. Attaching it here
            // rather than at runtime means it is in the saved scene and visible in the
            // inspector, which is how the absence would have been noticed in the first place.
            var weapons = root.AddComponent<WeaponController>();
            weapons.Library = WeaponSetup.Load();   // builds the asset if it is missing
            weapons.Socket = socket;
            weapons.Animation = root.GetComponentInChildren<PlayerAnimation>();
            combat.Weapons = weapons;

            if (weapons.Library == null)
                Debug.LogWarning("[Player] WeaponController has no WeaponLibrary; "
                                 + "every weapon lookup will fail.");

            return root;
        }

        static GameObject BuildCamera(GameObject player)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";

            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 900f;
            cam.fieldOfView = 62f;

            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;   // Phase 1 keeps the mobile frame budget clear
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            go.AddComponent<AudioListener>();

            var tpc = go.AddComponent<ThirdPersonCamera>();
            tpc.Target = player.transform;
            return go;
        }

        static void PlacePlayerAtSpawn(GameObject player, Terrain terrain)
        {
            Vector3 spawn = CityBuilder.SpawnPoint;

            // SpawnPoint is only populated when the city was built this session; fall back to
            // sampling the terrain at the city centre.
            if (spawn == Vector3.zero)
            {
                float x = TerrainBuilder.CityCenter.x;
                float z = TerrainBuilder.CityCenter.y;
                spawn = new Vector3(x, TerrainBuilder.SampleHeight(terrain, x, z) + 1.0f, z);
            }

            player.transform.position = spawn;
            player.transform.rotation = Quaternion.Euler(0f, 270f, 0f);   // face west, toward the sea
        }

        // ------------------------------------------------------------------- HUD

        static GameObject BuildHud(out HudContext context)
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Bias toward height so the thumb clusters keep their size on tall phones.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.65f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var circle = LoadSprite("ui_circle", MakeCircleSprite);
            var ring = LoadSprite("ui_ring", MakeRingSprite);

            // Order matters: the look pad is added first so it sits behind every button and
            // only receives taps that miss them.
            BuildLookZone(canvasGo);
            var stick = BuildJoystick(canvasGo, circle, ring);
            var buttons = BuildButtons(canvasGo);

            BuildReticle(canvasGo, circle);

            var readout = BuildVehicleReadout(canvasGo, circle);
            BuildStatusPanel(canvasGo, circle);
            BuildMinimap(canvasGo, circle);
            BuildMissionUi(canvasGo, circle);
            BuildShopUi(canvasGo, circle);
            BuildAdUi(canvasGo, circle);

            context = canvasGo.AddComponent<HudContext>();
            context.Jump = buttons.Jump;
            context.Sprint = buttons.Sprint;
            context.Interact = buttons.Interact;
            context.Attack = buttons.Attack;
            context.Aim = buttons.Aim;
            context.Reload = buttons.Reload;
            context.Gas = buttons.Gas;
            context.Brake = buttons.Brake;
            context.Handbrake = buttons.Handbrake;
            context.Horn = buttons.Horn;
            context.MoveStick = stick;
            context.VehicleReadout = readout;

            return canvasGo;
        }

        /// <summary>
        /// Points the police dispatcher at the cruiser and officer prefabs.
        ///
        /// <b>This has to be re-run after the prefab builders, not just here.</b> The assembler
        /// is step 8 of the pipeline; VehicleBuilder (step 9) and CharacterPrefabBuilder
        /// (step 10) both <c>DeleteAsset</c> and re-save these two prefabs, which gives them a
        /// new asset identity and leaves any reference taken beforehand dangling. The result
        /// was a dispatcher with two null prefabs after every full rebuild -- the wanted meter
        /// escalated to five stars and no police ever arrived, silently, because a null prefab
        /// logs nothing.
        ///
        /// Called again from <see cref="BuildEverything"/> once those two steps have run.
        /// </summary>
        internal static void WirePolicePrefabs(PoliceDispatcher dispatcher = null)
        {
            dispatcher = dispatcher != null
                ? dispatcher
                : Object.FindAnyObjectByType<PoliceDispatcher>(FindObjectsInactive.Include);

            if (dispatcher == null) return;

            dispatcher.CruiserPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Car_Police.prefab");
            dispatcher.OfficerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/PoliceOfficer.prefab");

            // Never fail silently again: a dispatcher with no cruiser is a police force that
            // does not exist, and nothing else in the game would report it.
            if (dispatcher.CruiserPrefab == null)
                Debug.LogError("[Police] Car_Police.prefab did not load -- no cruisers will spawn.");
            if (dispatcher.OfficerPrefab == null)
                Debug.LogError("[Police] PoliceOfficer.prefab did not load -- no officers will spawn.");

            EditorUtility.SetDirty(dispatcher);
        }

        static void EnsureEventSystem()
        {
            var existing = Object.FindAnyObjectByType<EventSystem>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            // This project runs Input System only; the legacy StandaloneInputModule would
            // throw the moment a pointer moved.
            go.AddComponent<InputSystemUIInputModule>();
        }

        static GameObject BuildLookZone(GameObject canvas)
        {
            var go = NewUi(canvas, "LookZone");
            var rt = go.GetComponent<RectTransform>();
            // Right ~55% of the screen, full height.
            rt.anchorMin = new Vector2(0.45f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);   // invisible but still raycastable
            img.raycastTarget = true;

            go.AddComponent<TouchLookZone>();
            return go;
        }

        static GameObject BuildJoystick(GameObject canvas, Sprite circle, Sprite ring)
        {
            var zone = NewUi(canvas, "MoveStick");
            var zrt = zone.GetComponent<RectTransform>();
            zrt.anchorMin = new Vector2(0f, 0f);
            // 0.55 rather than 0.62: the top strip of this zone is where the pause button now
            // lives. A floating stick re-centres wherever the thumb lands and thumbs rest low,
            // so the lost strip is the part of the zone that was never reached anyway.
            zrt.anchorMax = new Vector2(0.42f, 0.55f);
            zrt.offsetMin = Vector2.zero;
            zrt.offsetMax = Vector2.zero;

            var zoneImg = zone.AddComponent<Image>();
            zoneImg.color = new Color(0f, 0f, 0f, 0f);
            zoneImg.raycastTarget = true;

            var ringGo = NewUi(zone, "Ring");
            var ringImg = ringGo.AddComponent<Image>();
            ringImg.sprite = ring;
            // The kit's cyan at low alpha rather than plain white: the stick is the one control
            // that is always on screen, and it should read as part of the same instrument set
            // as the bars and the map without competing with them.
            ringImg.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.38f);
            ringImg.raycastTarget = false;
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.sizeDelta = new Vector2(260f, 260f);

            var knobGo = NewUi(zone, "Knob");
            var knobImg = knobGo.AddComponent<Image>();
            knobImg.sprite = circle;
            knobImg.color = new Color(UiTheme.Shield.r, UiTheme.Shield.g, UiTheme.Shield.b, 0.72f);
            knobImg.raycastTarget = false;
            var knobRt = knobGo.GetComponent<RectTransform>();
            knobRt.sizeDelta = new Vector2(108f, 108f);

            var stick = zone.AddComponent<VirtualJoystick>();
            stick.Ring = ringRt;
            stick.Knob = knobRt;
            stick.Radius = 110f;
            stick.FadeTargets = new Graphic[] { ringImg, knobImg };

            return zone;
        }

        /// <summary>
        /// The centre-screen crosshair.
        ///
        /// Four arms and a centre dot rather than a drawn cross: the gap in the middle is what
        /// keeps the thing you are shooting at visible, which a solid crosshair covers up at
        /// exactly the moment it matters. Built from the same disc the buttons use, tinted with
        /// the theme accent, so it belongs to the same instrument set as the joystick ring and
        /// the map bezel.
        ///
        /// Added late in the canvas order but with raycasts off on every part -- it sits over
        /// the middle of the screen, which is also the look pad, and a crosshair that ate touch
        /// input would stop the camera turning.
        /// </summary>
        static AimReticle BuildReticle(GameObject canvas, Sprite circle)
        {
            var root = NewUi(canvas, "AimReticle");
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(96f, 96f);

            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var colour = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.92f);

            // Centre dot: where the bullet actually goes.
            var dot = NewUi(root, "Dot");
            var drt = dot.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = Vector2.zero;
            drt.sizeDelta = new Vector2(6f, 6f);
            var dotImg = dot.AddComponent<Image>();
            dotImg.sprite = circle;
            dotImg.color = colour;
            dotImg.raycastTarget = false;

            // Four arms, N/E/S/W. Their authored offset is the tight, fully-aimed position;
            // AimReticle blooms them outwards while the weapon is down.
            var offsets = new[]
            {
                new Vector2(0f, 18f), new Vector2(0f, -18f),
                new Vector2(-18f, 0f), new Vector2(18f, 0f),
            };
            var arms = new RectTransform[offsets.Length];

            for (int i = 0; i < offsets.Length; i++)
            {
                var arm = NewUi(root, "Arm_" + i);
                var art = arm.GetComponent<RectTransform>();
                art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
                art.pivot = new Vector2(0.5f, 0.5f);
                art.anchoredPosition = offsets[i];
                bool vertical = Mathf.Abs(offsets[i].y) > Mathf.Abs(offsets[i].x);
                art.sizeDelta = vertical ? new Vector2(3f, 12f) : new Vector2(12f, 3f);

                var img = arm.AddComponent<Image>();
                img.color = colour;
                img.raycastTarget = false;
                arms[i] = art;
            }

            var reticle = root.AddComponent<AimReticle>();
            reticle.Group = group;
            reticle.Arms = arms;
            return reticle;
        }

        class ActionButtons
        {
            public HudButton Jump, Sprint, Interact, Attack, Aim, Reload;
            public HudButton Gas, Brake, Handbrake, Horn;
        }

        /// <summary>
        /// The right-thumb action cluster: one large primary, the rest fanned around it.
        ///
        /// <b>Layout.</b> Third-person shooters on a phone have converged on the same
        /// arrangement, and for a physical reason: a right thumb pivots at the bottom-right
        /// corner of the device and sweeps an arc up and to the left. So the action pressed
        /// most sits nearest that pivot at the largest size, and the rest are spaced along the
        /// arc at a radius the same thumb reaches without the hand regripping. This is that
        /// arrangement, laid out from this project's own geometry -- not traced from any
        /// particular game's cluster.
        ///
        /// <b>Circles, not the kit's squares.</b> The GUI kit ships Wide, Square and Slim
        /// silhouettes and no round one. A round target is what an arc wants: every point on
        /// its edge is the same distance from its centre, so a thumb landing off-centre is
        /// equally wrong in every direction rather than sliding off a corner. They are built
        /// from this project's own generated disc and ring, tinted from UiTheme, so they still
        /// belong to the same instrument set as the joystick ring and the vitals bars.
        ///
        /// <b>Shared slots.</b> On-foot and driving actions occupy the same four positions,
        /// because the two contexts are mutually exclusive and the thumb should not have to
        /// relearn where the primary is on getting into a car.
        /// </summary>
        static ActionButtons BuildButtons(GameObject canvas)
        {
            var cluster = NewUi(canvas, "ActionButtons");
            var crt = cluster.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 0f);
            crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-40f, 40f);
            crt.sizeDelta = new Vector2(FanWidth, FanHeight);

            // Positions are each button's centre, measured from the cluster's bottom-right
            // corner -- that is, from the thumb's pivot. Every pair is further apart than the
            // sum of their radii, asserted at build time by AuditFan.
            var b = new ActionButtons
            {
                // Primary: largest, nearest the pivot, and the only one with a warm rim.
                Attack = MakeCircleButton(cluster, HudAction.Attack, "FIRE", "gun", true,
                                          new Vector2(-120f, 120f), 164f, primary: true),

                Aim = MakeCircleButton(cluster, HudAction.Aim, "AIM", "zoom-in", true,
                                       new Vector2(-330f, 108f), 118f),
                Reload = MakeCircleButton(cluster, HudAction.Reload, "RELOAD", "redo", false,
                                          new Vector2(-320f, 268f), 106f),
                Jump = MakeCircleButton(cluster, HudAction.Jump, "JUMP", "up-arrow", false,
                                        new Vector2(-140f, 322f), 118f),
                // RUN latches rather than being held: tap once to lock sprint on, tap again
                // to drop it. Holding a button for the length of a street is the most tiring
                // thing a touch layout can ask for, and it fights the thumb that steers.
                Sprint = MakeCircleButton(cluster, HudAction.Sprint, "RUN", "bolt", false,
                                          new Vector2(-470f, 210f), 100f, latching: true),
                Interact = MakeCircleButton(cluster, HudAction.Interact, "ENTER", "door", false,
                                            new Vector2(-250f, 430f), 112f),

                // Driving, sharing those positions.
                Gas = MakeCircleButton(cluster, HudAction.Gas, "GAS", "fast-forward", true,
                                       new Vector2(-120f, 120f), 164f, primary: true),
                Brake = MakeCircleButton(cluster, HudAction.Brake, "BRAKE", "stop", true,
                                         new Vector2(-330f, 108f), 118f),
                Handbrake = MakeCircleButton(cluster, HudAction.Handbrake, "DRIFT", "shuffle-arrow",
                                             true, new Vector2(-140f, 322f), 118f),
                Horn = MakeCircleButton(cluster, HudAction.Horn, "HORN", "megaphone", false,
                                        new Vector2(-320f, 268f), 106f),
            };

            AuditFan(cluster);
            return b;
        }

        const float FanWidth = 560f;
        const float FanHeight = 500f;

        /// <summary>
        /// Fails the build if two buttons in the fan overlap, or if one leaves the cluster.
        ///
        /// A thumb control two units under its neighbour is invisible in a screenshot and
        /// unmissable in the hand, and the previous UI pass shipped a card overhanging its
        /// frame by 26 units for exactly that reason. Cheaper to assert than to look.
        /// </summary>
        static void AuditFan(GameObject cluster)
        {
            var buttons = cluster.GetComponentsInChildren<HudButton>(true);
            var seen = new System.Collections.Generic.List<RectTransform>();
            int problems = 0;

            foreach (var btn in buttons)
            {
                var rt = (RectTransform)btn.transform;
                float r = rt.sizeDelta.x * 0.5f;
                var c = rt.anchoredPosition;

                if (c.x - r < -FanWidth || c.x + r > 0f || c.y - r < 0f || c.y + r > FanHeight)
                {
                    Debug.LogError("[HUD] " + btn.name + " at " + c + " r=" + r + " leaves the "
                                   + FanWidth + "x" + FanHeight + " cluster.");
                    problems++;
                }

                foreach (var other in seen)
                {
                    float have = Vector2.Distance(other.anchoredPosition, c);

                    // Buttons sharing a slot by design (on-foot vs driving) are never on screen
                    // together, so an exact coincidence is intended, not a collision.
                    if (have < 0.5f) continue;

                    float need = r + other.sizeDelta.x * 0.5f;
                    if (have >= need) continue;

                    Debug.LogError("[HUD] " + btn.name + " overlaps " + other.name + ": centres "
                                   + have.ToString("F1") + " apart, need " + need.ToString("F1") + ".");
                    problems++;
                }
                seen.Add(rt);
            }

            if (problems == 0)
                Debug.Log("[HUD] Action fan: " + buttons.Length + " buttons, no overlaps, all "
                          + "inside " + FanWidth + "x" + FanHeight + ".");
        }

        /// <summary>
        /// One round thumb control: a tinted disc, a rim, a pictogram and a caption.
        ///
        /// The caption survives the change of shape. Glyph-only is the genre norm, but this
        /// project already established that a pictogram alone did not identify a control the
        /// first time it was seen, and making the button round does not make the glyph any
        /// clearer -- it only moves it.
        ///
        /// Pressed state is a tint inversion rather than a sprite swap: a generated disc has no
        /// painted pressed variant, and a bright fill under a darkened glyph is the strongest
        /// signal available at arm's length.
        /// </summary>
        static HudButton MakeCircleButton(GameObject parent, HudAction action, string label,
                                          string icon, bool hold, Vector2 centre, float size,
                                          bool primary = false, bool latching = false)
        {
            var go = NewUi(parent, "Btn_" + action);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = centre;
            rt.sizeDelta = new Vector2(size, size);

            // The disc is the raycast target and the graphic HudButton tints, so it is what the
            // thumb actually hits. The rim and the glyph are decoration and take no input.
            var disc = go.AddComponent<Image>();
            disc.sprite = LoadSprite("ui_circle", MakeCircleSprite);
            disc.type = Image.Type.Simple;
            disc.raycastTarget = true;

            var idle = primary
                ? new Color(UiTheme.Danger.r, UiTheme.Danger.g, UiTheme.Danger.b, 0.42f)
                : new Color(UiTheme.PanelDeep.r, UiTheme.PanelDeep.g, UiTheme.PanelDeep.b, 0.62f);
            var pressed = primary
                ? new Color(UiTheme.Danger.r, UiTheme.Danger.g, UiTheme.Danger.b, 0.95f)
                : new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.92f);
            disc.color = idle;

            // Rim at the full diameter, so the control keeps a defined edge against a bright
            // street rather than dissolving into it.
            var rimGo = NewUi(go, "Rim");
            var rimRt = rimGo.GetComponent<RectTransform>();
            rimRt.anchorMin = Vector2.zero;
            rimRt.anchorMax = Vector2.one;
            rimRt.offsetMin = Vector2.zero;
            rimRt.offsetMax = Vector2.zero;

            var rim = rimGo.AddComponent<Image>();
            rim.sprite = LoadSprite("ui_ring", MakeRingSprite);
            rim.raycastTarget = false;
            rim.color = primary
                ? new Color(UiTheme.Danger.r, UiTheme.Danger.g, UiTheme.Danger.b, 0.95f)
                : new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.70f);

            // Pictogram, set slightly high so the caption owns the lower third.
            var glyphGo = NewUi(go, "Icon");
            var grt = glyphGo.GetComponent<RectTransform>();
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = new Vector2(0f, size * 0.09f);
            grt.sizeDelta = new Vector2(size * 0.40f, size * 0.40f);

            var glyph = glyphGo.AddComponent<Image>();
            glyph.sprite = UiTheme.Icon(icon);
            glyph.color = UiTheme.TextOnPanel;
            glyph.preserveAspect = true;
            glyph.raycastTarget = false;

            // Caption inside the disc. Its width is the chord at that height, not the diameter,
            // so a long word cannot run out over the rim.
            float capY = -size * 0.27f;
            float chord = 2f * Mathf.Sqrt(Mathf.Max(1f, size * size * 0.25f - capY * capY));

            var textGo = NewUi(go, "Label");
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(0f, capY);
            trt.sizeDelta = new Vector2(chord * 0.92f, size * 0.20f);

            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.font = UiTheme.Font;
            text.fontSize = UiTheme.SnapSize(Mathf.RoundToInt(size * 0.135f));
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            // Light on a dark disc -- the inverse of the kit's square buttons, whose faces are
            // painted light.
            text.color = UiTheme.TextOnPanel;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            go.AddComponent<CanvasGroup>();
            var btn = go.AddComponent<HudButton>();
            btn.Action = action;
            btn.IsHoldButton = hold;
            btn.IsLatching = latching;
            btn.Background = disc;
            btn.NormalTint = idle;
            btn.PressedTint = pressed;
            // A generated disc has no painted pressed art, so leaving these null keeps
            // HudButton on its tint-only path.
            btn.NormalSprite = null;
            btn.PressedSprite = null;
            return btn;
        }

        /// <summary>
        /// Vitals top-left, wanted stars top-right, ammo above the action cluster, and the
        /// end-of-run banner across the middle.
        /// </summary>
        static PlayerStatusHud BuildStatusPanel(GameObject canvas, Sprite circle)
        {
            // The kit's painted star, not the generated one. It is drawn at 50 units, well
            // inside the range the kit's 128px Icons set is authored for.
            var star = UiTheme.Star;
            var font = UiTheme.Font;

            var panel = NewUi(canvas, "StatusPanel");
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            var hud = panel.AddComponent<PlayerStatusHud>();

            // The bar colours live on the component (it lerps health between two of them), so
            // they are set from the theme here rather than left on the component's defaults --
            // otherwise the vitals would still be the pre-re-skin green and red.
            hud.HealthGood = UiTheme.Positive;
            hud.HealthLow = UiTheme.Danger;
            hud.ArmorColor = UiTheme.Shield;
            hud.StarEarned = UiTheme.Gold;
            hud.StarEmpty = new Color(1f, 1f, 1f, 0.18f);
            hud.BustedColor = UiTheme.Shield;
            hud.WastedColor = UiTheme.Danger;

            // --- Vitals, top-left --------------------------------------------------------
            // Sit below the minimap, which occupies the top-left corner. Each bar is labelled
            // by one of the kit's icons rather than by a word: instruction 4 of the re-skin
            // brief, and it is also the only way two stacked bars stay distinguishable at HUD
            // size without a caption eating the width.
            hud.HealthFill = BuildVitalBar(panel, "Health", new Vector2(76f, -266f),
                                           new Vector2(232f, 22f), UiTheme.Positive,
                                           "heart", 34f);
            hud.ArmorFill = BuildVitalBar(panel, "Armor", new Vector2(76f, -296f),
                                          new Vector2(232f, 16f), UiTheme.Shield,
                                          "shield", 28f);

            // --- Wanted stars, top-right -------------------------------------------------
            var starsRoot = NewUi(panel, "WantedStars");
            var srt = starsRoot.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(1f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(1f, 1f);
            srt.anchoredPosition = new Vector2(-46f, -36f);
            srt.sizeDelta = new Vector2(300f, 56f);

            var stars = new Image[5];
            for (int i = 0; i < stars.Length; i++)
            {
                var s = NewUi(starsRoot, "Star" + i);
                var rt = s.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                // Laid out left-to-right within a right-anchored group, so earned stars fill
                // from the left the way every wanted meter the player has seen does.
                rt.anchoredPosition = new Vector2(-(4 - i) * 56f, 0f);
                rt.sizeDelta = new Vector2(50f, 50f);

                var img = s.AddComponent<Image>();
                img.sprite = star;
                img.raycastTarget = false;
                img.color = new Color(1f, 1f, 1f, 0.16f);
                stars[i] = img;
            }
            hud.Stars = stars;

            // --- Weapon and ammunition, top-right under the wanted stars -----------------
            // Moved out of the bottom-right corner, where it sat directly above the action
            // cluster and was the first thing the firing hand covered. Top-right is where the
            // genre puts it and is the one corner a right-handed grip never occludes.
            var ammo = NewUi(panel, "Ammo");
            var art = ammo.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(1f, 1f);
            art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(1f, 1f);
            // Clear of the 50-unit star row at y -36, plus its own gap.
            art.anchoredPosition = new Vector2(-46f, -104f);
            // Wider and taller than the old chip: it now carries a weapon name and a
            // magazine/reserve pair rather than a single number.
            art.sizeDelta = new Vector2(268f, 92f);

            var ammoGroup = ammo.AddComponent<CanvasGroup>();
            ammoGroup.alpha = 0f;
            ammoGroup.blocksRaycasts = false;

            // The count sits on one of the kit's dark chips rather than bare over the street.
            // It is the one HUD number that changes during a fight, and it was previously
            // white text on whatever the player happened to be standing in front of.
            var ammoPlate = ammo.AddComponent<Image>();
            ammoPlate.sprite = UiTheme.Chip;
            ammoPlate.type = Image.Type.Sliced;
            ammoPlate.pixelsPerUnitMultiplier = 1f;
            ammoPlate.color = new Color(1f, 1f, 1f, 0.86f);
            ammoPlate.raycastTarget = false;

            var ammoIcon = NewUi(ammo, "Icon");
            var airt = ammoIcon.GetComponent<RectTransform>();
            airt.anchorMin = airt.anchorMax = new Vector2(0f, 0.5f);
            airt.pivot = new Vector2(0f, 0.5f);
            airt.anchoredPosition = new Vector2(16f, 0f);
            airt.sizeDelta = new Vector2(48f, 48f);

            var ammoGlyph = ammoIcon.AddComponent<Image>();
            ammoGlyph.sprite = UiTheme.Icon("gun");
            ammoGlyph.color = UiTheme.TextOnPanelDim;
            ammoGlyph.preserveAspect = true;
            ammoGlyph.raycastTarget = false;

            // Which weapon, above the count. The pictogram set has one glyph per weapon class
            // rather than per weapon, so the name is what distinguishes a rifle from a sniper.
            var ammoName = NewUi(ammo, "WeaponName");
            var anrt = ammoName.GetComponent<RectTransform>();
            anrt.anchorMin = new Vector2(0f, 1f);
            anrt.anchorMax = new Vector2(1f, 1f);
            anrt.pivot = new Vector2(0.5f, 1f);
            anrt.offsetMin = new Vector2(72f, 0f);
            anrt.offsetMax = new Vector2(-20f, -12f);
            anrt.sizeDelta = new Vector2(anrt.sizeDelta.x, 26f);

            var ammoNameText = ammoName.AddComponent<Text>();
            ammoNameText.text = "";
            ammoNameText.font = UiTheme.Font;
            ammoNameText.fontSize = UiTheme.SnapSize(20);
            ammoNameText.alignment = TextAnchor.MiddleRight;
            ammoNameText.color = UiTheme.TextOnPanelDim;
            ammoNameText.raycastTarget = false;
            ammoNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            ammoNameText.verticalOverflow = VerticalWrapMode.Overflow;

            // The count goes on its own child, not on `ammo`: UI.Graphic is
            // [DisallowMultipleComponent], so an Image and a Text cannot share a GameObject.
            var ammoCount = NewUi(ammo, "Count");
            var acrt = ammoCount.GetComponent<RectTransform>();
            acrt.anchorMin = Vector2.zero;
            acrt.anchorMax = Vector2.one;
            // Inset on both axes past the chip's own 8 px frame. Horizontal alone left the
            // count's rect running the full height of the plate and 8 units onto its lip --
            // caught by the frame audit, not by looking.
            acrt.offsetMin = new Vector2(72f, 10f);
            acrt.offsetMax = new Vector2(-20f, -38f);   // clear of the weapon name above

            var ammoText = ammoCount.AddComponent<Text>();
            ammoText.text = "0";
            ammoText.font = UiTheme.Font;
            ammoText.fontSize = UiTheme.SnapSize(34);
            ammoText.fontStyle = FontStyle.Bold;
            ammoText.alignment = TextAnchor.MiddleRight;
            ammoText.color = UiTheme.TextOnPanel;
            ammoText.raycastTarget = false;
            ammoText.horizontalOverflow = HorizontalWrapMode.Overflow;
            ammoText.verticalOverflow = VerticalWrapMode.Overflow;

            hud.AmmoGroup = ammoGroup;
            hud.AmmoLabel = ammoText;
            hud.WeaponName = ammoNameText;
            hud.WeaponIcon = ammoGlyph;
            // Indexed by WeaponDefinition.Stance: 0 pistol, 1 alt pistol, 2 rifle,
            // 3 assault rifle, 4 bazooka. The kit has no distinct rifle pictogram, so the
            // three long-gun stances share the gun glyph and are told apart by the name above.
            hud.WeaponIconSprites = new[]
            {
                UiTheme.Icon("gun"), UiTheme.Icon("gun"), UiTheme.Icon("gun"),
                UiTheme.Icon("gun"), UiTheme.Icon("rocket"),
            };

            // --- End-of-run banner --------------------------------------------------------
            var banner = NewUi(panel, "EndBanner");
            var brt = banner.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(900f, 220f);

            var bannerGroup = banner.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.blocksRaycasts = false;

            var title = NewUi(banner, "Title");
            var trt = title.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.5f, 0.5f);
            trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(0f, 26f);
            trt.sizeDelta = new Vector2(880f, 110f);

            var titleText = title.AddComponent<Text>();
            titleText.text = "";
            titleText.font = UiTheme.Font;
            titleText.fontSize = UiTheme.SnapSize(96);
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.raycastTarget = false;

            var sub = NewUi(banner, "Subtitle");
            var subRt = sub.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0.5f, 0.5f);
            subRt.anchorMax = new Vector2(0.5f, 0.5f);
            subRt.pivot = new Vector2(0.5f, 0.5f);
            subRt.anchoredPosition = new Vector2(0f, -52f);
            subRt.sizeDelta = new Vector2(880f, 44f);

            var subText = sub.AddComponent<Text>();
            subText.text = "";
            subText.font = UiTheme.Font;
            subText.fontSize = UiTheme.SnapSize(26);
            subText.alignment = TextAnchor.MiddleCenter;
            subText.color = UiTheme.TextOnPanelDim;
            subText.raycastTarget = false;

            hud.EndBanner = bannerGroup;
            hud.EndTitle = titleText;
            hud.EndSubtitle = subText;

            return hud;
        }

        /// <summary>
        /// Schematic city map, top-left. The road grid is drawn once from the same constants
        /// the city uses, so the map and the streets cannot disagree.
        /// </summary>
        internal static Minimap BuildMinimap(GameObject canvas, Sprite circle)
        {
            const float mapSize = 232f;

            var frame = NewUi(canvas, "Minimap");
            var frt = frame.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 1f);
            frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 1f);
            frt.anchoredPosition = new Vector2(30f, -30f);
            frt.sizeDelta = new Vector2(mapSize, mapSize);

            // A round map, which is the genre's convention and is also the honest shape for
            // this one: the map is centred on the player and shows a fixed radius around them,
            // so a square frame was drawing more world in its corners than along its edges.
            //
            // The disc is a Mask, so the road grid and the blips are clipped to the circle
            // rather than being drawn to a square and overhanging it. MapArea has to be a child
            // of the masked object for that to apply, and the rim has to be a sibling drawn
            // after it -- a rim inside the mask would be clipped by its own circle.
            var discGo = NewUi(frame, "Disc");
            var drt = discGo.GetComponent<RectTransform>();
            drt.anchorMin = Vector2.zero;
            drt.anchorMax = Vector2.one;
            drt.offsetMin = Vector2.zero;
            drt.offsetMax = Vector2.zero;

            var disc = discGo.AddComponent<Image>();
            disc.sprite = LoadSprite("ui_circle", MakeCircleSprite);
            disc.raycastTarget = false;
            disc.color = new Color(UiTheme.PanelDeepest.r, UiTheme.PanelDeepest.g,
                                   UiTheme.PanelDeepest.b, 0.88f);

            var mask = discGo.AddComponent<Mask>();
            mask.showMaskGraphic = true;   // the disc is also the map's own background

            // Inner area the blips are positioned within, inside the mask.
            var area = NewUi(discGo, "MapArea");
            var art = area.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(0.5f, 0.5f);
            art.anchorMax = new Vector2(0.5f, 0.5f);
            art.pivot = new Vector2(0.5f, 0.5f);
            art.anchoredPosition = Vector2.zero;
            art.sizeDelta = new Vector2(mapSize - 16f, mapSize - 16f);

            // Bezel, outside the mask so it is not clipped by the circle it is drawing.
            var rimGo = NewUi(frame, "Rim");
            var rrt2 = rimGo.GetComponent<RectTransform>();
            rrt2.anchorMin = Vector2.zero;
            rrt2.anchorMax = Vector2.one;
            rrt2.offsetMin = Vector2.zero;
            rrt2.offsetMax = Vector2.zero;

            var rim = rimGo.AddComponent<Image>();
            rim.sprite = LoadSprite("ui_ring", MakeRingSprite);
            rim.raycastTarget = false;
            rim.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.75f);

            var minimap = frame.AddComponent<Minimap>();
            minimap.MapArea = art;

            // Bounds: the city pad plus a margin, so the whole district fits.
            float gridSpan = CityBuilder.Blocks * CityBuilder.Pitch;
            float ox = TerrainBuilder.CityCenter.x - gridSpan * 0.5f;
            float oz = TerrainBuilder.CityCenter.y - gridSpan * 0.5f;
            const float margin = 30f;

            minimap.WorldMin = new Vector2(ox - margin, oz - margin);
            minimap.WorldMax = new Vector2(ox + gridSpan + margin, oz + gridSpan + margin);

            DrawMinimapRoads(area, minimap);

            // Blips, drawn after the roads so they sit on top.
            // One shape and one colour per category, so the map is readable at a glance on a
            // 232-unit dial. The three that matter in a fight -- police, weapons, objective --
            // get the pictograms; the standing fixtures stay as plain discs so they recede.
            minimap.GiverBlipTemplate = MakeBlip(area, "GiverBlip", circle, UiTheme.Gold, 11f);
            minimap.ObjectiveBlip = MakeBlip(area, "ObjectiveBlip", circle, UiTheme.Accent, 13f);

            // Police: the kit's shield, in the shield blue already used for the armour bar.
            minimap.PoliceBlipTemplate = MakeBlip(area, "PoliceBlip", UiTheme.Icon("shield"),
                                                  UiTheme.Shield, 15f);
            // Weapons on the ground: the same gun pictogram the FIRE button and the ammo
            // readout use, so the three places a weapon is represented all agree.
            minimap.WeaponBlipTemplate = MakeBlip(area, "WeaponBlip", UiTheme.Icon("gun"),
                                                  UiTheme.Danger, 11f);
            // Shops and for-sale property: fixtures, so discs rather than pictograms.
            minimap.ShopBlipTemplate = MakeBlip(area, "ShopBlip", UiTheme.Icon("shop"),
                                                UiTheme.Positive, 13f);
            minimap.PropertyBlipTemplate = MakeBlip(area, "PropertyBlip", UiTheme.Icon("home"),
                                                    UiTheme.Lilac, 13f);

            var arrow = LoadSprite("ui_arrow", MakeArrowSprite);
            minimap.PlayerBlip = MakeBlip(area, "PlayerBlip", arrow, Color.white, 15f);

            return minimap;
        }

        /// <summary>Draws the road grid as thin bars, matching the real street layout.</summary>
        internal static void DrawMinimapRoads(GameObject area, Minimap minimap)
        {
            var roads = NewUi(area, "Roads");
            var rrt = roads.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0.5f, 0.5f);
            rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = ((RectTransform)area.transform).sizeDelta;

            Vector2 mapSize = ((RectTransform)area.transform).sizeDelta;
            float worldWidth = minimap.WorldMax.x - minimap.WorldMin.x;
            float pixelsPerMetre = mapSize.x / worldWidth;
            // Minimum 3, not 2. The corner map is 210 units across a 1,660 m city, which puts
            // a real street under half a unit wide -- at 2 px and low alpha the grid was there
            // in the hierarchy and invisible on the screen.
            float roadPixels = Mathf.Max(3f, CityBuilder.RoadWidth * pixelsPerMetre);

            // Steel blue: the streets read as a schematic under the blips rather than competing
            // with them, but they have to be legible at the corner map's 3 px first.
            var colour = new Color(UiTheme.Shield.r, UiTheme.Shield.g, UiTheme.Shield.b, 0.55f);

            for (int i = 0; i <= CityBuilder.Blocks; i++)
            {
                Vector3 centre = RoadNetworkBuilder.IntersectionCentre(i, i);

                // North-south street at this x.
                var ns = NewUi(roads, "NS" + i);
                var nsRt = ns.GetComponent<RectTransform>();
                nsRt.anchorMin = nsRt.anchorMax = new Vector2(0.5f, 0.5f);
                nsRt.sizeDelta = new Vector2(roadPixels, mapSize.y);
                nsRt.anchoredPosition = new Vector2(minimap.WorldToMap(centre).x, 0f);
                var nsImage = ns.AddComponent<Image>();
                nsImage.color = colour;
                nsImage.raycastTarget = false;

                // East-west street at this z.
                var ew = NewUi(roads, "EW" + i);
                var ewRt = ew.GetComponent<RectTransform>();
                ewRt.anchorMin = ewRt.anchorMax = new Vector2(0.5f, 0.5f);
                ewRt.sizeDelta = new Vector2(mapSize.x, roadPixels);
                ewRt.anchoredPosition = new Vector2(0f, minimap.WorldToMap(centre).y);
                var ewImage = ew.AddComponent<Image>();
                ewImage.color = colour;
                ewImage.raycastTarget = false;
            }
        }

        internal static RectTransform MakeBlip(GameObject parent, string name, Sprite sprite,
                                      Color colour, float size)
        {
            var go = NewUi(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = colour;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>
        /// Objective panel, result banner, toast, money/level readout, and the off-screen
        /// objective arrow.
        /// </summary>
        static MissionHud BuildMissionUi(GameObject canvas, Sprite circle)
        {
            var font = UiTheme.Font;

            var root = NewUi(canvas, "MissionHud");
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            var hud = root.AddComponent<MissionHud>();

            // --- Objective panel, left edge under the vitals ------------------------------
            var panel = NewUi(root, "ObjectivePanel");
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(34f, -320f);
            prt.sizeDelta = new Vector2(360f, 150f);

            hud.ObjectivePanel = panel.AddComponent<CanvasGroup>();
            hud.ObjectivePanel.alpha = 0f;

            var panelBg = panel.AddComponent<Image>();
            UiTheme.StylePanel(panelBg, UiTheme.Card);
            panelBg.color = new Color(1f, 1f, 1f, 0.92f);
            panelBg.raycastTarget = false;

            // The mission's own colours come off the component, so they are set from the theme
            // here for the same reason the vitals are: leaving them makes the banner flash the
            // pre-re-skin green and red over an indigo interface.
            hud.SuccessColour = UiTheme.Positive;
            hud.FailureColour = UiTheme.Danger;
            hud.TimerNormal = UiTheme.TextOnPanel;
            hud.TimerUrgent = UiTheme.Danger;

            // Inset 22 from the left and 20 from the top: the plate's painted frame is 17 px
            // wide and 14 tall, and four labels laid out from the panel's raw corner were all
            // sitting partly on it.
            hud.MissionTitle = Label(panel, "Title", new Vector2(22f, -20f), new Vector2(316f, 26f),
                                     font, UiTheme.Small, TextAnchor.UpperLeft,
                                     UiTheme.Accent, FontStyle.Bold);
            hud.ObjectiveLine = Label(panel, "Objective", new Vector2(22f, -50f), new Vector2(316f, 44f),
                                      font, UiTheme.Body, TextAnchor.UpperLeft,
                                      UiTheme.TextOnPanel, FontStyle.Normal);
            hud.TimerLine = Label(panel, "Timer", new Vector2(22f, -100f), new Vector2(150f, 30f),
                                  font, UiTheme.Label, TextAnchor.UpperLeft,
                                  UiTheme.TextOnPanel, FontStyle.Bold);
            hud.DistanceLine = Label(panel, "Distance", new Vector2(186f, -100f), new Vector2(152f, 30f),
                                     font, UiTheme.Body, TextAnchor.UpperRight,
                                     UiTheme.TextOnPanelDim, FontStyle.Normal);

            // --- Money and level, top-right under the weapon readout ----------------------
            // The top-right column, top to bottom: wanted stars (-36), weapon and ammunition
            // (-104), this (-208), the store button (-328). The weapon readout was inserted
            // above this one because it is the only line in the column that changes during a
            // fight; money and level are checked between them. Everything below moved down by
            // its height plus a gap rather than being shuffled to another corner.
            var wallet = NewUi(root, "Wallet");
            var wrt = wallet.GetComponent<RectTransform>();
            wrt.anchorMin = new Vector2(1f, 1f);
            wrt.anchorMax = new Vector2(1f, 1f);
            wrt.pivot = new Vector2(1f, 1f);
            wrt.anchoredPosition = new Vector2(-46f, -208f);
            wrt.sizeDelta = new Vector2(300f, 96f);

            // Money in the kit's coin gold with its coin beside it; level in the metallic grey
            // one step below it in the hierarchy. Instruction 8: the difference between these
            // two lines is size, weight and colour all pulling the same way, not size alone.
            var coin = NewUi(wallet, "CoinIcon");
            var crt2 = coin.GetComponent<RectTransform>();
            crt2.anchorMin = crt2.anchorMax = new Vector2(1f, 1f);
            crt2.pivot = new Vector2(1f, 1f);
            crt2.anchoredPosition = new Vector2(-186f, -2f);
            crt2.sizeDelta = new Vector2(38f, 38f);

            var coinImage = coin.AddComponent<Image>();
            coinImage.sprite = UiTheme.Coin;
            coinImage.preserveAspect = true;
            coinImage.raycastTarget = false;

            hud.MoneyLabel = Label(wallet, "Money", new Vector2(0f, 0f), new Vector2(300f, 44f),
                                   font, UiTheme.Heading, TextAnchor.UpperRight,
                                   UiTheme.Gold, FontStyle.Bold);
            hud.LevelLabel = Label(wallet, "Level", new Vector2(0f, -46f), new Vector2(300f, 24f),
                                   font, UiTheme.Small, TextAnchor.UpperRight,
                                   UiTheme.TextOnPanelDim, FontStyle.Bold);

            var xpTrack = NewUi(wallet, "XpTrack");
            var xrt = xpTrack.GetComponent<RectTransform>();
            xrt.anchorMin = new Vector2(1f, 1f);
            xrt.anchorMax = new Vector2(1f, 1f);
            xrt.pivot = new Vector2(1f, 1f);
            xrt.anchoredPosition = new Vector2(0f, -76f);
            xrt.sizeDelta = new Vector2(180f, 16f);

            // The thin mission-bar art, NOT the chunky level bar: the level bar's frame is
            // 11 px top and 10 bottom, so at the 16 units this readout has room for it would
            // be nothing but frame. The frame audit catches exactly this and said so.
            var xpBg = xpTrack.AddComponent<Image>();
            xpBg.sprite = UiTheme.BarTrack;
            xpBg.type = Image.Type.Sliced;
            xpBg.pixelsPerUnitMultiplier = 1f;
            xpBg.color = Color.white;
            xpBg.raycastTarget = false;

            var xpFill = NewUi(xpTrack, "XpFill");
            var xfrt = xpFill.GetComponent<RectTransform>();
            xfrt.anchorMin = Vector2.zero;
            xfrt.anchorMax = Vector2.one;
            xfrt.offsetMin = new Vector2(2f, 2f);
            xfrt.offsetMax = new Vector2(-2f, -2f);

            hud.XpFill = xpFill.AddComponent<Image>();
            hud.XpFill.sprite = UiTheme.BarFill;
            hud.XpFill.color = UiTheme.Lilac;
            hud.XpFill.type = Image.Type.Filled;
            hud.XpFill.fillMethod = Image.FillMethod.Horizontal;
            hud.XpFill.fillAmount = 0f;
            hud.XpFill.raycastTarget = false;

            // --- Result banner and toast --------------------------------------------------
            var banner = NewUi(root, "MissionResult");
            var brt = banner.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0f, 150f);
            brt.sizeDelta = new Vector2(900f, 150f);

            hud.ResultBanner = banner.AddComponent<CanvasGroup>();
            hud.ResultBanner.alpha = 0f;

            hud.ResultTitle = Label(banner, "ResultTitle", new Vector2(0f, 0f), new Vector2(900f, 80f),
                                    font, UiTheme.Display, TextAnchor.MiddleCenter,
                                    UiTheme.TextOnPanel, FontStyle.Bold);
            hud.ResultDetail = Label(banner, "ResultDetail", new Vector2(0f, -76f), new Vector2(900f, 40f),
                                     font, UiTheme.Label, TextAnchor.MiddleCenter,
                                     UiTheme.TextOnPanelDim, FontStyle.Normal);

            var toast = NewUi(root, "Toast");
            var trt = toast.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.anchoredPosition = new Vector2(0f, 250f);
            trt.sizeDelta = new Vector2(760f, 46f);

            hud.ToastGroup = toast.AddComponent<CanvasGroup>();
            hud.ToastGroup.alpha = 0f;

            hud.ToastLabel = Label(toast, "ToastLabel", Vector2.zero, new Vector2(760f, 46f),
                                   font, UiTheme.Label, TextAnchor.MiddleCenter,
                                   UiTheme.Accent, FontStyle.Bold);

            // --- Off-screen objective arrow -----------------------------------------------
            var arrowSprite = LoadSprite("ui_arrow", MakeArrowSprite);

            var arrow = NewUi(root, "ObjectiveArrow");
            var arrt = arrow.GetComponent<RectTransform>();
            arrt.anchorMin = arrt.anchorMax = new Vector2(0.5f, 0.5f);
            arrt.pivot = new Vector2(0.5f, 0.5f);
            arrt.sizeDelta = new Vector2(56f, 56f);

            var arrowImage = arrow.AddComponent<Image>();
            arrowImage.sprite = arrowSprite;
            arrowImage.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.95f);
            arrowImage.raycastTarget = false;

            var arrowLabel = Label(arrow, "ArrowDistance", new Vector2(0f, -46f), new Vector2(140f, 26f),
                                   font, UiTheme.Small, TextAnchor.MiddleCenter,
                                   UiTheme.Accent, FontStyle.Bold);
            arrowLabel.rectTransform.anchorMin = arrowLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            arrowLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            var marker = root.AddComponent<ObjectiveMarker>();
            marker.Arrow = arrt;
            marker.ArrowImage = arrowImage;
            marker.ArrowDistance = arrowLabel;
            marker.Beam = BuildObjectiveBeam();

            return hud;
        }

        /// <summary>
        /// The advertising surfaces: the full-screen mock ad, the menu banner, the rewarded
        /// offer prompt, the Remove Ads storefront, and the two buttons that reach them.
        ///
        /// Built last so every ad panel sorts above the gameplay HUD -- an ad the joystick can
        /// be pressed through is not testing what a real ad does.
        /// </summary>
        static void BuildAdUi(GameObject canvas, Sprite circle)
        {
            var font = UiTheme.Font;

            BuildAdOverlay(canvas, font);
            BuildAdOfferPrompt(canvas, font);
            BuildStorePanel(canvas, font);
            BuildAdButtons(canvas, circle, font);
        }

        static void BuildAdOverlay(GameObject canvas, Font font)
        {
            var root = NewUi(canvas, "AdOverlay");
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            var overlay = root.AddComponent<AdOverlayUI>();

            // --- Full-screen ad ---------------------------------------------------------
            var panel = NewUi(root, "Fullscreen");
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            var group = panel.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            // The kit's deep-space ground rather than a flat near-black, so a full-screen ad
            // still reads as part of this interface.
            var fill = panel.AddComponent<Image>();
            fill.color = new Color(UiTheme.PanelDeepest.r, UiTheme.PanelDeepest.g,
                                   UiTheme.PanelDeepest.b, 0.985f);

            var headline = Label(panel, "Headline", new Vector2(-450f, 120f), new Vector2(900f, 70f),
                                 font, UiTheme.Title, TextAnchor.MiddleCenter,
                                 UiTheme.Gold, FontStyle.Bold);
            CentreLabel(headline, new Vector2(0f, 110f), new Vector2(900f, 70f));

            var subline = Label(panel, "Subline", Vector2.zero, new Vector2(900f, 40f),
                                font, UiTheme.Body, TextAnchor.MiddleCenter,
                                UiTheme.TextOnPanelDim, FontStyle.Normal);
            CentreLabel(subline, new Vector2(0f, 50f), new Vector2(900f, 40f));

            var countdown = Label(panel, "Countdown", Vector2.zero, new Vector2(300f, 60f),
                                  font, UiTheme.Title, TextAnchor.MiddleCenter,
                                  UiTheme.TextOnPanel, FontStyle.Bold);
            CentreLabel(countdown, new Vector2(0f, -30f), new Vector2(300f, 60f));

            // Loading bar under the countdown, in the kit's bar art (instruction 6). This one
            // is the closest thing the game has to a loading screen, so it is the piece that
            // has to look like one.
            var track = NewUi(panel, "ProgressTrack");
            var trackRt = track.GetComponent<RectTransform>();
            trackRt.anchorMin = trackRt.anchorMax = new Vector2(0.5f, 0.5f);
            trackRt.pivot = new Vector2(0.5f, 0.5f);
            trackRt.anchoredPosition = new Vector2(0f, -92f);
            trackRt.sizeDelta = new Vector2(520f, 20f);

            var trackImage = track.AddComponent<Image>();
            trackImage.sprite = UiTheme.BarTrack;
            trackImage.type = Image.Type.Sliced;
            trackImage.pixelsPerUnitMultiplier = 1f;
            trackImage.color = Color.white;

            var progress = NewUi(track, "ProgressFill");
            var progressRt = progress.GetComponent<RectTransform>();
            progressRt.anchorMin = Vector2.zero;
            progressRt.anchorMax = Vector2.one;
            progressRt.offsetMin = new Vector2(3f, 3f);
            progressRt.offsetMax = new Vector2(-3f, -3f);

            var progressImage = progress.AddComponent<Image>();
            progressImage.sprite = UiTheme.BarFill;
            progressImage.color = UiTheme.Accent;
            progressImage.type = Image.Type.Filled;
            progressImage.fillMethod = Image.FillMethod.Horizontal;
            progressImage.fillAmount = 0f;

            var skip = MakePanelButton(panel, "Skip", "SKIP",
                                       new Vector2(0f, 0f), new Vector2(220f, 56f), font,
                                       UiTheme.Body, UiTheme.ButtonTone.Quiet);
            var skipRt = (RectTransform)skip.transform;
            skipRt.anchorMin = skipRt.anchorMax = new Vector2(0.5f, 0.5f);
            skipRt.pivot = new Vector2(0.5f, 0.5f);
            skipRt.anchoredPosition = new Vector2(0f, -170f);

            overlay.Panel = group;
            overlay.HeadlineLabel = headline;
            overlay.SublineLabel = subline;
            overlay.CountdownLabel = countdown;
            overlay.ProgressFill = progressImage;
            overlay.SkipButton = skip;
            overlay.SkipLabel = skip.transform.Find("Label").GetComponent<Text>();

            // --- Banner ------------------------------------------------------------------
            var banner = NewUi(root, "Banner");
            var brt = banner.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0f);
            brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 12f);
            brt.sizeDelta = new Vector2(640f, 90f);

            var bannerGroup = banner.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.blocksRaycasts = false;

            var bannerBg = banner.AddComponent<Image>();
            UiTheme.StylePanel(bannerBg, UiTheme.Row);

            // Inset past the plate's own 10/12 px frame rather than stretched over it.
            var bannerLabel = Label(banner, "BannerLabel", Vector2.zero, new Vector2(596f, 62f),
                                    font, UiTheme.Body, TextAnchor.MiddleCenter,
                                    UiTheme.TextOnPanelDim, FontStyle.Normal);
            CentreLabel(bannerLabel, Vector2.zero, new Vector2(596f, 62f));

            overlay.Banner = bannerGroup;
            overlay.BannerLabel = bannerLabel;
        }

        static void BuildAdOfferPrompt(GameObject canvas, Font font)
        {
            var root = NewUi(canvas, "AdOfferPrompt");
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            Dimmer(root);

            var card = NewUi(root, "Card");
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            // 900x460 rather than 760x320: this container paints an 87 px frame, so the old
            // card had 586x146 of usable space for two 62-tall buttons and two lines of text.
            crt.sizeDelta = new Vector2(900f, 460f);

            var cardBg = card.AddComponent<Image>();
            UiTheme.StylePanel(cardBg, UiTheme.Popup);

            var title = Label(card, "Title", new Vector2(100f, -100f), new Vector2(700f, 50f),
                              font, UiTheme.Heading, TextAnchor.UpperCenter,
                              UiTheme.Accent, FontStyle.Bold);
            var body = Label(card, "Body", new Vector2(100f, -158f), new Vector2(700f, 60f),
                             font, UiTheme.Body, TextAnchor.UpperCenter,
                             UiTheme.TextOnPanel, FontStyle.Normal);

            // WATCH is the offer, so it takes the gold family; declining takes the quiet one.
            var accept = MakePanelButton(card, "Accept", "WATCH",
                                         new Vector2(140f, -252f), new Vector2(280f, 62f), font,
                                         UiTheme.Body, UiTheme.ButtonTone.Strong);
            var decline = MakePanelButton(card, "Decline", "NO THANKS",
                                          new Vector2(480f, -252f), new Vector2(280f, 62f), font,
                                          UiTheme.Body, UiTheme.ButtonTone.Quiet);

            // Timeout bar along the bottom of the card, inset from the panel art's own frame so
            // it does not run out past the rounded corners.
            var timeoutTrack = NewUi(card, "TimeoutTrack");
            var ttRt = timeoutTrack.GetComponent<RectTransform>();
            ttRt.anchorMin = new Vector2(0f, 0f);
            ttRt.anchorMax = new Vector2(1f, 0f);
            ttRt.pivot = new Vector2(0.5f, 0f);
            ttRt.offsetMin = new Vector2(110f, 100f);
            ttRt.offsetMax = new Vector2(-110f, 116f);

            var timeoutImage = timeoutTrack.AddComponent<Image>();
            timeoutImage.sprite = UiTheme.BarFill;
            timeoutImage.color = UiTheme.Gold;
            timeoutImage.type = Image.Type.Filled;
            timeoutImage.fillMethod = Image.FillMethod.Horizontal;
            timeoutImage.fillAmount = 1f;

            var prompt = root.AddComponent<AdOfferPrompt>();
            prompt.Panel = group;
            prompt.TitleLabel = title;
            prompt.BodyLabel = body;
            prompt.AcceptButton = accept;
            prompt.AcceptLabel = accept.transform.Find("Label").GetComponent<Text>();
            prompt.DeclineButton = decline;
            prompt.DeclineLabel = decline.transform.Find("Label").GetComponent<Text>();
            prompt.TimeoutFill = timeoutImage;
        }

        static void BuildStorePanel(GameObject canvas, Font font)
        {
            var root = NewUi(canvas, "StorePanel");
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            Dimmer(root);

            var card = NewUi(root, "Card");
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            // Grown to fit inside this container's 87 px frame: usable x 87..913, y -87..-533.
            crt.sizeDelta = new Vector2(1000f, 620f);

            var cardBg = card.AddComponent<Image>();
            UiTheme.StylePanel(cardBg, UiTheme.Popup);

            Label(card, "Title", new Vector2(100f, -100f), new Vector2(560f, 46f),
                  font, UiTheme.Heading, TextAnchor.UpperLeft, UiTheme.Accent, FontStyle.Bold)
                .text = "STORE";

            var status = Label(card, "Status", new Vector2(100f, -152f), new Vector2(700f, 60f),
                               font, UiTheme.Small, TextAnchor.UpperLeft,
                               UiTheme.TextOnPanelDim, FontStyle.Normal);

            var removeAds = MakePanelButton(card, "RemoveAds", "REMOVE ADS",
                                            new Vector2(100f, -226f), new Vector2(700f, 70f), font,
                                            UiTheme.Label, UiTheme.ButtonTone.Strong);
            var restore = MakePanelButton(card, "Restore", "RESTORE PURCHASES",
                                          new Vector2(100f, -310f), new Vector2(340f, 56f), font,
                                          UiTheme.Small);
            var revoke = MakePanelButton(card, "Revoke", "REVOKE (TEST)",
                                         new Vector2(460f, -310f), new Vector2(340f, 56f), font,
                                         UiTheme.Small);
            var close = MakePanelButton(card, "Close", "CLOSE",
                                        new Vector2(560f, -462f), new Vector2(240f, 56f), font,
                                        UiTheme.Body, UiTheme.ButtonTone.Quiet);

            var store = root.AddComponent<StorePanel>();
            store.Available = UiTheme.Positive;
            store.Owned = UiTheme.Metallic;
            store.Busy = UiTheme.Gold;
            store.Panel = group;
            store.StatusLabel = status;
            store.RemoveAdsButton = removeAds;
            store.RemoveAdsLabel = removeAds.transform.Find("Label").GetComponent<Text>();
            store.RestoreButton = restore;
            store.RevokeButton = revoke;
            store.CloseButton = close;
        }

        /// <summary>Store access and the player-initiated "lose the heat" offer.</summary>
        static void BuildAdButtons(GameObject canvas, Sprite circle, Font font)
        {
            // Store button, tucked under the wanted stars.
            // -224, not -196: the wallet above it is 96 units tall from -104, so its XP bar
            // finished at -200 and the button's top edge was 4 units below it. Seen in a
            // play-mode frame over the city, not in any assertion.
            // 104 units, not 96: at 96 these measured 6.7 mm against a 7 mm touch floor on a
            // 20:9 phone. They are corner-anchored with nothing packed against them, so the
            // extra 8 units cost nothing in layout.
            var storeButton = MakeHudCircleButton(canvas, "StoreButton", circle, font, "shop",
                                                  new Vector2(1f, 1f), new Vector2(-46f, -328f), 104f,
                                                  "STORE");

            // Reference only -- StorePanel.Awake wires it at runtime.
            //
            // This used to be an onClick.AddListener lambda. A lambda cannot be serialised into
            // the scene under any circumstances, so the listener existed only in the editor
            // session that built the scene: the button worked when tested straight after a
            // rebuild and was completely dead in the APK.
            var storePanel = Object.FindAnyObjectByType<StorePanel>(FindObjectsInactive.Include);
            if (storePanel != null) storePanel.OpenButton = storeButton;
            else Debug.LogWarning("[Assembler] StorePanel not found; store button left unwired.");

            // Clear-wanted button, left of the store button, only while wanted.
            var clearGo = NewUi(canvas, "ClearWantedButton");
            var crt = clearGo.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-168f, -224f);
            crt.sizeDelta = new Vector2(104f, 104f);

            var clearGroup = clearGo.AddComponent<CanvasGroup>();
            clearGroup.alpha = 0f;

            var clearImage = clearGo.AddComponent<Image>();
            var clearButton = clearGo.AddComponent<Button>();

            // The kit's gold (strong) family: this is the one HUD control that costs the
            // player something, so it should not look like the neutral ones around it.
            UiTheme.StyleButton(clearButton, clearImage, UiTheme.ButtonTone.Strong,
                                UiTheme.ButtonShape.Square);

            var clearIcon = NewUi(clearGo, "Icon");
            var cirt = clearIcon.GetComponent<RectTransform>();
            cirt.anchorMin = cirt.anchorMax = new Vector2(0.5f, 0.5f);
            cirt.pivot = new Vector2(0.5f, 0.5f);
            cirt.anchoredPosition = Vector2.zero;
            cirt.sizeDelta = new Vector2(44f, 44f);

            var clearGlyph = clearIcon.AddComponent<Image>();
            clearGlyph.sprite = UiTheme.Icon("skull");
            clearGlyph.color = UiTheme.TextOnButton;
            clearGlyph.preserveAspect = true;
            clearGlyph.raycastTarget = false;

            var clearLabel = Label(clearGo, "Caption", Vector2.zero, new Vector2(110f, 24f),
                                   font, UiTheme.Micro, TextAnchor.UpperCenter,
                                   UiTheme.Gold, FontStyle.Bold);
            clearLabel.text = "LOSE HEAT";
            CentreLabel(clearLabel, new Vector2(0f, -62f), new Vector2(110f, 24f));

            var clear = clearGo.AddComponent<ClearWantedButton>();
            clear.Button = clearButton;
        }

        /// <summary>
        /// A square HUD control that sits over the world: pause, map, store, lose-heat.
        ///
        /// Takes the kit's square button art in the quiet family, and one of its pictograms
        /// rather than a word -- these are 78 to 96 units across, which is not enough room for
        /// a legible caption in a display face, and "II" for pause was never a word anyway.
        /// A caption is drawn under the button only where one was asked for.
        /// </summary>
        internal static Button MakeHudCircleButton(GameObject parent, string name, Sprite circle, Font font,
                                          string icon, Vector2 anchor, Vector2 position, float size,
                                          string caption = null)
        {
            var go = NewUi(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(size, size);

            var image = go.AddComponent<Image>();
            var button = go.AddComponent<Button>();
            UiTheme.StyleButton(button, image, UiTheme.ButtonTone.Quiet, UiTheme.ButtonShape.Square);
            // Slightly translucent: these are over the street, and an opaque plate at each
            // corner closes the frame in.
            image.color = new Color(1f, 1f, 1f, 0.90f);

            var glyphGo = NewUi(go, "Icon");
            var grt = glyphGo.GetComponent<RectTransform>();
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = Vector2.zero;
            grt.sizeDelta = new Vector2(size * 0.46f, size * 0.46f);

            var glyph = glyphGo.AddComponent<Image>();
            glyph.sprite = UiTheme.Icon(icon);
            glyph.color = UiTheme.TextOnButton;
            glyph.preserveAspect = true;
            glyph.raycastTarget = false;

            if (!string.IsNullOrEmpty(caption))
            {
                // Outside the button, not inside it: PHASE12 defect 1 was a rail caption placed
                // on the button art's bevel, which read as a smudge at both test resolutions.
                // Width capped at the button's own footprint plus a hair. These captions sit
                // under a row of buttons 112 units apart, and at size+40 the neighbouring
                // words ran into each other -- "LOSE HEAT" printed through "STORE".
                var box = new Vector2(size + 14f, 24f);
                var label = Label(go, "Caption", Vector2.zero, box,
                                  font, UiTheme.Micro, TextAnchor.UpperCenter,
                                  UiTheme.TextOnPanel, FontStyle.Bold);
                label.text = caption;
                CentreLabel(label, new Vector2(0f, -size * 0.5f - 14f), box);
            }

            return button;
        }

        /// <summary>Re-anchors a Label() to centre-anchored positioning.</summary>
        internal static void CentreLabel(Text label, Vector2 position, Vector2 size)
        {
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        /// <summary>
        /// The shop / garage / fast-travel panel. One modal panel serves all three; rows are
        /// cloned at runtime from the hidden template built here.
        /// </summary>
        static ShopUI BuildShopUi(GameObject canvas, Sprite circle)
        {
            var font = UiTheme.Font;

            var root = NewUi(canvas, "ShopUI");
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            // Full-screen dim: also the thing that swallows taps meant for the panel, so the
            // joystick underneath cannot be driven while shopping.
            Dimmer(root);

            // NOT the kit's `shop-container`, despite this being the shop. Its painted frame
            // is 165 px on every side, which on a 920x660 sheet leaves 590x330 -- the stock
            // rows alone are 828 wide. The first pass used it and the rows were drawn out past
            // both edges of the panel. Same 88 px container the pause menu uses, made bigger.
            var panel = NewUi(root, "Panel");
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1000f, 760f);

            var panelBg = panel.AddComponent<Image>();
            UiTheme.StylePanel(panelBg, UiTheme.Panel);

            // Everything below is laid out from the panel's top-left corner, inset past the
            // 90 px frame: x from 100 to 900, y from -100 to -670.
            var title = Label(panel, "Title", new Vector2(100f, -100f), new Vector2(500f, 44f),
                              font, UiTheme.Heading, TextAnchor.UpperLeft,
                              UiTheme.Accent, FontStyle.Bold);
            var subtitle = Label(panel, "Subtitle", new Vector2(100f, -146f), new Vector2(500f, 30f),
                                 font, UiTheme.Small, TextAnchor.UpperLeft,
                                 UiTheme.TextOnPanelDim, FontStyle.Normal);
            var money = Label(panel, "Money", new Vector2(560f, -100f), new Vector2(340f, 44f),
                              font, UiTheme.Heading, TextAnchor.UpperRight,
                              UiTheme.Gold, FontStyle.Bold);

            // Garage-only controls.
            var vehicleLabel = Label(panel, "VehicleLabel", new Vector2(100f, -190f),
                                     new Vector2(520f, 28f), font, UiTheme.Small,
                                     TextAnchor.UpperLeft, UiTheme.Shield, FontStyle.Bold);

            var cycleButton = MakePanelButton(panel, "CycleVehicle", "NEXT VEHICLE",
                                              new Vector2(660f, -184f), new Vector2(240f, 44f),
                                              font, UiTheme.Small);

            var rowContainer = NewUi(panel, "Rows");
            var rowsRt = rowContainer.GetComponent<RectTransform>();
            rowsRt.anchorMin = new Vector2(0f, 1f);
            rowsRt.anchorMax = new Vector2(0f, 1f);
            rowsRt.pivot = new Vector2(0f, 1f);
            rowsRt.anchoredPosition = new Vector2(100f, -238f);
            rowsRt.sizeDelta = new Vector2(800f, 340f);

            var rowTemplate = BuildShopRowTemplate(rowContainer, font);

            var closeButton = MakePanelButton(panel, "Close", "CLOSE",
                                              new Vector2(660f, -604f), new Vector2(240f, 48f),
                                              font, UiTheme.Small, UiTheme.ButtonTone.Quiet);

            var ui = root.AddComponent<ShopUI>();
            // The on-button variants: a stock row is one of the kit's light painted buttons,
            // so the status word sits on lilac, not on the dark panel behind it.
            ui.Affordable = UiTheme.AccentOnButton;
            ui.Blocked = UiTheme.DangerOnButton;
            ui.Owned = UiTheme.MetallicOnButton;
            ui.Panel = group;
            ui.TitleLabel = title;
            ui.SubtitleLabel = subtitle;
            ui.MoneyLabel = money;
            ui.RowTemplate = rowTemplate;
            ui.RowContainer = rowsRt;
            ui.CloseButton = closeButton;
            ui.CycleVehicleButton = cycleButton;
            ui.VehicleLabel = vehicleLabel;
            return ui;
        }

        /// <summary>One line of stock: name, description, price and a buy state, all clickable.</summary>
        static RectTransform BuildShopRowTemplate(GameObject parent, Font font)
        {
            var row = NewUi(parent, "RowTemplate");
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(800f, 66f);

            var bg = row.AddComponent<Image>();
            var button = row.AddComponent<Button>();
            // Slim: a stock row is 66 units tall and the wide button art's 96px silhouette
            // would put more frame than face on it.
            UiTheme.StyleButton(button, bg, UiTheme.ButtonTone.Quiet, UiTheme.ButtonShape.Slim);

            // No colour overrides here. StyleButton already gives the row all four of the kit's
            // painted states, and the previous highlightedColor of 1.4 grey was blowing the
            // painted art out rather than highlighting it.

            Label(row, "Name", new Vector2(24f, -8f), new Vector2(390f, 28f),
                  font, UiTheme.Body, TextAnchor.UpperLeft, UiTheme.TextOnButton, FontStyle.Bold);
            Label(row, "Description", new Vector2(24f, -34f), new Vector2(390f, 24f),
                  font, UiTheme.Micro, TextAnchor.UpperLeft, UiTheme.TextOnButtonDim,
                  FontStyle.Normal);
            Label(row, "Price", new Vector2(424f, -18f), new Vector2(190f, 34f),
                  font, UiTheme.Label, TextAnchor.UpperRight, UiTheme.TextOnButton, FontStyle.Bold);
            Label(row, "Status", new Vector2(622f, -18f), new Vector2(154f, 34f),
                  font, UiTheme.Body, TextAnchor.UpperRight, UiTheme.AccentOnButton,
                  FontStyle.Bold);

            row.SetActive(false);
            return rt;
        }

        internal static Button MakePanelButton(GameObject parent, string name, string caption,
                                      Vector2 position, Vector2 size, Font font, int fontSize,
                                      UiTheme.ButtonTone tone = UiTheme.ButtonTone.Normal)
        {
            var go = NewUi(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var image = go.AddComponent<Image>();
            var button = go.AddComponent<Button>();
            UiTheme.StyleButton(button, image, tone);

            // TextOnButton, not TextOnPanel: this caption is on the kit's light painted art,
            // and the panel behind it is dark. See the note at the top of UiTheme.
            var label = Label(go, "Label", Vector2.zero, size, font, fontSize,
                              TextAnchor.MiddleCenter, UiTheme.TextOnButton, FontStyle.Bold);
            // Label() styles the text but does not fill it; without this the button renders as
            // a blank rectangle.
            label.text = caption;
            label.rectTransform.anchoredPosition = Vector2.zero;

            return button;
        }

        /// <summary>Rotating pillar of light that stands at the objective in the world.</summary>
        static Transform BuildObjectiveBeam()
        {
            // Every previous beam, not just a visible one.
            //
            // This used to be GameObject.Find, which only returns *active* objects -- and the
            // last line of this method deactivates the beam, because it is only shown while a
            // mission has an objective point. So the cleanup never matched its own output and
            // every rebuild leaked another beam into the scene root. Twenty-two of them had
            // accumulated by the time it was noticed.
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t == null || t.name != "ObjectiveBeam" || t.parent != null) continue;
                Object.DestroyImmediate(t.gameObject);
            }

            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "ObjectiveBeam";
            beam.transform.localScale = new Vector3(3.2f, 22f, 3.2f);
            Object.DestroyImmediate(beam.GetComponent<Collider>());

            const string path = "Assets/Game/Materials/ObjectiveBeam.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", new Color(0.3f, 0.95f, 0.5f, 0.22f));
                // Transparent surface so the beam reads as light, not a green pillar.
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", new Color(0.3f, 0.95f, 0.5f) * 1.8f);
                AssetDatabase.CreateAsset(mat, path);
            }

            var renderer = beam.GetComponent<Renderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            beam.SetActive(false);
            return beam.transform;
        }

        internal static Text Label(GameObject parent, string name, Vector2 position, Vector2 size,
                          Font font, int fontSize, TextAnchor anchor, Color colour, FontStyle style)
        {
            var go = NewUi(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var text = go.AddComponent<Text>();
            // The caller's font and size are advisory. Everything renders in the theme face,
            // and every size is snapped onto UiTheme's seven-step scale -- the dynamic font
            // atlas grows an entry per distinct size, and PHASE8 measured 22 of them across
            // this UI. See DECISIONS.md D30.
            text.font = font != null ? font : UiTheme.Font;
            text.fontSize = UiTheme.SnapSize(fontSize);
            text.alignment = anchor;
            text.color = colour;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>
        /// A progress bar in the kit's art: its dark track, its cyan fill, and one of its
        /// icons pinned to the left of the track.
        ///
        /// The fill is <c>Image.Type.Filled</c>, not a scaled rect, so partial values read
        /// smoothly instead of stepping -- and the fill sprite is drawn Filled rather than
        /// Sliced, which means it keeps its own painted highlight rather than tiling it.
        /// </summary>
        static Image BuildVitalBar(GameObject parent, string name, Vector2 position,
                                   Vector2 size, Color colour, string icon, float iconSize)
        {
            if (!string.IsNullOrEmpty(icon))
            {
                var glyphGo = NewUi(parent, name + "Icon");
                var grt = glyphGo.GetComponent<RectTransform>();
                grt.anchorMin = new Vector2(0f, 1f);
                grt.anchorMax = new Vector2(0f, 1f);
                grt.pivot = new Vector2(1f, 0.5f);
                // Right-pivoted and placed a fixed gap left of the track, so the icon and the
                // bar keep their spacing whatever the bar's height is.
                grt.anchoredPosition = new Vector2(position.x - 10f, position.y - size.y * 0.5f);
                grt.sizeDelta = new Vector2(iconSize, iconSize);

                var glyph = glyphGo.AddComponent<Image>();
                glyph.sprite = UiTheme.Icon(icon);
                glyph.color = colour;
                glyph.preserveAspect = true;
                glyph.raycastTarget = false;
            }

            var bg = NewUi(parent, name + "Bg");
            var rt = bg.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var bgImage = bg.AddComponent<Image>();
            bgImage.sprite = UiTheme.BarTrack;
            bgImage.type = Image.Type.Sliced;
            bgImage.pixelsPerUnitMultiplier = 1f;
            bgImage.color = Color.white;
            bgImage.raycastTarget = false;

            // Inset by the track's own rim so the fill sits inside the frame rather than
            // covering it -- the kit paints a lip on the track and a flush fill hides it.
            var fill = NewUi(bg, name + "Fill");
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(3f, 3f);
            frt.offsetMax = new Vector2(-3f, -3f);

            var img = fill.AddComponent<Image>();
            img.sprite = UiTheme.BarFill;
            img.color = colour;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillAmount = 1f;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Speed and condition panel, top-centre, visible only while driving.</summary>
        static VehicleHud BuildVehicleReadout(GameObject canvas, Sprite circle)
        {
            var panel = NewUi(canvas, "VehicleReadout");
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 1f);
            prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0f, -34f);
            prt.sizeDelta = new Vector2(360f, 176f);

            var group = panel.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            // The readout sits on one of the kit's dark plates rather than floating over the
            // road. It is centre-top of the screen with the city moving behind it, which is the
            // worst backdrop in the game for bare text.
            var plate = panel.AddComponent<Image>();
            UiTheme.StylePanel(plate, UiTheme.Card);
            plate.color = new Color(1f, 1f, 1f, 0.88f);
            plate.raycastTarget = false;

            var font = UiTheme.Font;

            var speed = NewUi(panel, "Speed");
            var srt = speed.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 1f);
            srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0f, -14f);
            // 86 tall, not 74: 62 pt in the display face needs 79 units of line box.
            srt.sizeDelta = new Vector2(320f, 86f);

            var speedText = speed.AddComponent<Text>();
            speedText.text = "0";
            speedText.font = UiTheme.Font;
            speedText.fontSize = UiTheme.SnapSize(62);
            speedText.fontStyle = FontStyle.Bold;
            speedText.alignment = TextAnchor.MiddleCenter;
            speedText.color = UiTheme.TextOnPanel;
            speedText.raycastTarget = false;

            var unit = NewUi(panel, "Unit");
            var urt = unit.GetComponent<RectTransform>();
            urt.anchorMin = new Vector2(0.5f, 1f);
            urt.anchorMax = new Vector2(0.5f, 1f);
            urt.pivot = new Vector2(0.5f, 1f);
            urt.anchoredPosition = new Vector2(0f, -84f);
            urt.sizeDelta = new Vector2(320f, 26f);

            var unitText = unit.AddComponent<Text>();
            unitText.text = "KM/H";
            unitText.font = UiTheme.Font;
            unitText.fontSize = UiTheme.SnapSize(20);
            unitText.alignment = TextAnchor.MiddleCenter;
            unitText.color = UiTheme.TextOnPanelDim;
            unitText.raycastTarget = false;

            var name = NewUi(panel, "Name");
            var nrt = name.GetComponent<RectTransform>();
            nrt.anchorMin = new Vector2(0.5f, 1f);
            nrt.anchorMax = new Vector2(0.5f, 1f);
            nrt.pivot = new Vector2(0.5f, 1f);
            nrt.anchoredPosition = new Vector2(0f, -110f);
            nrt.sizeDelta = new Vector2(320f, 24f);

            var nameText = name.AddComponent<Text>();
            nameText.text = "";
            nameText.font = UiTheme.Font;
            nameText.fontSize = UiTheme.SnapSize(18);
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.color = UiTheme.Accent;
            nameText.raycastTarget = false;

            // Health bar: a filled image rather than a scaled rect so partial damage reads
            // smoothly instead of stepping.
            var barBg = NewUi(panel, "HealthBg");
            var bgrt = barBg.GetComponent<RectTransform>();
            bgrt.anchorMin = new Vector2(0.5f, 1f);
            bgrt.anchorMax = new Vector2(0.5f, 1f);
            bgrt.pivot = new Vector2(0.5f, 1f);
            bgrt.anchoredPosition = new Vector2(0f, -142f);
            bgrt.sizeDelta = new Vector2(220f, 16f);

            // Condition bar in the kit's art, matching the health and XP bars elsewhere so the
            // three read as one family (instruction 6).
            var bgImage = barBg.AddComponent<Image>();
            bgImage.sprite = UiTheme.BarTrack;
            bgImage.type = Image.Type.Sliced;
            bgImage.pixelsPerUnitMultiplier = 1f;
            bgImage.color = Color.white;
            bgImage.raycastTarget = false;

            var barFill = NewUi(barBg, "HealthFill");
            var fillRt = barFill.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(3f, 3f);
            fillRt.offsetMax = new Vector2(-3f, -3f);

            var fillImage = barFill.AddComponent<Image>();
            fillImage.sprite = UiTheme.BarFill;
            fillImage.color = UiTheme.Positive;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 1f;
            fillImage.raycastTarget = false;

            var hud = panel.AddComponent<VehicleHud>();
            // Set from the theme for the same reason as the player vitals: these live on the
            // component and would otherwise stay the pre-re-skin green/amber/red.
            hud.Healthy = UiTheme.Positive;
            hud.Damaged = UiTheme.Gold;
            hud.Critical = UiTheme.Danger;
            hud.Group = group;
            hud.SpeedLabel = speedText;
            hud.NameLabel = nameText;
            hud.HealthFill = fillImage;
            return hud;
        }

        internal static GameObject NewUi(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        /// <summary>
        /// A modal's screen dimmer, on its own child so it can bleed past the safe area.
        ///
        /// The panel root stays inside the safe-area container -- that is what keeps the card
        /// centred on it clear of a notch -- but a dimmer that stops at the same boundary
        /// leaves an undimmed strip of the world down each inset edge. See SafeAreaBleed.
        /// </summary>
        static Image Dimmer(GameObject root)
        {
            var go = NewUi(root, "Scrim");
            var image = go.AddComponent<Image>();
            image.color = UiTheme.Scrim;
            image.raycastTarget = true;
            go.AddComponent<SafeAreaBleed>();
            go.transform.SetAsFirstSibling();
            return image;
        }

        // --------------------------------------------------------------- sprites

        internal static Sprite LoadSprite(string name, System.Func<Texture2D> generator)
        {
            Directory.CreateDirectory(UiDir);
            string path = UiDir + "/" + name + ".png";

            if (!File.Exists(path))
            {
                var tex = generator();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            // Applied unconditionally, not just when the type is wrong. The project default
            // imports new textures as Sprite *Multiple*, which produces no single Sprite
            // sub-asset at all -- LoadAssetAtPath returns null and uGUI silently falls back to
            // drawing a plain white quad.
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                bool dirty = importer.textureType != TextureImporterType.Sprite
                             || importer.spriteImportMode != SpriteImportMode.Single
                             || !importer.alphaIsTransparency
                             || importer.mipmapEnabled;

                if (dirty)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.SaveAndReimport();
                }
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogError("[Scene] Sprite failed to load from " + path
                               + " -- HUD controls will render as blank squares.");
            return sprite;
        }

        internal static Texture2D MakeCircleSprite() => MakeRadial(256, 0f, 0.46f, 0.02f);
        internal static Texture2D MakeRingSprite() => MakeRadial(256, 0.40f, 0.47f, 0.015f);


        /// <summary>Upward-pointing triangle, used for the player blip and the objective arrow.</summary>
        internal static Texture2D MakeArrowSprite()
        {
            const int size = 128;

            // Slightly notched base so it reads as an arrowhead rather than a plain triangle.
            var polygon = new[]
            {
                new Vector2(0f, 0.46f),
                new Vector2(0.38f, -0.42f),
                new Vector2(0f, -0.18f),
                new Vector2(-0.38f, -0.42f),
            };

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int inside = 0;
                    for (int s = 0; s < 4; s++)
                    {
                        float sx = x + (s % 2) * 0.5f;
                        float sy = y + (s / 2) * 0.5f;
                        var p = new Vector2((sx - half) / size, (sy - half) / size);
                        if (PointInPolygon(p, polygon)) inside++;
                    }

                    px[y * size + x] = new Color32(255, 255, 255, (byte)(inside * 255 / 4));
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static bool PointInPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (poly[i].y > p.y == poly[j].y > p.y) continue;

                float t = (poly[j].x - poly[i].x) * (p.y - poly[i].y)
                          / (poly[j].y - poly[i].y) + poly[i].x;
                if (p.x < t) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Antialiased filled circle or annulus. Generated rather than shipped so the HUD has
        /// no binary art dependency and stays crisp at any DPI.
        /// </summary>
        static Texture2D MakeRadial(int size, float inner, float outer, float feather)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c * 0.5f;
                    float dy = (y - c) / c * 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(outer, outer - feather, r));
                    if (inner > 0f)
                        a *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, inner + feather, r));

                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
