using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Steal a marked vehicle and bring it back.
    ///
    /// Taking the car raises the alarm on purpose: the run home under a wanted level is the
    /// mission, not the theft itself. Guards at the lot make the pickup a fight rather than a
    /// walk, and the drop-off is only satisfied while the player is actually in the target
    /// vehicle -- abandoning it halfway is a failure, not a shortcut.
    /// </summary>
    public class HeistMission : MissionBase
    {
        [Header("The score")]
        public GameObject TargetVehiclePrefab;
        public Vector3 PickupPoint;
        public float PickupYaw;
        [Tooltip("Name shown in the objective line.")]
        public string TargetName = "the car";

        [Header("Drop-off")]
        public Vector3 DropOff;
        public float DropRadius = 8f;

        [Header("Security")]
        public GameObject GuardPrefab;
        public int GuardCount = 2;
        public float GuardRadius = 9f;

        [Header("Alarm")]
        [Tooltip("Heat added the moment the target vehicle is taken.")]
        public float AlarmHeat = 60f;

        GameObject _target;
        Vehicle _targetVehicle;
        readonly System.Collections.Generic.List<HostileNpc> _guards =
            new System.Collections.Generic.List<HostileNpc>();

        bool _stolen;

        protected override void OnBegin()
        {
            _stolen = false;
            _guards.Clear();

            if (TargetVehiclePrefab == null) { Fail("No target vehicle configured"); return; }

            _target = Instantiate(TargetVehiclePrefab,
                                  PickupPoint + Vector3.up * 0.4f,
                                  Quaternion.Euler(0f, PickupYaw, 0f));
            _target.name = MissionId + "_target";

            // The score must not be driven off by the traffic AI before the player arrives.
            var traffic = _target.GetComponent<TrafficCar>();
            if (traffic != null) Destroy(traffic);

            _targetVehicle = _target.GetComponent<Vehicle>();
            if (_targetVehicle != null) _targetVehicle.DisplayName = TargetName;

            SpawnGuards();
            SetObjective("Steal " + TargetName, PickupPoint);
        }

        void SpawnGuards()
        {
            if (GuardPrefab == null) return;

            for (int i = 0; i < GuardCount; i++)
            {
                float angle = i / (float)Mathf.Max(1, GuardCount) * Mathf.PI * 2f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * GuardRadius;

                var go = Instantiate(GuardPrefab, PickupPoint + offset + Vector3.up * 0.2f,
                                     Quaternion.LookRotation(-offset.normalized, Vector3.up));
                go.name = MissionId + "_guard_" + i;

                var officer = go.GetComponent<PoliceOfficer>();
                if (officer != null) Destroy(officer);

                var hostile = go.GetComponent<HostileNpc>() ?? go.AddComponent<HostileNpc>();
                hostile.Target = Player;

                var health = go.GetComponent<Health>();
                if (health != null)
                {
                    health.MaxHealth = 65f;
                    health.MaxArmor = 0f;
                    health.Revive();
                }

                _guards.Add(hostile);
            }
        }

        protected override void OnTick(float deltaTime)
        {
            if (_target == null) { Fail(TargetName + " was destroyed"); return; }

            if (_targetVehicle != null && _targetVehicle.IsWrecked)
            {
                Fail(TargetName + " was wrecked");
                return;
            }

            bool drivingTarget = PlayerIsDriving
                                 && PlayerDriving.CurrentVehicle == _targetVehicle;

            if (!_stolen)
            {
                if (!drivingTarget) return;

                _stolen = true;
                HeatSystem.Instance?.AddHeat(AlarmHeat, "Vehicle theft alarm");
                SetObjective("Lose the heat and deliver " + TargetName, DropOff);
                return;
            }

            // Abandoning the score halfway is a failure, not a pause.
            if (!drivingTarget)
            {
                SetObjective("Get back in " + TargetName, _target.transform.position);
                return;
            }

            SetObjective("Deliver " + TargetName, DropOff);

            if (Vector3.Distance(_target.transform.position, DropOff) <= DropRadius)
                Succeed();
        }

        protected override void OnEnd(bool success)
        {
            foreach (var g in _guards)
                if (g != null && !g.IsDead) Destroy(g.gameObject);
            _guards.Clear();

            if (_target != null)
            {
                // On success the car is "handed over" and removed; on failure it is left where
                // it lies so the world does not visibly rewrite itself under the player.
                if (success)
                {
                    if (PlayerDriving != null && PlayerDriving.IsDriving
                        && PlayerDriving.CurrentVehicle == _targetVehicle)
                        PlayerDriving.Exit();

                    Destroy(_target, 1.5f);
                }
            }

            _target = null;
            _targetVehicle = null;

            SetObjective(success ? "Score delivered" : "", null);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(PickupPoint, GuardRadius);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(DropOff, DropRadius);
            Gizmos.DrawLine(PickupPoint, DropOff);
        }
    }
}
