using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Decides how much police response the player's wanted level deserves, and spawns or
    /// stands down units to match.
    ///
    /// Response is expressed as a table indexed by star count rather than a formula, because
    /// the escalation curve is a design decision that wants tuning by hand, not a calculation.
    /// Units are spawned out of sight and cleaned up the moment the player is clean, so a long
    /// session does not accumulate cruisers.
    /// </summary>
    public class PoliceDispatcher : MonoBehaviour
    {
        public static PoliceDispatcher Instance { get; private set; }

        [Header("Prefabs")]
        public GameObject CruiserPrefab;
        public GameObject OfficerPrefab;

        [Header("Response by wanted level")]
        [Tooltip("Cruisers in pursuit at each star count, at the baseline population. "
                 + "Index 0 is zero stars.")]
        public int[] CruisersPerStar = { 0, 1, 2, 3, 4, 6 };

        [Header("Response by city size (Section 4.1)")]
        [Tooltip("Scale the police response with the size of the crowd, rather than dispatching "
                 + "the same handful of cars to a village and to a city.")]
        public bool ScaleWithPopulation = true;

        [Tooltip("The crowd size the CruisersPerStar table was tuned against. CrowdDirector's "
                 + "shipping default.")]
        public int BaselinePopulation = 300;

        [Tooltip("Ceiling on the multiplier, so the 1000-population stress setting cannot put "
                 + "twenty cruisers on screen and cost more than the crowd it is policing.")]
        public float MaxPopulationScale = 2.5f;

        [Header("Spawning")]
        [Tooltip("Cruisers appear at least this far away, so they never pop in on screen.")]
        public float MinSpawnDistance = 75f;
        public float MaxSpawnDistance = 170f;
        public float SpawnInterval = 2.5f;
        [Tooltip("Recycle a pursuit unit that falls further behind than this.")]
        public float AbandonDistance = 320f;

        [Header("Refs")]
        public Transform Player;

        [Header("Pooling")]
        [Tooltip("Dormant cruisers kept for reuse. Beyond this, stood-down units are destroyed. "
                 + "Sized for the scaled response: a pool smaller than the peak dispatch turns "
                 + "every stand-down into a destroy and every escalation into an instantiate.")]
        public int CruiserPoolSize = 16;
        [Tooltip("Dormant officers kept for reuse.")]
        public int OfficerPoolSize = 20;

        readonly List<PolicePursuit> _cruisers = new List<PolicePursuit>();
        readonly List<GameObject> _officers = new List<GameObject>();

        PrefabPool _cruiserPool;
        PrefabPool _officerPool;
        Transform _dormant;

        RoadNetwork _network;
        HeatSystem _heat;
        System.Random _rng;
        float _spawnTimer;

        public int ActiveCruisers => _cruisers.Count;
        public int ActiveOfficers => _officers.Count;

        /// <summary>Pool occupancy, for the performance readout.</summary>
        public string PoolReport =>
            "cruisers idle=" + (_cruiserPool != null ? _cruiserPool.Idle : 0)
            + "/created=" + (_cruiserPool != null ? _cruiserPool.Created : 0)
            + "  officers idle=" + (_officerPool != null ? _officerPool.Idle : 0)
            + "/created=" + (_officerPool != null ? _officerPool.Created : 0);

        void Awake()
        {
            Instance = this;
            _rng = new System.Random(9182);

            // Dormant units park under their own object so they are obvious in the hierarchy
            // and cannot be mistaken for live pursuit while asleep.
            _dormant = new GameObject("PolicePool").transform;
            _dormant.SetParent(transform, false);

            _cruiserPool = new PrefabPool(CruiserPrefab, transform, CruiserPoolSize);
            _officerPool = new PrefabPool(OfficerPrefab, _dormant, OfficerPoolSize);
        }

        /// <summary>Lets a pursuit unit put its officers out through the shared pool.</summary>
        public GameObject TakeOfficer(Vector3 position, Quaternion rotation)
            => _officerPool?.Take(position, rotation);

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _network = RoadNetwork.Instance;
            _heat = HeatSystem.Instance;

            if (Player == null)
            {
                var p = GameObject.FindWithTag("Player");
                if (p != null) Player = p.transform;
            }
        }

        void Update()
        {
            _heat ??= HeatSystem.Instance;
            _network ??= RoadNetwork.Instance;
            if (_heat == null || _network == null || Player == null) return;

            Prune();

            int stars = Mathf.Clamp(_heat.WantedLevel, 0, CruisersPerStar.Length - 1);
            int wanted = CruisersForStars(stars);

            if (wanted == 0) { StandDown(); return; }

            _spawnTimer -= Time.deltaTime;
            if (_cruisers.Count < wanted && _spawnTimer <= 0f)
            {
                _spawnTimer = SpawnInterval;
                SpawnCruiser();
            }

            RecycleStragglers();
        }

        /// <summary>
        /// Section 4.1. Cruisers to dispatch at a given star count, scaled by how big the city
        /// actually is.
        ///
        /// The table was tuned when the crowd was 66 people. Section 3 took that to 300 and
        /// allows 1000, and a fixed six-car maximum response in a city of a thousand reads as a
        /// police force that has given up -- the same six cars are simply a smaller fraction of
        /// a bigger place. Scaling keeps the *density* of the response constant, which is what
        /// the player actually perceives.
        ///
        /// Capped rather than linear. At 1000 the uncapped multiplier is 3.3x, which turns a
        /// five-star response into twenty pursuing cruisers -- more expensive than the entire
        /// crowd they are driving through, and a difficulty spike nobody asked for. The cap
        /// keeps the top-end response to 15 cars.
        ///
        /// Zero stars stays zero at any population: no crime, no police, however big the city.
        /// </summary>
        public int CruisersForStars(int stars)
        {
            stars = Mathf.Clamp(stars, 0, CruisersPerStar.Length - 1);
            int baseCount = CruisersPerStar[stars];
            if (baseCount == 0 || !ScaleWithPopulation) return baseCount;

            return Mathf.Max(baseCount, Mathf.RoundToInt(baseCount * PopulationScale));
        }

        /// <summary>
        /// The multiplier the crowd size implies. 1.0 when there is no crowd system in the
        /// scene, so a scene without one behaves exactly as it did before this existed.
        /// </summary>
        public float PopulationScale
        {
            get
            {
                if (!ScaleWithPopulation || BaselinePopulation <= 0) return 1f;

                int population = Pedestrian.Witnesses.Count;
                if (population <= 0) return 1f;

                return Mathf.Clamp((float)population / BaselinePopulation, 1f, MaxPopulationScale);
            }
        }

        void Prune()
        {
            _cruisers.RemoveAll(c => c == null || c.GetComponent<Vehicle>() == null
                                     || c.GetComponent<Vehicle>().IsWrecked);
            _officers.RemoveAll(o => o == null);
        }

        void SpawnCruiser()
        {
            if (CruiserPrefab == null) return;

            int node = FindSpawnNode();
            if (node < 0) return;

            var laneNode = _network.GetNode(node);
            var go = _cruiserPool.Take(
                laneNode.Position + Vector3.up * 0.35f,
                Quaternion.LookRotation(laneNode.Heading.ToVector(), Vector3.up));
            if (go == null) return;

            go.name = "Pursuit_" + _cruisers.Count;

            // A pursuit cruiser must not also be running the civilian traffic AI.
            var traffic = go.GetComponent<TrafficCar>();
            if (traffic != null) Destroy(traffic);

            var pursuit = go.GetComponent<PolicePursuit>() ?? go.AddComponent<PolicePursuit>();
            pursuit.OfficerPrefab = OfficerPrefab;
            pursuit.ResetDeployment();

            if (go.GetComponent<PoliceVehicle>() == null) go.AddComponent<PoliceVehicle>();

            _cruisers.Add(pursuit);
        }

        int FindSpawnNode()
        {
            for (int attempt = 0; attempt < 48; attempt++)
            {
                int candidate = _network.RandomDrivableNode(_rng);
                if (candidate < 0) continue;

                var node = _network.GetNode(candidate);
                float d = Vector3.Distance(node.Position, Player.position);
                if (d < MinSpawnDistance || d > MaxSpawnDistance) continue;

                return candidate;
            }
            return -1;
        }

        void RecycleStragglers()
        {
            foreach (var cruiser in _cruisers)
            {
                if (cruiser == null) continue;
                float d = Vector3.Distance(cruiser.transform.position, Player.position);
                if (d < AbandonDistance) continue;

                int node = FindSpawnNode();
                if (node < 0) continue;

                var laneNode = _network.GetNode(node);
                var body = cruiser.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.position = laneNode.Position + Vector3.up * 0.35f;
                    body.rotation = Quaternion.LookRotation(laneNode.Heading.ToVector(), Vector3.up);
                }
                cruiser.transform.SetPositionAndRotation(
                    laneNode.Position + Vector3.up * 0.35f,
                    Quaternion.LookRotation(laneNode.Heading.ToVector(), Vector3.up));
                cruiser.ResetDeployment();
            }
        }

        /// <summary>
        /// Take every unit off the street. Called when the player goes clean, is busted or dies.
        ///
        /// Units go back to the pool rather than being destroyed, so the next time heat rises
        /// the response costs a transform write instead of a round of instantiation.
        /// </summary>
        public void StandDown()
        {
            for (int i = _cruisers.Count - 1; i >= 0; i--)
            {
                var cruiser = _cruisers[i];
                if (cruiser == null) continue;

                // A wrecked car carries deformed geometry and a spent damage state; reusing it
                // would put a pre-crashed cruiser on the road.
                var vehicle = cruiser.GetComponent<Vehicle>();
                if (vehicle != null && vehicle.IsWrecked) Destroy(cruiser.gameObject);
                else _cruiserPool.Return(cruiser.gameObject);
            }
            _cruisers.Clear();

            for (int i = _officers.Count - 1; i >= 0; i--)
            {
                var officer = _officers[i];
                if (officer == null) continue;

                var health = officer.GetComponent<Health>();
                if (health != null && health.IsDead) Destroy(officer);
                else _officerPool.Return(officer);
            }
            _officers.Clear();
        }

        public void RegisterOfficer(GameObject officer)
        {
            if (officer != null) _officers.Add(officer);
        }

        /// <summary>Nearest pursuing unit, for the "losing them" indicator.</summary>
        public float DistanceToNearestUnit(Vector3 position)
        {
            float best = float.MaxValue;

            foreach (var c in _cruisers)
                if (c != null) best = Mathf.Min(best, Vector3.Distance(c.transform.position, position));

            foreach (var o in _officers)
                if (o != null) best = Mathf.Min(best, Vector3.Distance(o.transform.position, position));

            return best;
        }
    }
}
