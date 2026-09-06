using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>What the holder is doing with the weapon right now.</summary>
    public enum WeaponState
    {
        /// <summary>Ready. The only state from which an action may be started.</summary>
        Idle,
        /// <summary>Bringing a weapon up after a pickup or a switch.</summary>
        Equipping,
        /// <summary>Mid-shot. Covers the fire cooldown.</summary>
        Firing,
        /// <summary>Mid-reload.</summary>
        Reloading,
        /// <summary>Putting one weapon away before the next comes up.</summary>
        Switching,
        /// <summary>Dead, in a vehicle, or otherwise not allowed to use a weapon at all.</summary>
        Locked,
    }

    /// <summary>What happened when something walked into a pickup.</summary>
    public enum PickupOutcome
    {
        Rejected,
        /// <summary>Went into a free slot.</summary>
        Added,
        /// <summary>Already held this type; the rounds went to reserve (rule 2.3).</summary>
        AmmoMerged,
        /// <summary>Slots were full; the held weapon was dropped to make room (rule 2.3).</summary>
        SwappedAndDropped,
    }

    /// <summary>One carried weapon: which it is, and its ammunition.</summary>
    [System.Serializable]
    public class WeaponSlot
    {
        public string Id;
        public int Magazine;
        public int Reserve;

        public WeaponSlot(string id, int magazine, int reserve)
        {
            Id = id; Magazine = magazine; Reserve = reserve;
        }
    }

    /// <summary>
    /// The whole weapon mechanic: what is carried, how much ammunition it has, and which of
    /// fire / reload / switch / equip is legal right now.
    ///
    /// <b>One component, used by the player and by every armed NPC.</b> Section 2.9 of the build
    /// order requires exactly that -- not a full version for the player and a simplified one for
    /// enemies -- so that an NPC's weapon behaves the way the player's does and there is one
    /// place to fix when it does not. The holder (a player input script, or an NPC brain) only
    /// ever calls <see cref="TryFire"/>, <see cref="TryReload"/>, <see cref="TrySwitch"/> and
    /// <see cref="TryPickUp"/>; it never sets state itself.
    ///
    /// <b>Why a state machine rather than booleans.</b> Section 2.8 asks for the locking rules --
    /// no firing while reloading, no reloading while switching, no switching mid-shot -- to be
    /// enforced in one place instead of by flags spread across scripts. Here every action starts
    /// with the same question, <see cref="CanAct"/>, and every timed state returns to
    /// <see cref="WeaponState.Idle"/> through the same timer. Adding a state means adding it to
    /// one enum and one switch, and the locking comes free.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponController : MonoBehaviour
    {
        [Header("Carry")]
        [Tooltip("How many weapons can be carried at once. Rule 2.3 fires when this is full.")]
        public int MaxSlots = 2;

        [Header("Refs")]
        public WeaponLibrary Library;
        public WeaponSocket Socket;
        [Tooltip("Optional. Drives the equip/fire/reload upper-body animation.")]
        public PlayerAnimation Animation;

        [Header("Debug")]
        [SerializeField] List<WeaponSlot> _slots = new List<WeaponSlot>();
        [SerializeField] int _active = -1;

        WeaponState _state = WeaponState.Idle;
        float _stateTimer;
        int _pendingSlot = -1;

        // ------------------------------------------------------------------ reading

        public WeaponState State => _state;
        public bool HasWeapon => _active >= 0 && _active < _slots.Count;
        public WeaponSlot Active => HasWeapon ? _slots[_active] : null;
        public int SlotCount => _slots.Count;
        public IReadOnlyList<WeaponSlot> Slots => _slots;

        public string ActiveId => HasWeapon ? _slots[_active].Id : "";
        public int Magazine => HasWeapon ? _slots[_active].Magazine : 0;
        public int Reserve => HasWeapon ? _slots[_active].Reserve : 0;

        public WeaponDefinition ActiveDefinition =>
            Library != null && HasWeapon ? Library.Find(_slots[_active].Id) : null;

        /// <summary>Raised whenever the carried set or the ammunition changes, for the HUD.</summary>
        public event System.Action Changed;

        /// <summary>Raised on a shot actually being taken, with the weapon that fired.</summary>
        public event System.Action<WeaponDefinition> Fired;

        // ------------------------------------------------------------------ ticking

        void Update()
        {
            if (_stateTimer > 0f)
            {
                _stateTimer -= Time.deltaTime;
                if (_stateTimer > 0f) return;
                _stateTimer = 0f;
                FinishState();
            }
        }

        /// <summary>
        /// The single gate every action passes through.
        ///
        /// <see cref="WeaponState.Locked"/> is deliberately not time-based: it is held by the
        /// holder (dead, driving) and cleared by the holder, so a dead body cannot finish a
        /// reload it started.
        /// </summary>
        bool CanAct => _state == WeaponState.Idle;

        void Enter(WeaponState next, float seconds)
        {
            _state = next;
            _stateTimer = Mathf.Max(0f, seconds);
            if (_stateTimer <= 0f) FinishState();
        }

        void FinishState()
        {
            // Switching is the only state with work to do on the way out: the new weapon is
            // not put in the hand until the old one has been put away, which is what stops a
            // switch from being visually instant.
            if (_state == WeaponState.Switching && _pendingSlot >= 0)
            {
                _active = _pendingSlot;
                _pendingSlot = -1;
                ApplyToSocket();
                Enter(WeaponState.Equipping, EquipTime());
                return;
            }

            _state = WeaponState.Locked == _state ? WeaponState.Locked : WeaponState.Idle;
            Changed?.Invoke();
        }

        float EquipTime()
        {
            var def = ActiveDefinition;
            return def != null ? def.EquipSeconds : 0.45f;
        }

        // ------------------------------------------------------------------ locking

        /// <summary>
        /// Held by the owner while a weapon must not be usable at all -- dead, or driving.
        ///
        /// Cancels whatever was in progress rather than letting it complete, so a player who is
        /// shot mid-reload does not finish the reload after dying.
        /// </summary>
        public void SetLocked(bool locked)
        {
            if (locked)
            {
                _state = WeaponState.Locked;
                _stateTimer = 0f;
                _pendingSlot = -1;
            }
            else if (_state == WeaponState.Locked)
            {
                _state = WeaponState.Idle;
            }
        }

        // ------------------------------------------------------------------ actions

        /// <summary>
        /// Fires if the weapon is ready and loaded. Returns false and does nothing otherwise --
        /// callers do not need to know why, only whether a shot happened.
        /// </summary>
        public bool TryFire()
        {
            if (!CanAct || !HasWeapon) return false;

            var def = ActiveDefinition;
            if (def == null) return false;

            var slot = _slots[_active];
            if (slot.Magazine <= 0) return false;

            slot.Magazine--;
            if (Animation != null) Animation.TriggerShoot();

            Fired?.Invoke(def);
            Changed?.Invoke();

            Enter(WeaponState.Firing, def.FireCooldown);
            return true;
        }

        /// <summary>Reloads from reserve. No-op on a full magazine or an empty reserve.</summary>
        public bool TryReload()
        {
            if (!CanAct || !HasWeapon) return false;

            var def = ActiveDefinition;
            if (def == null) return false;

            var slot = _slots[_active];
            if (slot.Reserve <= 0 || slot.Magazine >= def.MagazineSize) return false;

            int wanted = def.MagazineSize - slot.Magazine;
            int taken = Mathf.Min(wanted, slot.Reserve);
            slot.Magazine += taken;
            slot.Reserve -= taken;

            if (Animation != null) Animation.TriggerReload();

            Changed?.Invoke();
            Enter(WeaponState.Reloading, def.ReloadSeconds);
            return true;
        }

        /// <summary>Switches to another carried weapon. Ignored mid-anything.</summary>
        public bool TrySwitch(int slot)
        {
            if (!CanAct) return false;
            if (slot < 0 || slot >= _slots.Count || slot == _active) return false;

            _pendingSlot = slot;
            var def = ActiveDefinition;
            Enter(WeaponState.Switching, def != null ? def.EquipSeconds : 0.45f);
            return true;
        }

        /// <summary>Cycles to the next carried weapon, for a one-button HUD.</summary>
        public bool TryCycle()
        {
            if (_slots.Count < 2) return false;
            return TrySwitch((_active + 1) % _slots.Count);
        }

        // ------------------------------------------------------------------ pickup

        /// <summary>
        /// Takes a weapon off the ground, applying the rules in build-order item 2.3.
        ///
        /// The three cases, in the order they are tested:
        ///   1. already carrying this type  -> the rounds go to that slot's reserve and no
        ///      second slot is created. Picking up a second pistol should not give you two
        ///      pistol slots.
        ///   2. a free slot                 -> it goes in and becomes active.
        ///   3. no free slot                -> the *active* weapon is dropped at the pickup's
        ///      own position and the new one takes its slot. The brief is explicit that a full
        ///      inventory must not block the pickup and must not destroy the old weapon.
        ///
        /// <paramref name="dropAt"/> is where a displaced weapon is left. Passing the pickup's
        /// own transform is what makes the swap read as an exchange on the ground.
        /// </summary>
        public PickupOutcome TryPickUp(string weaponId, int rounds, Vector3 dropAt,
                                       out string droppedId)
        {
            droppedId = "";
            if (string.IsNullOrEmpty(weaponId)) return PickupOutcome.Rejected;
            if (Library != null && Library.Find(weaponId) == null) return PickupOutcome.Rejected;

            // 1. Same type already carried: merge into reserve, capped.
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Id != weaponId) continue;

                var d = Library != null ? Library.Find(weaponId) : null;
                int cap = d != null ? d.ReserveCapacity : 96;
                _slots[i].Reserve = Mathf.Min(cap, _slots[i].Reserve + rounds);
                Changed?.Invoke();
                return PickupOutcome.AmmoMerged;
            }

            var def = Library != null ? Library.Find(weaponId) : null;
            int mag = def != null ? Mathf.Min(def.MagazineSize, rounds) : rounds;
            int spare = Mathf.Max(0, rounds - mag);

            // 2. Room to carry it.
            if (_slots.Count < MaxSlots)
            {
                _slots.Add(new WeaponSlot(weaponId, mag, spare));
                _active = _slots.Count - 1;
                ApplyToSocket();
                Enter(WeaponState.Equipping, EquipTime());
                Changed?.Invoke();
                return PickupOutcome.Added;
            }

            // 3. Full: drop what is in hand, take the new one into that slot.
            int target = Mathf.Clamp(_active, 0, _slots.Count - 1);
            droppedId = _slots[target].Id;
            int droppedRounds = _slots[target].Magazine + _slots[target].Reserve;

            SpawnDroppedPickup(droppedId, droppedRounds, dropAt);

            _slots[target] = new WeaponSlot(weaponId, mag, spare);
            _active = target;
            ApplyToSocket();
            Enter(WeaponState.Equipping, EquipTime());
            Changed?.Invoke();
            return PickupOutcome.SwappedAndDropped;
        }

        /// <summary>
        /// Leaves a displaced weapon in the world as a fresh pickup.
        ///
        /// Built from the same <see cref="WeaponPickup"/> the level uses rather than a bespoke
        /// dropped-weapon object, so a weapon swapped out on the ground behaves exactly like one
        /// that was always there -- including being picked back up with its ammunition intact,
        /// which is what item 2.10 tests for.
        /// </summary>
        void SpawnDroppedPickup(string id, int rounds, Vector3 where)
        {
            if (string.IsNullOrEmpty(id)) return;

            var go = new GameObject("WeaponPickup_" + id + "_dropped");
            go.transform.position = where;

            var pickup = go.AddComponent<WeaponPickup>();
            pickup.WeaponId = id;
            pickup.Rounds = Mathf.Max(1, rounds);
            // A dropped weapon is not a level fixture, so it does not come back on its own.
            pickup.RespawnSeconds = 0f;

            if (Library == null) return;
            var def = Library.Find(id);
            if (def == null || def.Prefab == null) return;

            var visual = Instantiate(def.Prefab, go.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * def.Scale;
            foreach (var col in visual.GetComponentsInChildren<Collider>()) col.enabled = false;
            pickup.Visual = visual.transform;
        }

        // ------------------------------------------------------------------ plumbing

        /// <summary>Puts the active weapon in the hand, or empties it.</summary>
        void ApplyToSocket()
        {
            // Section 11. The stance goes with the weapon: a rifle should be carried like a
            // rifle, not held out one-handed because every weapon reused the pistol clip.
            // Pushed here rather than on a timer so it changes at the same moment the mesh in
            // the hand does.
            if (Animation != null)
            {
                var def = ActiveDefinition;
                Animation.SetStance(def != null ? def.Stance : 0f);
            }

            if (Socket == null) return;

            if (!HasWeapon) { Socket.Holster(); return; }
            Socket.Equip(_slots[_active].Id);
        }

        /// <summary>Seeds a starting loadout. Used by the builders and by cheat codes.</summary>
        public void GiveWeapon(string id, int rounds)
        {
            TryPickUp(id, rounds, transform.position, out _);
        }

        /// <summary>Tops the active weapon's reserve up. Used by the infinite-ammo cheat.</summary>
        public void RefillReserve()
        {
            if (!HasWeapon) return;
            var def = ActiveDefinition;
            _slots[_active].Reserve = def != null ? def.ReserveCapacity : 96;
            Changed?.Invoke();
        }
    }
}
