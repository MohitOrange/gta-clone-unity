using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// An NPC who offers a job. Stand near them and press Interact.
    ///
    /// Shares the Interact button with vehicle entry, so it claims priority through
    /// <see cref="ActiveNearby"/> while the player is on foot inside its radius. Without an
    /// explicit winner, standing next to both a contact and a parked car would make the button
    /// do whichever thing ran first that frame.
    /// </summary>
    public class MissionGiver : MonoBehaviour
    {
        /// <summary>The giver currently claiming the Interact button, if any.</summary>
        public static MissionGiver ActiveNearby { get; private set; }

        [Header("Job")]
        public MissionBase Mission;

        [Header("Proximity")]
        public float InteractRadius = 4.5f;
        [Tooltip("Marker stays visible from this far away.")]
        public float MarkerVisibleRange = 400f;

        [Header("Visuals")]
        [Tooltip("Beacon that spins above the contact.")]
        public Transform Beacon;
        public float BeaconSpinSpeed = 60f;
        public float BeaconBobHeight = 0.25f;
        public float BeaconBobSpeed = 2f;

        public Renderer BeaconRenderer;
        public Color AvailableColour = new Color(1f, 0.82f, 0.25f);
        public Color LockedColour = new Color(0.45f, 0.47f, 0.5f);
        public Color ActiveColour = new Color(0.35f, 0.85f, 1f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        Transform _player;
        MaterialPropertyBlock _mpb;
        Vector3 _beaconRest;
        bool _inRange;

        /// <summary>True when this contact has a job the player could take right now.</summary>
        public bool IsAvailable
        {
            get
            {
                var manager = MissionManager.Instance;
                return manager != null && Mission != null && manager.CanAccept(Mission);
            }
        }

        /// <summary>Short reason the job cannot be taken, or empty when it can.</summary>
        public string BlockedReason
        {
            get
            {
                var manager = MissionManager.Instance;
                if (manager == null || Mission == null) return "No job";
                manager.CanAccept(Mission, out string reason);
                return reason;
            }
        }

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _player = player.transform;

            if (Beacon != null) _beaconRest = Beacon.localPosition;

            MissionManager.Instance?.Register(Mission);
        }

        void OnDisable()
        {
            if (ActiveNearby == this) ActiveNearby = null;
        }

        void Update()
        {
            if (_player == null) return;

            float distance = Vector3.Distance(transform.position, _player.position);

            AnimateBeacon(distance);

            var driving = _player.GetComponent<PlayerVehicleController>();
            bool onFoot = driving == null || !driving.IsDriving;

            bool nowInRange = onFoot && distance <= InteractRadius;

            if (nowInRange != _inRange)
            {
                _inRange = nowInRange;

                if (_inRange) ActiveNearby = this;
                else if (ActiveNearby == this) ActiveNearby = null;
            }

            if (!_inRange) return;

            // Keep the claim fresh in case two givers overlap.
            ActiveNearby = this;

            var hud = FindHud();
            if (hud != null) hud.InteractTargetInRange = true;

            var hub = InputHub.Instance;
            if (hub == null) return;

            hub.ClaimInteract(this, distance, InputHub.InteractPriorityContact);
            if (hub.ConsumeInteract(this)) TryAccept();
        }

        void TryAccept()
        {
            var manager = MissionManager.Instance;
            if (manager == null || Mission == null) return;

            if (!manager.CanAccept(Mission, out string reason))
            {
                MissionHud.Instance?.ShowToast(reason);
                return;
            }

            manager.Accept(Mission);
        }

        HudContext FindHud() => MissionHud.Instance != null ? MissionHud.Instance.Hud : null;

        void AnimateBeacon(float distance)
        {
            if (Beacon == null) return;

            bool visible = distance <= MarkerVisibleRange;
            Beacon.gameObject.SetActive(visible);
            if (!visible) return;

            Beacon.Rotate(Vector3.up, BeaconSpinSpeed * Time.deltaTime, Space.Self);
            Beacon.localPosition = _beaconRest
                + Vector3.up * (Mathf.Sin(Time.time * BeaconBobSpeed) * BeaconBobHeight);

            TintBeacon();
        }

        void TintBeacon()
        {
            if (BeaconRenderer == null) return;

            Color colour = LockedColour;
            var manager = MissionManager.Instance;

            if (manager != null && Mission != null)
            {
                if (manager.Active == Mission) colour = ActiveColour;
                else if (manager.CanAccept(Mission)) colour = AvailableColour;
            }

            _mpb ??= new MaterialPropertyBlock();
            BeaconRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, colour);
            _mpb.SetColor(EmissionColorId, colour * 2.6f);
            BeaconRenderer.SetPropertyBlock(_mpb);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, InteractRadius);
        }
    }
}
