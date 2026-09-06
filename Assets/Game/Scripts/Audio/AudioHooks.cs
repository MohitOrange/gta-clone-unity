using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The one file that knows both what happens in the game and what it should sound like.
    ///
    /// Kept separate from the systems it listens to for the same reason <c>AdRewards</c> is:
    /// combat, driving and missions should not have to grow an audio dependency to make a
    /// noise. Everything here is a subscription to an event that already existed.
    /// </summary>
    public class AudioHooks : MonoBehaviour
    {
        [Header("Impacts")]
        [Tooltip("Crashes below this closing speed are not worth a sound.")]
        public float MinCrashSpeed = 5f;
        [Tooltip("Closing speed treated as a full-volume crash.")]
        public float LoudCrashSpeed = 22f;

        PlayerCombat _combat;
        Health _playerHealth;
        Transform _player;

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                _player = player.transform;
                _combat = player.GetComponent<PlayerCombat>();
                _playerHealth = player.GetComponent<Health>();
            }

            if (_combat != null) _combat.Attacked += OnAttacked;
            if (_playerHealth != null) _playerHealth.Damaged += OnPlayerDamaged;

            VehicleHorn.Honked += OnHorn;
            VehicleImpact.Occurred += OnImpact;

            var progress = PlayerProgress.Instance;
            if (progress != null) progress.LeveledUp += OnLevelUp;

            var missions = MissionManager.Instance;
            if (missions != null) missions.MissionEnded += OnMissionEnded;
        }

        void OnDestroy()
        {
            if (_combat != null) _combat.Attacked -= OnAttacked;
            if (_playerHealth != null) _playerHealth.Damaged -= OnPlayerDamaged;

            VehicleHorn.Honked -= OnHorn;
            VehicleImpact.Occurred -= OnImpact;

            var progress = PlayerProgress.Instance;
            if (progress != null) progress.LeveledUp -= OnLevelUp;

            var missions = MissionManager.Instance;
            if (missions != null) missions.MissionEnded -= OnMissionEnded;
        }

        // ----------------------------------------------------------------- combat

        void OnAttacked(WeaponMode mode)
        {
            if (_player == null) return;

            var audio = AudioManager.Instance;
            if (audio == null) return;

            // Slight pitch variation, or repeated shots sound like a loop rather than a gun.
            if (mode == WeaponMode.Pistol)
                audio.Play(Sfx.Gunshot, _player.position, 1f, Random.Range(0.94f, 1.06f));
            else
                audio.Play(Sfx.Punch, _player.position, 1f, Random.Range(0.9f, 1.1f));
        }

        void OnPlayerDamaged(DamageInfo info)
        {
            if (_player == null) return;
            AudioManager.Instance?.Play(Sfx.Punch, _player.position, 0.5f, 0.7f);
        }

        // --------------------------------------------------------------- vehicles

        void OnHorn(Vehicle vehicle)
        {
            if (vehicle == null) return;
            AudioManager.Instance?.Play(Sfx.Horn, vehicle.transform.position);
        }

        void OnImpact(Vehicle vehicle, Vector3 point, float speed)
        {
            if (speed < MinCrashSpeed) return;

            // Volume tracks how hard the hit was; a kerb scrape and a head-on should not be
            // the same sound at the same level.
            float t = Mathf.InverseLerp(MinCrashSpeed, LoudCrashSpeed, speed);
            AudioManager.Instance?.Play(Sfx.Crash, point,
                                        Mathf.Lerp(0.35f, 1f, t),
                                        Mathf.Lerp(1.15f, 0.85f, t));
        }

        // ------------------------------------------------------------ progression

        void OnLevelUp(int level, string unlockId) => AudioManager.Instance?.PlayUi(Sfx.Chime);

        void OnMissionEnded(MissionBase mission, bool success, string title, string detail)
        {
            if (success) AudioManager.Instance?.PlayUi(Sfx.Chime, 0.9f);
        }
    }
}
