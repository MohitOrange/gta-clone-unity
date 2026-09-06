using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Engine sound for whatever the player is driving.
    ///
    /// One source, not one per vehicle. Twenty AI cars all running a looping AudioSource would
    /// cost twenty voices and twenty distance calculations every frame to produce a wash of
    /// engine noise nobody can pick apart; the car under the player is the only one whose
    /// engine carries information, so it is the only one that gets a sound.
    /// </summary>
    public class VehicleAudio : MonoBehaviour
    {
        [Header("Pitch")]
        [Tooltip("Engine pitch at a standstill.")]
        public float IdlePitch = 0.75f;
        [Tooltip("Engine pitch at TopSpeedKph and above.")]
        public float MaxPitch = 2.3f;
        public float TopSpeedKph = 150f;
        [Tooltip("How fast pitch chases speed. Instant tracking sounds like a synthesiser.")]
        public float PitchSmoothing = 6f;

        [Header("Level")]
        [Range(0f, 1f)] public float IdleVolume = 0.35f;
        [Range(0f, 1f)] public float DrivingVolume = 0.7f;
        public float FadeSpeed = 3f;

        PlayerVehicleController _driver;
        AudioSource _source;

        float _pitch;
        float _volume;

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _driver = player.GetComponent<PlayerVehicleController>();

            var manager = AudioManager.Instance;
            if (manager == null) return;

            var go = new GameObject("EngineVoice");
            go.transform.SetParent(manager.transform, false);

            _source = go.AddComponent<AudioSource>();
            _source.clip = manager.EngineLoop;
            _source.loop = true;
            _source.playOnAwake = false;
            // 2D: the source rides with the player's own vehicle, so panning it would put the
            // engine somewhere other than under the camera.
            _source.spatialBlend = 0f;
            _source.volume = 0f;
            _pitch = IdlePitch;
        }

        void Update()
        {
            if (_source == null) return;

            var vehicle = _driver != null ? _driver.CurrentVehicle : null;
            bool running = vehicle != null && !vehicle.IsWrecked;

            float targetPitch = IdlePitch;
            float targetVolume = 0f;

            if (running)
            {
                float t = Mathf.Clamp01(vehicle.SpeedKph / Mathf.Max(1f, TopSpeedKph));
                targetPitch = Mathf.Lerp(IdlePitch, MaxPitch, t);
                targetVolume = Mathf.Lerp(IdleVolume, DrivingVolume, t);

                if (!_source.isPlaying) _source.Play();
            }

            _pitch = Mathf.Lerp(_pitch, targetPitch, PitchSmoothing * Time.deltaTime);
            _volume = Mathf.MoveTowards(_volume, targetVolume, FadeSpeed * Time.deltaTime);

            var settings = AudioManager.Instance;
            _source.pitch = _pitch;
            _source.volume = _volume * (settings != null
                ? settings.MasterVolume * settings.SfxVolume
                : 1f);

            // Stop rather than run a silent loop, so the voice is not costing mixer time while
            // the player is on foot.
            if (!running && _volume <= 0.001f && _source.isPlaying) _source.Stop();
        }
    }
}
