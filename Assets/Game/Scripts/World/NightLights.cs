using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Switches the city's emissive window overlays on after dark.
    ///
    /// The Cartoon City buildings ship a separate low-poly shell of lit windows per model
    /// (66-244 triangles) that is meant to sit inside the building at night. Rather than a
    /// component per building, one component owns every overlay in the scene: the decision is
    /// the same for all of them, so making it once a frame beats making it ninety times.
    ///
    /// Disabled overlays cost nothing to draw, so by day this is free.
    /// </summary>
    [ExecuteAlways]
    public class NightLights : MonoBehaviour
    {
        [Tooltip("Emissive window shells, one per building that has one.")]
        public Renderer[] Lights = new Renderer[0];

        [Tooltip("Cycle position at which the lights come on (0 = midnight, 0.5 = noon).")]
        [Range(0f, 1f)] public float DuskAt = 0.76f;
        [Range(0f, 1f)] public float DawnAt = 0.26f;

        DayNightCycle _cycle;
        bool _lit;
        bool _applied;

        void OnEnable()
        {
            _cycle = FindAnyObjectByType<DayNightCycle>();
            _applied = false;
        }

        void Update()
        {
            if (_cycle == null)
            {
                _cycle = FindAnyObjectByType<DayNightCycle>();
                if (_cycle == null) return;
            }

            float t = _cycle.TimeOfDay;
            bool lit = t >= DuskAt || t <= DawnAt;

            // Only touch the renderers when the answer changes. Setting `enabled` on ninety
            // renderers every frame is a real cost for a value that flips twice a day.
            if (_applied && lit == _lit) return;

            _lit = lit;
            _applied = true;

            foreach (var r in Lights)
                if (r != null) r.enabled = lit;
        }
    }
}
