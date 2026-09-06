using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Visual feedback for vehicle condition: panels crumple toward the impact, paint dulls,
    /// and smoke starts once the vehicle is badly hurt.
    ///
    /// Crumpling moves existing vertices' *transforms* rather than deforming meshes, because
    /// per-vertex deformation would break batching and cost a mesh copy per vehicle -- far
    /// too expensive on mobile for what is essentially a readability cue.
    /// </summary>
    public class VehicleDamage : MonoBehaviour
    {
        [Header("Panels that visibly deform")]
        public Transform[] Panels = new Transform[0];

        [Header("Crumple")]
        [Tooltip("Maximum displacement of a panel at zero health.")]
        public float MaxDent = 0.16f;
        [Tooltip("Only panels within this radius of the impact deform.")]
        public float DentRadius = 1.8f;

        [Header("Paint")]
        public Renderer[] BodyRenderers = new Renderer[0];
        [Tooltip("Paint tint at zero health -- scorched, not just dark.")]
        public Color WreckedTint = new Color(0.33f, 0.30f, 0.28f);

        [Header("Smoke")]
        public ParticleSystem Smoke;
        [Tooltip("Health fraction below which smoke starts.")]
        [Range(0f, 1f)] public float SmokeThreshold = 0.4f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MaterialPropertyBlock _mpb;
        Vector3[] _restPositions;
        Color[] _restColors;
        float _lastHealth01 = 1f;

        void Awake()
        {
            _restPositions = new Vector3[Panels.Length];
            for (int i = 0; i < Panels.Length; i++)
                if (Panels[i] != null) _restPositions[i] = Panels[i].localPosition;

            _restColors = new Color[BodyRenderers.Length];
            for (int i = 0; i < BodyRenderers.Length; i++)
            {
                var r = BodyRenderers[i];
                _restColors[i] = r != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId)
                    ? r.sharedMaterial.GetColor(BaseColorId)
                    : Color.white;
            }

            if (Smoke != null) Smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void OnDamaged(float health01, Vector3 worldImpactPoint)
        {
            _lastHealth01 = health01;

            Crumple(health01, worldImpactPoint);
            Repaint(health01);
            UpdateSmoke(health01);
        }

        void Crumple(float health01, Vector3 impact)
        {
            float severity = 1f - health01;

            for (int i = 0; i < Panels.Length; i++)
            {
                var panel = Panels[i];
                if (panel == null) continue;

                float distance = Vector3.Distance(panel.position, impact);
                if (distance > DentRadius) continue;

                // Nearest panels deform most; push them away from the impact, into the body.
                float weight = 1f - Mathf.Clamp01(distance / DentRadius);
                Vector3 inward = transform.InverseTransformDirection(
                    (transform.position - impact).normalized);

                Vector3 dent = inward * (MaxDent * severity * weight);

                // Accumulate: repeated hits keep bending the same panel, up to the cap.
                Vector3 target = _restPositions[i] + dent;
                panel.localPosition = Vector3.MoveTowards(panel.localPosition, target, MaxDent);

                // A dented panel also sits slightly askew.
                panel.localRotation = Quaternion.Euler(
                    Random.Range(-4f, 4f) * severity * weight,
                    Random.Range(-4f, 4f) * severity * weight,
                    Random.Range(-6f, 6f) * severity * weight);
            }
        }

        void Repaint(float health01)
        {
            _mpb ??= new MaterialPropertyBlock();

            for (int i = 0; i < BodyRenderers.Length; i++)
            {
                var r = BodyRenderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, Color.Lerp(WreckedTint, _restColors[i], health01));
                r.SetPropertyBlock(_mpb);
            }
        }

        void UpdateSmoke(float health01)
        {
            if (Smoke == null) return;

            if (health01 <= SmokeThreshold)
            {
                var emission = Smoke.emission;
                // Thicker smoke the closer to wrecked.
                float t = Mathf.InverseLerp(SmokeThreshold, 0f, health01);
                emission.rateOverTime = Mathf.Lerp(8f, 42f, t);

                if (!Smoke.isPlaying) Smoke.Play();
            }
            else if (Smoke.isPlaying)
            {
                Smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>Restore a vehicle to showroom condition. Used by respawn and later by garages.</summary>
        public void Repair()
        {
            for (int i = 0; i < Panels.Length; i++)
            {
                if (Panels[i] == null) continue;
                Panels[i].localPosition = _restPositions[i];
                Panels[i].localRotation = Quaternion.identity;
            }

            Repaint(1f);
            UpdateSmoke(1f);
            _lastHealth01 = 1f;
        }
    }
}
