using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The person behind the wheel of an AI car, and what happens to them when the player
    /// takes it. Section 5.2.
    ///
    /// <b>Traffic cars had no driver at all.</b> `TrafficCar` steers the car directly and never
    /// occupies it, so before this component every car on the road was an empty shell rolling
    /// down the street, and "the driver gets out and runs" had nobody to eject. The obvious
    /// shortcut -- conjure a fleeing pedestrian at the moment of the carjack -- was rejected:
    /// the player would watch an empty car produce a person on being opened, which is worse
    /// than no driver at all because it draws attention to the trick.
    ///
    /// So the driver is real, and is a body the player can see through the windscreen before
    /// deciding to take the car. It is a visual occupant only -- no AI, no Pedestrian component,
    /// no collider -- because the car is already driving itself and a second brain in the seat
    /// would be two things steering one vehicle.
    ///
    /// <b>Cost control.</b> A skinned body per traffic car is 12 more animated characters in a
    /// scene that already carries 300. The body therefore exists only while the car is within
    /// <see cref="VisibleDistance"/> of the player and is destroyed again beyond it, so the
    /// steady-state cost is the handful of cars actually on screen rather than the whole pool.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class VehicleDriver : MonoBehaviour
    {
        [Header("Presence")]
        [Tooltip("Beyond this distance from the player the seated body is destroyed. Well past "
                 + "the range at which a face behind a windscreen is readable.")]
        public float VisibleDistance = 90f;

        [Tooltip("Seconds between distance checks. This does not need to be a per-frame job.")]
        public float CheckInterval = 0.8f;

        [Header("Seating")]
        [Tooltip("Fine adjustment applied after the body is anchored by its hips. Leave at zero "
                 + "unless a particular car's seat marker is off; the anchoring is measured.")]
        public Vector3 SeatTrim = Vector3.zero;

        Vehicle _vehicle;
        Transform _player;
        GameObject _body;
        float _timer;
        bool _ejected;

        /// <summary>True while there is somebody to throw out.</summary>
        public bool HasDriver => !_ejected;

        void Awake() => _vehicle = GetComponent<Vehicle>();

        void Start()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) _player = player.transform;
        }

        void Update()
        {
            if (_ejected || _player == null) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = CheckInterval;

            // A car the player is driving obviously has no NPC in the driver's seat. This also
            // covers the frame order case where the body outlives the eject by one check.
            if (_vehicle.IsPlayerDriven) { DestroyBody(); return; }

            bool near = Vector3.Distance(transform.position, _player.position) <= VisibleDistance;
            if (near && _body == null) CreateBody();
            else if (!near && _body != null) DestroyBody();
        }

        // ------------------------------------------------------------------ the carjack

        /// <summary>
        /// Section 5.2. Throws the driver out and sends them running.
        ///
        /// Returns true if somebody was actually ejected, so the caller can tell a carjack from
        /// simply getting into a parked car -- which matters, because only one of the two is a
        /// crime anybody witnesses.
        ///
        /// The car's own AI is switched off here rather than by the caller. A `TrafficCar` left
        /// enabled under a player-driven vehicle keeps writing steering and throttle every
        /// frame, and the two fight for the same rigidbody; the symptom is a car that pulls
        /// toward the nearest lane node no matter what the stick says.
        /// </summary>
        public bool Eject(Vector3 threat)
        {
            if (_ejected) return false;
            _ejected = true;

            var ai = GetComponent<TrafficCar>();
            if (ai != null) ai.enabled = false;

            Vector3 where = ExitSpot();
            DestroyBody();

            var crowd = Object.FindAnyObjectByType<CrowdDirector>();
            if (crowd == null)
            {
                // No crowd system in the scene: the driver is gone, but silently, which is the
                // one outcome 5.2 explicitly rules out. Say so rather than let it pass.
                Debug.LogWarning("[Carjack] No CrowdDirector, so the ejected driver could not be "
                                 + "given a body. The car is stolen but nobody got out of it.");
                return true;
            }

            var fled = crowd.SpawnFleeingPedestrian(where, threat);
            return fled != null;
        }

        /// <summary>
        /// Where the driver lands. The vehicle's own exit point if it has one, since that is
        /// already known to be clear of the bodywork, otherwise a step out to the left.
        /// </summary>
        Vector3 ExitSpot()
        {
            Vector3 spot = _vehicle.ExitPoint != null
                         ? _vehicle.ExitPoint.position
                         : transform.position - transform.right * 2.2f;

            // Put them on the ground rather than at the seat height they were sitting at.
            if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out var hit, 6f,
                                ~0, QueryTriggerInteraction.Ignore))
                spot = hit.point;

            return spot;
        }

        // ------------------------------------------------------------------ the seated body

        void CreateBody()
        {
            var crowd = Object.FindAnyObjectByType<CrowdDirector>();
            if (crowd == null) return;

            var template = crowd.DriverBodyTemplate();
            if (template == null) return;

            Transform seat = _vehicle.DriverSeat != null ? _vehicle.DriverSeat : transform;

            _body = Instantiate(template, seat);
            _body.name = "Driver";
            _body.transform.localPosition = Vector3.zero;
            _body.transform.localRotation = Quaternion.identity;

            // Strip everything that would make this a second actor rather than a passenger.
            // A Pedestrian in here would walk out of the car; a CharacterController would fight
            // the vehicle's own collider.
            //
            // Disabled first and destroyed second, and the order is not cosmetic. Destroy is
            // deferred to the end of the frame, so a component removed this way is still live
            // for every Update in between -- and the first version of this, which only called
            // Destroy, left a fully awake Pedestrian riding around inside the car writing its
            // own locomotion parameters over the animator. Setting enabled=false takes effect
            // immediately, which is what the frame in between actually needs.
            foreach (var ped in _body.GetComponentsInChildren<Pedestrian>())
            { ped.enabled = false; Destroy(ped); }
            foreach (var town in _body.GetComponentsInChildren<TownNpc>())
            { town.enabled = false; Destroy(town); }
            foreach (var pa in _body.GetComponentsInChildren<PlayerAnimation>())
            { pa.enabled = false; Destroy(pa); }
            foreach (var cc in _body.GetComponentsInChildren<CharacterController>())
            { cc.enabled = false; Destroy(cc); }
            foreach (var col in _body.GetComponentsInChildren<Collider>())
            { col.enabled = false; Destroy(col); }

            var animator = _body.GetComponentInChildren<Animator>();
            if (animator == null) return;

            animator.enabled = true;

            // Snap into the seated pose before measuring anything. The shared controller already
            // has a Sit state driven by InVehicle, built for the player riding in a car, so the
            // same pose serves a passenger. Play+Update rather than setting the bool and waiting:
            // the bool needs a transition, and the body has to be posed *now* because the
            // measurement below reads bone positions out of the current pose.
            foreach (var p in animator.parameters)
                if (p.name == "InVehicle") { animator.SetBool("InVehicle", true); break; }
            animator.Play("Sit", 0, 0f);
            animator.Update(0f);

            // ASSET GAP, flagged rather than papered over: the Sit state's clip is
            // HumanM@MilitaryIdle01 -- a *standing* idle. Neither animation pack ships a seated
            // pose, so "Sit" is a substitution made back in Phase 1 and nothing in this project
            // can actually sit down. The driver is therefore a standing figure occupying a car
            // seat, which reads acceptably through the tinted glass most of these cars have and
            // poorly through the clear-glass ones. Needs a seated clip, or sign-off on this.

            AnchorByHips(seat, animator);

            // Only cull after placing. CullCompletely stops the state machine off-screen, and a
            // body that has never evaluated a frame has no pose to hold -- BUG-023, which shipped
            // as a street full of T-posed pedestrians.
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
        }

        /// <summary>
        /// Puts the body's hips on the seat marker, rather than trusting a typed-in offset.
        ///
        /// <b>Why this is measured.</b> `DriverSeat` was authored for the player, whose pivot is
        /// the centre of a CharacterController capsule, so the marker sits 0.94 m above the car
        /// floor. A pedestrian body's pivot is at its feet. Parenting one to that marker put the
        /// feet where the hips belong and the driver rode along sitting on the roof with her
        /// torso through the windscreen -- which is what the first capture of this showed.
        ///
        /// Anchoring on the hips bone makes the placement a property of the rig and the seat
        /// rather than of a constant that happens to suit one car and one character, which
        /// matters here because Section 3B is about to make the character vary.
        /// </summary>
        void AnchorByHips(Transform seat, Animator animator)
        {
            if (!animator.isHuman)
            {
                // Generic rig: fall back to the renderer bounds, which still beats a constant.
                var rends = _body.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) return;
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float lift = b.center.y - _body.transform.position.y;
                _body.transform.localPosition = SeatTrim - new Vector3(0f, lift, 0f);
                return;
            }

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return;

            // Where the hips currently are, in the seat's own space. Subtracting that puts them
            // exactly on the marker.
            Vector3 hipsInSeat = seat.InverseTransformPoint(hips.position);
            _body.transform.localPosition = SeatTrim - hipsInSeat;
        }

        void DestroyBody()
        {
            if (_body == null) return;
            Destroy(_body);
            _body = null;
        }
    }
}
