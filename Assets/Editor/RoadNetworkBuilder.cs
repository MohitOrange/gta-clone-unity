using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Generates the driving lane graph, the traffic signals and the crosswalk paint,
    /// derived from the same grid constants <see cref="CityBuilder"/> uses.
    ///
    /// Sharing those constants rather than re-declaring them is the whole point: if the lane
    /// graph and the asphalt ever disagree, AI cars drive through buildings, and that failure
    /// is invisible until you watch it happen.
    /// </summary>
    public static class RoadNetworkBuilder
    {
        /// <summary>Half a carriageway. Lane centres sit this far either side of the centre line.</summary>
        const float LaneOffset = 3.5f;

        /// <summary>How far outside the junction box the stop line and exit nodes sit.</summary>
        const float JunctionMargin = 9f;

        /// <summary>
        /// Signal heads are only built on the inner grid. Controllers exist at every
        /// intersection so AI behaviour is uniform, but physical heads everywhere would cost
        /// renderers for signage a mobile player never sees at the map edge.
        ///
        /// <b>Phase 11 raised this from 1 to 2</b> when the grid went from 6x6 to 10x10
        /// junctions. At a border of 1 the expanded grid would have lit 64 junctions and spent
        /// about 1,020 renderers on traffic signals alone -- four times Phase 9's bill and
        /// more than the entire building stock. A border of 2 lights the inner 6x6, which is
        /// 36 junctions and about 576 renderers: 2.25x Phase 9 for a city 3.24x the area.
        /// The unlit ring is the outer suburbs, where the traffic density is lowest.
        /// </summary>
        const int LitBorder = 2;

        static int Lanes => CityBuilder.Blocks + 1;   // 10 roads each way for a 9-block grid

        [MenuItem("Tools/Mini GTA/11. Build Road Network", priority = 131)]
        public static RoadNetwork Build()
        {
            var old = GameObject.Find("RoadNetwork");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("RoadNetwork");
            var network = root.AddComponent<RoadNetwork>();

            var lightsRoot = new GameObject("Signals");
            lightsRoot.transform.SetParent(root.transform, false);

            var paintRoot = new GameObject("Crosswalks");
            paintRoot.transform.SetParent(root.transform, false);

            var mats = LoadMaterials();

            // --- Intersections -------------------------------------------------------
            var controllers = new List<TrafficLightController>();
            for (int j = 0; j < Lanes; j++)
            {
                for (int i = 0; i < Lanes; i++)
                {
                    Vector3 centre = IntersectionCentre(i, j);
                    bool lit = i >= LitBorder && i < Lanes - LitBorder
                            && j >= LitBorder && j < Lanes - LitBorder;

                    controllers.Add(BuildIntersection(lightsRoot, paintRoot, mats, i, j, centre, lit));
                }
            }

            // --- Lane nodes ----------------------------------------------------------
            var nodes = BuildLaneNodes();
            network.SetData(nodes, controllers);

            EditorUtility.SetDirty(network);

            int lightCount = 0;
            foreach (var c in controllers) lightCount += c.Lamps.Count;

            Debug.Log("[Roads] Lane graph: " + nodes.Count + " nodes across "
                      + controllers.Count + " intersections; " + lightCount + " signal heads built.");
            return network;
        }

        // ------------------------------------------------------------------- graph

        /// <summary>
        /// Node layout, per intersection and heading: an approach (stop line) and an exit.
        /// Index is deterministic so links can be computed without a lookup table.
        /// </summary>
        static int NodeIndex(int i, int j, Heading h, bool exit) =>
            (((j * Lanes + i) * 4) + (int)h) * 2 + (exit ? 1 : 0);

        static List<LaneNode> BuildLaneNodes()
        {
            int total = Lanes * Lanes * 4 * 2;
            var nodes = new List<LaneNode>(total);
            for (int n = 0; n < total; n++) nodes.Add(new LaneNode());

            // Position every node.
            for (int j = 0; j < Lanes; j++)
            {
                for (int i = 0; i < Lanes; i++)
                {
                    Vector3 centre = IntersectionCentre(i, j);
                    int intersection = j * Lanes + i;

                    for (int d = 0; d < 4; d++)
                    {
                        var h = (Heading)d;
                        Vector3 dir = h.ToVector();
                        Vector3 lane = h.RightOf() * LaneOffset;

                        var approach = nodes[NodeIndex(i, j, h, false)];
                        approach.Position = centre - dir * JunctionMargin + lane;
                        approach.Heading = h;
                        approach.IntersectionIndex = intersection;
                        approach.IsStopLine = true;

                        var exit = nodes[NodeIndex(i, j, h, true)];
                        exit.Position = centre + dir * JunctionMargin + lane;
                        exit.Heading = h;
                        exit.IntersectionIndex = -1;
                        exit.IsStopLine = false;
                    }
                }
            }

            // Link exits to the next intersection's matching approach.
            for (int j = 0; j < Lanes; j++)
            {
                for (int i = 0; i < Lanes; i++)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        var h = (Heading)d;
                        if (!Neighbour(i, j, h, out int ni, out int nj)) continue;

                        nodes[NodeIndex(i, j, h, true)].Next =
                            new[] { NodeIndex(ni, nj, h, false) };
                    }
                }
            }

            // Link approaches to the exits they may take: straight, right, left. No U-turns.
            for (int j = 0; j < Lanes; j++)
            {
                for (int i = 0; i < Lanes; i++)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        var h = (Heading)d;
                        var options = new List<int>(3);

                        foreach (var turn in new[] { h, h.TurnRight(), h.TurnLeft() })
                        {
                            int exitIndex = NodeIndex(i, j, turn, true);

                            // Only offer a turn that actually leads somewhere, otherwise cars
                            // drive off the grid and strand themselves at a dead end.
                            if (nodes[exitIndex].Next.Length == 0) continue;
                            options.Add(exitIndex);
                        }

                        nodes[NodeIndex(i, j, h, false)].Next = options.ToArray();
                    }
                }
            }

            return nodes;
        }

        static bool Neighbour(int i, int j, Heading h, out int ni, out int nj)
        {
            ni = i; nj = j;
            switch (h)
            {
                case Heading.North: nj = j + 1; break;
                case Heading.South: nj = j - 1; break;
                case Heading.East: ni = i + 1; break;
                default: ni = i - 1; break;
            }
            return ni >= 0 && ni < Lanes && nj >= 0 && nj < Lanes;
        }

        public static Vector3 IntersectionCentre(int i, int j)
        {
            float gridSpan = CityBuilder.Blocks * CityBuilder.Pitch;
            float ox = TerrainBuilder.CityCenter.x - gridSpan * 0.5f;
            float oz = TerrainBuilder.CityCenter.y - gridSpan * 0.5f;
            float y = TerrainBuilder.PlateauHeight + CityBuilder.RoadSurfaceY;

            return new Vector3(
                ox + i * CityBuilder.Pitch + CityBuilder.RoadWidth * 0.5f,
                y,
                oz + j * CityBuilder.Pitch + CityBuilder.RoadWidth * 0.5f);
        }

        // ------------------------------------------------------------------ signals

        static TrafficLightController BuildIntersection(GameObject lightsRoot, GameObject paintRoot,
                                                        Mats mats, int i, int j, Vector3 centre, bool lit)
        {
            var go = new GameObject("Junction_" + i + "_" + j);
            go.transform.SetParent(lightsRoot.transform, false);
            go.transform.position = centre;

            var controller = go.AddComponent<TrafficLightController>();

            // Checkerboard by half a cycle so neighbouring junctions run opposite phases, plus
            // a small deterministic jitter so a whole avenue never switches in unison.
            float halfCycle = controller.CycleLength * 0.5f;
            controller.PhaseOffset = ((i + j) % 2) * halfCycle + (i * 1.7f + j * 2.3f) % 4f;

            if (!lit) return controller;

            foreach (Heading h in new[] { Heading.North, Heading.East, Heading.South, Heading.West })
                controller.Lamps.Add(BuildSignalHead(go, mats, centre, h));

            BuildCrosswalks(paintRoot, mats, centre, i, j);
            return controller;
        }

        // ---- Tarbo City Traffic Lights: measured, not guessed -------------------------
        //
        // The pack ships each signal as ONE mesh with ONE material and no child objects, so
        // its three lenses cannot be addressed individually -- which is what TrafficLightLamps
        // needs. The model therefore supplies the pole, mast arm and housing, and three small
        // emissive quads are laid over its lens faces for the controller to drive. No
        // controller logic changed; TrafficLightLamps still just receives three Renderers.
        //
        // The numbers below were measured off the mesh by raycasting a grid at it, not read
        // off a screenshot. In the prefab's own space (origin at the pole base, lenses facing
        // -Z): the head band is y 6.50..7.30 with the lens apexes at y 6.90; the three lenses
        // sit at x -4.30 / -3.30 / -2.30, red first; the housing face is at z -0.342 and the
        // lens apexes protrude to z -0.519.
        //
        // The model is parented with a 180-degree Y turn so its lenses face the head's local
        // +Z, which is the side the oncoming driver sees -- the same convention the primitive
        // lamps used. That flip negates x and z, hence the positive values here.
        const string SignalPrefab =
            "Assets/Tarbo-CITY-TrafficLights/Prefabs/Props/Road/TB_CITY_Prop_TrafficLight_VSet_Black.prefab";

        const float LensY = 6.90f;
        const float LensZ = 0.535f;    // 16 mm proud of the lens apex at 0.519
        // The moulded lens is a 0.60 m octagon. The overlay is a square, so it has to be
        // small enough that its corners stay inside the octagon's flats -- 0.55 put them
        // just over the edge and the lit lamp read as a square patch rather than a lens.
        const float LensSize = 0.46f;
        static readonly float[] LensX = { 4.30f, 3.30f, 2.30f };   // red, yellow, green

        /// <summary>
        /// One signal head facing an approach. Placed on the far kerb of the junction, offset
        /// to the driver's right, with the mast arm reaching back over the lane it governs.
        ///
        /// <b>The placement is unchanged from Phase 2 and that is not an accident.</b> The
        /// pack's arm extends toward the model's -X, which after the facing rotation is the
        /// far side of the carriageway -- so the existing right-hand-kerb position puts the
        /// head directly over the approaching driver's lane. Moving the pole to the other
        /// kerb would swing the arm out over the pavement instead.
        /// </summary>
        static TrafficLightLamps BuildSignalHead(GameObject parent, Mats mats, Vector3 centre, Heading facing)
        {
            Vector3 dir = facing.ToVector();
            Vector3 right = facing.RightOf();

            Vector3 basePos = centre + dir * (CityBuilder.RoadWidth * 0.5f + 1.1f)
                                     + right * (CityBuilder.RoadWidth * 0.5f + 1.1f);

            var head = new GameObject("Signal_" + facing);
            head.transform.SetParent(parent.transform, false);
            head.transform.position = basePos;
            head.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SignalPrefab);
            if (source != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.name = "SignalModel";
                model.transform.SetParent(head.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                model.transform.localScale = Vector3.one;
            }
            else
            {
                // The pack is gone: fall back to the Phase 2 primitive pole so the junction
                // still reads as signalled rather than leaving three lamps floating in air.
                Debug.LogWarning("[Roads] Missing " + SignalPrefab + "; using a primitive pole.");
                Box(head, "Pole", mats.Pole, new Vector3(0f, LensY * 0.5f, 0f),
                    new Vector3(0.14f, LensY, 0.14f));
            }

            var lamps = head.AddComponent<TrafficLightLamps>();
            lamps.Facing = facing;

            lamps.RedLamp = Lens(head, "Red", mats.Lamp, LensX[0]);
            lamps.YellowLamp = Lens(head, "Yellow", mats.Lamp, LensX[1]);
            lamps.GreenLamp = Lens(head, "Green", mats.Lamp, LensX[2]);

            return lamps;
        }

        /// <summary>One emissive lens face, laid over the model's own moulded lens.</summary>
        static Renderer Lens(GameObject head, string name, Material mat, float x)
        {
            var go = Box(head, name, mat, new Vector3(x, LensY, LensZ),
                         new Vector3(LensSize, LensSize, 0.04f));
            return go.GetComponent<Renderer>();
        }

        /// <summary>
        /// Zebra paint at the four approaches to a junction.
        ///
        /// <b>The height here is load-bearing, not decoration.</b> The road slab's top face
        /// sits at <c>centre.y + 0.06</c>. This paint used to be centred at <c>+0.05</c> with a
        /// 0.02 thickness, which put its <i>top</i> face at exactly +0.06 -- perfectly coplanar
        /// with the road. The depth buffer cannot separate two coplanar surfaces, so the white
        /// paint tore across the dark asphalt in wide horizontal bands at every junction. The
        /// lane markings never showed it because they were already 2 cm proud.
        ///
        /// It is now centred at +0.11, spanning +0.10 to +0.12. That is 4 cm clear of the road
        /// surface and 2 cm clear of the lane markings, which occupy +0.06 to +0.08 and matter
        /// because the centre line runs straight through every crossing. No face of this box
        /// shares a plane with anything.
        /// </summary>
        static void BuildCrosswalks(GameObject parent, Mats mats, Vector3 centre, int i, int j)
        {
            float half = CityBuilder.RoadWidth * 0.5f;
            float y = centre.y + 0.11f;

            foreach (Heading h in new[] { Heading.North, Heading.East, Heading.South, Heading.West })
            {
                Vector3 dir = h.ToVector();
                Vector3 across = h.RightOf();

                Vector3 pos = centre + dir * (half + 1.4f);
                pos.y = y;

                var go = Box(parent, "Zebra_" + i + "_" + j + "_" + h, mats.Zebra, Vector3.zero, Vector3.one);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                go.transform.localScale = new Vector3(CityBuilder.RoadWidth - 1f, 0.02f, 2.2f);
            }
        }

        // ---------------------------------------------------------------- materials

        class Mats
        {
            public Material Pole, Lamp, Zebra;
        }

        static Mats LoadMaterials() => new Mats
        {
            Pole = Mat("Sig_Pole", new Color(0.19f, 0.20f, 0.21f), 0.35f, false),
            Lamp = Mat("Sig_Lamp", new Color(0.06f, 0.06f, 0.07f), 0.75f, true),
            Zebra = Mat("Sig_Zebra", new Color(0.86f, 0.86f, 0.82f), 0.08f, false),
        };

        static Material Mat(string name, Color color, float smoothness, bool emissive)
        {
            string path = "Assets/Game/Materials/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.enableInstancing = true;

            if (emissive)
            {
                // The keyword must be on in the shared material or per-lamp emission set
                // through a property block is ignored entirely.
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", Color.black);
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static GameObject Box(GameObject parent, string name, Material mat, Vector3 localPos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            return go;
        }
    }
}
