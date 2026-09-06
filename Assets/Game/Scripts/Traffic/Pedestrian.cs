using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>How a pedestrian chooses where to go next.</summary>
    public enum PedestrianRoute
    {
        /// <summary>Shuttles between two kerbs, waiting for the signal. Set up by the builder.</summary>
        Crossing,
        /// <summary>Picks random destinations inside a sidewalk rectangle.</summary>
        Wander,
    }

    /// <summary>
    /// A civilian: walks a beat, keeps out of other people's way, waits at the kerb for the
    /// signal, and runs (or dies) when things go wrong.
    ///
    /// Crossing permission comes from the same <see cref="TrafficLightController"/> the cars
    /// read, so pedestrians and traffic can never disagree about whose phase it is.
    ///
    /// Also the game's witness pool: <see cref="Witnesses"/> lets the crime system ask who was
    /// close enough to see something without a scene-wide search per offence.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class Pedestrian : MonoBehaviour
    {
        enum State { Walking, WaitingToCross, Crossing, Fleeing, Dead }

        /// <summary>
        /// How much of this pedestrian is being simulated, set by <see cref="CrowdDirector"/>.
        ///
        /// The crowd only affords full behaviour for the few dozen people actually near the
        /// camera. Everyone else is either walking on a cheaper tick or standing still with
        /// their animator switched off, which at 300-1000 people is the difference between a
        /// city and a slideshow.
        /// </summary>
        public enum CrowdLod
        {
            /// <summary>Every frame, with separation steering. Near the player.</summary>
            Full,
            /// <summary>Staggered tick, no separation steering. Mid distance.</summary>
            Simple,
            /// <summary>Not ticked at all. Far away or off screen.</summary>
            Frozen,
        }

        // ------------------------------------------------------------- witness pool

        static readonly List<Pedestrian> Registry = new List<Pedestrian>();

        /// <summary>Every living pedestrian. Read-only; do not mutate.</summary>
        public static IReadOnlyList<Pedestrian> Witnesses => Registry;

        /// <summary>How many living pedestrians are within <paramref name="radius"/>.</summary>
        public static int CountNear(Vector3 position, float radius)
            => CrowdGrid.CountWithin(position, radius);

        /// <summary>Scratch list for the static neighbour queries. Reused, never returned.</summary>
        static readonly List<Pedestrian> QueryScratch = new List<Pedestrian>(64);

        /// <summary>Send everyone within radius running from a threat.</summary>
        public static int AlarmNear(Vector3 position, float radius, Vector3 threat)
        {
            QueryScratch.Clear();
            CrowdGrid.Query(position, radius, QueryScratch);

            int alarmed = 0;
            for (int i = 0; i < QueryScratch.Count; i++)
            {
                var p = QueryScratch[i];
                if (p == null || p.IsDead) continue;
                p.Panic(threat);
                alarmed++;
            }
            return alarmed;
        }

        // ------------------------------------------------------------------- config

        [Header("Route")]
        public PedestrianRoute Route = PedestrianRoute.Crossing;
        public Vector3 PointA;
        public Vector3 PointB;

        [Tooltip("Wander mode: half-extents of the rectangle to roam, centred on PointA.")]
        public Vector2 WanderExtents = new Vector2(24f, 24f);

        [Tooltip("Intersection whose signal governs the crossing, or -1 for no signal.")]
        public int IntersectionIndex = -1;
        [Tooltip("Compass direction of travel from A to B. Decides which signal must be red.")]
        public Heading WalkHeading = Heading.East;

        [Header("Movement")]
        public float WalkSpeed = 1.35f;
        public float FleeSpeed = 4.2f;
        public float TurnSpeed = 8f;
        public float ArriveRadius = 0.6f;
        public float Gravity = -18f;

        [Header("Water")]
        [Tooltip("How deep a pedestrian will wade before turning back, in metres below the "
                 + "waterline. Around a third of a body height reads as paddling at the edge; "
                 + "beyond that they should be swimming, and there is no swim behaviour.")]
        public float MaxWadeDepth = 0.55f;

        [Tooltip("How far ahead the water check looks, as a multiple of the step about to be "
                 + "taken. Above 1 it brakes before the edge rather than at it.")]
        public float WadeLookahead = 2.5f;

        [Tooltip("Pause at each end before turning around, so the beat does not look robotic.")]
        public Vector2 DwellRange = new Vector2(1.5f, 4.5f);

        [Header("Avoidance")]
        [Tooltip("Personal space. Pedestrians inside it push this one aside.")]
        public float SeparationRadius = 1.15f;
        public float SeparationStrength = 1.6f;

        [Header("Kerb")]
        [Tooltip("How far from the kerb the pedestrian stops to wait.")]
        public float KerbStopDistance = 0.9f;

        [Header("Panic")]
        public float FleeDuration = 7f;
        [Tooltip("How far they try to get from the threat before calming down.")]
        public float SafeDistance = 22f;

        // ------------------------------------------------------------------- state

        CharacterController _cc;
        PlayerAnimation _anim;
        Health _health;
        RagdollLite _ragdoll;
        RoadNetwork _network;

        State _state = State.Walking;

        /// <summary>Simulation level. Owned by <see cref="CrowdDirector"/>; defaults to Full
        /// so a scene with no director behaves exactly as it did before.</summary>
        public CrowdLod Lod = CrowdLod.Full;

        /// <summary>
        /// Which frame in the stagger cycle this pedestrian ticks on.
        ///
        /// Handed out by a rolling counter on first tick, which spreads the population evenly
        /// across the cycle. Without it every Simple pedestrian would tick on the same frame
        /// and the saving would be a periodic spike rather than a lower average -- which on a
        /// phone reads as stutter, and is worse than the cost it removed.
        /// </summary>
        int _tickPhase = -1;

        static int _phaseCounter;

        /// <summary>How many frames a Simple pedestrian skips between ticks.</summary>
        public const int SimpleTickStride = 4;

        /// <summary>Seconds this tick represents. Equals Time.deltaTime at Full LOD, and a
        /// multiple of it for the staggered Simple band.</summary>
        float _lodStep;

        Renderer[] _renderers;
        bool _renderersOn = true;

        /// <summary>
        /// Whether <see cref="SetRenderersVisible"/> has actually written the renderer state
        /// once, as opposed to merely having a default in <see cref="_renderersOn"/>.
        ///
        /// <b>Without this the crowd is invisible.</b> CrowdDirector clones a living
        /// pedestrian to grow the population, and Instantiate copies component state -- so if
        /// the template happened to be outside the visible band at that moment, its renderers
        /// were already disabled and every clone inherited them disabled. The clone's
        /// _renderersOn initialiser still said "true", so the early-out in
        /// SetRenderersVisible(true) matched and returned, and the renderers were never turned
        /// back on. Measured: 414 pedestrians inside the visible band, 33 of them drawn, one
        /// of the missing 381 standing 9.3 m from the camera at Full LOD.
        ///
        /// A cached flag describing another object's state is only ever a guess. The first
        /// call now always writes through, which makes the flag true by construction instead
        /// of by assumption.
        /// </summary>
        bool _visibilityApplied;

        /// <summary>
        /// Shows or hides this pedestrian's meshes.
        ///
        /// Disabling the Animator stops the state machine but <b>not</b> the skinning: a
        /// Frozen pedestrian standing still at 200 m is still a full SkinnedMeshRenderer being
        /// skinned and drawn every frame. Measured at a population of 1,000: 160 animators
        /// running but 1,115 skinned renderers enabled and 388 of them inside the frustum,
        /// which was the whole of the remaining cost.
        ///
        /// The array is cached and the early-out is on the flag, because this is called from
        /// the director's per-frame sweep and a GetComponentsInChildren per pedestrian per
        /// frame would cost more than the draw it is trying to avoid.
        /// </summary>
        PlayerAnimation _animDriver;
        Health _healthDriver;
        bool _simulationOn = true;

        /// <summary>
        /// Switches off the sibling components that tick every frame but have nothing to do
        /// for a frozen pedestrian.
        ///
        /// At a population of 1,000 there are four per-frame calls per person -- Pedestrian,
        /// Health, TownNpc and PlayerAnimation -- and the last is the expensive one:
        /// <see cref="PlayerAnimation.LateUpdate"/> reads state info, transition state and
        /// layer weights off the Animator every frame, which is native interop, and it does it
        /// whether or not the Animator itself is enabled. Four thousand managed calls and
        /// several thousand interop calls a frame is why culling the renderers changed the
        /// frame time by 0.07 ms: the cost was never on the GPU.
        ///
        /// <b>Pedestrian itself is deliberately left enabled.</b> Its OnDisable removes it
        /// from the witness registry, which is the list the director sweeps -- disabling it
        /// would take it out of the crowd permanently and it would never be promoted back.
        /// Its own Update early-outs on one enum compare, which is already cheap.
        ///
        /// <b>RagdollLite is never touched</b> either: it owns the death coroutine, and a
        /// corpse that stops mid-collapse because it drifted out of the band would be BUG-012
        /// again by another route.
        /// </summary>
        public void SetSimulationEnabled(bool on)
        {
            if (_simulationOn == on) return;
            _simulationOn = on;

            if (_animDriver == null) _animDriver = GetComponentInChildren<PlayerAnimation>(true);
            if (_healthDriver == null) _healthDriver = GetComponent<Health>();

            if (_animDriver != null) _animDriver.enabled = on;

            // Health only ticks to regenerate. A frozen pedestrian not healing is invisible,
            // and Apply() still works on a disabled component, so damage is unaffected.
            if (_healthDriver != null) _healthDriver.enabled = on;
        }

        public void SetRenderersVisible(bool on)
        {
            if (_visibilityApplied && _renderersOn == on) return;
            _visibilityApplied = true;
            _renderersOn = on;

            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = on;
        }
        bool _headingToB = true;
        bool _hasCleared;
        float _dwellTimer;
        float _verticalVelocity;
        float _fleeTimer;
        Vector3 _threat;
        Vector3 _wanderTarget;
        Vector3 _origin;

        public bool IsDead => _state == State.Dead;
        public bool IsFleeing => _state == State.Fleeing;

        Vector3 Target => Route == PedestrianRoute.Wander
            ? _wanderTarget
            : (_headingToB ? PointB : PointA);

        Vector3 LegOrigin => _headingToB ? PointA : PointB;

        Heading CurrentHeading => _headingToB ? WalkHeading : WalkHeading.Opposite();

        bool NeedsSignal => Route == PedestrianRoute.Crossing && IntersectionIndex >= 0;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _anim = GetComponentInChildren<PlayerAnimation>();
            _health = GetComponent<Health>();
            _ragdoll = GetComponent<RagdollLite>();
        }

        void OnEnable()
        {
            Registry.Add(this);
            if (_health != null) _health.Died += OnDied;
        }

        void OnDisable()
        {
            Registry.Remove(this);
            if (_health != null) _health.Died -= OnDied;
        }

        void Start()
        {
            _network = RoadNetwork.Instance;
            _origin = PointA;
            transform.position = PointA;
            if (Route == PedestrianRoute.Wander) PickWanderTarget();
        }

        void Update()
        {
            if (_state == State.Dead) return;

            // --- crowd LOD, Section 3 -------------------------------------------------
            if (Lod == CrowdLod.Frozen) return;

            float step = Time.deltaTime;

            if (Lod == CrowdLod.Simple)
            {
                if (_tickPhase < 0) _tickPhase = (_phaseCounter++ & 0x7fffffff) % SimpleTickStride;
                if ((Time.frameCount + _tickPhase) % SimpleTickStride != 0) return;

                // Ticking one frame in four means this tick has to cover four frames of
                // movement, or everyone in the mid band walks at a quarter speed and the
                // band boundary becomes visible as a line where people suddenly hurry up.
                step = Time.deltaTime * SimpleTickStride;
            }

            _lodStep = step;
            // ---------------------------------------------------------------------------

            if (_dwellTimer > 0f)
            {
                _dwellTimer -= _lodStep;
                Move(Vector3.zero, WalkSpeed);
                return;
            }

            switch (_state)
            {
                case State.Walking: TickWalking(); break;
                case State.WaitingToCross: TickWaiting(); break;
                case State.Crossing: TickCrossing(); break;
                case State.Fleeing: TickFleeing(); break;
            }
        }

        // -------------------------------------------------------------- behaviours

        void TickWalking()
        {
            Vector3 to = Target - transform.position;
            to.y = 0f;
            float distance = to.magnitude;

            // A crossing leg pauses at the kerb until the signal allows it.
            if (NeedsSignal && !_hasCleared)
            {
                float travelled = Vector3.Distance(LegOrigin, transform.position);
                float legLength = Vector3.Distance(LegOrigin, Target);

                if (travelled >= Mathf.Max(0f, legLength * 0.5f - KerbStopDistance))
                {
                    _state = State.WaitingToCross;
                    Move(Vector3.zero, WalkSpeed);
                    return;
                }
            }

            if (distance <= ArriveRadius) { ArriveAtEnd(); return; }
            Move(to.normalized, WalkSpeed);
        }

        void TickWaiting()
        {
            if (MayCross())
            {
                _hasCleared = true;
                _state = State.Crossing;
                return;
            }
            Move(Vector3.zero, WalkSpeed);
        }

        void TickCrossing()
        {
            Vector3 to = Target - transform.position;
            to.y = 0f;

            if (to.magnitude <= ArriveRadius) { ArriveAtEnd(); return; }

            // Once committed, keep going even if the light turns -- stopping mid-road is worse.
            Move(to.normalized, WalkSpeed);
        }

        void TickFleeing()
        {
            _fleeTimer -= _lodStep;

            Vector3 away = transform.position - _threat;
            away.y = 0f;

            bool safe = away.magnitude >= SafeDistance;
            if (_fleeTimer <= 0f || safe)
            {
                _state = State.Walking;
                _hasCleared = false;
                if (Route == PedestrianRoute.Wander) PickWanderTarget();
                return;
            }

            Move(away.normalized, FleeSpeed);
        }

        void ArriveAtEnd()
        {
            if (Route == PedestrianRoute.Wander)
            {
                PickWanderTarget();
            }
            else
            {
                _headingToB = !_headingToB;
                _hasCleared = false;
            }

            _state = State.Walking;
            _dwellTimer = Random.Range(DwellRange.x, DwellRange.y);
            Move(Vector3.zero, WalkSpeed);
        }

        void PickWanderTarget()
        {
            // Sample around the starting point rather than the current one, so a pedestrian
            // cannot random-walk away from their block over time.
            // Reject targets out at sea rather than only steering away from them later. The
            // steer alone would leave a beach pedestrian permanently walking at a destination
            // they can never reach, shuffling along the waterline forever.
            ResolveWater();

            for (int attempt = 0; attempt < 6; attempt++)
            {
                _wanderTarget = _origin + new Vector3(
                    Random.Range(-WanderExtents.x, WanderExtents.x),
                    0f,
                    Random.Range(-WanderExtents.y, WanderExtents.y));
                _wanderTarget.y = transform.position.y;

                if (_water == null || DepthAt(_wanderTarget) <= MaxWadeDepth) return;
            }

            // Six tries all wet -- this pedestrian's box is mostly sea. Stay put rather than
            // marching at the horizon.
            _wanderTarget = transform.position;
        }

        bool MayCross()
        {
            if (!NeedsSignal) return true;

            _network ??= RoadNetwork.Instance;
            var junction = _network?.GetIntersection(IntersectionIndex);
            if (junction == null) return true;

            return junction.PedestrianMayCross(CurrentHeading);
        }

        // ------------------------------------------------------------------ public

        /// <summary>Send this pedestrian running from a threat position.</summary>
        public void Panic(Vector3 threatPosition)
        {
            if (_state == State.Dead) return;

            _threat = threatPosition;
            _fleeTimer = FleeDuration;
            _dwellTimer = 0f;

            if (_state != State.Fleeing) _state = State.Fleeing;
        }

        void OnDied(DamageInfo info)
        {
            if (_state == State.Dead) return;
            _state = State.Dead;

            // Neighbours saw it happen.
            AlarmNear(transform.position, 18f, info.Attacker != null
                ? info.Attacker.transform.position
                : transform.position);

            if (_ragdoll != null)
                _ragdoll.Collapse(info.Direction, Mathf.Clamp(info.Amount * 0.08f, 1.5f, 8f));
            else
                enabled = false;
        }

        // ------------------------------------------------------------------- water

        // Resolved once for the whole crowd. Terrain.SampleHeight is a heightmap lookup, not a
        // physics raycast, so the probes below are cheap -- but 300 pedestrians paying for
        // three of them every tick is not, which is why ShouldCheckWater gates on a float
        // compare first.
        static Terrain _terrain;
        static WaterVolume _water;
        static bool _waterResolved;

        static void ResolveWater()
        {
            if (_waterResolved) return;
            _waterResolved = true;
            _terrain = Object.FindAnyObjectByType<Terrain>();
            _water = Object.FindAnyObjectByType<WaterVolume>();
        }

        /// <summary>Ground height at a world XZ, or a very high value where there is no terrain.</summary>
        static float GroundAt(Vector3 world)
        {
            if (_terrain == null) return 1000f;
            return _terrain.SampleHeight(world) + _terrain.transform.position.y;
        }

        /// <summary>
        /// Water depth at a point. Negative on dry land.
        /// </summary>
        static float DepthAt(Vector3 world)
        {
            if (_water == null) return -1000f;
            return _water.SeaLevel - GroundAt(world);
        }

        /// <summary>
        /// Steers a pedestrian away from water deeper than they are willing to wade.
        ///
        /// <b>The bug this fixes.</b> Nothing in this class consulted the water at all.
        /// PickWanderTarget sampled a random point in a rectangle, and TickFleeing ran directly
        /// away from a threat -- so a pedestrian whose patrol box overlapped the beach, or who
        /// panicked facing the sea, walked straight out into the ocean and kept walking along
        /// the seabed for as long as the box or the panic lasted. A CharacterController with
        /// gravity has no opinion about being underwater.
        ///
        /// Turning back rather than swimming, deliberately: the player has a Swim state but
        /// Pedestrian has no swim behaviour, no buoyancy and no surface constraint, so
        /// "transition to swimming" would be a new movement mode, not a reused clip.
        ///
        /// The steer is a shore-follow, not a bounce: rotating the heading to whichever side is
        /// shallower keeps a pedestrian walking the waterline the way someone actually would,
        /// where reversing would make them oscillate on the spot at the water's edge.
        /// </summary>
        Vector3 AvoidDeepWater(Vector3 desired, float speed)
        {
            if (desired.sqrMagnitude < 0.0001f) return desired;

            ResolveWater();
            if (_water == null || _terrain == null) return desired;

            // Cheap gate: anyone standing well above the waterline cannot be about to wade.
            // This is a float compare, and it is what keeps the cost off the 95% of the crowd
            // that is in the city, which sits 7 m above sea level.
            if (transform.position.y > _water.SeaLevel + 2f) return desired;

            float step = Mathf.Max(0.35f, speed * _lodStep * WadeLookahead);
            Vector3 ahead = transform.position + desired * step;

            if (DepthAt(ahead) <= MaxWadeDepth) return desired;

            // Too deep straight on. Try the two alongshore headings and take the drier one.
            Vector3 left = Quaternion.Euler(0f, -75f, 0f) * desired;
            Vector3 right = Quaternion.Euler(0f, 75f, 0f) * desired;

            float dLeft = DepthAt(transform.position + left * step);
            float dRight = DepthAt(transform.position + right * step);

            if (dLeft <= MaxWadeDepth || dRight <= MaxWadeDepth)
                return (dLeft < dRight ? left : right).normalized;

            // Boxed in -- head back towards dry land.
            Vector3 back = -desired;
            if (DepthAt(transform.position + back * step) < DepthAt(ahead)) return back.normalized;

            return back.normalized;
        }

        /// <summary>
        /// True when this pedestrian is already standing in water deeper than they should be.
        /// Used to walk spawn mistakes back out rather than leaving someone on the seabed.
        /// </summary>
        bool InDeepWater()
        {
            ResolveWater();
            if (_water == null || _terrain == null) return false;
            if (transform.position.y > _water.SeaLevel + 2f) return false;
            return DepthAt(transform.position) > MaxWadeDepth;
        }

        // ---------------------------------------------------------------- movement

        void Move(Vector3 direction, float speed)
        {
            // Every state routes through here -- walking, crossing, fleeing and dwelling -- so
            // this is the one place the water rule has to be applied to cover all of them.
            // Fleeing is the case that mattered most: a panicking crowd on the beach ran at the
            // sea, and only a check on the actual motion catches that.
            Vector3 direction2 = AvoidDeepWater(direction, speed);

            // Already out of their depth (spawned there, or pushed): wade back towards land
            // under their own steam instead of standing in it.
            if (direction2.sqrMagnitude < 0.0001f && InDeepWater())
                direction2 = AvoidDeepWater(transform.forward, WalkSpeed);

            Vector3 desired = direction2;

            // Simple separation: step around anyone standing in your personal space.
            // Separation is the expensive half of the tick, and the mid band is far enough
            // away that a little overlap is not visible. Full LOD only.
            if (Lod == CrowdLod.Full && desired.sqrMagnitude > 0.0001f)
                desired = (desired + Separation() * SeparationStrength).normalized;

            bool moving = desired.sqrMagnitude > 0.0001f;

            if (moving)
            {
                Quaternion look = Quaternion.LookRotation(desired, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look,
                                                      1f - Mathf.Exp(-TurnSpeed * _lodStep));
            }

            _verticalVelocity = _cc.isGrounded ? -1f : _verticalVelocity + Gravity * _lodStep;

            Vector3 motion = desired * speed + Vector3.up * _verticalVelocity;
            _cc.Move(motion * _lodStep);

            // 0.5 is the walk pose on the locomotion blend, 1.0 the run.
            float blend = !moving ? 0f : (speed > WalkSpeed * 1.5f ? 1f : 0.5f);
            if (_anim != null) _anim.SetLocomotion(blend, true, false, 0f);
        }

        /// <summary>Scratch list for this pedestrian's neighbour query. One per instance.</summary>
        readonly List<Pedestrian> _neighbours = new List<Pedestrian>(16);

        /// <summary>
        /// Steer away from anyone standing too close.
        ///
        /// <b>This method is why the crowd could not grow.</b> It used to walk the entire
        /// registry, and it runs once per pedestrian per frame -- O(n²) over the whole
        /// population. At 66 pedestrians that is 4,356 distance checks a frame and invisible;
        /// at the 1,000 Section 3 asks for it is a million a frame, and it is pure CPU, so no
        /// amount of animation or renderer LOD touches it.
        ///
        /// It now asks <see cref="CrowdGrid"/> for the handful of people who could possibly
        /// be within SeparationRadius. The cost becomes local crowd density rather than total
        /// population, which is the property that actually scales.
        /// </summary>
        Vector3 Separation()
        {
            _neighbours.Clear();
            CrowdGrid.Query(transform.position, SeparationRadius, _neighbours);

            Vector3 push = Vector3.zero;

            for (int i = 0; i < _neighbours.Count; i++)
            {
                var other = _neighbours[i];
                if (other == null || other == this || other.IsDead) continue;

                Vector3 delta = transform.position - other.transform.position;
                delta.y = 0f;

                float d = delta.sqrMagnitude;
                if (d < 0.0001f) continue;

                // Closer neighbours push harder.
                push += delta.normalized * (1f - Mathf.Sqrt(d) / SeparationRadius);
            }

            return push;
        }

        void OnDrawGizmosSelected()
        {
            if (Route == PedestrianRoute.Crossing)
            {
                Gizmos.color = NeedsSignal ? Color.yellow : Color.cyan;
                Gizmos.DrawLine(PointA, PointB);
                Gizmos.DrawWireSphere(PointA, 0.4f);
                Gizmos.DrawWireSphere(PointB, 0.4f);
            }
            else
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(PointA, new Vector3(WanderExtents.x * 2f, 0.2f, WanderExtents.y * 2f));
            }
        }
    }
}
