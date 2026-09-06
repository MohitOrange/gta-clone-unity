using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MiniGTA
{
    /// <summary>
    /// Turns a quality tier into the actual numbers that decide the frame time.
    ///
    /// One class, because these settings are not independent: dropping the shadow distance
    /// while leaving the cascade count at four just wastes the cascades, and reducing draw
    /// distance without also relaxing the terrain's pixel error moves the cost rather than
    /// removing it. Keeping them together means a tier is one readable block of numbers.
    /// </summary>
    [DefaultExecutionOrder(-140)]
    public class PerformanceTuner : MonoBehaviour
    {
        /// <summary>The layer purely-decorative geometry lives on, so it can be culled early.</summary>
        public const string DetailLayer = "Detail";

        static PerformanceTuner _instance;

        /// <summary>
        /// Whether the missing-pipeline error has already been reported this session. Statics
        /// outlive a Play session when domain reloading is off, so this is reset on load --
        /// otherwise the warning fires once ever and is missed by every run after the first.
        /// </summary>
        static bool _pipelineErrorLogged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => _pipelineErrorLogged = false;

        public static PerformanceTuner Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<PerformanceTuner>();
                return _instance;
            }
            private set => _instance = value;
        }

        [System.Serializable]
        public class TierSettings
        {
            public float ShadowDistance = 70f;
            public int ShadowCascades = 1;
            public float RenderScale = 1f;
            public int MsaaSamples = 1;

            [Tooltip("Camera far clip.")]
            public float DrawDistance = 700f;
            [Tooltip("Distance at which decorative geometry stops drawing.")]
            public float DetailDistance = 160f;

            public float LodBias = 1f;
            [Tooltip("Terrain error tolerance in pixels. Higher is cheaper and coarser.")]
            public float TerrainPixelError = 8f;
            public float TerrainBasemapDistance = 400f;

            [Tooltip("Physics ticks per second.")]
            public float PhysicsHz = 50f;
            public int TargetFrameRate = 60;
        }

        [Header("Low")]
        public TierSettings Low = new TierSettings
        {
            ShadowDistance = 38f, ShadowCascades = 1, RenderScale = 0.75f, MsaaSamples = 1,
            DrawDistance = 420f, DetailDistance = 85f,
            LodBias = 0.55f, TerrainPixelError = 24f, TerrainBasemapDistance = 180f,
            PhysicsHz = 30f, TargetFrameRate = 30,
        };

        [Header("Medium")]
        public TierSettings Medium = new TierSettings
        {
            ShadowDistance = 65f, ShadowCascades = 1, RenderScale = 0.9f, MsaaSamples = 1,
            DrawDistance = 700f, DetailDistance = 170f,
            LodBias = 1f, TerrainPixelError = 10f, TerrainBasemapDistance = 400f,
            PhysicsHz = 50f, TargetFrameRate = 60,
        };

        [Header("High")]
        public TierSettings High = new TierSettings
        {
            ShadowDistance = 120f, ShadowCascades = 2, RenderScale = 1f, MsaaSamples = 2,
            DrawDistance = 900f, DetailDistance = 320f,
            LodBias = 1.5f, TerrainPixelError = 5f, TerrainBasemapDistance = 800f,
            PhysicsHz = 50f, TargetFrameRate = 60,
        };

        [Header("Safety")]
        [Tooltip("Longest single frame physics is allowed to catch up on. Without a cap a hitch "
                 + "makes physics run more steps, which causes a bigger hitch, and so on.")]
        public float MaxPhysicsCatchUp = 0.1f;

        Camera _camera;
        Terrain _terrain;
        int _detailLayer = -1;

        QualityTier _applied = (QualityTier)(-1);
        public QualityTier Applied => _applied;
        public TierSettings Current => For(_applied);

        // The URP asset is a project asset, not a scene object. Anything written into it during
        // Play mode in the editor sticks after exiting, so the originals are put back on the
        // way out.
        float _assetShadowDistance;
        int _assetCascades;
        float _assetRenderScale;
        int _assetMsaa;
        bool _assetCaptured;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            RestorePipelineAsset();
            if (_instance == this) _instance = null;
        }

        void Start()
        {
            _detailLayer = LayerMask.NameToLayer(DetailLayer);

            // Nothing has applied a tier yet if there are no settings in the scene.
            if (_applied == (QualityTier)(-1))
                Apply(GameSettings.Instance != null ? GameSettings.Instance.Tier : QualityTier.Medium);
        }

        public TierSettings For(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.Low: return Low;
                case QualityTier.High: return High;
                default: return Medium;
            }
        }

        public void Apply(QualityTier tier)
        {
            var s = For(tier);
            _applied = tier;

            ApplyPipeline(s);
            ApplyCamera(s);
            ApplyTerrain(s);
            ApplyPhysics(s);

            QualitySettings.lodBias = s.LodBias;
            QualitySettings.vSyncCount = 0;      // targetFrameRate is ignored while vsync is on
            Application.targetFrameRate = s.TargetFrameRate;

            // Skinned meshes are the most expensive thing in a crowd scene; on low tier accept
            // visible seams at the joints to get the CPU cost back.
            QualitySettings.skinWeights = tier == QualityTier.Low ? SkinWeights.TwoBones
                                                                  : SkinWeights.FourBones;
        }

        // ------------------------------------------------------------------ parts

        /// <summary>
        /// The active URP asset, or null with a loud complaint.
        ///
        /// <b>BUG-018.</b> Both call sites used to resolve this inline and <c>return</c> on
        /// null. That is what made the bug invisible for an entire phase: the project had no
        /// render pipeline assigned at all, so this resolved to null, so every tier setting --
        /// shadow distance, cascades, render scale, MSAA -- silently did nothing while the
        /// tier system reported that it had applied them. The PHASE7 tier table was measured
        /// against settings that were never applied, and none of those numbers were real.
        ///
        /// A missing pipeline is not a condition to shrug at. It means every URP material in
        /// the project renders magenta, so it is always a bug and never a configuration
        /// choice. It is logged once per session rather than per tier change, because the
        /// tuner re-applies on every quality change and a per-call error would bury the
        /// console it is trying to warn through.
        /// </summary>
        static UniversalRenderPipelineAsset ResolvePipeline()
        {
            var urp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset
                      ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) return urp;

            if (_pipelineErrorLogged) return null;
            _pipelineErrorLogged = true;

            var anyPipeline = QualitySettings.renderPipeline
                              ?? GraphicsSettings.defaultRenderPipeline;

            if (anyPipeline == null)
            {
                Debug.LogError(
                    "CRITICAL [PerformanceTuner] No render pipeline is assigned. "
                    + "GraphicsSettings.defaultRenderPipeline and QualitySettings.renderPipeline "
                    + "are both null, so the project is running the Built-in pipeline while its "
                    + "materials are URP -- the world will render magenta, and every quality "
                    + "tier setting is a silent no-op. Assign Assets/Settings/Mobile_RPAsset in "
                    + "Project Settings > Graphics. This is BUG-018; see PHASE14.md.");
            }
            else
            {
                Debug.LogError(
                    "CRITICAL [PerformanceTuner] The assigned render pipeline is '"
                    + anyPipeline.GetType().Name + "', not a UniversalRenderPipelineAsset. "
                    + "Every quality tier setting is a silent no-op. See PHASE14.md, BUG-018.");
            }

            return null;
        }

        void ApplyPipeline(TierSettings s)
        {
            var urp = ResolvePipeline();
            if (urp == null) return;

            if (!_assetCaptured)
            {
                _assetShadowDistance = urp.shadowDistance;
                _assetCascades = urp.shadowCascadeCount;
                _assetRenderScale = urp.renderScale;
                _assetMsaa = urp.msaaSampleCount;
                _assetCaptured = true;
            }

            urp.shadowDistance = s.ShadowDistance;
            urp.shadowCascadeCount = s.ShadowCascades;
            urp.renderScale = s.RenderScale;
            urp.msaaSampleCount = s.MsaaSamples;
        }

        void RestorePipelineAsset()
        {
#if UNITY_EDITOR
            if (!_assetCaptured) return;

            var urp = ResolvePipeline();
            if (urp == null) return;

            urp.shadowDistance = _assetShadowDistance;
            urp.shadowCascadeCount = _assetCascades;
            urp.renderScale = _assetRenderScale;
            urp.msaaSampleCount = _assetMsaa;
#endif
        }

        void ApplyCamera(TierSettings s)
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            _camera.farClipPlane = s.DrawDistance;

            if (_detailLayer < 0) _detailLayer = LayerMask.NameToLayer(DetailLayer);
            if (_detailLayer < 0) return;

            // Per-layer far planes. Decoration -- rooflines, kerb paint, park furniture --
            // stops drawing well before the buildings do, which is invisible at speed and is
            // several hundred renderers on a phone.
            var distances = _camera.layerCullDistances;
            distances[_detailLayer] = s.DetailDistance;
            _camera.layerCullDistances = distances;

            // layerCullSpherical is deliberately not set. It would be the nicer boundary --
            // props would not swing in and out as the camera turned -- but URP only honours the
            // planar form, and setting it logs a warning on every tier change to say so.
        }

        void ApplyTerrain(TierSettings s)
        {
            if (_terrain == null) _terrain = Terrain.activeTerrain;
            if (_terrain == null) return;

            _terrain.heightmapPixelError = s.TerrainPixelError;
            _terrain.basemapDistance = s.TerrainBasemapDistance;
            _terrain.drawTreesAndFoliage = _applied != QualityTier.Low;
            _terrain.shadowCastingMode = _applied == QualityTier.Low
                ? ShadowCastingMode.Off
                : ShadowCastingMode.On;
        }

        void ApplyPhysics(TierSettings s)
        {
            Time.fixedDeltaTime = 1f / Mathf.Max(15f, s.PhysicsHz);

            // The cap that actually protects the frame rate: a 300 ms stall would otherwise
            // queue 15 physics steps, each of which makes the next frame later still.
            Time.maximumDeltaTime = MaxPhysicsCatchUp;
            Time.maximumParticleDeltaTime = MaxPhysicsCatchUp;

            Physics.defaultSolverIterations = _applied == QualityTier.Low ? 4 : 6;
            Physics.defaultSolverVelocityIterations = 1;
        }

        // --------------------------------------------------------------- reporting

        /// <summary>Everything a diagnostic readout needs, without exposing the internals.</summary>
        public string Describe()
        {
            var s = Current;
            return _applied
                   + "  shadow=" + s.ShadowDistance + "m"
                   + "  draw=" + s.DrawDistance + "m"
                   + "  detail=" + s.DetailDistance + "m"
                   + "  scale=" + s.RenderScale
                   + "  physics=" + Mathf.RoundToInt(1f / Time.fixedDeltaTime) + "Hz"
                   + "  fps cap=" + Application.targetFrameRate;
        }
    }
}
