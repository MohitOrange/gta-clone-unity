using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Rotates the sun and grades ambient/fog to match, on a compressed day length.
    ///
    /// Mobile-conscious: this drives light colour, intensity and ambient only. It does not
    /// touch shadow distance or resolution per-frame, and it disables the light entirely at
    /// deep night so the renderer can skip the shadow pass.
    /// </summary>
    [ExecuteAlways]
    public class DayNightCycle : MonoBehaviour
    {
        [Header("Time")]
        [Tooltip("Real seconds for one full in-game day, dawn to dawn. Still the whole cycle "
                 + "however NightSpeedMultiplier is set -- the multiplier changes how that "
                 + "budget is split between light and dark, not how long the day is.")]
        public float DayLengthSeconds = 300f;
        [Tooltip("0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.")]
        [Range(0f, 1f)] public float TimeOfDay = 0.32f;
        public bool Advance = true;

        [Tooltip("How much faster the clock runs while the sun is down. 1 = the old uniform "
                 + "rate. Higher shortens the night and lengthens the day by the same amount.")]
        [Range(1f, 6f)] public float NightSpeedMultiplier = 2.6f;

        [Header("Lights")]
        public Light Sun;
        public Light Moon;

        [Header("Sun")]
        [Tooltip("Compass direction the sun travels along, in degrees.")]
        public float SunHeading = 30f;
        public float SunIntensity = 1.7f;
        public Gradient SunColor;

        [Header("Ambient / Fog")]
        public Gradient AmbientColor;
        public Gradient FogColor;
        public bool ControlFog = true;
        public float FogStartDistance = 260f;
        public float FogEndDistance = 950f;

        const float MoonIntensity = 0.12f;

        [Header("Weather")]
        [Tooltip("Whether it may rain at all. Off leaves the sky permanently clear.")]
        public bool EnableRain = true;

        [Tooltip("Odds that any given day turns wet, rolled once as the day ticks over.")]
        [Range(0f, 1f)] public float RainChancePerDay = 0.30f;

        [Tooltip("How much of a day a shower lasts, as a fraction of the cycle.")]
        public Vector2 RainDurationDays = new Vector2(0.08f, 0.20f);

        [Tooltip("Seconds to fade the sky over at the start and end of a shower.")]
        public float RainFadeSeconds = 7f;

        [Tooltip("Fraction of direct sunlight left at the height of a shower.")]
        [Range(0f, 1f)] public float RainSunDamping = 0.45f;

        [Tooltip("How far in the fog closes when it rains, metres off the far distance.")]
        public float RainFogPullIn = 380f;

        /// <summary>Current hour in 0..24, for HUD clocks and later mission scheduling.</summary>
        public float Hour => TimeOfDay * 24f;

        /// <summary>
        /// When the sun is up, as fractions of the 24-hour clock.
        ///
        /// These are not arbitrary: they are the points either side of which the sun light is
        /// below the horizon, so they decide when the street lamps come on. Moving them would
        /// leave the sun set during what the game calls day. The night is shortened by running
        /// the clock faster through it instead -- see <see cref="NightSpeedMultiplier"/>.
        /// </summary>
        public const float NightEndsAt = 0.24f;
        public const float NightBeginsAt = 0.78f;

        public bool IsNight => TimeOfDay < NightEndsAt || TimeOfDay > NightBeginsAt;

        /// <summary>Share of the 24-hour clock that is dark. 0.46 at the default thresholds.</summary>
        public static float NightFraction => NightEndsAt + (1f - NightBeginsAt);

        /// <summary>Share of the 24-hour clock that is lit. 0.54 at the default thresholds.</summary>
        public static float DayFraction => 1f - NightFraction;

        /// <summary>Real seconds of daylight per cycle, at the current settings.</summary>
        public float DaylightSeconds => DayLengthSeconds <= 0f ? 0f : DayFraction / DayRate;

        /// <summary>Real seconds of darkness per cycle, at the current settings.</summary>
        public float NightSeconds =>
            DayLengthSeconds <= 0f ? 0f
            : NightFraction / (DayRate * Mathf.Max(1f, NightSpeedMultiplier));

        /// <summary>
        /// Clock rate during daylight.
        ///
        /// Solved so that one whole cycle still takes <see cref="DayLengthSeconds"/> whatever
        /// the multiplier is: total = DayFraction/r + NightFraction/(m*r), so
        /// r = (DayFraction + NightFraction/m) / DayLengthSeconds. Without this the multiplier
        /// would simply make the whole day shorter rather than trading night for day.
        /// </summary>
        float DayRate
        {
            get
            {
                float m = Mathf.Max(1f, NightSpeedMultiplier);
                return (DayFraction + NightFraction / m) / Mathf.Max(0.01f, DayLengthSeconds);
            }
        }

        // ------------------------------------------------------------------ weather
        //
        // Weather lives here rather than in its own always-on system because the day clock is
        // what schedules it and the lighting is what it changes -- both of which this class
        // already owns. WeatherSystem reads Wetness and draws the rain; it makes no decisions.
        // See DECISIONS.md D29.

        /// <summary>
        /// 0 clear, 1 raining hard. Ramps rather than switching so the sky closes over and
        /// clears up instead of snapping between two states.
        /// </summary>
        public float Wetness { get; private set; }

        /// <summary>True while a shower is scheduled, including its fade in and out.</summary>
        public bool IsRaining => _rainUntil > 0f && TimeOfDay <= _rainUntil && TimeOfDay >= _rainFrom;

        float _rainFrom = -1f;
        float _rainUntil = -1f;
        float _lastTimeOfDay = -1f;
        bool _rolledToday;

        /// <summary>
        /// Starts a shower now, lasting <paramref name="days"/> of the cycle. Used by the
        /// weather test hook; the game itself lets <see cref="RollWeather"/> decide.
        /// </summary>
        public void ForceRain(float days = 0.15f)
        {
            _rainFrom = TimeOfDay;
            _rainUntil = TimeOfDay + Mathf.Max(0.001f, days);
            _rolledToday = true;
        }

        /// <summary>Stops any shower immediately.</summary>
        public void ClearWeather()
        {
            _rainFrom = _rainUntil = -1f;
            Wetness = 0f;
        }

        /// <summary>
        /// Decides whether today is wet. Rolled once per day, as the clock wraps.
        ///
        /// A per-frame chance would make rain start and stop constantly; a per-day roll gives
        /// a day that is wet or is not, which is what a player actually notices.
        /// </summary>
        void RollWeather()
        {
            _rolledToday = true;
            if (!EnableRain || Random.value > RainChancePerDay) return;

            // Somewhere in the day, but not so late that the shower is cut off by the wrap.
            float span = Random.Range(RainDurationDays.x, RainDurationDays.y);
            _rainFrom = Random.Range(0.05f, Mathf.Max(0.06f, 0.95f - span));
            _rainUntil = _rainFrom + span;
        }

        /// <summary>Advances the wetness ramp toward whatever the schedule says it should be.</summary>
        void UpdateWeather(float dt)
        {
            // A new day: clear yesterday's shower and roll again.
            if (_lastTimeOfDay >= 0f && TimeOfDay < _lastTimeOfDay)
            {
                _rainFrom = _rainUntil = -1f;
                _rolledToday = false;
            }
            _lastTimeOfDay = TimeOfDay;

            if (!_rolledToday) RollWeather();

            float target = (EnableRain && IsRaining) ? 1f : 0f;
            float step = RainFadeSeconds > 0.01f ? dt / RainFadeSeconds : 1f;
            Wetness = Mathf.MoveTowards(Wetness, target, step);
        }

        void Reset() => BuildDefaultGradients();

        void OnEnable()
        {
            if (SunColor == null || SunColor.colorKeys.Length == 0) BuildDefaultGradients();
            Apply();
        }

        void Update()
        {
            if (Advance && Application.isPlaying && DayLengthSeconds > 0.01f)
            {
                // Faster through the dark hours, so the player spends more of the session in
                // daylight without the night ever being skipped.
                float rate = DayRate * (IsNight ? Mathf.Max(1f, NightSpeedMultiplier) : 1f);
                TimeOfDay += Time.deltaTime * rate;
                TimeOfDay -= Mathf.Floor(TimeOfDay);
            }

            if (Application.isPlaying) UpdateWeather(Time.deltaTime);
            Apply();
        }

        void OnValidate() => Apply();

        /// <summary>
        /// Re-apply lighting immediately. Needed when Sun/Moon are wired up from script after
        /// AddComponent, because OnEnable has already run by then with null references.
        /// </summary>
        public void Refresh() => Apply();

        void Apply()
        {
            // -90 deg at TimeOfDay 0 puts the sun straight down at midnight and straight up at noon.
            float elevation = TimeOfDay * 360f - 90f;

            if (Sun != null)
            {
                Sun.transform.rotation = Quaternion.Euler(elevation, SunHeading, 0f);

                // Dot against world up: positive only while the sun is above the horizon.
                float above = Mathf.Clamp01(Vector3.Dot(-Sun.transform.forward, Vector3.up));

                // Daylight does not scale linearly with sun elevation -- it saturates once the
                // sun clears the horizon haze. Ramping to full by ~20 degrees keeps mid-morning
                // looking like mid-morning instead of permanent dusk.
                float daylight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(above / 0.34f));

                Sun.color = SunColor.Evaluate(TimeOfDay);

                // Rain takes the edge off the direct sun rather than switching it off. A
                // shower that killed the key light entirely flattened the whole city into
                // ambient and read as dusk, not as weather.
                float damp = Mathf.Lerp(1f, RainSunDamping, Wetness);
                Sun.intensity = SunIntensity * daylight * damp;
                Sun.enabled = Sun.intensity > 0.001f;
            }

            if (Moon != null)
            {
                Moon.transform.rotation = Quaternion.Euler(elevation + 180f, SunHeading, 0f);
                float above = Mathf.Clamp01(Vector3.Dot(-Moon.transform.forward, Vector3.up));
                Moon.intensity = MoonIntensity * above;
                Moon.enabled = above > 0.001f;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            Color ambient = AmbientColor.Evaluate(TimeOfDay);
            if (Wetness > 0.001f)
            {
                // Overcast light is flatter, cooler and slightly dimmer than the same hour in
                // sunshine. Desaturating toward the fog colour rather than toward grey keeps
                // the sky and the ground agreeing with each other.
                Color overcast = FogColor.Evaluate(TimeOfDay) * 0.85f;
                ambient = Color.Lerp(ambient, overcast, Wetness * 0.65f);
            }
            RenderSettings.ambientLight = ambient;

            if (ControlFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;

                Color fog = FogColor.Evaluate(TimeOfDay);
                if (Wetness > 0.001f)
                    fog = Color.Lerp(fog, new Color(0.55f, 0.57f, 0.60f), Wetness * 0.55f);
                RenderSettings.fogColor = fog;

                // Held back to the far half of the view distance: fog here is for aerial depth
                // on the mountains, not for hiding draw distance, and starting it close washes
                // the whole city out. Rain closes it in, which is the single strongest cue
                // that the weather has changed -- more so than the particles themselves.
                RenderSettings.fogStartDistance = Mathf.Lerp(FogStartDistance, FogStartDistance * 0.45f, Wetness);
                RenderSettings.fogEndDistance = FogEndDistance - RainFogPullIn * Wetness;
            }
        }

        void BuildDefaultGradients()
        {
            SunColor = Gradient3(
                (0.22f, new Color(0.95f, 0.45f, 0.25f)),   // sunrise
                (0.50f, new Color(1.00f, 0.96f, 0.88f)),   // noon
                (0.80f, new Color(0.99f, 0.52f, 0.28f)));  // sunset

            AmbientColor = Gradient3(
                (0.00f, new Color(0.07f, 0.09f, 0.17f)),   // night
                (0.50f, new Color(0.66f, 0.70f, 0.78f)),   // day: sky bounce is the main fill
                (1.00f, new Color(0.07f, 0.09f, 0.17f)));

            FogColor = Gradient3(
                (0.00f, new Color(0.06f, 0.08f, 0.14f)),
                (0.50f, new Color(0.70f, 0.79f, 0.90f)),
                (1.00f, new Color(0.06f, 0.08f, 0.14f)));
        }

        static Gradient Gradient3(params (float t, Color c)[] stops)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[stops.Length];
            for (int i = 0; i < stops.Length; i++) ck[i] = new GradientColorKey(stops[i].c, stops[i].t);
            g.SetKeys(ck, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
