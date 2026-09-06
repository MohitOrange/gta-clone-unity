using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Generates the car, motorbike and boat prefabs.
    ///
    /// Cars are now built around the "Mobile Optimized Free Low Poly Cars" meshes; the bike and
    /// the boat are still primitives, because that pack ships neither. The physics rig is still
    /// defined in code -- what changed is that the numbers are now <i>measured off the mesh</i>
    /// rather than typed in, so a different car model from the same pack fits its own wheels
    /// without anyone editing a constant.
    ///
    /// The pack gives a body with one MeshRenderer and four named tire children. What it does
    /// not give is a Rigidbody, WheelColliders, seats or damage wiring, which is what this does.
    /// </summary>
    public static class VehicleBuilder
    {
        const string PrefabDir = "Assets/Game/Prefabs";
        const string MatDir = "Assets/Game/Materials";
        const string CarPackDir = "Assets/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs";

        /// <summary>
        /// Six visibly different bodies rather than six recolours of one.
        ///
        /// The pack shares a single 512px colour-atlas material across every model, so using
        /// six models costs exactly as many draw calls as using one -- the variety is free.
        /// Tinting instead would have multiplied that atlas, taking the windows and headlights
        /// with it.
        /// </summary>
        static readonly string[] TrafficModels =
        {
            "Sport Car_39",
            "Hatchback Car_15",
            "N_Muscle Car_10",
            "Classic Car_9",
            "N Van_10",
            "Pick Up_11",
        };

        const string PoliceModel = "Police Car N_4";

        [MenuItem("Tools/Mini GTA/10. Build Vehicle Prefabs", priority = 130)]
        public static void BuildAll()
        {
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(MatDir);

            var created = new List<string>();

            for (int i = 0; i < TrafficModels.Length; i++)
                created.Add(BuildCar("Car_" + i, TrafficModels[i], police: false));

            created.Add(BuildCar("Car_Police", PoliceModel, police: true));
            created.Add(BuildBike("Bike"));
            created.Add(BuildBoat("Boat"));

            AssetDatabase.SaveAssets();
            Debug.Log("[Vehicles] Built " + created.Count(c => c != null) + " prefabs:\n  "
                      + string.Join("\n  ", created.Where(c => c != null)));
        }

        // -------------------------------------------------------------------- car

        /// <summary>
        /// What a pack car turned out to be, once measured. Everything downstream -- hull size,
        /// centre of mass, seat position, camera distance -- is derived from these rather than
        /// from constants that only happened to suit the old primitive car.
        /// </summary>
        struct CarFit
        {
            public Transform Body;
            public Bounds BodyLocal;              // body mesh, in the car root's space
            public Transform[] Tyres;             // FL, FR, RL, RR in that order
            public Vector3[] TyreLocal;
            public float WheelRadius;
        }

        static string BuildCar(string name, string modelName, bool police)
        {
            string modelPath = CarPackDir + "/" + modelName + ".prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null)
            {
                Debug.LogError("[Vehicles] Missing car model " + modelPath
                               + ". Cannot build " + name + ".");
                return null;
            }

            var root = new GameObject(name);

            var body = (GameObject)PrefabUtility.InstantiatePrefab(source);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);

            // The four tyres have to move out from under the body (see Measure), and Unity
            // forbids reparenting a child that still belongs to a prefab instance. Unpacking
            // also makes the vehicle prefab we are about to save the single authority on this
            // car, rather than something that silently re-inherits from the pack on reimport.
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            if (!Measure(root, body, out CarFit fit))
            {
                Object.DestroyImmediate(root);
                return null;
            }

            // --- Physics -------------------------------------------------------------
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1320f;
            rb.linearDamping = 0.06f;
            rb.angularDamping = 2.6f;
            // Continuous stops a fast car tunnelling through a building wall at 130 kph.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // The pack ships its own BoxCollider on the body mesh. Left in place it becomes a
            // second hull in the compound, offset from ours and half-buried in the road.
            foreach (var col in body.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(col);

            var hull = root.AddComponent<BoxCollider>();
            // Reach down to just above the contact patch: the wheel colliders carry the car, so
            // a hull that touches the ground would ride on the road instead of on the springs.
            float hullBottom = 0.05f;
            float hullTop = fit.BodyLocal.max.y;
            hull.center = new Vector3(fit.BodyLocal.center.x,
                                      (hullBottom + hullTop) * 0.5f,
                                      fit.BodyLocal.center.z);
            hull.size = new Vector3(fit.BodyLocal.size.x, hullTop - hullBottom, fit.BodyLocal.size.z);

            var car = root.AddComponent<CarController>();
            car.Kind = VehicleKind.Car;
            car.DisplayName = police ? "Police Cruiser" : DisplayNameFor(modelName);
            // Frame the actual car rather than a fixed 8.2 m that suited a 4.3 m box.
            car.CameraDistance = Mathf.Round((fit.BodyLocal.size.z * 1.75f) * 10f) / 10f;
            car.CameraPivotOffset = new Vector3(0f, fit.BodyLocal.size.y + 0.9f, 0f);
            car.HideOccupant = true;
            car.CentreOfMass = new Vector3(0f, -0.45f, -0.1f);

            var wheelRoot = new GameObject("Wheels");
            wheelRoot.transform.SetParent(root.transform, false);

            car.Wheels = new[]
            {
                MakeWheel(wheelRoot, "FL", fit, 0, steers: true,  drives: false),
                MakeWheel(wheelRoot, "FR", fit, 1, steers: true,  drives: false),
                MakeWheel(wheelRoot, "RL", fit, 2, steers: false, drives: true),
                MakeWheel(wheelRoot, "RR", fit, 3, steers: false, drives: true),
            };

            // Seat: driver's side, at about window height, a little forward of centre.
            AddSeats(root,
                new Vector3(-fit.BodyLocal.size.x * 0.18f, fit.BodyLocal.center.y, 0.05f),
                new Vector3(-(fit.BodyLocal.size.x * 0.5f + 0.7f), 0.2f, 0f));

            if (police) BuildLightbar(root, fit);

            var damage = root.AddComponent<VehicleDamage>();
            // One mesh, so one panel. The body shifts against its own wheels on a hit, which
            // reads as a crumple; the old car had four boxes to crumple independently.
            damage.Panels = new[] { body.transform };
            damage.MaxDent = 0.09f;
            damage.DentRadius = 2.4f;
            damage.BodyRenderers = body.GetComponentsInChildren<MeshRenderer>(true)
                                       .Select(r => (Renderer)r).ToArray();
            damage.Smoke = MakeSmoke(root, new Vector3(0f, fit.BodyLocal.center.y,
                                                       fit.BodyLocal.max.z * 0.9f));

            if (police) root.AddComponent<PoliceVehicle>();

            return SavePrefab(root, name);
        }

        /// <summary>
        /// Pulls the four tyres out of the body and measures everything the rig needs.
        ///
        /// The tyres have to be reparented: they are driven from the wheel colliders every
        /// frame, so leaving them under the body would fight <see cref="VehicleDamage"/>, which
        /// moves the body to crumple it.
        /// </summary>
        static bool Measure(GameObject root, GameObject body, out CarFit fit)
        {
            fit = default;

            var bodyFilter = body.GetComponent<MeshFilter>();
            if (bodyFilter == null || bodyFilter.sharedMesh == null)
            {
                Debug.LogError("[Vehicles] " + body.name + " has no mesh on its root.");
                return false;
            }

            // Tyres are named "<Model> FL Tire" and so on. Match on the corner code, not the
            // full name, so a differently-named model in the same pack still resolves.
            var tyres = new Transform[4];
            string[] codes = { " FL ", " FR ", " BL ", " BR " };
            foreach (Transform child in body.transform)
            {
                string padded = " " + child.name + " ";
                for (int i = 0; i < codes.Length; i++)
                    if (padded.Contains(codes[i])) tyres[i] = child;
            }

            if (tyres.Any(t => t == null))
            {
                Debug.LogError("[Vehicles] " + body.name + " is missing a tyre; found "
                    + string.Join(",", body.transform.Cast<Transform>().Select(t => t.name)));
                return false;
            }

            var wheels = new GameObject("Tyres");
            wheels.transform.SetParent(root.transform, false);

            // Order the rig wants: FL, FR, RL, RR. The pack names the rears B(ack), not R(ear).
            var ordered = new[] { tyres[0], tyres[1], tyres[2], tyres[3] };
            var localPos = new Vector3[4];
            float radius = 0f;

            for (int i = 0; i < ordered.Length; i++)
            {
                localPos[i] = root.transform.InverseTransformPoint(ordered[i].position);
                ordered[i].SetParent(wheels.transform, true);

                var mf = ordered[i].GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    // Axle runs along the mesh's X; the two large extents are the tread circle.
                    Vector3 s = mf.sharedMesh.bounds.size;
                    radius = Mathf.Max(radius, Mathf.Max(s.y, s.z) * 0.5f);
                }
            }

            if (radius < 0.05f)
            {
                Debug.LogWarning("[Vehicles] Could not measure a wheel radius on " + body.name
                                 + "; falling back to the tyre centre height.");
                radius = Mathf.Max(0.25f, localPos[0].y);
            }

            Bounds meshLocal = bodyFilter.sharedMesh.bounds;
            fit = new CarFit
            {
                Body = body.transform,
                BodyLocal = new Bounds(meshLocal.center, meshLocal.size),
                Tyres = ordered,
                TyreLocal = localPos,
                WheelRadius = radius,
            };
            return true;
        }

        static string DisplayNameFor(string modelName)
        {
            // "N_Muscle Car_10" -> "Muscle Car". Model names carry the pack's own numbering.
            string s = modelName;
            int underscore = s.LastIndexOf('_');
            if (underscore > 0) s = s.Substring(0, underscore);
            if (s.StartsWith("N_") || s.StartsWith("N ")) s = s.Substring(2);
            return s.Trim();
        }

        static CarController.Wheel MakeWheel(GameObject parent, string name, CarFit fit, int index,
                                             bool steers, bool drives)
        {
            var colliderGo = new GameObject("WC_" + name);
            colliderGo.transform.SetParent(parent.transform, false);
            colliderGo.transform.localPosition = new Vector3(
                fit.TyreLocal[index].x, fit.WheelRadius, fit.TyreLocal[index].z);

            var wc = colliderGo.AddComponent<WheelCollider>();
            wc.radius = fit.WheelRadius;
            wc.mass = 22f;
            wc.wheelDampingRate = 0.28f;
            wc.suspensionDistance = 0.24f;
            wc.forceAppPointDistance = 0.11f;

            var spring = wc.suspensionSpring;
            spring.spring = 38000f;
            spring.damper = 4600f;
            spring.targetPosition = 0.45f;
            wc.suspensionSpring = spring;

            var fwd = wc.forwardFriction;
            fwd.extremumSlip = 0.36f; fwd.extremumValue = 1.1f;
            fwd.asymptoteSlip = 0.85f; fwd.asymptoteValue = 0.62f;
            fwd.stiffness = 2.0f;
            wc.forwardFriction = fwd;

            var side = wc.sidewaysFriction;
            side.extremumSlip = 0.26f; side.extremumValue = 1.1f;
            side.asymptoteSlip = 0.58f; side.asymptoteValue = 0.72f;
            side.stiffness = 2.3f;
            wc.sidewaysFriction = side;

            return new CarController.Wheel
            {
                Collider = wc,
                Mesh = fit.Tyres[index],
                Steers = steers,
                Drives = drives,
                Brakes = true,
                // The pack's tyres are modelled as wheels, axle already on X. The 90 degree
                // correction exists for Unity's Y-up cylinder primitive and would lay these
                // flat -- PHASE2 bug #4, in reverse. See CarController.TyreMeshCorrection.
                MeshRotationEuler = Vector3.zero,
            };
        }

        /// <summary>Roof lights, sized to whatever body they are going on.</summary>
        static void BuildLightbar(GameObject root, CarFit fit)
        {
            var bar = new GameObject("Lightbar");
            bar.transform.SetParent(root.transform, false);

            float y = fit.BodyLocal.max.y + 0.07f;
            float dx = fit.BodyLocal.size.x * 0.16f;

            Box(bar, "LightbarRed", Mat("Veh_SirenRed", new Color(0.9f, 0.1f, 0.1f), 0.6f),
                new Vector3(-dx, y, -0.18f), new Vector3(0.44f, 0.14f, 0.3f));
            Box(bar, "LightbarBlue", Mat("Veh_SirenBlue", new Color(0.15f, 0.3f, 0.95f), 0.6f),
                new Vector3(dx, y, -0.18f), new Vector3(0.44f, 0.14f, 0.3f));
        }

        // ------------------------------------------------------------------- bike

        static string BuildBike(string name)
        {
            var root = new GameObject(name);

            const float wheelRadius = 0.33f;
            const float axleFront = 0.78f;
            const float axleRear = -0.72f;
            const float bodyY = wheelRadius + 0.28f;

            var frameMat = Mat("Veh_BikeFrame", new Color(0.55f, 0.12f, 0.12f), 0.6f);
            var tyreMat = Mat("Veh_Tyre", new Color(0.07f, 0.07f, 0.08f), 0.15f);

            // Lean pivot: visual parts hang off this so the bike can bank without tilting
            // the wheel colliders, which must stay upright to work.
            var lean = new GameObject("LeanBody");
            lean.transform.SetParent(root.transform, false);
            lean.transform.localPosition = new Vector3(0f, bodyY, 0f);

            Box(lean, "Frame", frameMat, new Vector3(0f, 0f, 0f), new Vector3(0.3f, 0.34f, 1.55f));
            Box(lean, "Tank", frameMat, new Vector3(0f, 0.26f, 0.18f), new Vector3(0.34f, 0.26f, 0.62f));
            Box(lean, "Seat", Mat("Veh_Seat", new Color(0.09f, 0.09f, 0.1f), 0.3f),
                new Vector3(0f, 0.3f, -0.38f), new Vector3(0.3f, 0.16f, 0.6f));
            Box(lean, "Bars", frameMat, new Vector3(0f, 0.46f, 0.66f), new Vector3(0.72f, 0.08f, 0.08f));

            var body = root.AddComponent<Rigidbody>();
            body.mass = 235f;
            body.linearDamping = 0.05f;
            body.angularDamping = 3.2f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var hull = root.AddComponent<CapsuleCollider>();
            hull.direction = 2;                    // Z axis
            hull.center = new Vector3(0f, bodyY, 0f);
            hull.radius = 0.3f;
            hull.height = 1.9f;

            var bike = root.AddComponent<BikeController>();
            bike.Kind = VehicleKind.Bike;
            bike.DisplayName = "Motorbike";
            bike.CameraDistance = 6.6f;
            bike.CameraPivotOffset = new Vector3(0f, 1.7f, 0f);
            bike.HideOccupant = false;             // you should see the rider on a bike
            bike.LeanBody = lean.transform;

            var wheelRoot = new GameObject("Wheels");
            wheelRoot.transform.SetParent(root.transform, false);

            var front = MakeCylinderWheel(wheelRoot, "F", tyreMat, new Vector3(0f, wheelRadius, axleFront), wheelRadius);
            var rear = MakeCylinderWheel(wheelRoot, "R", tyreMat, new Vector3(0f, wheelRadius, axleRear), wheelRadius);

            bike.FrontWheel = front.Collider;
            bike.RearWheel = rear.Collider;
            bike.FrontMesh = front.Mesh;
            bike.RearMesh = rear.Mesh;

            AddSeats(root, new Vector3(0f, bodyY + 0.42f, -0.3f), new Vector3(-1.1f, 0.2f, 0f));

            var damage = root.AddComponent<VehicleDamage>();
            damage.Panels = new[] { lean.transform };
            damage.MaxDent = 0.06f;
            damage.BodyRenderers = new[] { lean.transform.Find("Frame").GetComponent<Renderer>() };
            damage.Smoke = MakeSmoke(root, new Vector3(0f, bodyY, -0.5f));

            return SavePrefab(root, name);
        }

        /// <summary>
        /// A wheel whose visual is a Unity cylinder primitive. Still used by the bike, because
        /// the car pack ships no motorbike.
        /// </summary>
        static CarController.Wheel MakeCylinderWheel(GameObject parent, string name, Material tyreMat,
                                                     Vector3 localPos, float radius)
        {
            var colliderGo = new GameObject("WC_" + name);
            colliderGo.transform.SetParent(parent.transform, false);
            colliderGo.transform.localPosition = localPos;

            var wc = colliderGo.AddComponent<WheelCollider>();
            wc.radius = radius;
            wc.mass = 22f;
            wc.wheelDampingRate = 0.28f;
            wc.suspensionDistance = 0.24f;
            wc.forceAppPointDistance = 0.11f;

            var spring = wc.suspensionSpring;
            spring.spring = 38000f;
            spring.damper = 4600f;
            spring.targetPosition = 0.45f;
            wc.suspensionSpring = spring;

            var fwd = wc.forwardFriction;
            fwd.extremumSlip = 0.36f; fwd.extremumValue = 1.1f;
            fwd.asymptoteSlip = 0.85f; fwd.asymptoteValue = 0.62f;
            fwd.stiffness = 2.0f;
            wc.forwardFriction = fwd;

            var side = wc.sidewaysFriction;
            side.extremumSlip = 0.26f; side.extremumValue = 1.1f;
            side.asymptoteSlip = 0.58f; side.asymptoteValue = 0.72f;
            side.stiffness = 2.3f;
            wc.sidewaysFriction = side;

            // A cylinder is Y-up by default, so roll it onto its side.
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mesh.name = "Tyre_" + name;
            mesh.transform.SetParent(parent.transform, false);
            mesh.transform.localPosition = localPos;
            mesh.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            mesh.transform.localScale = new Vector3(radius * 2f, 0.11f, radius * 2f);
            mesh.GetComponent<Renderer>().sharedMaterial = tyreMat;
            Object.DestroyImmediate(mesh.GetComponent<Collider>());

            return new CarController.Wheel
            {
                Collider = wc,
                Mesh = mesh.transform,
                Steers = false,
                Drives = false,
                Brakes = true,
                MeshRotationEuler = new Vector3(0f, 0f, 90f),
            };
        }

        // ------------------------------------------------------------------- boat

        static string BuildBoat(string name)
        {
            var root = new GameObject(name);

            const float hullLength = 5.4f;
            const float hullWidth = 2.1f;
            const float hullHeight = 0.9f;

            var hullMat = Mat("Veh_BoatHull", new Color(0.88f, 0.88f, 0.9f), 0.62f);
            var deckMat = Mat("Veh_BoatDeck", new Color(0.55f, 0.40f, 0.26f), 0.3f);

            var hullGo = Box(root, "Hull", hullMat, new Vector3(0f, 0f, 0f),
                new Vector3(hullWidth, hullHeight, hullLength));

            Box(root, "Deck", deckMat, new Vector3(0f, hullHeight * 0.5f, -0.4f),
                new Vector3(hullWidth * 0.9f, 0.1f, hullLength * 0.55f));
            Box(root, "Bow", hullMat, new Vector3(0f, 0.16f, hullLength * 0.46f),
                new Vector3(hullWidth * 0.55f, hullHeight * 0.8f, 0.9f));
            Box(root, "Console", hullMat, new Vector3(0f, hullHeight * 0.5f + 0.35f, 0.35f),
                new Vector3(0.8f, 0.7f, 0.5f));

            var body = root.AddComponent<Rigidbody>();
            body.mass = 900f;
            body.linearDamping = 1.4f;
            body.angularDamping = 2.6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var hull = root.AddComponent<BoxCollider>();
            hull.center = new Vector3(0f, 0.1f, 0f);
            hull.size = new Vector3(hullWidth, hullHeight + 0.4f, hullLength);

            var boat = root.AddComponent<BoatController>();
            boat.Kind = VehicleKind.Boat;
            boat.DisplayName = "Speedboat";
            boat.CameraDistance = 10f;
            boat.CameraPivotOffset = new Vector3(0f, 2.4f, 0f);
            boat.HideOccupant = false;

            // Four corners: the minimum that produces both pitch and roll from the waves.
            var floats = new GameObject("FloatPoints");
            floats.transform.SetParent(root.transform, false);

            var points = new List<Transform>();
            float fx = hullWidth * 0.42f;
            float fz = hullLength * 0.42f;
            foreach (var p in new[]
            {
                new Vector3(-fx, -hullHeight * 0.4f,  fz),
                new Vector3( fx, -hullHeight * 0.4f,  fz),
                new Vector3(-fx, -hullHeight * 0.4f, -fz),
                new Vector3( fx, -hullHeight * 0.4f, -fz),
            })
            {
                var t = new GameObject("Float").transform;
                t.SetParent(floats.transform, false);
                t.localPosition = p;
                points.Add(t);
            }
            boat.FloatPoints = points.ToArray();

            AddSeats(root, new Vector3(0f, hullHeight * 0.5f + 0.15f, -0.35f), new Vector3(-1.5f, 0.6f, 0f));

            var damage = root.AddComponent<VehicleDamage>();
            damage.Panels = new Transform[0];
            damage.BodyRenderers = new[] { hullGo.GetComponent<Renderer>() };
            damage.Smoke = MakeSmoke(root, new Vector3(0f, 0.6f, -hullLength * 0.4f));

            return SavePrefab(root, name);
        }

        // ------------------------------------------------------------------ shared

        static void AddSeats(GameObject root, Vector3 seatLocal, Vector3 exitLocal)
        {
            var seat = new GameObject("DriverSeat").transform;
            seat.SetParent(root.transform, false);
            seat.localPosition = seatLocal;

            var exit = new GameObject("ExitPoint").transform;
            exit.SetParent(root.transform, false);
            exit.localPosition = exitLocal;

            var vehicle = root.GetComponent<Vehicle>();
            vehicle.DriverSeat = seat;
            vehicle.ExitPoint = exit;
        }

        static ParticleSystem MakeSmoke(GameObject root, Vector3 localPos)
        {
            var go = new GameObject("DamageSmoke");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;

            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.startLifetime = 1.6f;
            main.startSpeed = 1.4f;
            main.startSize = 0.9f;
            main.startColor = new Color(0.18f, 0.18f, 0.18f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.16f;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Mat("Veh_Smoke", new Color(0.2f, 0.2f, 0.2f), 0f);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        static GameObject Box(GameObject parent, string name, Material mat, Vector3 localPos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            // The vehicle's own hull collider does the colliding; per-panel colliders would
            // fight it and make the car snag on its own bodywork.
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            return go;
        }

        static Material Mat(string name, Color color, float smoothness)
        {
            string path = MatDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0.1f);
            mat.enableInstancing = true;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static string SavePrefab(GameObject root, string name)
        {
            string path = PrefabDir + "/" + name + ".prefab";
            AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }
    }
}
