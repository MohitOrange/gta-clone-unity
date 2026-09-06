using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Someone standing behind a counter. Walk up and press Interact to open their business.
    ///
    /// Disabled by <see cref="Interior.SetOccupied"/> whenever the room is empty, so a dozen
    /// shopkeepers scattered across off-map rooms cost nothing while the player is outside.
    /// </summary>
    public class InteriorNpc : MonoBehaviour
    {
        public enum Role { Shopkeeper, GarageMechanic, Concierge }

        [Header("Business")]
        public Role Job = Role.Shopkeeper;
        public Shop Shop;

        [Header("Proximity")]
        public float InteractRadius = 3f;
        [Tooltip("Turn to face the customer.")]
        public bool FacePlayer = true;
        public float TurnSpeed = 6f;

        Transform _player;
        PlayerAnimation _anim;

        void Awake() => _anim = GetComponentInChildren<PlayerAnimation>();

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _player = player.transform;
        }

        void Update()
        {
            if (_player == null) return;

            Vector3 toPlayer = _player.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            if (FacePlayer && distance < 12f && toPlayer.sqrMagnitude > 0.01f)
            {
                Quaternion look = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look,
                                                      1f - Mathf.Exp(-TurnSpeed * Time.deltaTime));
            }

            // Keep them in an idle pose rather than the bind pose.
            if (_anim != null) _anim.SetLocomotion(0f, true, false, 0f);

            if (distance > InteractRadius) return;

            var hud = MissionHud.Instance != null ? MissionHud.Instance.Hud : null;
            if (hud != null) hud.InteractTargetInRange = true;

            var hub = InputHub.Instance;
            if (hub == null) return;

            hub.ClaimInteract(this, distance, InputHub.InteractPriorityContact);
            if (!hub.ConsumeInteract(this)) return;

            Open();
        }

        void Open()
        {
            // Section 10. The shopkeeper greets you rather than staring.
            var anim = GetComponentInChildren<PlayerAnimation>();
            if (anim != null) anim.TriggerTalk();

            var ui = ShopUI.Instance;
            if (ui == null) return;

            if (ui.IsOpen) { ui.Close(); return; }

            switch (Job)
            {
                case Role.Shopkeeper:
                case Role.GarageMechanic:
                    if (Shop != null) ui.Open(Shop);
                    break;

                case Role.Concierge:
                    ui.OpenFastTravel();
                    break;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, InteractRadius);
        }
    }
}
