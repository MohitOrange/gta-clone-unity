using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Moves the player between the street and an interior, and adjusts everything that has to
    /// change with them.
    ///
    /// Going indoors is not just a teleport: the world's ambient light has to be replaced (the
    /// day/night cycle is still running outside and would darken a lit shop at night), the
    /// traffic and police simulations have to stop chasing a player who is now a kilometre off
    /// the map, and the camera has to be pulled in so it does not sit outside the wall.
    /// </summary>
    public class InteriorManager : MonoBehaviour
    {
        static InteriorManager _instance;

        public static InteriorManager Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<InteriorManager>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Camera")]
        [Tooltip("Closer follow distance while indoors, so the camera stays inside the room.")]
        public float InteriorCameraDistance = 3.4f;
        public Vector3 InteriorPivotOffset = new Vector3(0f, 1.5f, 0f);

        [Header("Refs")]
        public GameObject Player;
        public ThirdPersonCamera CameraRig;

        Interior _current;
        DayNightCycle _cycle;
        TrafficSpawner _traffic;
        PoliceDispatcher _police;

        float _outdoorCameraDistance;
        Vector3 _outdoorPivot;
        bool _cameraCaptured;

        public Interior Current => _current;
        public bool IsInside => _current != null;

        public event System.Action<Interior> Entered;
        public event System.Action<Interior> Exited;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            if (Player == null) Player = GameObject.FindWithTag("Player");
            if (CameraRig == null && Camera.main != null)
                CameraRig = Camera.main.GetComponent<ThirdPersonCamera>();

            _cycle = FindAnyObjectByType<DayNightCycle>();
            _traffic = FindAnyObjectByType<TrafficSpawner>();
            _police = FindAnyObjectByType<PoliceDispatcher>();
        }

        // ------------------------------------------------------------------- enter

        public bool Enter(Interior interior, Vector3 returnPosition, Quaternion returnRotation)
        {
            if (interior == null || IsInside || Player == null) return false;
            if (interior.EntryPoint == null)
            {
                Debug.LogWarning("[Interior] " + interior.InteriorId + " has no entry point.");
                return false;
            }

            var driving = Player.GetComponent<PlayerVehicleController>();
            if (driving != null && driving.IsDriving)
            {
                MissionHud.Instance?.ShowToast("Get out of the vehicle first");
                return false;
            }

            _current = interior;
            interior.ReturnPosition = returnPosition;
            interior.ReturnRotation = returnRotation;
            interior.SetOccupied(true);

            MovePlayer(interior.EntryPoint.position, interior.EntryPoint.rotation);

            ApplyInteriorLighting(interior);
            SetOutdoorSimulation(false);
            ApplyInteriorCamera(true);

            if (interior.IsGarage) Garage.Instance?.RefreshDisplay();

            if (interior.SavesOnEntry)
            {
                SaveSystem.Instance?.Save();
                MissionHud.Instance?.ShowToast("Progress saved");
            }

            Entered?.Invoke(interior);
            return true;
        }

        // -------------------------------------------------------------------- exit

        public void Exit()
        {
            if (!IsInside) return;

            var interior = _current;
            _current = null;

            if (interior.IsGarage) Garage.Instance?.ClearDisplay();

            MovePlayer(interior.ReturnPosition, interior.ReturnRotation);
            interior.SetOccupied(false);

            RestoreOutdoorLighting();
            SetOutdoorSimulation(true);
            ApplyInteriorCamera(false);

            Exited?.Invoke(interior);
        }

        // ------------------------------------------------------------------ pieces

        void MovePlayer(Vector3 position, Quaternion rotation)
        {
            var controller = Player.GetComponent<PlayerController>();
            if (controller != null) controller.Warp(position);
            else Player.transform.position = position;

            Player.transform.rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);

            if (CameraRig != null) CameraRig.AlignBehindTarget();
        }

        void ApplyInteriorLighting(Interior interior)
        {
            // Stop the day/night cycle writing over the indoor ambient every frame.
            if (_cycle != null) _cycle.enabled = false;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = interior.AmbientColour;

            if (interior.DisableFogInside) RenderSettings.fog = false;
        }

        void RestoreOutdoorLighting()
        {
            if (_cycle != null)
            {
                _cycle.enabled = true;
                // Re-apply immediately so the world does not flash the indoor ambient for a frame.
                _cycle.Refresh();
            }
            else
            {
                RenderSettings.fog = true;
            }
        }

        void SetOutdoorSimulation(bool running)
        {
            if (_traffic != null) _traffic.enabled = running;

            // The dispatcher keeps its units but stops spawning and re-homing them while the
            // player is off-map; a wanted level survives the visit, which is the point.
            if (_police != null) _police.enabled = running;
        }

        void ApplyInteriorCamera(bool inside)
        {
            if (CameraRig == null) return;

            if (inside)
            {
                if (!_cameraCaptured)
                {
                    _outdoorCameraDistance = CameraRig.Distance;
                    _outdoorPivot = CameraRig.PivotOffset;
                    _cameraCaptured = true;
                }

                CameraRig.Distance = InteriorCameraDistance;
                CameraRig.PivotOffset = InteriorPivotOffset;
            }
            else if (_cameraCaptured)
            {
                CameraRig.Distance = _outdoorCameraDistance;
                CameraRig.PivotOffset = _outdoorPivot;
            }

            CameraRig.AlignBehindTarget();
        }
    }
}
