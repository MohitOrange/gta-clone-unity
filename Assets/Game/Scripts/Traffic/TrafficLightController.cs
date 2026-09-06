using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    public enum LightState { Red, Yellow, Green }

    /// <summary>
    /// One intersection's signal cycle, plus the lamp renderers that display it.
    ///
    /// Modelled as a single position within a repeating cycle rather than as a state machine
    /// with transitions. That makes <see cref="PhaseOffset"/> work the obvious way -- an
    /// offset junction simply starts further along the same cycle -- whereas a transition
    /// machine has to be stepped forward to desynchronise, and starts every junction on green
    /// if you forget.
    ///
    /// Two vehicle phases (north-south, then east-west) separated by yellow and a short
    /// all-red. Pedestrians cross an axis while that axis's *vehicles* are stopped, so the
    /// crossing signal is derived from the same clock rather than tracked separately.
    /// </summary>
    public class TrafficLightController : MonoBehaviour
    {
        [Header("Timing (seconds)")]
        public float GreenDuration = 9f;
        public float YellowDuration = 2.2f;
        [Tooltip("All-red gap between phases so a car finishing a turn can clear.")]
        public float AllRedDuration = 1.1f;

        [Tooltip("Seconds to advance this junction's clock, staggering it against its neighbours.")]
        public float PhaseOffset;

        [Header("Lamps")]
        public List<TrafficLightLamps> Lamps = new List<TrafficLightLamps>();

        float _cycleTime;
        LightState _lastNorthSouth = (LightState)(-1);
        LightState _lastEastWest = (LightState)(-1);

        public LightState NorthSouth { get; private set; } = LightState.Green;
        public LightState EastWest { get; private set; } = LightState.Red;

        /// <summary>Full cycle: both phases, each with its green, yellow and trailing all-red.</summary>
        public float CycleLength => 2f * (GreenDuration + YellowDuration + AllRedDuration);

        void Start()
        {
            _cycleTime = Mathf.Repeat(PhaseOffset, CycleLength);
            Evaluate();
        }

        void Update()
        {
            _cycleTime = Mathf.Repeat(_cycleTime + Time.deltaTime, CycleLength);
            Evaluate();
        }

        void Evaluate()
        {
            float g = GreenDuration;
            float y = YellowDuration;
            float r = AllRedDuration;
            float t = _cycleTime;

            if (t < g) { NorthSouth = LightState.Green; EastWest = LightState.Red; }
            else if (t < g + y) { NorthSouth = LightState.Yellow; EastWest = LightState.Red; }
            else if (t < g + y + r) { NorthSouth = LightState.Red; EastWest = LightState.Red; }
            else if (t < 2f * g + y + r) { NorthSouth = LightState.Red; EastWest = LightState.Green; }
            else if (t < 2f * g + 2f * y + r) { NorthSouth = LightState.Red; EastWest = LightState.Yellow; }
            else { NorthSouth = LightState.Red; EastWest = LightState.Red; }

            // Lamps only need touching when something actually changed; pushing a property
            // block every frame across 64 heads is pure waste.
            if (NorthSouth == _lastNorthSouth && EastWest == _lastEastWest) return;
            _lastNorthSouth = NorthSouth;
            _lastEastWest = EastWest;

            for (int i = 0; i < Lamps.Count; i++)
                if (Lamps[i] != null)
                    Lamps[i].Display(StateFor(Lamps[i].Facing));
        }

        /// <summary>Signal governing a vehicle approaching along the given heading.</summary>
        public LightState StateFor(Heading heading) =>
            heading.IsNorthSouth() ? NorthSouth : EastWest;

        /// <summary>
        /// True when a pedestrian may walk along <paramref name="walkHeading"/>. Walking
        /// east-west means crossing the north-south carriageway, so it is the north-south
        /// vehicle signal that must be red.
        /// </summary>
        public bool PedestrianMayCross(Heading walkHeading)
        {
            LightState blocking = walkHeading.IsNorthSouth() ? EastWest : NorthSouth;
            return blocking == LightState.Red;
        }

        /// <summary>Seconds until the given approach turns red. Used by AI to judge a yellow.</summary>
        public float TimeUntilRed(Heading heading)
        {
            LightState s = StateFor(heading);
            if (s == LightState.Red) return 0f;

            float g = GreenDuration;
            float y = YellowDuration;
            float r = AllRedDuration;
            float t = _cycleTime;

            if (heading.IsNorthSouth())
                return Mathf.Max(0f, (g + y) - t);

            // East-west green starts after the north-south phase completes.
            float ewStart = g + y + r;
            return Mathf.Max(0f, (ewStart + g + y) - t);
        }
    }
}
