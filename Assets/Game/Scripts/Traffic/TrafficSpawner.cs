using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Keeps a fixed pool of AI cars circulating near the player.
    ///
    /// A fixed pool rather than spawn-on-demand: the count is the frame budget, and on mobile
    /// that budget must be a hard number rather than an emergent one. Cars that drift too far
    /// away are recycled to a lane near the player instead of being destroyed, so the world
    /// feels populated wherever you are without the cost ever growing.
    /// </summary>
    public class TrafficSpawner : MonoBehaviour
    {
        public static TrafficSpawner Instance { get; private set; }

        [Header("Population")]
        public GameObject[] CarPrefabs = new GameObject[0];
        [Tooltip("Total AI cars alive at once. This is the mobile frame budget -- raise carefully.")]
        public int TrafficCount = 12;
        [Tooltip("Of the total, how many are police cars (they witness your traffic violations).")]
        public int PoliceCount = 2;
        public GameObject PolicePrefab;

        [Header("Recycling")]
        [Tooltip("Recycle a car further than this from the player.")]
        public float DespawnDistance = 260f;
        [Tooltip("Never drop a recycled car closer to the player than this.")]
        public float MinSpawnDistance = 70f;
        public float MaxSpawnDistance = 200f;
        public float RecycleCheckInterval = 1.5f;
        [Tooltip("Anything below this height has fallen out of the world and is rescued at once.")]
        public float KillFloorY = -20f;

        [Header("Refs")]
        public Transform Player;

        readonly List<TrafficCar> _cars = new List<TrafficCar>();
        RoadNetwork _network;
        System.Random _rng;
        float _checkTimer;

        void Awake()
        {
            Instance = this;
            _rng = new System.Random(4242);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _network = RoadNetwork.Instance;
            if (_network == null || _network.NodeCount == 0)
            {
                Debug.LogWarning("[Traffic] No road network; traffic disabled.");
                enabled = false;
                return;
            }

            if (Player == null)
            {
                var p = GameObject.FindWithTag("Player");
                if (p != null) Player = p.transform;
            }

            Populate();
        }

        void Populate()
        {
            if (CarPrefabs.Length == 0)
            {
                Debug.LogWarning("[Traffic] No car prefabs assigned; traffic disabled.");
                enabled = false;
                return;
            }

            for (int i = 0; i < TrafficCount; i++)
            {
                bool police = i < PoliceCount && PolicePrefab != null;
                GameObject prefab = police ? PolicePrefab : CarPrefabs[_rng.Next(CarPrefabs.Length)];

                var go = Instantiate(prefab, transform);
                go.name = (police ? "Police_" : "Traffic_") + i;

                var ai = go.GetComponent<TrafficCar>();
                if (ai == null) ai = go.AddComponent<TrafficCar>();

                // Section 5.2. Somebody is driving it, and can be pulled out of it.
                //
                // Police cars are deliberately excluded. An officer yanked out of a cruiser and
                // sent running as a panicked civilian is the wrong behaviour on every level --
                // the police response belongs to PoliceOfficer and PolicePursuit, which have
                // their own arrest flow. Stealing an empty cruiser stays possible; stealing one
                // out from under an officer is a Section 4 design question, not a carjack.
                if (!police && go.GetComponent<VehicleDriver>() == null)
                    go.AddComponent<VehicleDriver>();

                _cars.Add(ai);
                PlaceAwayFromPlayer(ai, initial: true);
            }

            Debug.Log("[Traffic] Spawned " + _cars.Count + " AI cars ("
                      + Mathf.Min(PoliceCount, _cars.Count) + " police).");
        }

        void Update()
        {
            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f || Player == null) return;
            _checkTimer = RecycleCheckInterval;

            foreach (var car in _cars)
            {
                if (car == null) continue;

                // Section 5.3. A car the player has taken is no longer traffic. Without this
                // the recycler teleports it to a fresh lane node the moment the player walks
                // 260 m from wherever they parked it -- so the car is not where it was left,
                // and there is nothing on screen to explain why. The height check below is
                // deliberately still skipped for these: rescuing a claimed car that has fallen
                // through the world would teleport it just the same, and a car lost down a hole
                // is a bug to fix rather than a reason to move the player's property.
                if (IsClaimed(car)) continue;

                // Height check first: a fallen car is also "far away", but it needs rescuing
                // for a different reason and must never be skipped.
                if (car.transform.position.y < KillFloorY)
                {
                    PlaceAwayFromPlayer(car, initial: false);
                    continue;
                }

                float d = Vector3.Distance(car.transform.position, Player.position);
                if (d > DespawnDistance) PlaceAwayFromPlayer(car, initial: false);
            }
        }

        /// <summary>Move a car to a fresh lane node. Called on stuck cars and distant ones.</summary>
        public void Recycle(TrafficCar car)
        {
            if (IsClaimed(car)) return;   // see the note in Update
            PlaceAwayFromPlayer(car, initial: false);
        }

        /// <summary>True once the player has driven this car. Section 5.3.</summary>
        static bool IsClaimed(TrafficCar car)
        {
            if (car == null) return false;
            var vehicle = car.GetComponent<Vehicle>();
            return vehicle != null && vehicle.ClaimedByPlayer;
        }

        void PlaceAwayFromPlayer(TrafficCar car, bool initial)
        {
            if (_network == null) return;

            int chosen = -1;

            // Try for a node in the sweet spot: far enough not to pop in on screen, close
            // enough that the player actually meets it.
            for (int attempt = 0; attempt < 40; attempt++)
            {
                int candidate = _network.RandomDrivableNode(_rng);
                if (candidate < 0) continue;

                var node = _network.GetNode(candidate);
                if (node == null) continue;

                if (Player != null)
                {
                    float d = Vector3.Distance(node.Position, Player.position);
                    if (d < MinSpawnDistance || d > MaxSpawnDistance) continue;
                }

                if (IsNodeOccupied(node.Position, car)) continue;

                chosen = candidate;
                break;
            }

            // Always fall back to any drivable node. Without this, a car that fails the
            // distance window keeps whatever position it had -- and for one that has fallen
            // out of the world that means falling forever, because the only thing that would
            // rescue it is the very placement that just gave up.
            if (chosen < 0) chosen = _network.RandomDrivableNode(_rng);
            if (chosen < 0) return;

            if (!car.PlaceOnNode(chosen))
                Debug.LogWarning("[Traffic] Failed to place " + car.name + " on node " + chosen);
        }

        bool IsNodeOccupied(Vector3 position, TrafficCar ignore)
        {
            foreach (var other in _cars)
            {
                if (other == null || other == ignore) continue;
                if ((other.transform.position - position).sqrMagnitude < 64f) return true;
            }

            // Do not drop a car on top of the player either.
            if (Player != null && (Player.position - position).sqrMagnitude < 36f) return true;
            return false;
        }
    }
}
