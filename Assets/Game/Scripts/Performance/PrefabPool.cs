using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Keeps used instances alive and asleep instead of destroying them.
    ///
    /// The police response is the worst offender in this game: reaching four stars instantiates
    /// four cruisers -- each a rigidbody, four wheel colliders, a dozen renderers and three
    /// behaviours -- and going clean destroys the lot, so a player who repeatedly gains and
    /// loses heat pays that cost over and over. Reusing the same objects turns the spike into
    /// a transform assignment.
    ///
    /// A pooled object is deactivated, not reset. Anything with state that must not survive a
    /// reuse has to clear it in OnEnable or be cleared by the caller after <see cref="Take"/> --
    /// which is why the dispatcher calls ResetDeployment on every cruiser it takes out.
    /// </summary>
    public class PrefabPool
    {
        readonly GameObject _prefab;
        readonly Transform _parent;
        readonly int _capacity;
        readonly Stack<GameObject> _idle = new Stack<GameObject>();

        int _created;

        /// <summary>Instances currently asleep in the pool.</summary>
        public int Idle => _idle.Count;

        /// <summary>Instances this pool has ever made, live or idle.</summary>
        public int Created => _created;

        public PrefabPool(GameObject prefab, Transform parent, int capacity = 12)
        {
            _prefab = prefab;
            _parent = parent;
            _capacity = Mathf.Max(1, capacity);
        }

        /// <summary>An active instance at the given pose, recycled if one is available.</summary>
        public GameObject Take(Vector3 position, Quaternion rotation)
        {
            if (_prefab == null) return null;

            GameObject go = null;

            // Skip anything destroyed out from under us -- a wrecked cruiser is removed by the
            // damage system, not returned here.
            while (_idle.Count > 0 && go == null) go = _idle.Pop();

            if (go == null)
            {
                go = Object.Instantiate(_prefab, position, rotation, _parent);
                _created++;
                return go;
            }

            // Physics state does not clear itself. A rigidbody put back to sleep carrying the
            // velocity it crashed with will launch itself the moment it wakes.
            var body = go.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);

            if (body != null)
            {
                // Written after activation: a disabled rigidbody ignores these.
                body.position = position;
                body.rotation = rotation;
            }

            return go;
        }

        /// <summary>Puts an instance to sleep, or destroys it if the pool is already full.</summary>
        public void Return(GameObject go)
        {
            if (go == null) return;

            if (_idle.Count >= _capacity)
            {
                Object.Destroy(go);
                return;
            }

            go.SetActive(false);
            if (_parent != null) go.transform.SetParent(_parent, false);
            _idle.Push(go);
        }

        /// <summary>Destroys everything asleep. For a hard reset, not for ordinary use.</summary>
        public void Clear()
        {
            while (_idle.Count > 0)
            {
                var go = _idle.Pop();
                if (go != null) Object.Destroy(go);
            }
        }
    }
}
