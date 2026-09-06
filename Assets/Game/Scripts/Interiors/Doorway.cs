using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A door on a building. Stand in front of it on foot and press Interact to go inside.
    ///
    /// Claims the Interact button the same way <see cref="MissionGiver"/> does, so a doorway
    /// next to a parked car never gets you into the car instead of the shop.
    /// </summary>
    public class Doorway : MonoBehaviour
    {
        /// <summary>The doorway currently claiming the Interact button, if any.</summary>
        public static Doorway ActiveNearby { get; private set; }

        [Header("Destination")]
        public Interior Target;

        [Header("Proximity")]
        public float InteractRadius = 3.2f;

        [Header("Sign")]
        [Tooltip("Lit panel above the door.")]
        public Renderer SignRenderer;
        public Color OpenColour = new Color(0.35f, 0.9f, 1f);
        public Color ClosedColour = new Color(0.4f, 0.42f, 0.45f);

        [Tooltip("Property this door belongs to, if any. A locked property cannot be entered.")]
        public Property OwningProperty;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        Transform _player;
        MaterialPropertyBlock _mpb;
        bool _inRange;

        /// <summary>False when this door belongs to a property the player has not bought.</summary>
        public bool IsOpen =>
            OwningProperty == null || OwningProperty.IsOwned;

        /// <summary>Diagnostics for the inspector and for tests.</summary>
        public bool HasPlayerReference => _player != null;
        public bool PlayerInRange => _inRange;

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _player = player.transform;

            TintSign();
        }

        void OnDisable()
        {
            if (ActiveNearby == this) ActiveNearby = null;
        }

        void Update()
        {
            if (_player == null || Target == null) return;

            // Nobody is standing at a door while they are inside a room.
            var interiors = InteriorManager.Instance;
            if (interiors != null && interiors.IsInside)
            {
                if (ActiveNearby == this) ActiveNearby = null;
                _inRange = false;
                return;
            }

            TintSign();

            var driving = _player.GetComponent<PlayerVehicleController>();
            bool onFoot = driving == null || !driving.IsDriving;

            float distance = Vector3.Distance(transform.position, _player.position);
            bool nowInRange = onFoot && distance <= InteractRadius;

            if (nowInRange != _inRange)
            {
                _inRange = nowInRange;
                if (_inRange) ActiveNearby = this;
                else if (ActiveNearby == this) ActiveNearby = null;
            }

            if (!_inRange) return;
            ActiveNearby = this;

            var hud = MissionHud.Instance != null ? MissionHud.Instance.Hud : null;
            if (hud != null) hud.InteractTargetInRange = true;

            var hub = InputHub.Instance;
            if (hub == null) return;

            hub.ClaimInteract(this, distance, InputHub.InteractPriorityContact);
            if (!hub.ConsumeInteract(this)) return;

            if (!IsOpen)
            {
                MissionHud.Instance?.ShowToast(
                    OwningProperty != null ? "Locked - " + OwningProperty.PurchasePrompt : "Locked");
                return;
            }

            InteriorManager.Instance?.Enter(Target, transform.position + transform.forward * 2f,
                                            transform.rotation);
        }

        void TintSign()
        {
            if (SignRenderer == null) return;

            Color colour = IsOpen ? OpenColour : ClosedColour;

            _mpb ??= new MaterialPropertyBlock();
            SignRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, colour);
            _mpb.SetColor(EmissionColorId, colour * (IsOpen ? 2.2f : 0.4f));
            SignRenderer.SetPropertyBlock(_mpb);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, InteractRadius);
        }
    }
}
