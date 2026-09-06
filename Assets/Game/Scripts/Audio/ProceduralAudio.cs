using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Generates every sound in the game as raw samples at runtime.
    ///
    /// The project ships with no audio assets, and inventing binary .wav files is not something
    /// that can be reviewed in a diff. Synthesising them keeps the repo free of opaque blobs,
    /// makes every sound a handful of readable numbers, and means the whole audio system is
    /// testable without sourcing a library first. Swap any of these for a real clip later --
    /// AudioManager only cares that it gets an AudioClip.
    /// </summary>
    public static class ProceduralAudio
    {
        public const int SampleRate = 44100;

        /// <summary>Deterministic noise so a rebuild produces identical sound.</summary>
        static float Noise(ref uint state)
        {
            // xorshift: fast, no allocation, and repeatable from a seed.
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state / (float)uint.MaxValue) * 2f - 1f;
        }

        static AudioClip Build(string name, int samples, System.Func<int, float> generator,
                               bool loop = false)
        {
            var data = new float[samples];
            for (int i = 0; i < samples; i++) data[i] = Mathf.Clamp(generator(i), -1f, 1f);

            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Short attack/decay envelope, 0..1 over the clip.</summary>
        static float Envelope(float t, float attack, float decay)
        {
            if (t < attack) return t / Mathf.Max(0.0001f, attack);
            return Mathf.Exp(-(t - attack) / Mathf.Max(0.0001f, decay));
        }

        // -------------------------------------------------------------------- sfx

        /// <summary>Pistol shot: noise burst with a fast crack and a short tail.</summary>
        public static AudioClip Gunshot()
        {
            const float length = 0.28f;
            int samples = (int)(SampleRate * length);
            uint seed = 0x9E3779B9;

            return Build("sfx_gunshot", samples, i =>
            {
                float t = i / (float)SampleRate;
                float env = Envelope(t, 0.001f, 0.055f);

                // Noise for the crack, plus a low thump for body.
                float crack = Noise(ref seed) * env;
                float thump = Mathf.Sin(2f * Mathf.PI * 90f * t) * Envelope(t, 0.002f, 0.09f) * 0.6f;

                return (crack * 0.8f + thump) * 0.7f;
            });
        }

        /// <summary>Melee impact: duller and lower than a gunshot.</summary>
        public static AudioClip Punch()
        {
            const float length = 0.2f;
            int samples = (int)(SampleRate * length);
            uint seed = 0x1234567;

            return Build("sfx_punch", samples, i =>
            {
                float t = i / (float)SampleRate;
                float env = Envelope(t, 0.004f, 0.05f);
                float body = Mathf.Sin(2f * Mathf.PI * 140f * t) * env;
                return (body * 0.7f + Noise(ref seed) * env * 0.35f) * 0.6f;
            });
        }

        /// <summary>Car horn: two detuned square waves, the classic minor-third parp.</summary>
        public static AudioClip Horn()
        {
            const float length = 0.55f;
            int samples = (int)(SampleRate * length);

            return Build("sfx_horn", samples, i =>
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Min(1f, t / 0.02f) * Mathf.Min(1f, (length - t) / 0.06f);

                float a = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 370f * t));
                float b = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 440f * t));

                return (a + b) * 0.18f * env;
            });
        }

        /// <summary>Collision crunch: broadband noise with a fast decay.</summary>
        public static AudioClip Crash()
        {
            const float length = 0.45f;
            int samples = (int)(SampleRate * length);
            uint seed = 0xC0FFEE;

            return Build("sfx_crash", samples, i =>
            {
                float t = i / (float)SampleRate;
                float env = Envelope(t, 0.003f, 0.12f);
                float metal = Mathf.Sin(2f * Mathf.PI * 220f * t + Mathf.Sin(t * 90f) * 4f) * 0.3f;
                return (Noise(ref seed) * 0.7f + metal) * env * 0.75f;
            });
        }

        /// <summary>UI click: a very short blip.</summary>
        public static AudioClip UiClick()
        {
            const float length = 0.06f;
            int samples = (int)(SampleRate * length);

            return Build("sfx_click", samples, i =>
            {
                float t = i / (float)SampleRate;
                return Mathf.Sin(2f * Mathf.PI * 880f * t) * Envelope(t, 0.001f, 0.012f) * 0.35f;
            });
        }

        /// <summary>Reward chime: a rising major arpeggio.</summary>
        public static AudioClip Chime()
        {
            const float length = 0.7f;
            int samples = (int)(SampleRate * length);
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };

            return Build("sfx_chime", samples, i =>
            {
                float t = i / (float)SampleRate;
                int step = Mathf.Clamp((int)(t / 0.13f), 0, notes.Length - 1);
                float local = t - step * 0.13f;

                return Mathf.Sin(2f * Mathf.PI * notes[step] * t)
                       * Envelope(local, 0.005f, 0.09f) * 0.3f;
            });
        }

        /// <summary>Footstep: a soft filtered thud.</summary>
        public static AudioClip Footstep()
        {
            const float length = 0.12f;
            int samples = (int)(SampleRate * length);
            uint seed = 0xABCDEF;

            return Build("sfx_step", samples, i =>
            {
                float t = i / (float)SampleRate;
                return Noise(ref seed) * Envelope(t, 0.002f, 0.028f) * 0.28f;
            });
        }

        // ------------------------------------------------------------------ loops

        /// <summary>
        /// Engine loop. Pitch is shifted at playback by the vehicle's speed, so this is a
        /// single cycle-accurate idle tone that loops seamlessly.
        /// </summary>
        public static AudioClip EngineLoop()
        {
            const float frequency = 55f;
            // Exactly a whole number of cycles, or the loop point clicks.
            const int cycles = 22;
            int samples = Mathf.RoundToInt(SampleRate * cycles / frequency);
            uint seed = 0x5EED;

            return Build("loop_engine", samples, i =>
            {
                float t = i / (float)SampleRate;

                // Sawtooth fundamental plus two harmonics reads as "engine" better than a sine.
                float phase = (t * frequency) % 1f;
                float saw = phase * 2f - 1f;
                float h2 = Mathf.Sin(2f * Mathf.PI * frequency * 2f * t) * 0.3f;
                float h3 = Mathf.Sin(2f * Mathf.PI * frequency * 3f * t) * 0.15f;

                return (saw * 0.5f + h2 + h3 + Noise(ref seed) * 0.05f) * 0.35f;
            }, loop: true);
        }

        /// <summary>Wind/city ambience: slow-moving filtered noise.</summary>
        public static AudioClip Ambience()
        {
            const float length = 8f;
            int samples = (int)(SampleRate * length);
            uint seed = 0xFEEDFACE;

            float smoothed = 0f;

            return Build("loop_ambience", samples, i =>
            {
                float t = i / (float)SampleRate;

                // One-pole lowpass turns white noise into wind.
                smoothed += (Noise(ref seed) - smoothed) * 0.02f;

                // Fade the seam at both ends so the loop does not click.
                float seam = Mathf.Min(1f, t / 0.5f) * Mathf.Min(1f, (length - t) / 0.5f);
                float swell = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * t / length);

                return smoothed * 6f * swell * seam * 0.5f;
            }, loop: true);
        }

        /// <summary>
        /// Music bed: a slow four-chord loop in A minor, built from soft triangle waves.
        /// Not a soundtrack, but enough to stop the city feeling silent.
        /// </summary>
        public static AudioClip MusicLoop()
        {
            const float barLength = 3.2f;
            const int bars = 4;
            float length = barLength * bars;
            int samples = (int)(SampleRate * length);

            // Am - F - C - G, the most walked-on progression there is, one bar each.
            float[][] chords =
            {
                new[] { 220.00f, 261.63f, 329.63f },
                new[] { 174.61f, 220.00f, 261.63f },
                new[] { 130.81f, 164.81f, 196.00f },
                new[] { 196.00f, 246.94f, 293.66f },
            };

            return Build("loop_music", samples, i =>
            {
                float t = i / (float)SampleRate;
                int bar = Mathf.Clamp((int)(t / barLength), 0, bars - 1);
                float local = t - bar * barLength;

                float value = 0f;
                foreach (float note in chords[bar])
                {
                    // Triangle wave: softer than a saw, less sterile than a sine.
                    float phase = (t * note) % 1f;
                    value += (Mathf.Abs(phase * 4f - 2f) - 1f) * 0.16f;
                }

                // Gentle swell in and out of each bar, and a seam fade at the loop point.
                float envelope = Mathf.Min(1f, local / 0.35f) * Mathf.Min(1f, (barLength - local) / 0.5f);
                float seam = Mathf.Min(1f, t / 0.6f) * Mathf.Min(1f, (length - t) / 0.6f);

                return value * envelope * seam * 0.5f;
            }, loop: true);
        }
    }
}
