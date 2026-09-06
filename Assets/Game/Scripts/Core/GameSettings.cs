using UnityEngine;

namespace MiniGTA
{
    public enum QualityTier { Low = 0, Medium = 1, High = 2 }

    /// <summary>
    /// Player-facing options, and the one place that pushes them into the systems that care.
    ///
    /// Stored in PlayerPrefs rather than the save file on purpose: settings belong to the
    /// device, not to the character. Wiping your progress should not reset your volume, and
    /// a quality tier that suits this phone means nothing on another one.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class GameSettings : MonoBehaviour
    {
        const string Prefix = "minigta.";

        static GameSettings _instance;

        public static GameSettings Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<GameSettings>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Audio")]
        [Range(0f, 1f)] public float MasterVolume = 0.9f;
        [Range(0f, 1f)] public float MusicVolume = 0.5f;
        [Range(0f, 1f)] public float SfxVolume = 0.85f;

        [Header("Controls")]
        [Tooltip("Multiplier on the authored look sensitivity. 1 = as tuned.")]
        [Range(0.3f, 2.5f)] public float LookSensitivity = 1f;
        [Tooltip("Mirrors the HUD so the stick sits on the right.")]
        public bool LeftHanded;
        public bool InvertLookY;

        [Header("Graphics")]
        public QualityTier Tier = QualityTier.Medium;

        /// <summary>Fired after any setting changes and has been applied.</summary>
        public event System.Action Changed;

        // The authored sensitivity, captured before the multiplier is ever applied. Without
        // this the multiplier would compound every time the slider moved.
        float _baseTouchSensitivity = -1f;
        float _baseMouseSensitivity = -1f;

        bool _loaded;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
            Load();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start() => ApplyAll();

        // ------------------------------------------------------------------ store

        public void Load()
        {
            if (_loaded) return;
            _loaded = true;

            MasterVolume = PlayerPrefs.GetFloat(Prefix + "master", MasterVolume);
            MusicVolume = PlayerPrefs.GetFloat(Prefix + "music", MusicVolume);
            SfxVolume = PlayerPrefs.GetFloat(Prefix + "sfx", SfxVolume);

            LookSensitivity = PlayerPrefs.GetFloat(Prefix + "sensitivity", LookSensitivity);
            LeftHanded = PlayerPrefs.GetInt(Prefix + "lefthanded", 0) == 1;
            InvertLookY = PlayerPrefs.GetInt(Prefix + "invertY", 0) == 1;

            // No stored tier means a first run: guess from the hardware rather than assuming.
            Tier = (QualityTier)PlayerPrefs.GetInt(Prefix + "tier", (int)DetectTier());
        }

        public void Store()
        {
            PlayerPrefs.SetFloat(Prefix + "master", MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "music", MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "sfx", SfxVolume);

            PlayerPrefs.SetFloat(Prefix + "sensitivity", LookSensitivity);
            PlayerPrefs.SetInt(Prefix + "lefthanded", LeftHanded ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "invertY", InvertLookY ? 1 : 0);

            PlayerPrefs.SetInt(Prefix + "tier", (int)Tier);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// First-run guess at what this device can handle.
        ///
        /// Deliberately conservative. A phone that could have run High and got Medium loses a
        /// little draw distance; a phone that gets High and cannot run it gets a slideshow and
        /// an uninstall.
        /// </summary>
        public static QualityTier DetectTier()
        {
#if UNITY_EDITOR
            return QualityTier.High;
#else
            int memory = SystemInfo.systemMemorySize;      // MB
            int cores = SystemInfo.processorCount;

            if (memory >= 6000 && cores >= 8) return QualityTier.High;
            if (memory >= 3000 && cores >= 4) return QualityTier.Medium;
            return QualityTier.Low;
#endif
        }

        // ------------------------------------------------------------------ apply

        public void ApplyAll()
        {
            ApplyAudio();
            ApplyControls();
            ApplyGraphics();
            Changed?.Invoke();
        }

        public void ApplyAudio() => AudioManager.Instance?.ApplySettings();

        public void ApplyControls()
        {
            var hub = InputHub.Instance;
            if (hub != null)
            {
                if (_baseTouchSensitivity < 0f)
                {
                    _baseTouchSensitivity = hub.TouchLookSensitivity;
                    _baseMouseSensitivity = hub.MouseLookSensitivity;
                }

                hub.TouchLookSensitivity = _baseTouchSensitivity * LookSensitivity;
                hub.MouseLookSensitivity = _baseMouseSensitivity * LookSensitivity;
            }

            var camera = FindAnyObjectByType<ThirdPersonCamera>();
            if (camera != null) camera.InvertY = InvertLookY;

            HudLayout.Instance?.SetLeftHanded(LeftHanded);
        }

        public void ApplyGraphics() => PerformanceTuner.Instance?.Apply(Tier);

        // ---------------------------------------------------------------- setters

        public void SetMasterVolume(float value) { MasterVolume = Mathf.Clamp01(value); Commit(audio: true); }
        public void SetMusicVolume(float value) { MusicVolume = Mathf.Clamp01(value); Commit(audio: true); }
        public void SetSfxVolume(float value) { SfxVolume = Mathf.Clamp01(value); Commit(audio: true); }

        public void SetLookSensitivity(float value)
        {
            LookSensitivity = Mathf.Clamp(value, 0.3f, 2.5f);
            Commit(controls: true);
        }

        public void SetLeftHanded(bool value) { LeftHanded = value; Commit(controls: true); }
        public void SetInvertLookY(bool value) { InvertLookY = value; Commit(controls: true); }

        public void SetTier(QualityTier tier) { Tier = tier; Commit(graphics: true); }

        void Commit(bool audio = false, bool controls = false, bool graphics = false)
        {
            if (audio) ApplyAudio();
            if (controls) ApplyControls();
            if (graphics) ApplyGraphics();

            Store();
            Changed?.Invoke();
        }
    }
}
