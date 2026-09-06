using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A walk-in room, built off-map and teleported to rather than streamed as a scene.
    ///
    /// Scene-swapping would mean unloading the city -- losing the traffic, the pursuit, and any
    /// mission in progress -- and paying a load screen every time the player opens a shop door.
    /// Rooms parked in dead space above the map cost nothing when nobody is in them (they are
    /// simply out of every camera's frustum), keep the whole game in one scene, and let a
    /// wanted level survive the player ducking indoors.
    /// </summary>
    public class Interior : MonoBehaviour
    {
        [Header("Identity")]
        public string InteriorId = "interior.unnamed";
        public string DisplayName = "Interior";

        [Header("Points")]
        [Tooltip("Where the player appears on entering.")]
        public Transform EntryPoint;
        [Tooltip("Trigger the player walks into to leave.")]
        public Transform ExitTrigger;

        [Header("Lighting")]
        [Tooltip("Ambient colour while inside. The sun cannot reach here, so the room supplies " +
                 "its own light rather than paying for real-time point lights on mobile.")]
        public Color AmbientColour = new Color(0.42f, 0.40f, 0.36f);
        [Tooltip("Fog is disabled indoors; a small room inside a fogged world looks hazy.")]
        public bool DisableFogInside = true;

        [Header("Occupants")]
        public Shop Shop;
        public bool IsGarage;

        [Tooltip("Walking in here writes a save. True for the safehouse.")]
        public bool SavesOnEntry;

        /// <summary>Where in the world the player came from, set on entry.</summary>
        public Vector3 ReturnPosition { get; set; }
        public Quaternion ReturnRotation { get; set; }

        public bool IsOccupied { get; private set; }

        public void SetOccupied(bool occupied)
        {
            IsOccupied = occupied;

            // The room's contents only need to tick while somebody is standing in it.
            foreach (var npc in GetComponentsInChildren<InteriorNpc>(true))
                npc.enabled = occupied;

            // Re-arm the way out on every entry. The player arrives near the door, so without
            // a fresh grace period they trip the exit on their first frame inside.
            if (!occupied || ExitTrigger == null) return;

            var exit = ExitTrigger.GetComponent<InteriorExit>();
            if (exit != null) exit.Arm();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            if (EntryPoint != null) Gizmos.DrawWireSphere(EntryPoint.position, 0.6f);

            Gizmos.color = Color.green;
            if (ExitTrigger != null) Gizmos.DrawWireCube(ExitTrigger.position, Vector3.one * 2f);
        }
    }
}
