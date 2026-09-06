using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Thin bridge between gameplay state and the Animator.
    ///
    /// All blending lives in the AnimatorController asset (built by AnimatorBuilder), not
    /// here -- that is what lets a different Mixamo model or a re-authored state machine be
    /// dropped in without touching code. This class only translates physical state into
    /// parameters, and it verifies each parameter exists so a half-built controller degrades
    /// quietly instead of throwing every frame.
    /// </summary>
    public class PlayerAnimation : MonoBehaviour
    {
        public static readonly int SpeedHash = Animator.StringToHash("Speed");
        // Section 7: local-space movement direction, driving the 2D locomotion blend.
        public static readonly int MoveXHash = Animator.StringToHash("MoveX");
        public static readonly int MoveYHash = Animator.StringToHash("MoveY");
        public static readonly int TurnHash = Animator.StringToHash("Turn");
        public static readonly int GroundedHash = Animator.StringToHash("Grounded");
        public static readonly int InWaterHash = Animator.StringToHash("InWater");
        public static readonly int VerticalHash = Animator.StringToHash("VerticalSpeed");
        public static readonly int JumpHash = Animator.StringToHash("Jump");
        public static readonly int InVehicleHash = Animator.StringToHash("InVehicle");
        public static readonly int PunchHash = Animator.StringToHash("Punch");
        public static readonly int ShootHash = Animator.StringToHash("Shoot");
        public static readonly int HitHash = Animator.StringToHash("Hit");
        // Phase 14 Section 2: reload gets its own upper-body state, and death is a full-body
        // bool rather than a trigger so it survives a re-Bind after a character model swap --
        // the same reasoning as InVehicle.
        public static readonly int ReloadHash = Animator.StringToHash("Reload");
        public static readonly int DeadHash = Animator.StringToHash("Dead");
        // Section 10: NPC conversation gesture.
        public static readonly int TalkHash = Animator.StringToHash("Talk");
        // Section 11: which weapon stance the held weapon uses.
        public static readonly int StanceHash = Animator.StringToHash("Stance");

        [Tooltip("Seconds to smooth the locomotion blend. Stops a twitchy stick strobing walk/run.")]
        public float SpeedDamping = 0.12f;

        [Header("Masked layers")]
        [Tooltip("Seconds to fade the upper-body and hand layers in and out.")]
        public float LayerFade = 0.12f;

        [Tooltip("Section 2.6. How long an armed character keeps the weapon up after firing or "
                 + "reloading. Holding the stance permanently is what the 9b-fix removed; "
                 + "dropping it the instant a shot ends makes a firefight look like twitching.")]
        public float AimStanceSeconds = 3f;

        Animator _animator;
        bool _hasSpeed, _hasGrounded, _hasInWater, _hasVertical, _hasJump, _hasInVehicle;
        bool _hasMoveX, _hasMoveY, _hasTurn;
        bool _hasPunch, _hasShoot, _hasHit, _hasReload, _hasDead, _hasTalk, _hasStance;

        int _upperLayer = -1;
        int _handsLayer = -1;
        int _aimLayer = -1;
        bool _armed;

        // Section 2.6. The stance is held while either is live: a countdown refreshed by combat,
        // or an explicit hold for a future aim-down-sights control.
        float _aimHold;
        bool _aimHeld;

        static readonly int RestTagHash = Animator.StringToHash("Rest");

        public Animator Animator => _animator;

        void Awake() => Bind();

        /// <summary>
        /// Re-resolves the Animator. Call after hot-swapping the character model so the
        /// driver picks up the new rig without a scene reload.
        /// </summary>
        public void Bind()
        {
            _animator = GetComponent<Animator>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();

            _hasSpeed = _hasGrounded = _hasInWater = _hasVertical = _hasJump = _hasInVehicle = false;
            _hasMoveX = _hasMoveY = _hasTurn = false;
            _hasPunch = _hasShoot = _hasHit = false;
            _hasReload = _hasDead = _hasTalk = _hasStance = false;
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            // Root motion is baked out at import; the CharacterController owns movement.
            _animator.applyRootMotion = false;

            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == SpeedHash) _hasSpeed = true;
                else if (p.nameHash == MoveXHash) _hasMoveX = true;
                else if (p.nameHash == MoveYHash) _hasMoveY = true;
                else if (p.nameHash == TurnHash) _hasTurn = true;
                else if (p.nameHash == GroundedHash) _hasGrounded = true;
                else if (p.nameHash == InWaterHash) _hasInWater = true;
                else if (p.nameHash == VerticalHash) _hasVertical = true;
                else if (p.nameHash == JumpHash) _hasJump = true;
                else if (p.nameHash == InVehicleHash) _hasInVehicle = true;
                else if (p.nameHash == PunchHash) _hasPunch = true;
                else if (p.nameHash == ReloadHash) _hasReload = true;
                else if (p.nameHash == DeadHash) _hasDead = true;
                else if (p.nameHash == TalkHash) _hasTalk = true;
                else if (p.nameHash == StanceHash) _hasStance = true;
                else if (p.nameHash == ShootHash) _hasShoot = true;
                else if (p.nameHash == HitHash) _hasHit = true;
            }

            _upperLayer = _animator.GetLayerIndex("UpperBody");
            _handsLayer = _animator.GetLayerIndex("Hands");
            _aimLayer = _animator.GetLayerIndex("AimPose");

            // Start both masked layers silent. An Override layer left at weight 1 with an empty
            // state does not contribute nothing -- it writes the humanoid zero pose over every
            // masked bone, which is what put every character in an arms-forward "zombie" stance
            // before this was fixed. Nothing may raise these except LateUpdate below.
            if (_upperLayer >= 0) _animator.SetLayerWeight(_upperLayer, 0f);
            if (_handsLayer >= 0) _animator.SetLayerWeight(_handsLayer, 0f);
            if (_aimLayer >= 0) _animator.SetLayerWeight(_aimLayer, 0f);
        }

        /// <summary>
        /// Fades the masked layers in and out.
        ///
        /// The upper body is raised only while a combat one-shot is genuinely playing, which is
        /// asked of the animator itself rather than tracked with timers: the resting state
        /// carries a "Rest" tag, so "not resting, or mid-transition" is exactly "an action is on
        /// screen". That stays correct if a clip's length or an exit time changes.
        ///
        /// LateUpdate, so this runs after everything that may have fired a trigger this frame.
        /// </summary>
        void LateUpdate()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            float step = LayerFade > 0.0001f ? Time.deltaTime / LayerFade : 1f;

            if (_upperLayer >= 0)
            {
                var current = _animator.GetCurrentAnimatorStateInfo(_upperLayer);
                // tagHash rather than IsTag(string): this runs every frame on every character.
                bool acting = current.tagHash != RestTagHash || _animator.IsInTransition(_upperLayer);
                Fade(_upperLayer, acting ? 1f : 0f, step);
            }

            if (_handsLayer >= 0)
                Fade(_handsLayer, _armed ? 1f : 0f, step);

            // Section 2.6. The aim pose is a whole layer of its own carrying one looping state,
            // so unlike the one-shot layer it can be faded to any weight at any time without
            // ever crossing an empty state. That is what keeps the blend into and out of a run
            // clean instead of popping through the humanoid zero pose.
            if (_aimLayer >= 0)
            {
                if (_aimHold > 0f) _aimHold -= Time.deltaTime;
                bool stance = _armed && (_aimHeld || _aimHold > 0f);
                Fade(_aimLayer, stance ? 1f : 0f, step);
            }
        }

        void Fade(int layer, float target, float step)
        {
            float w = _animator.GetLayerWeight(layer);
            if (Mathf.Approximately(w, target)) return;
            _animator.SetLayerWeight(layer, Mathf.MoveTowards(w, target, step));
        }

        /// <summary>
        /// Section 7. Local-space movement direction for the 2D locomotion blend, where
        /// (0,1) is straight ahead and (1,0) is a pure right strafe.
        ///
        /// Damped like Speed is. An undamped direction snaps between the eight clips as the
        /// stick crosses a diagonal, which reads worse than the sliding it replaces.
        /// </summary>
        public void SetMoveDirection(Vector2 local)
        {
            if (_animator == null) return;
            if (_hasMoveX) _animator.SetFloat(MoveXHash, local.x, SpeedDamping, Time.deltaTime);
            if (_hasMoveY) _animator.SetFloat(MoveYHash, local.y, SpeedDamping, Time.deltaTime);
        }

        /// <summary>Signed turn rate, for turn-in-place. Negative left, positive right.</summary>
        public void SetTurn(float turn)
        {
            if (_animator != null && _hasTurn)
                _animator.SetFloat(TurnHash, turn, SpeedDamping, Time.deltaTime);
        }

        public void SetLocomotion(float speed01, bool grounded, bool inWater, float verticalSpeed)
        {
            if (_animator == null) return;

            if (_hasSpeed) _animator.SetFloat(SpeedHash, speed01, SpeedDamping, Time.deltaTime);
            if (_hasGrounded) _animator.SetBool(GroundedHash, grounded);
            if (_hasInWater) _animator.SetBool(InWaterHash, inWater);
            if (_hasVertical) _animator.SetFloat(VerticalHash, verticalSpeed);
        }

        public void TriggerJump()
        {
            if (_animator != null && _hasJump) _animator.SetTrigger(JumpHash);
        }

        /// <summary>
        /// Switches the rig into the seated pose. Driven as a bool rather than a trigger so the
        /// state survives a re-Bind after a character model swap.
        /// </summary>
        public void SetInVehicle(bool seated)
        {
            if (_animator != null && _hasInVehicle) _animator.SetBool(InVehicleHash, seated);
        }

        /// <summary>Melee swing. Plays on the upper-body layer so the legs keep walking.</summary>
        public void TriggerPunch()
        {
            if (_animator != null && _hasPunch) _animator.SetTrigger(PunchHash);
        }

        /// <summary>Pistol discharge, upper body only.</summary>
        public void TriggerShoot()
        {
            RefreshAimStance();
            if (_animator != null && _hasShoot) _animator.SetTrigger(ShootHash);
        }

        /// <summary>Reload. Upper body, so the legs keep walking through it.</summary>
        public void TriggerReload()
        {
            RefreshAimStance();
            if (_animator != null && _hasReload) _animator.SetTrigger(ReloadHash);
        }

        /// <summary>
        /// Section 2.6. Puts the character into the held firing stance and restarts its timeout.
        /// Called on every shot and every reload, so a sustained firefight holds the gun up
        /// continuously and it comes down a few seconds after the shooting stops.
        /// </summary>
        public void RefreshAimStance() => _aimHold = AimStanceSeconds;

        /// <summary>
        /// Section 2.6. Holds the firing stance indefinitely, for an aim-down-sights control.
        /// Nothing calls this yet -- there is no ADS button in the mobile layout -- but the
        /// stance is driven from one place so adding one is a single call rather than a new
        /// animation path.
        /// </summary>
        public void SetAiming(bool aiming) => _aimHeld = aiming;

        /// <summary>True while the weapon is held up. For tests and for HUD feedback.</summary>
        public bool InAimStance => _armed && (_aimHeld || _aimHold > 0f);

        /// <summary>
        /// Drops the character into its death state.
        ///
        /// <b>BUG-012.</b> Killed NPCs used to carry on playing HumanM@Walk01_Forward -- verified
        /// live on a pedestrian punched to zero health, which stood there walking on the spot.
        /// The pack ships Death01-03 and nothing was wired to them. A bool rather than a
        /// trigger because death is a state the character stays in, not an event it passes
        /// through, and because a trigger would be lost by the re-Bind that follows a skin swap.
        /// </summary>
        public void SetDead(bool dead)
        {
            if (_animator != null && _hasDead) _animator.SetBool(DeadHash, dead);
        }

        /// <summary>
        /// Section 10. A conversational gesture, upper body only so the speaker keeps standing.
        ///
        /// The pack has shipped Talk01 since Phase 9b and nothing referenced it, so every
        /// conversation in the game happened between two people standing perfectly still.
        /// </summary>
        public void TriggerTalk()
        {
            if (_animator != null && _hasTalk) _animator.SetTrigger(TalkHash);
        }

        /// <summary>
        /// Section 11. Selects the carry/aim stance: 0 pistol, 1 alternate pistol, 2 rifle,
        /// 3 assault rifle, 4 bazooka.
        ///
        /// Set immediately rather than damped. A stance change happens on an equip or a switch,
        /// which the state machine already covers with an Equipping/Switching lock -- damping it
        /// would slide the arms between two carry poses while the weapon in the hand has
        /// already changed.
        /// </summary>
        public void SetStance(float stance)
        {
            if (_animator != null && _hasStance) _animator.SetFloat(StanceHash, stance);
        }

        /// <summary>Flinch on taking a hit.</summary>
        public void TriggerHit()
        {
            if (_animator != null && _hasHit) _animator.SetTrigger(HitHash);
        }

        /// <summary>
        /// Switches the idle/aim pose between empty-handed and armed.
        ///
        /// <b>There is no `Armed` animator parameter any more, on purpose.</b> The controller
        /// declared one from Phase 1 onwards and not a single state or transition ever read it,
        /// while the thing it was supposed to do -- raise the grip and the weapon stance -- was
        /// being done from here by layer weight the whole time. A parameter with a producer and
        /// no consumer is worse than no parameter: it reads as wiring, so the next person to
        /// touch this assumes setting it changes the pose, and it does not.
        ///
        /// Wiring a real transition to it was the alternative and was rejected. An `Armed`-gated
        /// state on a masked Override layer is precisely the structure that produced the
        /// zombie-arms defect in Phase 9b, and the layer-weight approach that replaced it is
        /// already correct and already verified. Removing the parameter is the smaller change
        /// and the safer one.
        /// </summary>
        public void SetArmed(bool armed) => _armed = armed;
    }
}
