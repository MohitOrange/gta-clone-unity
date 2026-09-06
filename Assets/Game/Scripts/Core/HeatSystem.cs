using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Accumulated police attention, and the wanted stars derived from it.
    ///
    /// Phase 2 only feeds it (traffic violations) and displays it; the pursuit behaviour that
    /// consumes it arrives in Phase 3. It lives here now so violations are recorded from the
    /// moment traffic exists, rather than being retrofitted onto driving code later.
    /// </summary>
    public class HeatSystem : MonoBehaviour
    {
        static HeatSystem _instance;

        /// <summary>
        /// Resolves lazily rather than relying solely on Awake. A script recompile resets
        /// statics without re-running Awake on objects that already exist, which would
        /// otherwise leave this null for the rest of the play session and silently stop
        /// recording every offence.
        /// </summary>
        public static HeatSystem Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<HeatSystem>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Scale")]
        [Tooltip("Heat needed for each successive wanted star.")]
        public float[] StarThresholds = { 20f, 55f, 110f, 190f, 300f };

        [Header("Decay")]
        [Tooltip("Heat lost per second once nothing has been witnessed for CoolOffDelay.")]
        public float DecayPerSecond = 3.5f;
        [Tooltip("Seconds of good behaviour before heat starts falling.")]
        public float CoolOffDelay = 8f;

        [Header("Offence values")]
        public float RedLightHeat = 14f;
        public float VehicleCollisionHeat = 6f;
        public float PedestrianHitHeat = 45f;
        public float FiredWeaponHeat = 12f;
        public float AssaultHeat = 18f;
        public float KillCivilianHeat = 55f;
        public float VehicleTheftHeat = 22f;
        public float AssaultPoliceHeat = 40f;
        public float KillPoliceHeat = 90f;

        float _heat;
        float _sinceLastOffence;

        public float Heat => _heat;
        public int WantedLevel { get; private set; }
        public bool IsWanted => WantedLevel > 0;

        /// <summary>Seconds since the last recorded offence. Drives the "losing them" feel.</summary>
        public float TimeSinceOffence => _sinceLastOffence;

        /// <summary>Highest star count reachable, from the threshold table.</summary>
        public int MaxWantedLevel => StarThresholds.Length;

        /// <summary>Fired when the star count changes. HUD and (later) police AI listen.</summary>
        public event System.Action<int> WantedLevelChanged;

        /// <summary>Fired for every recorded offence, with a short human-readable reason.</summary>
        public event System.Action<string, float> OffenceRecorded;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            _sinceLastOffence += Time.deltaTime;

            if (_sinceLastOffence >= CoolOffDelay && _heat > 0f)
            {
                _heat = Mathf.Max(0f, _heat - DecayPerSecond * Time.deltaTime);
                Recompute();
            }
        }

        /// <summary>
        /// Record an offence. Only call this when it was actually witnessed -- an unseen crime
        /// generating heat is the single most annoying bug this system can have.
        /// </summary>
        public void AddHeat(float amount, string reason)
        {
            if (amount <= 0f) return;

            _heat += amount;
            _sinceLastOffence = 0f;
            Recompute();

            OffenceRecorded?.Invoke(reason, amount);
        }

        public void Clear()
        {
            _heat = 0f;
            Recompute();
        }

        void Recompute()
        {
            int stars = 0;
            for (int i = 0; i < StarThresholds.Length; i++)
                if (_heat >= StarThresholds[i]) stars = i + 1;

            if (stars == WantedLevel) return;
            WantedLevel = stars;
            WantedLevelChanged?.Invoke(stars);
        }
    }
}
