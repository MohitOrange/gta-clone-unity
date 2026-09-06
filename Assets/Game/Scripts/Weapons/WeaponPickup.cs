using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A weapon lying in the world that the player walks up to and takes with Interact.
    ///
    /// <b>This is what makes the player start unarmed.</b> Before Phase 11 the pistol was
    /// simply switched on in the scene assembler, so every new game began with a gun already
    /// in hand and the "armed" HUD context on the first frame. Arming is now something that
    /// happens in the world, at a place, as a result of an input. See DECISIONS.md D28.
    ///
    /// Deliberately not a trigger volume. Walking over a gun and silently acquiring it gives
    /// the player no moment of choice and no way to leave it; a proximity prompt plus an
    /// explicit press is the same interaction the doors, shops and mission givers already use,
    /// so it needs no new HUD affordance and no new button.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponPickup : MonoBehaviour
    {
        [Header("Contents")]
        [Tooltip("Weapon id in the WeaponLibrary. The socket equips this once taken.")]
        public string WeaponId = "pistol";
        [Tooltip("Rounds handed over. PlayerCombat clamps to its own MaxAmmo.")]
        public int Rounds = 24;

        [Header("Interaction")]
        public float InteractRadius = 2.6f;
        [Tooltip("Seconds before it comes back. Zero leaves it taken for good.")]
        public float RespawnSeconds = 45f;

        [Header("Presentation")]
        [Tooltip("The weapon model. Spun and bobbed so it reads as a pickup, not scenery.")]
        public Transform Visual;
        public float SpinDegreesPerSecond = 55f;
        public float BobHeight = 0.13f;
        public float BobCyclesPerSecond = 0.6f;

        Transform _player;
        PlayerCombat _combat;
        PlayerVehicleController _driving;
        float _respawnAt = -1f;
        Vector3 _visualHome;
        bool _taken;

        /// <summary>True while the weapon is on the ground and can be taken.</summary>
        public bool Available => !_taken;

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                _player = player.transform;
                _combat = player.GetComponentInChildren<PlayerCombat>();
                _driving = player.GetComponent<PlayerVehicleController>();
            }

            if (Visual != null) _visualHome = Visual.localPosition;

            // Desynchronise the bob so a row of pickups does not pulse in unison.
            _bobPhase = Random.value * Mathf.PI * 2f;
        }

        float _bobPhase;

        void Update()
        {
            if (_taken)
            {
                if (RespawnSeconds > 0f && Time.time >= _respawnAt) Restore();
                return;
            }

            Animate();

            if (_player == null || _combat == null) return;

            // Not while driving: the Interact button belongs to the car at that point, and a
            // player who brushes a kerbside pickup at 60 km/h has not chosen to pick anything up.
            if (_driving != null && _driving.IsDriving) return;

            float sqr = (_player.position - transform.position).sqrMagnitude;
            if (sqr > InteractRadius * InteractRadius) return;

            // Already carrying this and topped up: say nothing and take no input, so the
            // prompt does not sit there offering something that would do nothing.
            if (_combat.HasPistol && _combat.Ammo >= _combat.MaxAmmo) return;

            var hud = MissionHud.Instance != null ? MissionHud.Instance.Hud : null;
            if (hud != null) hud.InteractTargetInRange = true;

            var hub = InputHub.Instance;
            if (hub == null) return;

            hub.ClaimInteract(this, Mathf.Sqrt(sqr), InputHub.InteractPriorityItem);
            if (!hub.ConsumeInteract(this)) return;

            Take();
        }

        void Animate()
        {
            if (Visual == null) return;

            Visual.Rotate(Vector3.up, SpinDegreesPerSecond * Time.deltaTime, Space.Self);

            float bob = Mathf.Sin(Time.time * BobCyclesPerSecond * Mathf.PI * 2f + _bobPhase);
            Visual.localPosition = _visualHome + Vector3.up * (bob * BobHeight);
        }

        void Take()
        {
            bool wasArmed = _combat.HasPistol;

            // GivePistol both flags the weapon as owned and selects it, which is what makes
            // WeaponSocket equip the model on its next frame. Nothing here talks to the
            // socket or the animator directly.
            _combat.GivePistol(Rounds);

            MissionHud.Instance?.ShowToast(wasArmed
                ? "+" + Rounds + " rounds"
                : "Picked up the pistol");

            AudioManager.Instance?.PlayUi(Sfx.Chime);

            _taken = true;
            _respawnAt = Time.time + RespawnSeconds;
            if (Visual != null) Visual.gameObject.SetActive(false);
        }

        void Restore()
        {
            _taken = false;
            if (Visual != null)
            {
                Visual.gameObject.SetActive(true);
                Visual.localPosition = _visualHome;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, InteractRadius);
        }
    }
}
