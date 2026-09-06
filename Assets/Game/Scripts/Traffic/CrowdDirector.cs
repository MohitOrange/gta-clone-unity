using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Grows the pedestrian population to a target and decides how much of each person is
    /// simulated, by distance from the camera.
    ///
    /// <b>Why this exists as a budget rather than a spawn count.</b> The Phase 7 re-baseline
    /// measured 2.29 ms of headroom against a 60 fps budget at 66 pedestrians -- on a desktop,
    /// in the Editor. Section 3 asks for 300-1000 on a mid-range phone. There is no version of
    /// "spawn more and see" that survives that, so the population is decoupled from the cost:
    /// the crowd can be a thousand people while the number being fully simulated stays roughly
    /// constant, because it is capped rather than derived.
    ///
    /// Three bands:
    /// <list type="bullet">
    /// <item>Full -- every frame with separation steering. Capped by <see cref="MaxFullSim"/>,
    /// not by distance alone, so walking into a dense square cannot blow the budget.</item>
    /// <item>Simple -- one frame in four, no separation, animator still running.</item>
    /// <item>Frozen -- not ticked, animator disabled entirely. Most of the population.</item>
    /// </list>
    ///
    /// The banding pass is itself time-sliced. Sorting a thousand people by distance every
    /// frame to decide who is near would be its own O(n log n) per frame, which is the kind of
    /// bookkeeping that eats the saving it was written to produce.
    /// </summary>
    [DefaultExecutionOrder(-130)]
    public class CrowdDirector : MonoBehaviour
    {
        [Header("Population")]
        [Tooltip("How many pedestrians to maintain. 300 is the shipping value; see "
                 + "ShippingMaxPopulation and TrySetPopulation for the stress-test path.")]
        public int TargetPopulation = 300;

        /// <summary>
        /// The most this will maintain in a shipping build.
        ///
        /// 300 measured at 13.61 ms in the Editor on desktop -- less than the 66 the scene
        /// shipped with cost before Section 3. 1,000 works and is stable but sits at ~29 ms
        /// there, and the Editor on a desktop is the generous case; the target is a mid-range
        /// Android phone. So 1,000 stays reachable as a stress test and is not a default.
        /// </summary>
        public const int ShippingMaxPopulation = 300;

        /// <summary>Population the NPC_MAX stress test raises the crowd to.</summary>
        public const int StressTestPopulation = 1000;

        /// <summary>
        /// Whether the stress-test population may be requested at all.
        ///
        /// Development builds and the Editor only, per the standing resolution that cheats are
        /// dev-gated and off by default in a shipping build.
        /// </summary>
        public static bool StressTestAllowed => Debug.isDebugBuild || Application.isEditor;

        /// <summary>
        /// Sets the crowd size, refusing anything above the shipping cap unless this is a
        /// development build.
        ///
        /// TODO (Section 9): wire the NPC_MAX cheat code to this. The capability is kept here
        /// rather than in the cheat system so the cap and the reasoning live next to the
        /// numbers they came from.
        /// </summary>
        public bool TrySetPopulation(int target)
        {
            int max = StressTestAllowed ? StressTestPopulation : ShippingMaxPopulation;

            if (target < 0 || target > max)
            {
                Debug.LogWarning("[Crowd] refused population " + target
                                 + "; permitted maximum here is " + max);
                return false;
            }

            TargetPopulation = target;
            return true;
        }
        [Tooltip("Pedestrians added per frame while growing, so arriving at the target does "
                 + "not cost one enormous hitch.")]
        public int SpawnsPerFrame = 4;
        [Tooltip("Radius around the player that new pedestrians are scattered into.")]
        public float SpawnRadius = 220f;
        [Tooltip("How far off the traffic lane a pedestrian stands, in metres -- the pavement.")]
        public float SidewalkOffsetMin = 4.5f;
        public float SidewalkOffsetMax = 7.5f;

        [Header("LOD bands, metres from the camera")]
        public float FullBand = 45f;
        public float SimpleBand = 120f;
        [Tooltip("Beyond this, a pedestrian's meshes are switched off entirely. This is the "
                 + "band that decides the frame cost at high population: a Frozen pedestrian "
                 + "still skins and draws, so freezing the AI alone does not pay for itself.")]
        public float VisibleBand = 150f;

        [Header("Budget")]
        [Tooltip("Hard cap on fully simulated pedestrians. This is the number that protects "
                 + "the frame, so it is a cap and not a consequence of the band radius.")]
        public int MaxFullSim = 40;
        [Tooltip("Hard cap on the Simple band. Everyone past it is Frozen.")]
        public int MaxSimpleSim = 120;

        [Tooltip("How many pedestrians are re-banded per frame. The whole population is swept "
                 + "every ceil(population / this) frames.")]
        public int RebandPerFrame = 64;

        [Header("Recycling")]
        [Tooltip("A pedestrian further than this from the camera is picked up and put back "
                 + "down near the player. 0 disables recycling.")]
        public float RecycleDistance = 320f;
        [Tooltip("Most pedestrians moved per sweep frame. A cap, because teleporting hundreds "
                 + "of people in one frame is a visible hitch.")]
        public int RecyclesPerFrame = 3;

        [Header("Variety (Section 3B)")]
        [Tooltip("Seed for the model pick. Fixed so a run is reproducible -- a crowd that comes "
                 + "out different every launch cannot be photographed for comparison, and a "
                 + "performance measurement over a different mix of models is not a repeat of "
                 + "the previous one.")]
        public int VarietySeed = 20260905;

        [Tooltip("Avoid picking the same model twice in a row, so identical characters do not "
                 + "spawn as visible pairs. Ignored when the pool holds only one model.")]
        public bool NoImmediateRepeat = true;

        [Header("Diagnostics")]
        public bool LogOnGrowth = true;

        Transform _camera;
        int _cursor;
        int _fullThisSweep;
        int _simpleThisSweep;
        int _fullLastSweep;
        int _simpleLastSweep;
        int _frozenLastSweep;

        GameObject _template;
        readonly List<Pedestrian> _spawned = new List<Pedestrian>();

        // --- Section 3B: the model pool -------------------------------------------------
        //
        // One representative body per distinct character model already standing in the scene.
        // The scene ships nine of them; the crowd used to be clones of whichever one happened
        // to be first in the registry, so a street of 300 people was 300 copies of one person
        // -- and which person was not even stable between runs, because "first in the registry"
        // depends on scene load order.
        readonly List<GameObject> _pool = new List<GameObject>();
        readonly List<string> _poolNames = new List<string>();
        readonly List<float> _poolWeights = new List<float>();
        float _weightTotal;
        System.Random _variety;
        int _lastPicked = -1;

        /// <summary>Counts from the last completed sweep. Diagnostics only.</summary>
        public int FullCount => _fullLastSweep;
        public int SimpleCount => _simpleLastSweep;
        public int FrozenCount => _frozenLastSweep;
        public int Population => Pedestrian.Witnesses.Count;

        void Update()
        {
            if (_camera == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _camera = cam.transform;
            }

            Grow();
            Reband();
        }

        // ------------------------------------------------------------------ population

        /// <summary>
        /// Adds pedestrians a few at a time until the target is reached.
        ///
        /// Cloned from a living pedestrian rather than instantiated from a prefab, because
        /// there is no pedestrian prefab -- the 66 the scene shipped with are baked directly
        /// into City.unity by the editor-time TrafficBuilder. Cloning an existing one gets the
        /// whole assembly (body, Animator, CharacterController, Health, RagdollLite) already
        /// wired, without adding an asset the builder would then have to be taught about.
        /// </summary>
        void Grow()
        {
            var all = Pedestrian.Witnesses;
            if (all.Count >= TargetPopulation) return;

            BuildPool();
            if (_pool.Count == 0) return;

            int before = all.Count;
            int budget = Mathf.Max(1, SpawnsPerFrame);

            for (int i = 0; i < budget && Pedestrian.Witnesses.Count < TargetPopulation; i++)
            {
                Vector3 at = ScatterPoint();

                // Section 3B: picked per spawn, not once for the whole crowd.
                var template = PickTemplate();
                if (template == null) break;

                var clone = Instantiate(template, at, Quaternion.Euler(0f, Random.value * 360f, 0f));
                clone.name = "NPC_Crowd";

                var ped = clone.GetComponent<Pedestrian>();
                if (ped != null)
                {
                    // Give the clone its own beat before Pedestrian.Start runs.
                    //
                    // Start does `transform.position = PointA`, and Instantiate copied PointA
                    // from the template -- so without this every clone teleports onto the
                    // template's route on its first frame and the whole crowd piles into one
                    // spot several hundred metres away. Measured: 534 clones with a centroid
                    // 780 m from the camera, and not one of them close enough to be banded
                    // above Frozen. Start has not run yet at this point in the same frame,
                    // so writing PointA here is what Start will pick up.
                    ped.PointA = at;
                    ped.PointB = at;
                    ped.Route = PedestrianRoute.Wander;
                    ped.WanderExtents = new Vector2(22f, 22f);

                    // Start Frozen. The banding pass promotes whoever is actually near the
                    // camera on the next sweep, so a spawn burst never lands as a burst of
                    // full-rate simulation.
                    ped.Lod = Pedestrian.CrowdLod.Frozen;
                    _spawned.Add(ped);
                }

                // A crowd filler is not one of the town's characters. TownNpc carries the
                // interaction prompt and the quest hook, and it ticks every frame to check
                // whether the player is close enough to talk to -- 934 of those for people
                // who have nothing to say. Cloning the template dragged it along; drop it.
                var town = clone.GetComponent<TownNpc>();
                if (town != null) Destroy(town);

                Desynchronise(clone);

                SetAnimatorEnabled(clone, false);
                if (ped != null) ped.SetSimulationEnabled(false);
            }

            if (LogOnGrowth && Pedestrian.Witnesses.Count >= TargetPopulation && before < TargetPopulation)
                Debug.Log("[Crowd] population reached " + Pedestrian.Witnesses.Count
                          + " (target " + TargetPopulation + ")");
        }

        /// <summary>
        /// Where to put a new pedestrian.
        ///
        /// <b>On a pavement, not on a random patch of ground.</b> Scattering uniformly across
        /// a disc around the player is what the first version did, and at a spawn radius of
        /// 220 m that is 152,000 m² -- one person per 152 m², spread across beach, sea and
        /// open desert alike. A thousand people placed that way photograph as an empty
        /// landscape with half a dozen specks in it: the population is real but it does not
        /// read as a crowd, because crowds are not uniform, they follow streets.
        ///
        /// So the road network picks the spot and the pedestrian steps off the lane onto the
        /// kerb. The disc remains as a fallback for anywhere with no road nearby -- the beach
        /// the player currently starts on being exactly that case.
        /// </summary>
        Vector3 ScatterPoint()
        {
            Vector3 centre = _camera != null ? _camera.position : transform.position;

            var net = RoadNetwork.Instance;
            if (net != null && net.NodeCount > 0)
            {
                float radiusSqr = SpawnRadius * SpawnRadius;

                // A bounded number of tries rather than a filtered list: building the list of
                // in-range nodes would be an O(nodes) pass per spawn, and this runs several
                // times a frame while the crowd is growing.
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    var node = net.GetNode(Random.Range(0, net.NodeCount));
                    if (node == null) continue;
                    if ((node.Position - centre).sqrMagnitude > radiusSqr) continue;

                    Vector3 side = node.Heading.RightOf()
                                   * (Random.value < 0.5f ? -1f : 1f)
                                   * Random.Range(SidewalkOffsetMin, SidewalkOffsetMax);

                    return Ground(node.Position + side, centre);
                }
            }

            Vector2 disc = Random.insideUnitCircle * SpawnRadius;
            return Ground(new Vector3(centre.x + disc.x, centre.y, centre.z + disc.y), centre);
        }

        /// <summary>
        /// Drops a point onto whatever is underneath it. A pedestrian that spawns inside the
        /// ground or sixty metres above it is worse than one in a slightly odd place.
        /// </summary>
        static Vector3 Ground(Vector3 at, Vector3 centre)
        {
            Vector3 probe = new Vector3(at.x, centre.y + 60f, at.z);

            if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 220f,
                                ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.1f;

            return new Vector3(at.x, centre.y, at.z);
        }

        // ------------------------------------------------------------------ banding

        void Reband()
        {
            var all = Pedestrian.Witnesses;
            if (all.Count == 0) return;

            Vector3 eye = _camera.position;
            float full = FullBand * FullBand;
            float simple = SimpleBand * SimpleBand;
            float visible = VisibleBand * VisibleBand;

            int budget = Mathf.Min(Mathf.Max(1, RebandPerFrame), all.Count);
            float recycle = RecycleDistance * RecycleDistance;
            int recycled = 0;

            for (int i = 0; i < budget; i++)
            {
                if (_cursor >= all.Count)
                {
                    // Sweep complete: publish the counts and start the next one.
                    _fullLastSweep = _fullThisSweep;
                    _simpleLastSweep = _simpleThisSweep;
                    _frozenLastSweep = Mathf.Max(0, all.Count - _fullThisSweep - _simpleThisSweep);
                    _fullThisSweep = 0;
                    _simpleThisSweep = 0;
                    _cursor = 0;
                }

                var ped = all[_cursor++];
                if (ped == null) continue;

                if (ped.IsDead)
                {
                    // A corpse is mid-collapse or waiting to despawn. RagdollLite owns its
                    // animator from that point, so do not fight it for control.
                    continue;
                }

                float d = (ped.transform.position - eye).sqrMagnitude;

                // Bring the stragglers along.
                //
                // Without this the crowd is built once, around wherever the camera happened to
                // be, and then stays there: walk four streets over and the city is empty
                // again, with all thousand people milling about behind you. Measured that
                // exactly -- 1,000 alive, 9 of them drawn, because the population had been
                // grown at the beach and the player had walked into town.
                //
                // TrafficSpawner already does this for cars (PlaceAwayFromPlayer); this is the
                // same idea for people. Capped per frame because teleporting hundreds of
                // pedestrians in one frame is a visible hitch, and they are all far away and
                // invisible anyway, so there is no hurry.
                if (RecycleDistance > 0f && d > recycle && recycled < RecyclesPerFrame)
                {
                    Vector3 spot = ScatterPoint();
                    ped.transform.position = spot;
                    ped.PointA = spot;
                    ped.PointB = spot;
                    recycled++;

                    d = (spot - eye).sqrMagnitude;
                }

                // Visibility is a separate axis from simulation: someone can be worth drawing
                // while not being worth ticking. Cheap on no-change, so it is safe to call
                // every sweep rather than only on a band transition.
                bool seen = d <= visible;
                ped.SetRenderersVisible(seen);

                Pedestrian.CrowdLod want;
                if (d <= full && _fullThisSweep < MaxFullSim) { want = Pedestrian.CrowdLod.Full; _fullThisSweep++; }
                else if (d <= simple && _simpleThisSweep < MaxSimpleSim) { want = Pedestrian.CrowdLod.Simple; _simpleThisSweep++; }
                else want = Pedestrian.CrowdLod.Frozen;

                // Applied every sweep, not only on a band change.
                //
                // Both calls early-out on no-change, so this is cheap -- and putting them
                // after an `if (ped.Lod == want) continue` is what made the first version of
                // this useless: a pedestrian that spawned Frozen and stayed Frozen never
                // changed band, so its drivers were never switched off. Measured 998 of 1,000
                // PlayerAnimation components still ticking with 847 pedestrians frozen.
                bool live = want != Pedestrian.CrowdLod.Frozen;

                // The animator follows *visibility*, not the simulation band.
                //
                // Tying it to the band put a street full of people in the T-pose. Disabling an
                // Animator freezes the rig wherever it is, and a pedestrian that spawned
                // Frozen never evaluated a single frame -- so it had no pose to hold and sat
                // in the bind pose, arms straight out, in full view. PHASE9B shipped exactly
                // that bug once already.
                //
                // If you can see someone they must look right, so the rule is simply: drawn
                // implies animated. The saving is unaffected for the population that matters,
                // because the several hundred people past the visible band have their meshes
                // switched off anyway and get no animator either.
                SetAnimatorEnabled(ped.gameObject, seen);
                ped.SetSimulationEnabled(live);

                if (ped.Lod == want) continue;
                ped.Lod = want;
            }
        }

        /// <summary>
        /// Section 5.2. Puts one pedestrian into the world at a spot, awake, drawn and already
        /// running away from <paramref name="threat"/>, and returns it.
        ///
        /// This is how a carjacked driver leaves the car. Reusing the crowd's own body means
        /// the person who scrambles out is the same kind of character as everyone else on the
        /// street, and inherits the flee behaviour, the witness registration and the panic
        /// propagation that a bespoke "fleeing driver" object would each have to reimplement --
        /// including, usefully, the fact that a fleeing pedestrian is itself a witness, so a
        /// carjack in a busy street is reported by the victim.
        ///
        /// Unlike a crowd filler this one starts Full and simulating rather than Frozen. A
        /// driver thrown out of their own car in front of the player is the one pedestrian in
        /// the scene guaranteed to be looked at.
        /// </summary>
        public Pedestrian SpawnFleeingPedestrian(Vector3 at, Vector3 threat)
        {
            var template = ResolveTemplate();
            if (template == null) return null;

            Vector3 away = at - threat;
            away.y = 0f;
            var facing = away.sqrMagnitude > 0.01f
                       ? Quaternion.LookRotation(away.normalized)
                       : Quaternion.Euler(0f, Random.value * 360f, 0f);

            var clone = Instantiate(template, at, facing);
            clone.name = "NPC_EjectedDriver";

            var ped = clone.GetComponent<Pedestrian>();
            if (ped != null)
            {
                // Same BUG-022 trap as the crowd path: Start writes PointA over the position,
                // so a clone that keeps the template's route teleports onto it immediately.
                ped.PointA = at;
                ped.PointB = at;
                ped.Route = PedestrianRoute.Wander;
                ped.WanderExtents = new Vector2(30f, 30f);
                ped.Lod = Pedestrian.CrowdLod.Full;
            }

            var town = clone.GetComponent<TownNpc>();
            if (town != null) Destroy(town);

            SetAnimatorEnabled(clone, true);

            if (ped != null)
            {
                ped.SetSimulationEnabled(true);
                ped.SetRenderersVisible(true);   // BUG-024: Instantiate copies renderer state
                ped.Panic(threat);
                _spawned.Add(ped);
            }

            return ped;
        }

        /// <summary>
        /// Section 5.2. The body an AI car's seated driver is copied from. Public because
        /// <see cref="VehicleDriver"/> needs a person and should not have to go hunting the
        /// scene for one; the crowd already owns the answer.
        /// </summary>
        public GameObject DriverBodyTemplate() => ResolveTemplate();

        /// <summary>
        /// Section 3B. One body to clone, picked from the pool of distinct character models
        /// standing in the scene.
        ///
        /// <b>What this replaces.</b> The old version returned <i>the first living pedestrian in
        /// the registry</i> and cached it forever, so every one of the 234+ pedestrians grown to
        /// reach the population target was a copy of that one body. The scene ships nine
        /// distinct human models and the crowd used one of them. Worse, which one was not
        /// stable between runs -- "first in the registry" is scene load order, so the same build
        /// produced a street of Ch02s one launch and a street of Apocalyptic characters the next,
        /// which makes a crowd screenshot impossible to compare against a previous crowd
        /// screenshot.
        ///
        /// The pick is seeded rather than <c>Random.value</c> for the same reason: a run has to
        /// be reproducible, or a performance measurement over a different mix of models is not a
        /// repeat of the previous measurement.
        /// </summary>
        GameObject PickTemplate()
        {
            BuildPool();
            if (_pool.Count == 0) return null;
            if (_pool.Count == 1) return _pool[0];

            _variety ??= new System.Random(VarietySeed);

            int index = PickWeighted();

            // No immediate repeat. Spawns are consumed in bursts from one point in the
            // pavement walk, so two identical bodies picked in a row land near each other and
            // read as a duplicate rather than as a coincidence.
            if (NoImmediateRepeat && index == _lastPicked)
                index = (index + 1 + _variety.Next(_pool.Count - 1)) % _pool.Count;

            _lastPicked = index;
            return _pool[index];
        }

        /// <summary>
        /// Collects one representative per distinct character model, once.
        ///
        /// Grouped by mesh rather than by prefab because these bodies are attached at build time
        /// by CharacterCatalog and no longer carry a distinguishing prefab identity -- the mesh
        /// is what actually differs between them.
        ///
        /// <b>Clones are excluded, and that is not optional.</b> This runs while the crowd is
        /// growing, so anything this method produced is already in the registry by the time it
        /// is called again; letting those back in would make the pool converge on whatever it
        /// happened to pick first, which is the bug it exists to fix.
        /// </summary>
        void BuildPool()
        {
            if (_pool.Count > 0) return;

            var all = Pedestrian.Witnesses;
            for (int i = 0; i < all.Count; i++)
            {
                var ped = all[i];
                if (ped == null || ped.IsDead) continue;
                if (IsClone(ped.gameObject)) continue;

                // Keyed and costed over the WHOLE body, not the first renderer found.
                //
                // The Mixamo bodies are split across five or six meshes -- body, hair, shirt,
                // pants, shoes -- so costing the first one found reads Ch02 as 19,960 triangles
                // when it is really about 53,000, and keys two variants of the same character
                // as different models depending on child order. The first version of this did
                // exactly that and the "cost weighting" produced an almost uniform spread.
                var renderers = ped.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (renderers.Length == 0) continue;

                // vertexCount, NOT triangles.Length.
                //
                // Every one of these meshes imports with Read/Write disabled, and on a
                // non-readable mesh `triangles` returns an EMPTY array in a player build. The
                // editor keeps a CPU copy so it reads correctly there -- which is the worst
                // possible failure mode: the weighting looks right on this machine and silently
                // degrades to uniform on the phone, putting six times the intended triangle load
                // on the device and nowhere else. vertexCount needs no CPU copy and is a fine
                // proxy for the same cost.
                string key = "";
                int cost = 0;
                foreach (var r in renderers)
                {
                    if (r.sharedMesh == null) continue;
                    cost += r.sharedMesh.vertexCount;
                    if (key.Length == 0) key = r.sharedMesh.name;
                }
                if (key.Length == 0) continue;
                if (_poolNames.Contains(key)) continue;

                // A body whose meshes have not finished loading costs 0 and would flatten the
                // weighting to uniform. Abandon the whole build and retry next frame rather
                // than caching a pool that is quietly wrong -- BuildPool runs on the first
                // Grow(), which is early enough for this to happen.
                if (cost <= 0)
                {
                    _pool.Clear(); _poolNames.Clear(); _poolWeights.Clear(); _weightTotal = 0f;
                    return;
                }

                _poolNames.Add(key);
                _pool.Add(ped.gameObject);

                // Weighted by body cost, so every model appears and the cheap one appears most.
                //
                // Measured: the Apocalyptic base is ~6.1k triangles across its four meshes, the
                // Mixamo bodies 50-58k across six or seven. Picking uniformly puts roughly six
                // times the triangle load on the street that the Section 3 budget was measured
                // against -- that cap was fought for, and a variety pass which quietly spends it
                // is not an improvement. Inverse-cost weighting is self-tuning: add a cheaper
                // model and it naturally carries more of the crowd, with no table to go stale.
                float weight = 1f / cost;
                _poolWeights.Add(weight);
                _weightTotal += weight;
            }

            // Nothing authored to sample yet (the crowd director can tick before the scene's
            // own pedestrians register). Fall back to the old behaviour for this frame rather
            // than spawning nothing -- BuildPool is cheap and will find them next time.
            if (_pool.Count == 0)
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && !all[i].IsDead) { return; }
            }

            if (_pool.Count > 0 && LogOnGrowth)
            {
                var shares = new List<string>();
                for (int i = 0; i < _pool.Count; i++)
                    shares.Add(_poolNames[i] + " "
                               + Mathf.RoundToInt(100f * _poolWeights[i] / _weightTotal) + "%");
                Debug.Log("[Crowd] model pool: " + _pool.Count + " distinct characters -- "
                          + string.Join(", ", shares));
            }
        }

        /// <summary>Weighted index into the pool. Falls back to uniform if weights are absent.</summary>
        int PickWeighted()
        {
            if (_weightTotal <= 0f || _poolWeights.Count != _pool.Count)
                return _variety.Next(_pool.Count);

            double roll = _variety.NextDouble() * _weightTotal;
            for (int i = 0; i < _poolWeights.Count; i++)
            {
                roll -= _poolWeights[i];
                if (roll <= 0d) return i;
            }
            return _pool.Count - 1;
        }

        static bool IsClone(GameObject go)
            => go.name.StartsWith("NPC_Crowd") || go.name.StartsWith("NPC_EjectedDriver");

        /// <summary>Kept for callers that only need <i>a</i> body. Now pool-backed.</summary>
        GameObject ResolveTemplate()
        {
            var picked = PickTemplate();
            if (picked != null) _template = picked;
            return _template;
        }

        /// <summary>
        /// Section 9. Breaks a clone out of lockstep with the rest of the crowd.
        ///
        /// Two separate causes, and fixing only one leaves the effect visible:
        ///
        /// 1. <b>Same clip.</b> Every pedestrian played Idle01. The pack ships a second idle
        ///    that nothing referenced, so IdleVariant picks between them per character.
        /// 2. <b>Same phase.</b> More important, and the reason a crowd reads as mechanical even
        ///    with two idles: every clone starts its clip at normalised time 0, so three hundred
        ///    people breathe on exactly the same frame. Offsetting the start time is what
        ///    actually breaks the pattern -- two clips out of phase look like a crowd; two
        ///    hundred copies of one clip in phase look like a screensaver.
        ///
        /// Deterministic from the shared variety RNG, so a seeded run stays reproducible.
        /// </summary>
        void Desynchronise(GameObject clone)
        {
            var animator = clone.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return;

            _variety ??= new System.Random(VarietySeed);

            foreach (var p in animator.parameters)
            {
                if (p.name != "IdleVariant") continue;
                animator.SetFloat("IdleVariant", (float)_variety.NextDouble());
                break;
            }

            // Start somewhere else in the loop. Play on the base layer only: the masked layers
            // are driven by weight and have nothing to desynchronise.
            var state = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(state.fullPathHash, 0, (float)_variety.NextDouble());
        }

        static void SetAnimatorEnabled(GameObject go, bool on)
        {
            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null) return;

            // Skinned mesh skinning is the single biggest per-NPC cost in a crowd, and a
            // disabled Animator stops paying it. Culling alone does not: CullCompletely stops
            // the state machine but the mesh is still skinned when it is on screen, and a
            // Frozen pedestrian on screen at 200 m is exactly the case there are most of.
            if (animator.enabled != on) animator.enabled = on;
        }
    }
}
