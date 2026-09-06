using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    public enum Sfx { Gunshot, Punch, Horn, Crash, UiClick, Chime, Footstep }

    /// <summary>
    /// Music, ambience and one-shot effects, with separate volume buses.
    ///
    /// One-shots come from a fixed pool of AudioSources rather than
    /// PlayClipAtPoint, which allocates a GameObject per sound and is a steady source of GC
    /// churn on mobile. When the pool is exhausted the oldest voice is stolen -- a dropped
    /// footstep is better than a frame hitch.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        static AudioManager _instance;

        public static AudioManager Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<AudioManager>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Voices")]
        [Tooltip("Simultaneous one-shot effects. Beyond this the oldest is stolen.")]
        public int VoiceCount = 12;

        [Header("Mix")]
        [Range(0f, 1f)] public float MasterVolume = 1f;
        [Range(0f, 1f)] public float MusicVolume = 0.5f;
        [Range(0f, 1f)] public float SfxVolume = 0.85f;

        [Header("Ambience")]
        [Tooltip("Ambience is quieter indoors, where street noise should not reach.")]
        [Range(0f, 1f)] public float IndoorAmbienceScale = 0.25f;
        public float AmbienceFadeSpeed = 1.5f;

        AudioSource _music;
        AudioSource _ambience;
        readonly List<AudioSource> _voices = new List<AudioSource>();
        int _nextVoice;

        readonly Dictionary<Sfx, AudioClip> _clips = new Dictionary<Sfx, AudioClip>();
        AudioClip _engineLoop;

        float _ambienceTarget = 1f;
        float _ambienceCurrent = 1f;

        public AudioClip EngineLoop => _engineLoop;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            Instance = this;

            BuildClips();
            BuildSources();
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            ApplySettings();
            PlayMusic();
            PlayAmbience();

            var interiors = InteriorManager.Instance;
            if (interiors != null)
            {
                interiors.Entered += _ => _ambienceTarget = IndoorAmbienceScale;
                interiors.Exited += _ => _ambienceTarget = 1f;
            }
        }

        void BuildClips()
        {
            _clips[Sfx.Gunshot] = ProceduralAudio.Gunshot();
            _clips[Sfx.Punch] = ProceduralAudio.Punch();
            _clips[Sfx.Horn] = ProceduralAudio.Horn();
            _clips[Sfx.Crash] = ProceduralAudio.Crash();
            _clips[Sfx.UiClick] = ProceduralAudio.UiClick();
            _clips[Sfx.Chime] = ProceduralAudio.Chime();
            _clips[Sfx.Footstep] = ProceduralAudio.Footstep();

            _engineLoop = ProceduralAudio.EngineLoop();
        }

        void BuildSources()
        {
            _music = CreateSource("Music", loop: true);
            _music.clip = ProceduralAudio.MusicLoop();

            _ambience = CreateSource("Ambience", loop: true);
            _ambience.clip = ProceduralAudio.Ambience();

            for (int i = 0; i < VoiceCount; i++)
            {
                var voice = CreateSource("Voice_" + i, loop: false);
                // One-shots are positioned in the world; music and ambience are not.
                voice.spatialBlend = 1f;
                voice.rolloffMode = AudioRolloffMode.Linear;
                voice.minDistance = 6f;
                voice.maxDistance = 90f;
                _voices.Add(voice);
            }
        }

        AudioSource CreateSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }

        void Update()
        {
            // Ambience ducks smoothly rather than cutting when a door opens.
            _ambienceCurrent = Mathf.MoveTowards(_ambienceCurrent, _ambienceTarget,
                                                 AmbienceFadeSpeed * Time.unscaledDeltaTime);
            if (_ambience != null)
                _ambience.volume = MasterVolume * MusicVolume * 0.6f * _ambienceCurrent;
        }

        // ------------------------------------------------------------------- mix

        /// <summary>Re-read volumes from settings and apply them to every source.</summary>
        public void ApplySettings()
        {
            var settings = GameSettings.Instance;
            if (settings != null)
            {
                MasterVolume = settings.MasterVolume;
                MusicVolume = settings.MusicVolume;
                SfxVolume = settings.SfxVolume;
            }

            if (_music != null) _music.volume = MasterVolume * MusicVolume;
            if (_ambience != null)
                _ambience.volume = MasterVolume * MusicVolume * 0.6f * _ambienceCurrent;

            foreach (var voice in _voices) voice.volume = MasterVolume * SfxVolume;

            // A muted master should silence everything, including anything already playing.
            AudioListener.volume = 1f;
        }

        public void PlayMusic()
        {
            if (_music == null || _music.isPlaying) return;
            _music.Play();
        }

        public void StopMusic() => _music?.Stop();

        public void PlayAmbience()
        {
            if (_ambience == null || _ambience.isPlaying) return;
            _ambience.Play();
        }

        // ---------------------------------------------------------------- one-shot

        /// <summary>Play an effect in the world.</summary>
        public void Play(Sfx sfx, Vector3 position, float volumeScale = 1f, float pitch = 1f)
        {
            if (!_clips.TryGetValue(sfx, out var clip) || clip == null) return;

            var voice = NextVoice();
            voice.transform.position = position;
            voice.spatialBlend = 1f;
            voice.pitch = pitch;
            voice.volume = MasterVolume * SfxVolume * volumeScale;
            voice.PlayOneShot(clip);
        }

        /// <summary>Play an effect without a position, for UI.</summary>
        public void PlayUi(Sfx sfx, float volumeScale = 1f)
        {
            if (!_clips.TryGetValue(sfx, out var clip) || clip == null) return;

            var voice = NextVoice();
            voice.spatialBlend = 0f;
            voice.pitch = 1f;
            voice.volume = MasterVolume * SfxVolume * volumeScale;
            voice.PlayOneShot(clip);
        }

        AudioSource NextVoice()
        {
            // Prefer an idle voice; steal round-robin only when all are busy.
            for (int i = 0; i < _voices.Count; i++)
            {
                var candidate = _voices[(_nextVoice + i) % _voices.Count];
                if (!candidate.isPlaying)
                {
                    _nextVoice = (_nextVoice + i + 1) % _voices.Count;
                    return candidate;
                }
            }

            var stolen = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Count;
            return stolen;
        }
    }
}
