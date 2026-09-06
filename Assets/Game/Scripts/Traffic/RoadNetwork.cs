using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>Compass direction of travel. Order matters: turning is index arithmetic.</summary>
    public enum Heading { North = 0, East = 1, South = 2, West = 3 }

    public static class HeadingUtil
    {
        public static Vector3 ToVector(this Heading h) => h switch
        {
            Heading.North => Vector3.forward,
            Heading.East => Vector3.right,
            Heading.South => Vector3.back,
            _ => Vector3.left,
        };

        /// <summary>Right-hand side of the road for this heading.</summary>
        public static Vector3 RightOf(this Heading h) => Vector3.Cross(Vector3.up, h.ToVector());

        public static Heading TurnRight(this Heading h) => (Heading)(((int)h + 1) & 3);
        public static Heading TurnLeft(this Heading h) => (Heading)(((int)h + 3) & 3);
        public static Heading Opposite(this Heading h) => (Heading)(((int)h + 2) & 3);

        /// <summary>True for North/South. Used to split intersections into two light phases.</summary>
        public static bool IsNorthSouth(this Heading h) => h == Heading.North || h == Heading.South;
    }

    /// <summary>One point on a lane. Traffic drives from node to node along <see cref="Next"/>.</summary>
    [System.Serializable]
    public class LaneNode
    {
        public Vector3 Position;
        public int[] Next = System.Array.Empty<int>();

        /// <summary>Direction of travel when arriving at this node.</summary>
        public Heading Heading;

        /// <summary>Index into <see cref="RoadNetwork.Intersections"/>, or -1 for open road.</summary>
        public int IntersectionIndex = -1;

        /// <summary>True if this node sits at an intersection stop line and must obey its light.</summary>
        public bool IsStopLine;
    }

    /// <summary>
    /// Directed lane graph for the city grid, generated to match the asphalt exactly.
    ///
    /// A graph rather than free-form steering because traffic has to agree with the road
    /// markings and with the traffic lights; waypoints make "which light governs me" a lookup
    /// instead of a guess.
    /// </summary>
    public class RoadNetwork : MonoBehaviour
    {
        public static RoadNetwork Instance { get; private set; }

        [SerializeField] List<LaneNode> _nodes = new List<LaneNode>();
        [SerializeField] List<TrafficLightController> _intersections = new List<TrafficLightController>();

        public IReadOnlyList<LaneNode> Nodes => _nodes;
        public IReadOnlyList<TrafficLightController> Intersections => _intersections;

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        public LaneNode GetNode(int index) =>
            index >= 0 && index < _nodes.Count ? _nodes[index] : null;

        public int NodeCount => _nodes.Count;

        /// <summary>Editor-time population. Runtime code should treat the graph as read-only.</summary>
        public void SetData(List<LaneNode> nodes, List<TrafficLightController> intersections)
        {
            _nodes = nodes;
            _intersections = intersections;
        }

        public TrafficLightController GetIntersection(int index) =>
            index >= 0 && index < _intersections.Count ? _intersections[index] : null;

        /// <summary>Random node with at least one successor -- a safe place to spawn traffic.</summary>
        public int RandomDrivableNode(System.Random rng)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                int i = rng.Next(_nodes.Count);
                if (_nodes[i].Next.Length > 0 && !_nodes[i].IsStopLine) return i;
            }
            return -1;
        }

        /// <summary>Nearest node to a world position, for snapping a spawned car onto a lane.</summary>
        public int NearestNode(Vector3 position)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                float d = (_nodes[i].Position - position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = i; }
            }
            return best;
        }

        void OnDrawGizmosSelected()
        {
            if (_nodes == null) return;

            foreach (var n in _nodes)
            {
                Gizmos.color = n.IsStopLine ? Color.red : new Color(0.3f, 0.8f, 1f);
                Gizmos.DrawSphere(n.Position, 0.6f);

                if (n.Next == null) continue;
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
                foreach (int next in n.Next)
                {
                    var target = GetNode(next);
                    if (target != null) Gizmos.DrawLine(n.Position, target.Position);
                }
            }
        }
    }
}
