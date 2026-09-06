using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Walk a client to a destination and keep them breathing.
    ///
    /// The client follows the *player* rather than pathing to the destination independently.
    /// That is the whole design: it forces the player to lead, to stay close, and to turn and
    /// fight rather than sprinting ahead — which is what makes an escort feel like an escort
    /// instead of a slow race.
    /// </summary>
    public class EscortMission : MissionBase
    {
        [Header("Client")]
        public GameObject ClientPrefab;
        public Vector3 ClientStart;
        public float ClientHealth = 90f;

        [Header("Destination")]
        public Vector3 Destination;
        public float ArriveRadius = 7f;

        [Header("Following")]
        [Tooltip("How far behind the player the client trails.")]
        public float FollowDistance = 3.2f;
        public float ClientSpeed = 3.6f;
        [Tooltip("Fail if the player abandons the client beyond this range.")]
        public float AbandonDistance = 55f;

        [Header("Ambush")]
        public GameObject EnemyPrefab;
        [Tooltip("Ambush points along the route. Enemies spawn as the player nears each.")]
        public Vector3[] AmbushPoints = new Vector3[0];
        public int EnemiesPerAmbush = 2;
        public float AmbushTriggerRadius = 30f;

        EscortClient _client;
        Health _clientHealth;
        readonly List<HostileNpc> _enemies = new List<HostileNpc>();
        bool[] _ambushSprung;

        protected override void OnBegin()
        {
            _enemies.Clear();
            _ambushSprung = new bool[AmbushPoints.Length];

            if (ClientPrefab == null) { Fail("No client configured"); return; }

            var go = Instantiate(ClientPrefab, ClientStart + Vector3.up * 0.2f, Quaternion.identity);
            go.name = MissionId + "_client";

            // The client is a civilian, not a cop or a shooter.
            foreach (var unwanted in go.GetComponents<MonoBehaviour>())
                if (unwanted is PoliceOfficer || unwanted is HostileNpc || unwanted is Pedestrian)
                    Destroy(unwanted);

            _clientHealth = go.GetComponent<Health>();
            if (_clientHealth != null)
            {
                _clientHealth.MaxHealth = ClientHealth;
                _clientHealth.MaxArmor = 0f;
                _clientHealth.StartingArmor = 0f;
                _clientHealth.Revive();
                _clientHealth.Died += OnClientDied;
            }

            _client = go.GetComponent<EscortClient>() ?? go.AddComponent<EscortClient>();
            _client.Leader = Player;
            _client.FollowDistance = FollowDistance;
            _client.MoveSpeed = ClientSpeed;

            SetObjective("Escort the client to the drop", Destination);
        }

        protected override void OnTick(float deltaTime)
        {
            if (_client == null) { Fail("Lost the client"); return; }

            float clientToDestination = Vector3.Distance(_client.transform.position, Destination);
            float playerToClient = DistanceToPlayer(_client.transform.position);

            if (playerToClient > AbandonDistance) { Fail("You abandoned the client"); return; }

            TriggerAmbushes();

            if (clientToDestination <= ArriveRadius) { Succeed(); return; }

            // Point the marker at whichever matters more: the client if they have fallen
            // behind, otherwise the destination.
            if (playerToClient > FollowDistance * 5f)
                SetObjective("Wait for the client", _client.transform.position);
            else
                SetObjective("Escort the client to the drop", Destination);
        }

        void TriggerAmbushes()
        {
            if (EnemyPrefab == null) return;

            for (int i = 0; i < AmbushPoints.Length; i++)
            {
                if (_ambushSprung[i]) continue;
                if (DistanceToPlayer(AmbushPoints[i]) > AmbushTriggerRadius) continue;

                _ambushSprung[i] = true;
                SpawnAmbush(AmbushPoints[i], i);
            }
        }

        void SpawnAmbush(Vector3 centre, int index)
        {
            for (int i = 0; i < EnemiesPerAmbush; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Random.Range(4f, 10f);

                var go = Instantiate(EnemyPrefab, centre + offset + Vector3.up * 0.2f, Quaternion.identity);
                go.name = MissionId + "_ambush" + index + "_" + i;

                var officer = go.GetComponent<PoliceOfficer>();
                if (officer != null) Destroy(officer);

                var hostile = go.GetComponent<HostileNpc>() ?? go.AddComponent<HostileNpc>();

                // Ambushers go for the client, which is what makes protecting them a job.
                hostile.Target = _client != null ? _client.transform : Player;

                var health = go.GetComponent<Health>();
                if (health != null)
                {
                    health.MaxHealth = 55f;
                    health.MaxArmor = 0f;
                    health.Revive();
                }

                _enemies.Add(hostile);
            }
        }

        void OnClientDied(DamageInfo info) => Fail("The client was killed");

        protected override void OnEnd(bool success)
        {
            if (_clientHealth != null) _clientHealth.Died -= OnClientDied;

            foreach (var e in _enemies)
                if (e != null && !e.IsDead) Destroy(e.gameObject);
            _enemies.Clear();

            if (_client != null) Destroy(_client.gameObject, success ? 2.5f : 0f);
            _client = null;

            SetObjective(success ? "Client delivered" : "", null);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(Destination, ArriveRadius);
            Gizmos.DrawWireSphere(ClientStart, 1f);
            Gizmos.DrawLine(ClientStart, Destination);

            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
            if (AmbushPoints != null)
                foreach (var p in AmbushPoints) Gizmos.DrawWireSphere(p, AmbushTriggerRadius);
        }
    }
}
