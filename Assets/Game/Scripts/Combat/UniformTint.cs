using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Recolours a character toward a uniform colour at runtime.
    ///
    /// Uses a MaterialPropertyBlock so every officer keeps sharing the source Mixamo materials
    /// and still batches; assigning tinted material copies instead would create one material
    /// instance per officer and break batching exactly when the most of them are on screen.
    /// </summary>
    public class UniformTint : MonoBehaviour
    {
        public Color Tint = new Color(0.24f, 0.30f, 0.46f);
        [Range(0f, 1f)] public float Strength = 0.65f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Start() => Apply();

        public void Apply()
        {
            var mpb = new MaterialPropertyBlock();

            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial == null) continue;

                Color source = r.sharedMaterial.HasProperty(BaseColorId)
                    ? r.sharedMaterial.GetColor(BaseColorId)
                    : Color.white;

                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, Color.Lerp(source, Tint, Strength));
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
