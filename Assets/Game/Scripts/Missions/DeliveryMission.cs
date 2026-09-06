using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Timed drive: reach each checkpoint in order before the clock runs out.
    ///
    /// Requires the player to be in a vehicle, and fails if they get out for too long -- a
    /// delivery run you can complete on foot is not a driving mission, and the time limit
    /// alone will not enforce that on a short route.
    /// </summary>
    public class DeliveryMission : MissionBase
    {
        [Header("Route")]
        [Tooltip("Checkpoints in order. The last one is the drop-off.")]
        public Vector3[] Checkpoints = new Vector3[0];
        [Tooltip("How close counts as reaching a checkpoint.")]
        public float ArriveRadius = 9f;

        [Header("Vehicle requirement")]
        public bool RequiresVehicle = true;
        [Tooltip("Grace period on foot before the run is scrubbed.")]
        public float OnFootGrace = 12f;

        [Header("Cargo")]
        [Tooltip("Shown in the objective line, e.g. 'the package'.")]
        public string CargoName = "the package";

        int _index;
        float _onFootTimer;

        /// <summary>
        /// Whether the player has been behind the wheel at least once this run.
        ///
        /// <b>This is the whole of BUG-010.</b> The vehicle requirement used to start policing
        /// the moment the mission began -- but a job is accepted on foot, standing next to a
        /// contact, and the player then has to go and find a car. That takes longer than the
        /// 12-second grace essentially every time, so a 165-second delivery failed at 12
        /// seconds with "You left the vehicle", which reads to a player as the timer expiring
        /// early. Nothing was wrong with the clock.
        ///
        /// The grace exists to scrub a run where the player abandons the car mid-delivery. It
        /// cannot do that job before there is a car to abandon, so it does not start until
        /// there has been one.
        /// </summary>
        bool _hasDriven;

        protected override void OnBegin()
        {
            _index = 0;
            _onFootTimer = 0f;
            _hasDriven = false;

            if (Checkpoints.Length == 0)
            {
                Fail("No route set");
                return;
            }

            AnnounceCurrent();
        }

        protected override void OnTick(float deltaTime)
        {
            if (RequiresVehicle)
            {
                if (PlayerIsDriving)
                {
                    _onFootTimer = 0f;
                    _hasDriven = true;
                }
                else if (_hasDriven)
                {
                    // Only once there has been a car to leave. See _hasDriven.
                    _onFootTimer += deltaTime;
                    if (_onFootTimer >= OnFootGrace)
                    {
                        Fail("You left the vehicle");
                        return;
                    }
                }
            }

            if (DistanceToPlayer(Checkpoints[_index]) > ArriveRadius) return;

            _index++;
            if (_index >= Checkpoints.Length) { Succeed(); return; }

            AnnounceCurrent();
        }

        void AnnounceCurrent()
        {
            bool last = _index == Checkpoints.Length - 1;
            int remaining = Checkpoints.Length - _index;

            string text = last
                ? "Deliver " + CargoName
                : "Checkpoint " + (_index + 1) + " of " + Checkpoints.Length
                  + "  (" + remaining + " left)";

            SetObjective(text, Checkpoints[_index]);
        }

        protected override void OnEnd(bool success) => SetObjective(success ? "Delivered" : "", null);

        void OnDrawGizmosSelected()
        {
            if (Checkpoints == null) return;

            Gizmos.color = Color.cyan;
            for (int i = 0; i < Checkpoints.Length; i++)
            {
                Gizmos.DrawWireSphere(Checkpoints[i], ArriveRadius);
                if (i > 0) Gizmos.DrawLine(Checkpoints[i - 1], Checkpoints[i]);
            }
        }
    }
}
