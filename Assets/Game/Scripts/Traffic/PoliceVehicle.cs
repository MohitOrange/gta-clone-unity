using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Marks a vehicle as police, so it can witness the player's offences.
    ///
    /// Must live in a file of its own: Unity only creates the MonoScript binding a
    /// MonoBehaviour needs when the file name matches the class name, and without that
    /// binding AddComponent appears to succeed but the component never survives being
    /// serialised into a prefab.
    /// </summary>
    public class PoliceVehicle : MonoBehaviour
    {
        static readonly List<PoliceVehicle> All = new List<PoliceVehicle>();

        public static int Count => All.Count;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Nearest police unit within range, or null. Range check only -- no LOS yet.</summary>
        public static PoliceVehicle NearestWitness(Vector3 position, float radius)
        {
            float bestSqr = radius * radius;
            PoliceVehicle best = null;

            foreach (var unit in All)
            {
                if (unit == null) continue;
                float d = (unit.transform.position - position).sqrMagnitude;
                if (d <= bestSqr) { bestSqr = d; best = unit; }
            }
            return best;
        }
    }
}
