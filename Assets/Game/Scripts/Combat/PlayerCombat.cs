using UnityEngine;

namespace MiniGTA
{
    public enum WeaponMode { Unarmed, Pistol }

    /// <summary>
    /// The player's melee and pistol.
    ///
    /// Both go through <see cref="Health.Apply"/> and report to <see cref="CrimeReporter"/>,
    /// so the wanted consequences of a punch and a bullet are decided in one place rather than
    /// per weapon. Aim follows the camera, which on a phone means the right-thumb look pad is
    /// also the gun sight -- no separate aim mode to fight the movement stick.
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Loadout")]
        public WeaponMode Mode = WeaponMode.Unarmed;
        public bool HasPistol;
        public int Ammo = 34;
        public int MaxAmmo = 120;

        [Header("Melee")]
        public float MeleeRange = 2.0f;
        public float MeleeRadius = 0.85f;
        public float MeleeDamage = 24f;
        public float MeleeCooldown = 0.55f;
        [Tooltip("Shove applied to whatever gets hit.")]
        public float MeleeKnockback = 4f;

        [Header("Pistol")]
        public float PistolDamage = 34f;
        public float PistolRange = 90f;
        public float PistolCooldown = 0.3f;
        [Tooltip("Cone of inaccuracy in degrees. Zero would feel robotic.")]
        public float PistolSpread = 1.4f;

        [Header("Reload")]
        [Tooltip("How long a reload press is remembered while the weapon is busy. A press "
                 + "landing during a shot or an equip is held for this long and retried, "
                 + "rather than being thrown away.")]
        public float ReloadBufferSeconds = 0.6f;

        [Header("Targeting")]
        public LayerMask HitMask = ~0;
        [Tooltip("Aim origin. Falls back to the main camera.")]
        public Transform AimSource;

        [Header("Refs")]
        public HudContext Hud;
        public Transform MuzzlePoint;

        [Tooltip("Section 2. When present this owns ammunition, carry slots and the legality of "
                 + "firing/reloading/switching; PlayerCombat keeps the ballistics. Resolved "
                 + "automatically in Awake -- leave empty.")]
        public WeaponController Weapons;

        PlayerAnimation _anim;
        PlayerVehicleController _driving;
        Health _health;
        float _cooldown;
        float _reloadWanted;

        readonly Collider[] _meleeHits = new Collider[12];

        /// <summary>
        /// Whether a weapon is up.
        ///
        /// <b>The WeaponController is the authority when one is present.</b> Both used to exist
        /// and only one was ever consulted: the Section 2 state machine was written, compiled,
        /// and attached to nothing, so the game ran the Phase-7 two-field version
        /// (<see cref="Mode"/> + <see cref="HasPistol"/>) and five cheat codes that reach for a
        /// WeaponController silently did nothing. The fields below remain as the fallback for
        /// any rig that genuinely has no controller -- an NPC body, a test scene -- rather than
        /// being deleted, because that fallback is what keeps this component usable on its own.
        /// </summary>
        public bool IsArmed => Weapons != null
                             ? Weapons.HasWeapon && Weapons.State != WeaponState.Locked
                             : Mode == WeaponMode.Pistol && HasPistol;

        /// <summary>Rounds in the magazine. Reads through the controller when there is one.</summary>
        public int LoadedRounds => Weapons != null ? Weapons.Magazine : Ammo;

        /// <summary>Fired on every shot or swing, for HUD and audio.</summary>
        public event System.Action<WeaponMode> Attacked;

        void Awake()
        {
            _anim = GetComponentInChildren<PlayerAnimation>();
            _driving = GetComponent<PlayerVehicleController>();
            _health = GetComponent<Health>();

            if (Weapons == null) Weapons = GetComponent<WeaponController>();
        }

        void Update()
        {
            var hub = InputHub.Instance;
            if (hub == null) return;

            if (_cooldown > 0f) _cooldown -= Time.deltaTime;

            // No fighting while dead or behind the wheel.
            bool canFight = (_health == null || !_health.IsDead)
                            && (_driving == null || !_driving.IsDriving);

            PushHudContext(canFight);

            // The aim stance follows the button directly. It is set even when a shot would be
            // illegal -- mid-reload, empty magazine -- because the weapon staying up while you
            // reload is correct, and dropping it would read as the control having failed.
            if (_anim != null) _anim.SetAiming(canFight && IsArmed && hub.AimHeld);

            if (!canFight)
            {
                // Drain anything queued while dead or driving, so it does not fire the instant
                // the player respawns or steps out of the car.
                hub.ConsumeAttack();
                hub.ConsumeReload();
                _reloadWanted = 0f;
                return;
            }

            TickReload(hub);

            // Weapon switching. WeaponController.TryCycle already owns the slot arithmetic and
            // the locking rules -- it refuses mid-shot and mid-reload, and does nothing with
            // fewer than two weapons carried -- so this is a call, not a reimplementation.
            if (hub.ConsumeWeaponSwitch() && Weapons != null) Weapons.TryCycle();

            // A tap fires once; holding the trigger keeps firing. Consume unconditionally so a
            // tap cannot survive the cooldown and fire late.
            bool tapped = hub.ConsumeAttack();
            if ((tapped || hub.FireHeld) && _cooldown <= 0f) Attack();
        }

