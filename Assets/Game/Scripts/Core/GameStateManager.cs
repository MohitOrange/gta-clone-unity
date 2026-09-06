using UnityEngine;

namespace MiniGTA
{
    public enum GameState { Playing, Busted, Wasted }

    /// <summary>
    /// Owns the run-ending states and the respawn that follows.
    ///
    /// Both endings converge on the same recovery -- lose the heat, lose the weapon or some
    /// cash, wake up somewhere safe -- because a mobile open world should never make failure
    /// feel like a reload. The distinction between Busted and Wasted is what it costs and
    /// where you wake up, not whether you continue.
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        static GameStateManager _instance;

        /// <summary>Resolves lazily: a script recompile resets statics without re-running Awake.</summary>
        public static GameStateManager Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<GameStateManager>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Respawn")]
        [Tooltip("Where the player wakes after being wasted.")]
        public Transform HospitalSpawn;
        [Tooltip("Where the player is released after being busted.")]
        public Transform PoliceStationSpawn;
        [Tooltip("Seconds the end-of-run screen holds before respawning.")]
        public float RespawnDelay = 3.5f;

        [Header("Penalties")]
        [Tooltip("Health restored on respawn, as a fraction of maximum.")]
        [Range(0.1f, 1f)] public float RespawnHealth = 1f;
        [Tooltip("Being busted confiscates your weapon.")]
        public bool BustConfiscatesWeapon = true;

        [Header("Refs")]
        public GameObject Player;

        GameState _state = GameState.Playing;
        float _respawnTimer;

        public GameState State => _state;
        public bool IsPlaying => _state == GameState.Playing;

        /// <summary>Fired whenever the run state changes. The HUD listens.</summary>
        public event System.Action<GameState> StateChanged;

        Health _playerHealth;
        PlayerController _playerController;
        PlayerVehicleController _playerDriving;
        PlayerCombat _playerCombat;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            if (Player == null) Player = GameObject.FindWithTag("Player");
            if (Player == null) return;

            _playerHealth = Player.GetComponent<Health>();
            _playerController = Player.GetComponent<PlayerController>();
            _playerDriving = Player.GetComponent<PlayerVehicleController>();
            _playerCombat = Player.GetComponent<PlayerCombat>();

            if (_playerHealth != null) _playerHealth.Died += OnPlayerDied;
        }

        void OnDestroyInternal()
        {
            if (_playerHealth != null) _playerHealth.Died -= OnPlayerDied;
        }

        void Update()
        {
            if (_state == GameState.Playing) return;

            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer <= 0f) Respawn();
        }

        void OnPlayerDied(DamageInfo info) => Wasted();

        public void Bust()
        {
            if (_state != GameState.Playing) return;
            EndRun(GameState.Busted);
        }

        public void Wasted()
        {
            if (_state != GameState.Playing) return;
            EndRun(GameState.Wasted);
        }

        void EndRun(GameState ending)
        {
            _state = ending;
            _respawnTimer = RespawnDelay;

            // Get the player out of any vehicle before freezing them, or they stay parented to
            // a car that is about to be despawned with the rest of the pursuit.
            if (_playerDriving != null && _playerDriving.IsDriving) _playerDriving.Exit();

            if (_playerController != null) _playerController.enabled = false;

            HeatSystem.Instance?.Clear();
            PoliceDispatcher.Instance?.StandDown();

            StateChanged?.Invoke(_state);
        }

        /// <summary>
        /// Cancel a pending respawn and put the player back on their feet where they fell.
        ///
        /// Used by the rewarded-ad revive. Deliberately keeps the player's position and clears
        /// their heat: reviving into the same firefight that just killed them, still wanted,
        /// would make the reward worthless.
        /// </summary>
        public bool ReviveInPlace(float healthFraction = 0.75f)
        {
            if (_state == GameState.Playing) return false;

            _respawnTimer = 0f;

            if (_playerHealth != null) _playerHealth.Revive(healthFraction);
            if (_playerController != null) _playerController.enabled = true;

            HeatSystem.Instance?.Clear();
            PoliceDispatcher.Instance?.StandDown();

            _state = GameState.Playing;
            StateChanged?.Invoke(_state);
            return true;
        }

        /// <summary>Hold the end-of-run screen open while something (an ad offer) decides.</summary>
        public void PauseRespawnCountdown(float seconds)
        {
            if (_state == GameState.Playing) return;
            _respawnTimer = Mathf.Max(_respawnTimer, seconds);
        }

        public void Respawn()
        {
            Transform target = _state == GameState.Busted ? PoliceStationSpawn : HospitalSpawn;
            Vector3 position = target != null
                ? target.position
                : (Player != null ? Player.transform.position + Vector3.up : Vector3.zero);

            if (_playerHealth != null) _playerHealth.Revive(RespawnHealth);

            if (_playerController != null)
            {
                _playerController.enabled = true;
                _playerController.Warp(position);
            }

            if (_state == GameState.Busted && BustConfiscatesWeapon && _playerCombat != null)
            {
                _playerCombat.HasPistol = false;
                _playerCombat.Ammo = 0;
                _playerCombat.Mode = WeaponMode.Unarmed;
            }

            HeatSystem.Instance?.Clear();

            _state = GameState.Playing;
            StateChanged?.Invoke(_state);
        }
    }
}
