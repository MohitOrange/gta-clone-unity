using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The player's own map marker.
    ///
    /// <b>Deliberately not part of the mission system.</b> A waypoint here is navigation
    /// convenience and nothing else: it does not start, advance or complete anything, it is not
    /// saved, and a mission objective always outranks it on the screen. Keeping it out of
    /// MissionManager means a player dropping a pin on a shop cannot interfere with a job they
    /// are running, which is the failure mode that makes this kind of feature annoying.
    ///
    /// Static rather than a scene component because both ends of it -- the map screen that sets
    /// it and the on-screen arrow that reads it -- are rebuilt independently by the interface
    /// builder, and a serialised reference between them is one more thing that can come back
    /// null after a rebuild. There is exactly one player, so there is exactly one waypoint.
    /// </summary>
    public static class MapWaypoint
    {
        /// <summary>Where the player has asked to be pointed, in world space.</summary>
        public static Vector3 Point { get; private set; }

        /// <summary>Whether a waypoint is currently set.</summary>
        public static bool Active { get; private set; }

        /// <summary>
        /// What the waypoint was placed on, so the map can show which marker is selected and
        /// so tapping the same marker again can toggle it off.
        /// </summary>
        public static Object Owner { get; private set; }

        /// <summary>Raised whenever the waypoint is set, moved or cleared.</summary>
        public static event System.Action Changed;

        /// <summary>
        /// Points the player at a place.
        ///
        /// Setting a new one replaces whatever was there, with no need to clear first -- the
        /// player taps a different icon and the arrow simply swings round.
        /// </summary>
        public static void Set(Vector3 point, Object owner = null)
        {
            Point = point;
            Owner = owner;
            Active = true;
            Changed?.Invoke();
        }

        /// <summary>Removes the waypoint without selecting another.</summary>
        public static void Clear()
        {
            Active = false;
            Owner = null;
            Changed?.Invoke();
        }

        /// <summary>
        /// Sets the waypoint, or clears it if this is already the selected marker.
        /// Tapping the same icon twice is the quickest way to undo a pin.
        /// </summary>
        public static void Toggle(Vector3 point, Object owner)
        {
            if (Active && owner != null && Owner == owner) Clear();
            else Set(point, owner);
        }

        /// <summary>
        /// Play mode only. Statics outlive a play session when domain reloading is off, so a
        /// waypoint from the previous run would otherwise still be set on the next one.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            Active = false;
            Owner = null;
            Point = Vector3.zero;
            Changed = null;
        }
    }
}