        /// <summary>
        /// Deliberate reload, at any magazine level, with the press buffered.
        ///
        /// Two things are new here. The first is that a reload can be asked for at all:
        /// <see cref="WeaponController.TryReload"/> used to have exactly one caller -- the
        /// empty-magazine fallback in <see cref="Attack"/> -- so the only way to top a
        /// magazine up was to fire it dry first.
        ///
        /// The second is the buffer, and it is the half that matters in a fight.
        /// <c>TryReload</c> is refused unless the weapon is Idle, and a thumb reaches for
        /// RELOAD during the shot it just fired. Consuming the press and calling TryReload
        /// once threw it away in exactly that case: measured in a live session, a press
        /// landing while the state machine was still in Firing did nothing at all, which
        /// reads as a dead button. Holding the request for a moment and retrying turns that
        /// into the reload starting as soon as the shot finishes.
        ///
        /// The window is deliberately short. Long enough to cover a fire cooldown or an
        /// equip (0.11 s and 0.65 s for the rifle), short enough that a press cannot surface
        /// as a surprise reload seconds later.
        /// </summary>
        void TickReload(InputHub hub)
        {
            if (hub.ConsumeReload()) _reloadWanted = ReloadBufferSeconds;
            if (_reloadWanted <= 0f || Weapons == null) return;

            _reloadWanted -= Time.deltaTime;
            if (Weapons.TryReload()) _reloadWanted = 0f;
        }

        void PushHudContext(bool canFight)
        {
            if (Hud == null || Hud.Context == PlayerContext.Driving) return;
            if (Hud.Context == PlayerContext.Swimming) return;

            Hud.Context = canFight && IsArmed ? PlayerContext.OnFootArmed : PlayerContext.OnFoot;
        }

        void Attack()
        {
            // Running dry falls back to fists rather than doing nothing -- an unresponsive
            // button is the worst possible way to tell a player they are out of ammo.
            if (Weapons != null)
            {
                // The state machine decides whether a shot is legal (not mid-reload, not
                // mid-switch, magazine not empty) and does the ammunition and the animation.
                // Everything it does not do -- the ray, the damage, the tracer, the crime --
                // stays here, because that is the half PlayerCombat already got right.
                var def = Weapons.ActiveDefinition;
                if (def != null && def.ClipFamily == "Melee") { Melee(); return; }

                if (Weapons.TryFire())
                {
                    FireBallistics(def != null ? def.Damage : PistolDamage,
                                   def != null ? def.Range : PistolRange,
                                   def != null ? def.Spread : PistolSpread);
                    return;
                }

                // Refused. An empty magazine with rounds in reserve means reload -- otherwise
                // TryReload has no caller anywhere in the game and every weapon is
                // single-magazine. Empty everywhere falls through to fists, exactly as the
                // pre-controller version did: an unresponsive button is the worst possible way
                // to tell a player they are out of ammo.
                if (Weapons.HasWeapon && Weapons.Magazine <= 0 && Weapons.Reserve > 0)
                {
                    Weapons.TryReload();
                    return;
                }

                if (Weapons.HasWeapon && Weapons.Magazine > 0) return;
                Melee();
                return;
            }

            if (IsArmed && Ammo > 0) FirePistol();
            else Melee();
        }

        // ------------------------------------------------------------------ melee

        void Melee()
        {
            _cooldown = MeleeCooldown;
            if (_anim != null) _anim.TriggerPunch();
            Attacked?.Invoke(WeaponMode.Unarmed);

            Vector3 origin = transform.position + Vector3.up * 1.1f;
            Vector3 forward = MeleeForward();

            int count = Physics.OverlapSphereNonAlloc(
                origin + forward * (MeleeRange * 0.5f), MeleeRadius, _meleeHits,
                HitMask, QueryTriggerInteraction.Ignore);

            bool connected = false;
            for (int i = 0; i < count; i++)
            {
                var target = _meleeHits[i].GetComponentInParent<Health>();
                if (target == null || target == _health || target.IsDead) continue;

                // Only hit what is roughly in front, so a swing does not clip someone behind.
                Vector3 toTarget = target.transform.position - transform.position;
                toTarget.y = 0f;
                if (Vector3.Dot(toTarget.normalized, forward) < 0.2f) continue;

                var info = new DamageInfo(MeleeDamage, target.transform.position + Vector3.up,
                                          forward, DamageSource.Melee, gameObject);
                bool killed = target.Apply(info);
                connected = true;

                ReportAttackOn(target.gameObject, killed);
            }

            // A missed swing still alarms anyone watching, but is not itself a crime.
            if (!connected)
                Pedestrian.AlarmNear(transform.position, 10f, transform.position);
        }

        // ----------------------------------------------------------------- pistol

