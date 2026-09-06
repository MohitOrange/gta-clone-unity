using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The three lamps on one signal head, facing one approach.
    ///
    /// Colour is pushed through a MaterialPropertyBlock rather than by swapping materials:
    /// swapping would break the shared-material batching that keeps 60-odd signal heads
    /// affordable, and would leak a material instance per lamp per switch.
    /// </summary>
    public class TrafficLightLamps : MonoBehaviour
    {
        [Tooltip("Which approach this head faces. Drivers heading this way read this signal.")]
        public Heading Facing;

        public Renderer RedLamp;
        public Renderer YellowLamp;
        public Renderer GreenLamp;

        [Header("Colours")]
        public Color RedOn = new Color(1f, 0.12f, 0.08f);
        public Color YellowOn = new Color(1f, 0.72f, 0.05f);
        public Color GreenOn = new Color(0.15f, 1f, 0.25f);
        [Tooltip("Unlit lamps stay visible as dark glass rather than vanishing.")]
        public Color OffTint = new Color(0.06f, 0.06f, 0.07f);

        [Tooltip("Emission multiplier for the lit lamp. Above 1 so it reads in daylight.")]
        public float EmissionBoost = 3.2f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock _mpb;

        public void Display(LightState state)
        {
            _mpb ??= new MaterialPropertyBlock();

            Apply(RedLamp, RedOn, state == LightState.Red);
            Apply(YellowLamp, YellowOn, state == LightState.Yellow);
            Apply(GreenLamp, GreenOn, state == LightState.Green);
        }

        void Apply(Renderer r, Color onColor, bool lit)
        {
            if (r == null) return;

            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, lit ? onColor : OffTint);
            _mpb.SetColor(EmissionColorId, lit ? onColor * EmissionBoost : Color.black);
            r.SetPropertyBlock(_mpb);
        }
    }
}
