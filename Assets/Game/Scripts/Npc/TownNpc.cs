using UnityEngine;

namespace MiniGTA
{
    /// <summary>One thing a townsperson says, and who says it.</summary>
    [System.Serializable]
    public class DialogueLine
    {
        [Tooltip("Blank means the NPC's own name.")]
        public string Speaker = "";
        [TextArea(1, 3)] public string Text = "";
    }

    /// <summary>
    /// A pedestrian you can actually talk to.
    ///
    /// Added on top of an existing <see cref="Pedestrian"/> rather than replacing it: the
    /// crowd already walks, already has a body from CharacterCatalog and already animates,
    /// and 66 of them are already placed. Converting a portion of that crowd costs a component
    /// and a few strings; spawning fifty new characters would cost fifty more skinned meshes
    /// on a renderer budget that PHASE11 already doubled.
    ///
    /// Interaction goes through the <see cref="InputHub"/> claim arbiter at the Contact tier,
    /// the same as doors, shopkeepers and mission contacts -- so adding fifty of these cannot
    /// reopen BUG-002's first-come-first-served race.
    /// </summary>
    [RequireComponent(typeof(Pedestrian))]
    public class TownNpc : MonoBehaviour
    {
        [Header("Identity")]
        public string NpcId = "npc.unnamed";
        public string DisplayName = "Passer-by";

        [Header("Dialogue")]
        [Tooltip("Two to four lines. The last one is where a mission hook is offered.")]
        public DialogueLine[] Lines = new DialogueLine[0];

        [Header("Mission hook")]
        [Tooltip("Optional. If set, finishing the dialogue offers this job.")]
        public MissionBase Mission;

        [Header("Interaction")]
        public float InteractRadius = 3.2f;

        [Tooltip("Stop walking while the player is talking to them.")]
        public bool HoldStillWhileTalking = true;

        Transform _player;
        Pedestrian _pedestrian;
        bool _talking;

        public bool IsQuestGiver => Mission != null;

        void Awake() => _pedestrian = GetComponent<Pedestrian>();

        void OnEnable() => NpcManager.Register(this);
        void OnDisable() => NpcManager.Unregister(this);

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _player = player.transform;
        }

        void Update()
        {
            if (_player == null || Lines == null || Lines.Length == 0) return;

            // Nobody is chatting on the pavement while the player is in a room off the map.
            var interiors = InteriorManager.Instance;
            if (interiors != null && interiors.IsInside) return;

            float distance = Vector3.Distance(transform.position, _player.position);
            if (distance > InteractRadius) { ReleaseHold(); return; }

            var hud = MissionHud.Instance != null ? MissionHud.Instance.Hud : null;
            if (hud != null) hud.InteractTargetInRange = true;

            var hub = InputHub.Instance;
            if (hub == null) return;

            hub.ClaimInteract(this, distance, InputHub.InteractPriorityContact);
            if (!hub.ConsumeInteract(this)) return;

            Talk();
        }

        void Talk()
        {
            var panel = DialoguePanel.Instance;
            if (panel == null) return;

            if (HoldStillWhileTalking && _pedestrian != null)
            {
                _pedestrian.enabled = false;
                _talking = true;
            }

            // Section 10. Gesture while speaking, on the masked upper body so the hold-still
            // above is unaffected -- the NPC stops walking and still moves their hands.
            var anim = GetComponentInChildren<PlayerAnimation>();
            if (anim != null) anim.TriggerTalk();

            panel.Open(this);
        }

        /// <summary>Let them walk again once the conversation ends.</summary>
        public void ReleaseHold()
        {
            if (!_talking) return;

            _talking = false;
            if (_pedestrian != null) _pedestrian.enabled = true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = IsQuestGiver ? Color.yellow : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, InteractRadius);
        }
    }
}
