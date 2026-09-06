using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Watches the player's vehicle for running red lights.
    ///
    /// A violation only counts if a police unit is close enough to see it, which is what makes
    /// the risk legible: blowing a red on an empty street is free, doing it in front of a
    /// cruiser is not. Entry into the junction box is latched so one crossing cannot be
    /// counted on successive frames.
    /// </summary>
    public class RedLightMonitor : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Half-width of the junction box, in metres, measured from its centre.")]
        public float JunctionHalfWidth = 8.5f;
        [Tooltip("A violation must be within this distance of a police unit to be witnessed.")]
        public float WitnessRadius = 55f;
        [Tooltip("Ignore crawling through a red -- this is about running it.")]
        public float MinViolationSpeedKph = 12f;

        [Header("Refs")]
        public PlayerVehicleController Driver;

        RoadNetwork _network;
        int _latchedIntersection = -1;

        void Start()
        {
            _network = RoadNetwork.Instance;
            if (Driver == null) Driver = GetComponent<PlayerVehicleController>();
        }

        void Update()
        {
            if (_network == null || Driver == null || !Driver.IsDriving) { _latchedIntersection = -1; return; }

            var vehicle = Driver.CurrentVehicle;
            if (vehicle == null) return;

            int index = FindOccupiedJunction(vehicle.transform.position, out TrafficLightController junction);

            if (index < 0)
            {
                _latchedIntersection = -1;   // left the box; ready to judge the next one
                return;
            }

            if (index == _latchedIntersection) return;   // already judged this crossing
            _latchedIntersection = index;

            if (vehicle.SpeedKph < MinViolationSpeedKph) return;

            Heading heading = NearestHeading(vehicle.transform.forward);
            if (junction.StateFor(heading) != LightState.Red) return;

            var witness = PoliceVehicle.NearestWitness(vehicle.transform.position, WitnessRadius);
            if (witness == null) return;

            HeatSystem.Instance?.AddHeat(
                HeatSystem.Instance.RedLightHeat, "Ran a red light");
        }

        int FindOccupiedJunction(Vector3 position, out TrafficLightController junction)
        {
            junction = null;

            for (int i = 0; i < _network.Intersections.Count; i++)
            {
                var candidate = _network.GetIntersection(i);
                if (candidate == null) continue;

                Vector3 delta = candidate.transform.position - position;
                if (Mathf.Abs(delta.x) <= JunctionHalfWidth && Mathf.Abs(delta.z) <= JunctionHalfWidth)
                {
                    junction = candidate;
                    return i;
                }
            }
            return -1;
        }

        static Heading NearestHeading(Vector3 forward)
        {
            forward.y = 0f;
            if (Mathf.Abs(forward.x) > Mathf.Abs(forward.z))
                return forward.x >= 0f ? Heading.East : Heading.West;
            return forward.z >= 0f ? Heading.North : Heading.South;
        }
    }
}
