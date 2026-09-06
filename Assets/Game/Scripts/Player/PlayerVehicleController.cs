using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Owns the on-foot to driving transition and feeds player input to whatever is being
    /// driven.
    ///
    /// Sits on the Player rather than on the vehicle so that exactly one object decides which
    /// controller is live at any moment. Entering disables the CharacterController outright --
    /// leaving it enabled while parented to a moving vehicle makes it fight the rigidbody and
    /// jitter the player out through the roof.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerVehicleController : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("How close the player must be to a vehicle for the Interact button to appear.")]
        public float EnterRadius = 3.6f;
        public LayerMask VehicleMask = ~0;

        [Header("Refs")]
        public HudContext Hud;
        public ThirdPersonCamera CameraRig;

        [Header("Exit")]
        [Tooltip("Refuse to step out above this speed, so you cannot bail at motorway pace.")]
        public float MaxExitSpeedKph = 25f;

        PlayerController _onFoot;
        CharacterController _cc;
        PlayerAnimation _anim;
        Renderer[] _renderers;

        Vehicle _current;
        Vehicle _nearest;
        float _scanTimer;

        readonly Collider[] _hits = new Collider[12];

        public bool IsDriving => _current != null;
        public Vehicle CurrentVehicle => _current;

        void Awake()
        {
            _onFoot = GetComponent<PlayerController>();
            _cc = GetComponent<CharacterController>();
            _anim = GetComponentInChildren<PlayerAnimation>();
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        void Update()
        {
            var hub = InputHub.Instance;
            if (hub == null) return;

            if (_current == null) TickOnFoot(hub);
            else TickDriving(hub);
        }

        // ---------------------------------------------------------------- on foot

        void TickOnFoot(InputHub hub)
        {
            // Scanning every frame is wasted work; a few times a second is well inside
            // human reaction time for a prompt appearing.
            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = 0.15f;
                _nearest = FindNearestVehicle();
            }

            // Contacts and doorways claim the Interact button ahead of vehicles, so talking to
            // someone or opening a shop door never accidentally steals the car parked beside it.
            // That ordering is now InputHub's job -- a vehicle claims the lowest tier -- but the
            // prompt still has to light up when one of them is the thing in range.
            bool otherHasPriority = MissionGiver.ActiveNearby != null || Doorway.ActiveNearby != null;

            if (Hud != null) Hud.InteractTargetInRange = _nearest != null || otherHasPriority;

            if (_nearest == null) return;

            hub.ClaimInteract(this, Vector3.Distance(transform.position, _nearest.transform.position),
                              InputHub.InteractPriorityVehicle);

            if (hub.ConsumeInteract(this)) Enter(_nearest);
        }

        Vehicle FindNearestVehicle()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position + Vector3.up, EnterRadius, _hits, VehicleMask,
                QueryTriggerInteraction.Collide);

            Vehicle best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var v = _hits[i].GetComponentInParent<Vehicle>();
                if (v == null || v.IsOccupied || v.IsWrecked) continue;

                float d = (v.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = v; }
            }
            return best;
        }

        // ---------------------------------------------------------------- driving

        void TickDriving(InputHub hub)
        {
            _current.SetInput(hub.Throttle, hub.Steer, hub.HandbrakeHeld);

            // Aircraft additionally get the stick and the altitude buttons. SetInput still
            // runs so shared behaviour (horn, damage, occupancy) is unchanged.
            if (_current.Kind == VehicleKind.Helicopter)
                _current.SetFlightInput(hub.Move, hub.Climb);

            if (hub.ConsumeHorn()) _current.Honk();

            if (hub.ConsumeInteract())
            {
                if (_current.SpeedKph <= MaxExitSpeedKph) Exit();
                // Above the limit the press is simply ignored; the button stays lit so the
                // player can see the request was received but the car is going too fast.
            }
        }

        // ------------------------------------------------------------ transitions

        public void Enter(Vehicle vehicle)
        {
            if (vehicle == null || vehicle.IsOccupied || vehicle.IsWrecked) return;

            _current = vehicle;
            vehicle.OnEnter(gameObject);

            _onFoot.enabled = false;
            _cc.enabled = false;

            transform.SetParent(vehicle.DriverSeat != null ? vehicle.DriverSeat : vehicle.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            SetRenderersVisible(!vehicle.HideOccupant);
            if (_anim != null) _anim.SetInVehicle(true);

            if (CameraRig != null)
                CameraRig.SetTarget(vehicle.transform, vehicle.CameraDistance, vehicle.CameraPivotOffset);

            if (Hud != null)
            {
                Hud.Context = PlayerContext.Driving;
                Hud.InteractTargetInRange = true;
                if (Hud.VehicleReadout != null) Hud.VehicleReadout.Bind(vehicle);
            }

            // Section 5.3. The car is the player's from here on, and stays theirs when parked.
            vehicle.ClaimForPlayer();

            // Section 5.2. If somebody was driving it, they get out and run.
            var driver = vehicle.GetComponent<VehicleDriver>();
            bool carjacked = driver != null && driver.Eject(transform.position);

            // Taking a car in front of witnesses is grand theft auto. The witness check inside
            // CrimeReporter means doing it down an empty street costs nothing.
            //
            // Pulling somebody out of a moving car is not the same offence as getting into a
            // parked one, so a carjack also panics the street: the victim is a witness the
            // instant they land, and the alarm spreads from there.
            if (carjacked)
                CrimeReporter.ReportAndPanic(Crime.StoleVehicle, vehicle.transform.position,
                                             transform.position, gameObject);
            else
                CrimeReporter.Report(Crime.StoleVehicle, vehicle.transform.position, gameObject);
        }

        public void Exit()
        {
            if (_current == null) return;

            var vehicle = _current;
            Vector3 exitPos = vehicle.GetExitPosition();

            vehicle.OnExit(gameObject);
            _current = null;

            transform.SetParent(null, true);

            // Re-enable in order: the controller must exist before Warp moves it.
            _cc.enabled = true;
            _onFoot.enabled = true;
            _onFoot.Warp(exitPos);
            transform.rotation = Quaternion.Euler(0f, vehicle.transform.eulerAngles.y, 0f);

            SetRenderersVisible(true);
            if (_anim != null) _anim.SetInVehicle(false);

            if (CameraRig != null) CameraRig.ClearTarget(transform);

            if (Hud != null)
            {
                Hud.Context = PlayerContext.OnFoot;
                Hud.InteractTargetInRange = false;
                if (Hud.VehicleReadout != null) Hud.VehicleReadout.Bind(null);
            }

            _nearest = vehicle;   // still standing next to it
        }

        void SetRenderersVisible(bool visible)
        {
            foreach (var r in _renderers)
                if (r != null) r.enabled = visible;
        }
    }
}
