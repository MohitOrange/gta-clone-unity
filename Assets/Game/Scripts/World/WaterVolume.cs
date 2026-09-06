using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The ocean surface. Owns the authoritative water height that swimming, buoyancy and
    /// (later) boats all test against.
    ///
    /// The mesh is rendered by a shader that displaces vertices into waves; this component
    /// mirrors that same wave maths on the CPU so gameplay agrees with what the player
    /// sees. Both sides must use identical constants -- they are pushed to the material on
    /// Start so they can never drift apart.
    /// </summary>
    [ExecuteAlways]
    public class WaterVolume : MonoBehaviour
    {
        public static WaterVolume Instance { get; private set; }

        [Header("Surface")]
        [Tooltip("Still-water height in world units. Terrain below this is underwater.")]
        public float SeaLevel = 8f;

        [Header("Waves (must match OceanWater.shader)")]
        public float WaveAmplitude = 0.35f;
        public float WaveLength = 14f;
        public float WaveSpeed = 0.6f;

        Renderer _renderer;
        MaterialPropertyBlock _mpb;

        /// <summary>Still sea level, ignoring wave displacement. Use for coarse tests.</summary>
        public static float Level => Instance != null ? Instance.SeaLevel : 0f;

        void OnEnable()
        {
            Instance = this;
            _renderer = GetComponent<Renderer>();
            PushToMaterial();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void OnValidate()
        {
            transform.position = new Vector3(transform.position.x, SeaLevel, transform.position.z);
            PushToMaterial();
        }

        void PushToMaterial()
        {
            if (_renderer == null) return;
            _mpb ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat("_WaveAmplitude", WaveAmplitude);
            _mpb.SetFloat("_WaveLength", WaveLength);
            _mpb.SetFloat("_WaveSpeed", WaveSpeed);
            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>
        /// Wave-displaced surface height at a world position. Mirrors the vertex shader so
        /// a swimmer bobs with the same crests the player can see.
        /// </summary>
        public float SurfaceHeightAt(Vector3 worldPos)
        {
            float t = Application.isPlaying ? Time.time : 0f;
            float k = 2f * Mathf.PI / Mathf.Max(0.001f, WaveLength);
            float w1 = Mathf.Sin((worldPos.x + worldPos.z) * k + t * WaveSpeed);
            float w2 = Mathf.Sin((worldPos.x * 0.7f - worldPos.z * 1.3f) * k * 0.6f + t * WaveSpeed * 1.4f);
            return SeaLevel + (w1 + w2 * 0.5f) * WaveAmplitude;
        }

        /// <summary>True if the given world point is beneath the wavy surface.</summary>
        public bool IsSubmerged(Vector3 worldPos) => worldPos.y < SurfaceHeightAt(worldPos);
    }
}
