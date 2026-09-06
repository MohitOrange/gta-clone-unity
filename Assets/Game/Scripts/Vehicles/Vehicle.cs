using UnityEngine;

namespace MiniGTA
{
    public enum VehicleKind { Car, Bike, Boat, Helicopter }

    /// <summary>
    /// Everything a drivable shares: occupancy, damage, horn, and the camera framing it wants.
    ///
    /// Input arrives as normalised intent (throttle -1..1, steer -1..1) rather than as button
    /// state, so the same three subclasses serve the player, the traffic AI and later the
    /// police pursuit driver without any of them knowing about the HUD.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public abstract class Vehicle : MonoBehaviour
    {
        [Header("Identity")]
        public VehicleKind Kind = VehicleKind.Car;
        public string DisplayName = "Vehicle";

        [Header("Seating")]
        public Transform DriverSeat;
        public Transform ExitPoint;
        [Tooltip("Hide the driver's model while aboard. True for enclosed cabins, false for " +
                 "bikes and boats where the rider should be visible.")]
        public bool HideOccupant = true;

        [Header("Camera framing")]
        public float CameraDistance = 8f;
        public Vector3 CameraPivotOffset = new Vector3(0f, 2.0f, 0f);

        [Header("Damage")]
        public float MaxHealth = 100f;
        [Tooltip("Impact speed below which a collision does no damage, so kerbs are harmless.")]
        public float DamageThreshold = 4.5f;
        public float DamagePerImpulse = 0.55f;

        protected Rigidbody Body;
        VehicleDamage _damage;

        public float Health { get; private set; }
        public bool IsOccupied { get; private set; }
        public bool IsWrecked => Health <= 0f;

        /// <summary>Signed forward speed in m/s. Negative when reversing.</summary>
        public float ForwardSpeed => Vector3.Dot(Body.linearVelocity, transform.forward);
        public float SpeedKph => Mathf.Abs(ForwardSpeed) * 3.6f;

        // --- Current input, written by whoever is driving ---
        protected float Throttle;   // -1 reverse/brake .. 1 forward
        protected float Steer;      // -1 left .. 1 right
        protected bool Handbrake;

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody>();
            _damage = GetComponent<VehicleDamage>();
            Health = MaxHealth;
        }

        public void SetInput(float throttle, float steer, bool handbrake)
        {
            Throttle = Mathf.Clamp(throttle, -1f, 1f);
            Steer = Mathf.Clamp(steer, -1f, 1f);
            Handbrake = handbrake;
        }

        public void ClearInput() => SetInput(0f, 0f, true);

        /// <summary>
        /// Flight input, for vehicles that have an axis the ground vehicles do not.
        ///
        /// A separate entry point rather than more parameters on <see cref="SetInput"/>: cars,
        /// bikes and boats have no collective and would ignore it, and widening the call every
        /// ground vehicle makes for the benefit of one flying one is the wrong trade. Default
        /// is a no-op so nothing else has to know this exists.
        /// </summary>
        /// <param name="cyclic">Stick. x steers, y pitches forward and back.</param>
        /// <param name="collective">Altitude. +1 climb, -1 descend.</param>
        public virtual void SetFlightInput(Vector2 cyclic, float collective) { }

        /// <summary>True while the player specifically is at the wheel, not just any occupant.</summary>
        public bool IsPlayerDriven { get; private set; }

        /// <summary>
        /// Section 5.3. True once the player has driven this vehicle at any point this session.
        ///
        /// <b>Applied default: ownership is permanent for the session, not released on exit.</b>
        /// The alternative -- ownership lapsing once the player walks a certain distance away --
        /// reads better on paper and worse in play, because the distance that releases a car is
        /// exactly the distance at which the player cannot see it being taken, so the car simply
        /// stops being where it was left with no explanation. Permanent is the rule a player can
        /// actually predict: park it, come back, it is there.
        ///
        /// The cost is a slow leak of cars out of the traffic pool -- every car the player ever
        /// touches stops being recycled. At a pool of 12 that matters, so TrafficSpawner treats
        /// a claimed car as no longer its business and tops the pool back up instead.
        /// </summary>
        public bool ClaimedByPlayer { get; private set; }