        /// <summary>Legacy path, for a rig with no <see cref="WeaponController"/>.</summary>
        void FirePistol()
        {
            _cooldown = PistolCooldown;
            Ammo--;

            if (_anim != null) _anim.TriggerShoot();
            FireBallistics(PistolDamage, PistolRange, PistolSpread);
        }

        /// <summary>
        /// The ray, the damage, the tracer and the crime. Shared by both paths so the state
        /// machine and the legacy fields cannot drift into shooting differently.
        ///
        /// Ammunition, cooldown and the fire animation are deliberately NOT here: whoever
        /// called this has already done them, and doing them twice is how a magazine
        /// decrements two rounds per trigger pull.
        /// </summary>
        void FireBallistics(float damage, float range, float spread)
        {
            Attacked?.Invoke(WeaponMode.Pistol);

            Vector3 origin = AimOrigin();
            Vector3 direction = ApplySpread(AimForwardPrecise(), spread);

            // Firing a gun in public is a crime regardless of what it hits.
            CrimeReporter.ReportAndPanic(Crime.FiredWeapon, transform.position, transform.position);

            bool didHit = Physics.Raycast(origin, direction, out RaycastHit hit, range,
                                          HitMask, QueryTriggerInteraction.Ignore);

            // Item 2.4. Drawn from the muzzle, not from the aim origin: the aim origin is the
            // camera, so a tracer starting there is a streak out of the player's own face.
            Vector3 muzzle = MuzzlePoint != null ? MuzzlePoint.position : origin;
            WeaponVfx.Ensure().Shot(muzzle,
                                    didHit ? hit.point : origin + direction * range,
                                    didHit,
                                    didHit ? hit.normal : Vector3.zero);

            if (didHit)
            {
                var target = hit.collider.GetComponentInParent<Health>();
                if (target != null && target != _health && !target.IsDead)
                {
                    var info = new DamageInfo(damage, hit.point, direction,
                                              DamageSource.Bullet, gameObject);
                    bool killed = target.Apply(info);
                    ReportAttackOn(target.gameObject, killed);
                }
            }

            // Face where the shot went, so the body reads as aiming.
            Vector3 flat = direction; flat.y = 0f;
            if (flat.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
        }

        static Vector3 ApplySpread(Vector3 forward, float degrees)
        {
            if (degrees <= 0f) return forward;
            return Quaternion.Euler(
                Random.Range(-degrees, degrees),
                Random.Range(-degrees, degrees), 0f) * forward;
        }

        // ------------------------------------------------------------------ crime

        void ReportAttackOn(GameObject victim, bool killed)
        {
            bool isPolice = victim.GetComponentInParent<PoliceOfficer>() != null
                            || victim.GetComponentInParent<PoliceVehicle>() != null;

            Crime crime = isPolice
                ? (killed ? Crime.KilledPolice : Crime.AssaultedPolice)
                : (killed ? Crime.KilledCivilian : Crime.AssaultedCivilian);

            CrimeReporter.ReportAndPanic(crime, victim.transform.position, transform.position);
        }

        // ----------------------------------------------------------------- aiming

        Transform Aim => AimSource != null ? AimSource
                       : (Camera.main != null ? Camera.main.transform : transform);

        Vector3 AimOrigin() => Aim == transform
            ? transform.position + Vector3.up * 1.5f
            : Aim.position;

        /// <summary>
        /// Flattened aim, for melee facing checks.
        ///
        /// <b>This is the character's facing, not the camera's, and the difference is the whole
        /// of BUG-008.</b> It used to read <c>Aim.forward</c> -- the camera -- which is right for
        /// a bullet and wrong for a fist. A third-person camera orbits independently of the
        /// body, so the two routinely disagree: measured in a live session with the player
        /// facing (-1,0,0) and the camera facing (0,0,1), a target standing 1.2 m directly in
        /// front of the character was 1.55 m from the swing's 0.85 m sphere -- outside it
        /// entirely -- and the facing gate scored dot 0.000 against a 0.2 threshold, so it was
        /// rejected twice over. Melee landed only when the camera happened to line up with the
        /// body, which is what "punch doesn't work" actually meant.
        ///
        /// Bullets keep using the camera: you shoot where you look. A punch goes where you face.
        /// </summary>
        Vector3 MeleeForward()
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.001f ? f.normalized : transform.forward;
        }

        /// <summary>Full 3D aim, for bullets.</summary>
        Vector3 AimForwardPrecise() => Aim.forward;

        // ---------------------------------------------------------------- loadout

        public void GivePistol(int rounds)
        {
            if (Weapons != null) Weapons.GiveWeapon("pistol", rounds);

            HasPistol = true;
            Ammo = Mathf.Min(MaxAmmo, Ammo + rounds);
            Mode = WeaponMode.Pistol;
        }

        public void ToggleWeapon()
        {
            if (!HasPistol) { Mode = WeaponMode.Unarmed; return; }
            Mode = Mode == WeaponMode.Pistol ? WeaponMode.Unarmed : WeaponMode.Pistol;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Vector3 origin = transform.position + Vector3.up * 1.1f;
            Gizmos.DrawWireSphere(origin + transform.forward * (MeleeRange * 0.5f), MeleeRadius);
        }
    }
}
