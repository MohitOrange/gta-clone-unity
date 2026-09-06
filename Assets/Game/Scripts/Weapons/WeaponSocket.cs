using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Attaches a weapon prefab to a bone on a humanoid rig, and swaps it on request.
    ///
    /// Deliberately generic: it knows about a socket, a library and an id, and nothing about
    /// pistols. Any prefab listed in the <see cref="WeaponLibrary"/> attaches through the same
    /// code path, so adding the rifle later is a data change, not a code change.
    ///
    /// The socket is resolved from the Animator's humanoid avatar rather than by searching for
    /// a bone by name. Bone naming differs between rigs -- the Mixamo characters use
    /// "mixamorig:RightHand", the Apocalyptic character's Blender metarig uses "hand.R" -- but
    /// <c>GetBoneTransform(HumanBodyBones.RightHand)</c> is the same call on both, which is the
    /// whole point of having gone through a humanoid avatar in Phase 1.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class WeaponSocket : MonoBehaviour
    {
        [Header("Socket")]
        public HumanBodyBones AttachBone = HumanBodyBones.RightHand;
        [Tooltip("Offset of the grip from the bone origin, in bone space.")]
        public Vector3 SocketPosition = Vector3.zero;
        public Vector3 SocketEuler = Vector3.zero;
        [Tooltip("Set explicitly to skip humanoid resolution entirely.")]
        public Transform ExplicitSocket;

        [Header("Loadout")]
        public WeaponLibrary Library;
        [Tooltip("Equipped on Start. Empty means start empty-handed.")]
        public string StartingWeapon = "";

        [Header("Automatic")]
        [Tooltip("Follow PlayerCombat: show the weapon while armed, hide it while not.")]
        public bool FollowCombatState = true;
        [Tooltip("Weapon shown when PlayerCombat reports armed.")]
        public string ArmedWeapon = "pistol";

        Transform _socket;
        GameObject _instance;
        string _equipped = "";
        PlayerCombat _combat;
        PlayerAnimation _anim;
        bool _lastArmed;
        bool _socketFailed;

        /// <summary>Barrel tip of the equipped weapon, or null when empty-handed.</summary>
        public Transform Muzzle { get; private set; }

        /// <summary>Id of the equipped weapon, or "" when empty-handed.</summary>
        public string Equipped => _equipped;

        void Awake()
        {
            _combat = GetComponentInParent<PlayerCombat>();
            if (_combat == null) _combat = GetComponent<PlayerCombat>();
            _anim = GetComponentInChildren<PlayerAnimation>() ?? GetComponent<PlayerAnimation>();
        }

        void Start()
        {
            if (!string.IsNullOrEmpty(StartingWeapon))
            {
                Equip(StartingWeapon);
                // A character that always carries (an officer) is always armed as far as the
                // animator is concerned; it has no PlayerCombat to follow.
                if (!FollowCombatState) PushArmedPose(true);
            }

            if (FollowCombatState && _combat != null)
            {
                _lastArmed = _combat.IsArmed;
                if (_lastArmed) Equip(ArmedWeapon); else Holster();
                PushArmedPose(_lastArmed);
            }
        }

        void Update()
        {
            if (!FollowCombatState || _combat == null) return;

            bool armed = _combat.IsArmed;
            if (armed == _lastArmed) return;

            _lastArmed = armed;
            if (armed) Equip(ArmedWeapon); else Holster();
            PushArmedPose(armed);
        }

        /// <summary>
        /// Tells the animator whether a weapon is in the hand.
        ///
        /// The `Armed` bool was declared by the Phase 1 controller and then never set by
        /// anything, which is why PHASE9 recorded the pistol sitting in a relaxed open hand as
        /// a rough edge. The socket already knows the answer -- it is the thing that put the
        /// weapon there -- so it is the right place to say so. Phase 9b gives the bool a state
        /// to drive: the upper-body layer's armed hold pose.
        /// </summary>
        void PushArmedPose(bool armed)
        {
            if (_anim == null) return;
            _anim.SetArmed(armed);
        }

        // ---------------------------------------------------------------- socket

        /// <summary>
        /// Finds (once) or creates the child transform weapons hang from.
        ///
        /// The socket is a child of the hand bone, not the hand bone itself, so the grip offset
        /// can be authored without disturbing the skeleton the animator is writing to every
        /// frame. Writing the offset onto the bone would be overwritten by the next pose.
        /// </summary>
        public Transform ResolveSocket()
        {
            if (_socket != null) { ApplyOffsets(_socket); return _socket; }
            if (ExplicitSocket != null) { _socket = ExplicitSocket; ApplyOffsets(_socket); return _socket; }
            if (_socketFailed) return null;

            var animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                _socketFailed = true;
                Debug.LogWarning("[Weapon] " + name + " has no humanoid Animator; no weapon socket. "
                                 + "Set ExplicitSocket to attach anyway.", this);
                return null;
            }

            var bone = animator.GetBoneTransform(AttachBone);
            if (bone == null)
            {
                _socketFailed = true;
                Debug.LogWarning("[Weapon] " + name + "'s avatar has no " + AttachBone + " bone.", this);
                return null;
            }

            // Reuse the socket across a re-Equip, and across a re-created component.
            var existing = bone.Find("WeaponSocket");
            if (existing == null)
            {
                existing = new GameObject("WeaponSocket").transform;
                existing.SetParent(bone, false);
            }

            _socket = existing;
            ApplyOffsets(_socket);
            return _socket;
        }

        /// <summary>
        /// Places the socket on the bone, and cancels the bone's inherited scale.
        ///
        /// That last part is not optional. Rigs exported from Blender routinely carry a 100x
        /// scale on the armature with 1.0 on every bone, which the SkinnedMeshRenderer hides --
        /// it bakes the bind pose, so the character looks right. Anything *parented to a bone*
        /// does not get that treatment and inherits the lot: the first build of this put a
        /// 19-metre pistol in the player's hand. Dividing out the bone's lossy scale gives the
        /// socket a world scale of 1, so a weapon's Scale in the library means metres, on any
        /// rig, whatever the exporter did.
        /// </summary>
        void ApplyOffsets(Transform socket)
        {
            socket.localPosition = SocketPosition;
            socket.localRotation = Quaternion.Euler(SocketEuler);

            Vector3 inherited = socket.parent != null ? socket.parent.lossyScale : Vector3.one;
            socket.localScale = new Vector3(
                Mathf.Abs(inherited.x) < 1e-5f ? 1f : 1f / inherited.x,
                Mathf.Abs(inherited.y) < 1e-5f ? 1f : 1f / inherited.y,
                Mathf.Abs(inherited.z) < 1e-5f ? 1f : 1f / inherited.z);
        }

        // --------------------------------------------------------------- loadout

        /// <summary>Attaches the weapon with this id. Returns false if it could not.</summary>
        public bool Equip(string id)
        {
            if (_equipped == id && _instance != null) return true;

            var def = Library != null ? Library.Find(id) : null;
            if (def == null || def.Prefab == null)
            {
                Debug.LogWarning("[Weapon] No definition or prefab for '" + id + "' on " + name, this);
                return false;
            }

            var socket = ResolveSocket();
            if (socket == null) return false;

            Holster();

            _instance = Instantiate(def.Prefab, socket);
            _instance.name = "Weapon_" + def.Id;
            _instance.transform.localPosition = def.LocalPosition;
            _instance.transform.localRotation = Quaternion.Euler(def.LocalEuler);
            _instance.transform.localScale = Vector3.one * def.Scale;

            // A weapon in a hand must not collide with the world -- the hand is already inside
            // the character controller, and a live collider there fights every wall.
            foreach (var col in _instance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            // Held geometry moves every frame, so it can never be batched as static, and a
            // shadow from a 20 cm prop is not worth a caster.
            foreach (var r in _instance.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (def.HasMuzzle)
            {
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(_instance.transform, false);
                muzzle.localPosition = def.MuzzleLocal;
                Muzzle = muzzle;
            }
            else
            {
                Muzzle = null;
            }

            _equipped = def.Id;
            if (_combat != null) _combat.MuzzlePoint = Muzzle;
            return true;
        }

        /// <summary>Removes whatever is in the hand.</summary>
        public void Holster()
        {
            if (_instance != null)
            {
                if (Application.isPlaying) Destroy(_instance);
                else DestroyImmediate(_instance);
            }

            _instance = null;
            Muzzle = null;
            _equipped = "";
            if (_combat != null) _combat.MuzzlePoint = null;
        }
    }
}