        /// <summary>Marks this vehicle as the player's for the rest of the session.</summary>
        public void ClaimForPlayer() => ClaimedByPlayer = true;

        public virtual void OnEnter(GameObject occupant)
        {
            IsOccupied = true;
            IsPlayerDriven = occupant != null && occupant.CompareTag("Player");
        }

        public virtual void OnExit(GameObject occupant)
        {
            IsOccupied = false;
            IsPlayerDriven = false;
            ClearInput();
        }

        public void Honk() => VehicleHorn.Broadcast(this);

        [Header("Pedestrian impacts")]
        [Tooltip("Speed below which hitting a person only knocks them down.")]
        public float LethalImpactSpeed = 9f;
        [Tooltip("Damage dealt to a person per m/s of impact.")]
        public float PedestrianDamagePerSpeed = 9f;

        void OnCollisionEnter(Collision collision)
        {
            // Relative velocity along the contact normal: a graze at speed should not cost the
            // same as a head-on hit.
            float impact = collision.relativeVelocity.magnitude;

            HandlePersonImpact(collision, impact);

            if (impact < DamageThreshold) return;

            VehicleImpact.Broadcast(this, collision.GetContact(0).point, impact);

            float amount = (impact - DamageThreshold) * DamagePerImpulse;
            ApplyDamage(amount, collision.GetContact(0).point);
        }

        /// <summary>
        /// Hitting a person hurts them, not the car. Handled here rather than on the pedestrian
        /// because only the vehicle knows how fast the impact was.
        /// </summary>
        void HandlePersonImpact(Collision collision, float impact)
        {
            var victim = collision.collider.GetComponentInParent<Health>();
            if (victim == null || victim.IsDead) return;

            // Ignore anything that is not a person on foot.
            bool isPerson = victim.GetComponentInParent<Pedestrian>() != null
                            || victim.GetComponentInParent<PoliceOfficer>() != null;
            if (!isPerson) return;

            if (impact < 2.5f) return;   // nudging someone at walking pace is not an assault

            Vector3 point = collision.GetContact(0).point;
            Vector3 direction = collision.relativeVelocity.sqrMagnitude > 0.01f
                ? -collision.relativeVelocity.normalized
                : transform.forward;

            float damage = impact >= LethalImpactSpeed
                ? impact * PedestrianDamagePerSpeed
                : impact * PedestrianDamagePerSpeed * 0.4f;

            bool killed = victim.Apply(new DamageInfo(
                damage, point, direction, DamageSource.Vehicle, gameObject));

            // Only the player's driving earns a wanted level; AI traffic accidents do not.
            if (!IsPlayerDriven) return;

            CrimeReporter.ReportAndPanic(
                killed ? Crime.KilledCivilian : Crime.RanOverCivilian,
                point, transform.position, gameObject);
        }

        public void ApplyDamage(float amount, Vector3 worldPoint)
        {
            if (amount <= 0f || IsWrecked) return;

            Health = Mathf.Max(0f, Health - amount);
            if (_damage != null) _damage.OnDamaged(Health / MaxHealth, worldPoint);
        }

        /// <summary>Where a driver should be placed when stepping out.</summary>
        public Vector3 GetExitPosition()
        {
            if (ExitPoint != null) return ExitPoint.position;
            return transform.position + transform.right * -2.2f + Vector3.up * 0.5f;
        }
    }

    /// <summary>Tiny broadcast hook so pedestrians and traffic can react to a horn.</summary>
    public static class VehicleHorn
    {
        public static event System.Action<Vehicle> Honked;
        public static void Broadcast(Vehicle v) => Honked?.Invoke(v);
    }

    /// <summary>
    /// Collisions hard enough to cost condition, for anything that wants to react to a crash.
    ///
    /// Static like the horn hook rather than an AudioSource on every car: there are twenty-odd
    /// vehicles in the world and at most one of them is crashing, so paying for a source per
    /// car to serve a sound that plays once a minute is the wrong trade on a phone.
    /// </summary>
    public static class VehicleImpact
    {
        /// <summary>Vehicle, contact point, and closing speed in m/s.</summary>
        public static event System.Action<Vehicle, Vector3, float> Occurred;

        public static void Broadcast(Vehicle v, Vector3 point, float speed)
            => Occurred?.Invoke(v, point, speed);
    }
}
