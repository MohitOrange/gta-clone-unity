using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>What the player is currently doing. Drives which buttons are on screen.</summary>
    public enum PlayerContext
    {
        OnFoot,
        OnFootArmed,
        Swimming,
        Driving,
    }

    /// <summary>
    /// Owns the show/hide rules for the whole action-button cluster.
    ///
    /// Keeping the rules in one table -- rather than scattering SetActive calls through the
    /// player, vehicle and weapon code -- is what stops the HUD drifting out of sync as
    /// later phases add vehicles and weapons. New context, one new row.
    /// </summary>
    public class HudContext : MonoBehaviour
    {
        [Header("Buttons")]
        public HudButton Jump;
        public HudButton Sprint;
        public HudButton Interact;
        public HudButton Attack;
        [Tooltip("Aim-down-sights. Armed only -- an aim button with nothing in hand does "
                 + "nothing, and a dead control is worse than an absent one.")]
        public HudButton Aim;
        [Tooltip("Deliberate reload. Armed only, same reason as Aim.")]
        public HudButton Reload;
        public HudButton Horn;
        public HudButton Handbrake;
        public HudButton Gas;
        public HudButton Brake;

        [Header("Driving scheme")]
        [Tooltip("One stick drives: its Y axis is the throttle and its X the wheel, so the gas "
                 + "and reverse pedals are redundant and stay hidden. Turn this off to put the "
                 + "pedals back -- InputHub still honours them if something holds them down.")]
        public bool StickThrottle = true;

        [Header("Panels")]
        public GameObject MoveStick;

        [Header("Readouts")]
        public VehicleHud VehicleReadout;

        [Header("State")]
        [SerializeField] PlayerContext _context = PlayerContext.OnFoot;

        /// <summary>Set by the player controller when it enters/leaves water, vehicles, etc.</summary>
        public PlayerContext Context
        {
            get => _context;
            set
            {
                if (_context == value) return;
                _context = value;
                Apply();
            }
        }

        /// <summary>
        /// Interact is special: it also depends on whether anything is actually in range,
        /// so it is gated by proximity on top of the context rules.
        /// </summary>
        public bool InteractTargetInRange
        {
            get => _interactInRange;
            set { if (_interactInRange != value) { _interactInRange = value; Apply(); } }
        }
        bool _interactInRange;

        void Start() => Apply();

        void Apply()
        {
            bool onFoot = _context == PlayerContext.OnFoot || _context == PlayerContext.OnFootArmed;
            bool driving = _context == PlayerContext.Driving;
            bool swimming = _context == PlayerContext.Swimming;

            Show(Jump, onFoot);
            Show(Sprint, onFoot || swimming);

            // Interact stays available while driving -- that is how you get out.
            Show(Interact, _interactInRange || driving);

            // Always available on foot: the button is melee when unarmed and the trigger when
            // armed, so it never disappears out from under the player's thumb mid-fight.
            Show(Attack, onFoot);

            // Aim and Reload are the two controls that only mean something with a weapon in
            // hand, so they appear with one and go away with it. Attack deliberately does not
            // follow that rule -- it is the melee button when unarmed.
            bool armed = _context == PlayerContext.OnFootArmed;
            Show(Aim, armed);
            Show(Reload, armed);

            Show(Horn, driving);
            Show(Handbrake, driving);

            // The handbrake stays: it is a distinct manoeuvre, not a throttle control.
            Show(Gas, driving && !StickThrottle);
            Show(Brake, driving && !StickThrottle);

            // A run lock is meaningless behind the wheel, and carrying it through a drive would
            // have the player sprint away the moment they step out.
            if (driving && InputHub.Instance != null) InputHub.Instance.ClearSprintLock();

            if (MoveStick != null) MoveStick.SetActive(true);          // steering reuses the stick
            if (VehicleReadout != null) VehicleReadout.SetVisible(driving);
        }

        static void Show(HudButton b, bool visible)
        {
            if (b != null) b.SetVisible(visible);
        }

        readonly List<HudButton> _scratch = new List<HudButton>();

        /// <summary>Editor/debug helper: list which buttons are currently on screen.</summary>
        public string DescribeVisible()
        {
            _scratch.Clear();
            if (Jump != null) _scratch.Add(Jump);
            if (Sprint != null) _scratch.Add(Sprint);
            if (Interact != null) _scratch.Add(Interact);
            if (Attack != null) _scratch.Add(Attack);
            if (Aim != null) _scratch.Add(Aim);
            if (Reload != null) _scratch.Add(Reload);
            if (Horn != null) _scratch.Add(Horn);
            if (Handbrake != null) _scratch.Add(Handbrake);
            if (Gas != null) _scratch.Add(Gas);
            if (Brake != null) _scratch.Add(Brake);

            var names = new List<string>();
            foreach (var b in _scratch)
                if (b.Visible) names.Add(b.Action.ToString());
            return string.Join(", ", names);
        }
    }
}
