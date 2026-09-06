using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Draws the rain. Decides nothing -- <see cref="DayNightCycle.Wetness"/> is the single
    /// source of truth for whether, and how hard, it is raining.
    ///
    /// Built as one particle system that rides above the camera rather than as weather volumes
    /// placed around the map. On a 1600 m island the player can only ever be under one patch of
    /// sky at a time, so simulating rain anywhere else is particles nobody sees. The emitter is
    /// a flat box overhead, wide enough to cover the near view and no wider.
    ///
    /// The particle system is created in code rather than authored as a prefab so the whole
    /// effect -- shape, rate, size, the material -- is versioned with this file and cannot
    /// drift out of sync with the numbers the fade logic assumes.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class WeatherSystem : MonoBehaviour
    {
        [Tooltip("Drives everything. Found automatically if left empty.")]
        public DayNightCycle Cycle;

        [Header("Emitter")]
        [Tooltip("Height above the camera the drops spawn at.")]
        public float CeilingHeight = 14f;
        [Tooltip("Width of the overhead emitter box, metres.")]
        public float CoverWidth = 34f;
        [Tooltip("Drops per second at full downpour.")]
        public float MaxRate = 900f;

        [Header("Audio")]
        public AudioSource Loop;
        public float MaxLoopVolume = 0.45f;

        ParticleSystem _rain;
        Transform _camera;

        void Awake()
        {
            _rain = GetComponent<ParticleSystem>();

            if (Cycle == null) Cycle = FindAnyObjectByType<DayNightCycle>();
        }

        void LateUpdate()
        {
            // LateUpdate: the camera has already moved this frame, so the ceiling lands
            // where the player is now rather than one frame behind them.
            if (_camera == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _camera = cam.transform;
            }

            transform.position = _camera.position + Vector3.up * CeilingHeight;

            float wet = Cycle != null ? Cycle.Wetness : 0f;

            // BUG-017. This used to write through an EmissionModule cached in Awake, which
            // threw "Do not create your own module instances" on every single frame -- 25
            // exceptions a second in the console, and a per-frame managed exception on
            // device. The module structs are handles holding a pointer back to the owning
            // system, not values: a cached one does not survive a domain reload, so the
            // field was still there after a script recompile but its owner was gone. They
            // are cheap to fetch, so fetch one at the point of use.
            var emission = _rain.emission;
            emission.rateOverTime = MaxRate * wet;

            // Stopped rather than left emitting at zero: an idle particle system still costs
            // a simulation step and a draw call every frame, and it is clear far more often
            // than it is raining.
            bool shouldPlay = wet > 0.001f;
            if (shouldPlay && !_rain.isPlaying) _rain.Play();
            else if (!shouldPlay && _rain.isPlaying) _rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            if (Loop != null)
            {
                Loop.volume = MaxLoopVolume * wet;
                if (shouldPlay && !Loop.isPlaying) Loop.Play();
                else if (!shouldPlay && Loop.isPlaying) Loop.Stop();
            }
        }

        /// <summary>
        /// Configures the attached particle system for rain.
        ///
        /// Called by the scene builder at edit time, and safe to call again -- it overwrites
        /// every module it touches, so re-running the build cannot leave a half-tuned emitter.
        /// </summary>
        public void ConfigureEmitter(Material dropMaterial)
        {
            var ps = GetComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(16f, 22f);
            // Thin. These are stretched along velocity by the renderer below, so the start
            // size is the drop's *width*: at 0.07 the near streaks read as white bars rather
            // than rain.
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.055f);
            main.startColor = new Color(0.90f, 0.94f, 1.00f, 0.85f);
            main.gravityModifier = 1.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1400;
            main.startRotation = 0f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(CoverWidth, 0.1f, CoverWidth);
            shape.rotation = Vector3.zero;

            // Straight down. Wind would need the drops re-angled to match, and slanted rain
            // that does not agree with the stretch direction reads as a bug.
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(-6f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            // Nothing else: collision, lights, sub-emitters and trails are all off by default
            // and every one of them is expensive on a phone. Rain that splashes is a desktop
            // feature; this has to run alongside the whole city.
            var collision = ps.collision;
            collision.enabled = false;
            var lights = ps.lights;
            lights.enabled = false;
            var trails = ps.trails;
            trails.enabled = false;
            var noise = ps.noise;
            noise.enabled = false;

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 3.6f;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (dropMaterial != null) renderer.sharedMaterial = dropMaterial;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
