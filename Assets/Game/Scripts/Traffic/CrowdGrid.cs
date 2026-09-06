using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A uniform spatial hash over the pedestrian population, so "who is near me" stops being
    /// a scan of everybody.
    ///
    /// <b>This is the single thing that decides whether the crowd can grow.</b> Every
    /// neighbour query in the game used to walk the whole registry, and
    /// <see cref="Pedestrian"/>'s separation steering does one of those per pedestrian per
    /// frame -- which is O(n²). At the 66 pedestrians the scene shipped with that is 4,356
    /// distance checks a frame and nobody notices. At the 300-1000 Section 3 asks for it is
    /// 90,000 to 1,000,000 a frame, and no amount of animation LOD buys that back, because
    /// the cost is not in the rendering.
    ///
    /// With a grid the same query touches only the cells that can possibly contain a hit, so
    /// the cost follows local crowd density rather than total population. A thousand
    /// pedestrians spread over a city cost each other almost nothing; a thousand standing in
    /// one square still cost, which is correct -- that genuinely is a lot of neighbours.
    ///
    /// <b>Rebuilt lazily, once per frame, on first use.</b> Deliberately not driven by a
    /// MonoBehaviour with an execution order: that would be a fourth thing that has to exist
    /// in the scene and be ordered correctly before the crowd works, and this project has
    /// already lost a phase to two silent "the prerequisite was missing" bugs (BUG-018,
    /// BUG-019). A structure that cannot be stale by construction cannot be misconfigured.
    /// </summary>
    public static class CrowdGrid
    {
        /// <summary>
        /// Cell edge in metres. Must be at least the largest radius ever queried through
        /// <see cref="Query"/> in one step, or a 3x3 block will not cover the search circle.
        /// Separation uses ~1.15 m; alarms use up to ~18 m and pay for a wider block.
        /// </summary>
        public const float CellSize = 4f;

        static readonly Dictionary<long, List<Pedestrian>> Cells = new Dictionary<long, List<Pedestrian>>(512);
        static readonly Stack<List<Pedestrian>> Spare = new Stack<List<Pedestrian>>(512);

        static int _builtFrame = -1;
        static int _lastPopulation;

        /// <summary>Pedestrians indexed at the last rebuild. Diagnostics only.</summary>
        public static int Population => _lastPopulation;

        /// <summary>Occupied cells at the last rebuild. Diagnostics only.</summary>
        public static int CellCount => Cells.Count;

        /// <summary>
        /// Statics outlive a Play session when domain reloading is off, which would leave the
        /// grid full of destroyed pedestrians from the previous run.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            Cells.Clear();
            Spare.Clear();
            _builtFrame = -1;
            _lastPopulation = 0;
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        static void Bucket(Vector3 p, out int x, out int z)
        {
            x = Mathf.FloorToInt(p.x / CellSize);
            z = Mathf.FloorToInt(p.z / CellSize);
        }

        /// <summary>
        /// Rebuilds the index if it has not already been built this frame.
        /// </summary>
        public static void EnsureFresh()
        {
            if (_builtFrame == Time.frameCount) return;
            _builtFrame = Time.frameCount;

            // Lists are recycled rather than dropped: this runs every frame, and handing the
            // GC a few hundred lists a frame is exactly the kind of steady garbage that shows
            // up as periodic hitching on a phone rather than as a lower average frame rate.
            foreach (var pair in Cells)
            {
                pair.Value.Clear();
                Spare.Push(pair.Value);
            }
            Cells.Clear();

            var all = Pedestrian.Witnesses;
            _lastPopulation = 0;

            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.IsDead) continue;

                Bucket(p.transform.position, out int x, out int z);
                long key = Key(x, z);

                if (!Cells.TryGetValue(key, out var list))
                {
                    list = Spare.Count > 0 ? Spare.Pop() : new List<Pedestrian>(8);
                    Cells[key] = list;
                }

                list.Add(p);
                _lastPopulation++;
            }
        }

        /// <summary>
        /// Every living pedestrian within <paramref name="radius"/> of <paramref name="centre"/>,
        /// appended to <paramref name="results"/>.
        ///
        /// The caller owns the list and is expected to reuse it. Results are not sorted, and
        /// the caller is not excluded from them -- callers that care check identity, which is
        /// cheaper than making this method know about them.
        /// </summary>
        public static void Query(Vector3 centre, float radius, List<Pedestrian> results)
        {
            if (results == null) return;
            EnsureFresh();

            float sqr = radius * radius;

            // How many cells out the search circle can reach.
            int reach = Mathf.Max(1, Mathf.CeilToInt(radius / CellSize));
            Bucket(centre, out int cx, out int cz);

            for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                if (!Cells.TryGetValue(Key(cx + dx, cz + dz), out var list)) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p == null) continue;

                    Vector3 delta = p.transform.position - centre;
                    delta.y = 0f;
                    if (delta.sqrMagnitude <= sqr) results.Add(p);
                }
            }
        }

        /// <summary>
        /// How many living pedestrians are within <paramref name="radius"/>. Counts without
        /// building a list, for callers that only want the number.
        /// </summary>
        public static int CountWithin(Vector3 centre, float radius)
        {
            EnsureFresh();

            float sqr = radius * radius;
            int reach = Mathf.Max(1, Mathf.CeilToInt(radius / CellSize));
            Bucket(centre, out int cx, out int cz);

            int count = 0;
            for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                if (!Cells.TryGetValue(Key(cx + dx, cz + dz), out var list)) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p == null) continue;

                    Vector3 delta = p.transform.position - centre;
                    delta.y = 0f;
                    if (delta.sqrMagnitude <= sqr) count++;
                }
            }
            return count;
        }
    }
}
