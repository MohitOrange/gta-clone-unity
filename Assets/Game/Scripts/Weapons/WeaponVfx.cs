using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The visual half of a gunshot: muzzle flash, tracer, impact spark.
    ///
    /// One shared service rather than a set of effects hung off each weapon prefab, for the
    /// reason given in CLAUDE.md §6 -- the player, the police and every armed NPC fire the
    /// same way, so the effect belongs in one place and is corrected in one place.
    ///
    /// <b>Everything here is built procedurally in Awake.</b> No prefab, no texture, no
    /// material, no particle asset. That is deliberate: §6 says not to generate art assets,
    /// and a shot with no visible effect at all is worse than a plain one. See the asset
    /// list in PHASE14.md for what would upgrade this in a polish pass -- the call sites
    /// would not change.
    ///
    /// Everything is pooled. A gunfight is the one moment this project allocates hardest,
    /// and instantiating a LineRenderer per bullet on a phone is how a firefight turns into
    /// a GC pause.
    /// </summary>
    public class WeaponVfx : MonoBehaviour
    {
        public static WeaponVfx Instance { get; private set; }

        [Header("Tracer")]
        [Tooltip("How long the bullet streak stays on screen. Very short -- it reads as a "
                 + "flash along the path, not a laser beam.")]
        public float TracerSeconds = 0.045f;
        public float TracerWidth = 0.035f;
        public Color TracerColor = new Color(1f, 0.86f, 0.45f, 1f);
        [Tooltip("How many tracers can be on screen at once. Older ones are recycled.")]
        public int TracerPool = 12;

        [Header("Muzzle flash")]
        public float FlashSeconds = 0.05f;
        public float FlashIntensity = 4.5f;
        public float FlashRange = 6f;
        public Color FlashColor = new Color(1f, 0.82f, 0.5f, 1f);
        [Tooltip("Kept small on purpose. Each live flash is an extra realtime light, and URP "
                 + "on mobile has a hard per-object additional-light limit -- past it lights "
                 + "are silently dropped, so an unbounded pool would make flashes vanish at "
                 + "random rather than fail loudly.")]
        public int FlashPool = 4;

        [Header("Impact")]
        public float ImpactSeconds = 0.35f;
        public int ImpactPool = 8;

        LineRenderer[] _tracers;
        float[] _tracerUntil;
        int _tracerNext;

        Light[] _flashes;
        float[] _flashUntil;
        int _flashNext;

        ParticleSystem[] _impacts;
        int _impactNext;

        Material _tracerMaterial;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            BuildTracerMaterial();
            BuildTracers();
            BuildFlashes();
            BuildImpacts();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_tracerMaterial != null) Destroy(_tracerMaterial);
        }

        /// <summary>
        /// Ensures the service exists. Called by whoever fires first, so the scene does not
        /// have to remember to carry the object.
        /// </summary>
        public static WeaponVfx Ensure()
        {
            if (Instance != null) return Instance;

            var existing = FindAnyObjectByType<WeaponVfx>();
            if (existing != null) { Instance = existing; return Instance; }

            var go = new GameObject("WeaponVfx");
            return go.AddComponent<WeaponVfx>();
        }

        /// <summary>
        /// Draw one shot.
        /// </summary>
        /// <param name="muzzle">Where the barrel ends.</param>
        /// <param name="end">Where the bullet stopped -- the hit point, or the end of its range.</param>
        /// <param name="hit">False for a shot that hit nothing, which gets no impact spark.</param>
        /// <param name="normal">Surface normal at the hit, so the spark sprays outward.</param>
        public void Shot(Vector3 muzzle, Vector3 end, bool hit, Vector3 normal)
        {
            Tracer(muzzle, end);
            Flash(muzzle);
            if (hit) Impact(end, normal);
        }

        // ------------------------------------------------------------------ tracer

        void Tracer(Vector3 from, Vector3 to)
        {
            if (_tracers == null || _tracers.Length == 0) return;

            var line = _tracers[_tracerNext];
            _tracerUntil[_tracerNext] = Time.time + TracerSeconds;
            _tracerNext = (_tracerNext + 1) % _tracers.Length;

            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.enabled = true;
        }

        // ------------------------------------------------------------------ flash

        void Flash(Vector3 at)
        {
            if (_flashes == null || _flashes.Length == 0) return;

            var light = _flashes[_flashNext];
            _flashUntil[_flashNext] = Time.time + FlashSeconds;
            _flashNext = (_flashNext + 1) % _flashes.Length;

            light.transform.position = at;
            light.enabled = true;
        }

        // ------------------------------------------------------------------ impact

        void Impact(Vector3 at, Vector3 normal)
        {
            if (_impacts == null || _impacts.Length == 0) return;

            var ps = _impacts[_impactNext];
            _impactNext = (_impactNext + 1) % _impacts.Length;

            ps.transform.position = at;
            ps.transform.rotation = normal.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;

            ps.Play(true);
        }

        // ------------------------------------------------------------------ lifetime

        void Update()
        {
            float now = Time.time;

            for (int i = 0; i < _tracers.Length; i++)
                if (_tracers[i].enabled && now >= _tracerUntil[i]) _tracers[i].enabled = false;

            for (int i = 0; i < _flashes.Length; i++)
                if (_flashes[i].enabled && now >= _flashUntil[i]) _flashes[i].enabled = false;
        }

        // ------------------------------------------------------------------ construction

        void BuildTracerMaterial()
        {
            // Sprites/Default rather than a URP Unlit: it is vertex-coloured and additive-ish
            // without needing a texture or a keyword set, and it is present in every project
            // regardless of pipeline. Nothing here should depend on an imported asset.
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            _tracerMaterial = new Material(shader) { name = "TracerMaterial (runtime)" };
            _tracerMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        void BuildTracers()
        {
            int n = Mathf.Max(1, TracerPool);
            _tracers = new LineRenderer[n];
            _tracerUntil = new float[n];

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(TracerColor, 0f), new GradientColorKey(TracerColor, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });

            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Tracer" + i);
                go.transform.SetParent(transform, false);

                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.widthMultiplier = TracerWidth;
                line.numCapVertices = 0;
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                line.sharedMaterial = _tracerMaterial;
                line.colorGradient = gradient;
                line.enabled = false;

                _tracers[i] = line;
            }
        }

        void BuildFlashes()
        {
            int n = Mathf.Max(1, FlashPool);
            _flashes = new Light[n];
            _flashUntil = new float[n];

            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("MuzzleFlash" + i);
                go.transform.SetParent(transform, false);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = FlashColor;
                light.intensity = FlashIntensity;
                light.range = FlashRange;
                // Realtime shadows on a light that lives for 50ms are pure cost.
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.enabled = false;

                _flashes[i] = light;
            }
        }

        void BuildImpacts()
        {
            int n = Mathf.Max(1, ImpactPool);
            _impacts = new ParticleSystem[n];

            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Impact" + i);
                go.transform.SetParent(transform, false);

                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                // Module structs are handles, not values -- fetch, write, discard. Caching one
                // in a field is BUG-017.
                var main = ps.main;
                main.duration = ImpactSeconds;
                main.loop = false;
                main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 0.85f, 0.45f), new Color(1f, 0.6f, 0.2f));
                main.gravityModifier = 1.1f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 12;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 10) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 25f;
                shape.radius = 0.01f;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.06f;
                renderer.sharedMaterial = _tracerMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                _impacts[i] = ps;
            }
        }
    }
}
