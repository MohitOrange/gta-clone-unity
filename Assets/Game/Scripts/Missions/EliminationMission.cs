using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Clear out a group of hostiles at a fixed location.
    ///
    /// Enemies are spawned on accept and destroyed on cleanup rather than living in the scene,
    /// so a mission the player never takes costs nothing at runtime -- important when several
    /// of these exist across the map.
    /// </summary>
    public class EliminationMission : MissionBase
    {
        [Header("Target site")]
        public Vector3 SiteCentre;
        [Tooltip("Enemies are scattered within this radius of the site.")]
        public float SiteRadius = 12f;
        [Tooltip("Player must get this close before the fight is considered joined.")]
        public float ApproachRadius = 45f;

        [Header("Enemies")]
        public GameObject EnemyPrefab;
        public int EnemyCount = 4;
        [Tooltip("Health each enemy gets, overriding the prefab.")]
        public float EnemyHealth = 60f;

        readonly List<HostileNpc> _enemies = new List<HostileNpc>();
        int _killed;
        bool _engaged;

        protected override void OnBegin()
        {
            _enemies.Clear();
            _killed = 0;
            _engaged = false;

            if (EnemyPrefab == null) { Fail("No enemies configured"); return; }

            Spawn();
            SetObjective("Get to the hideout", SiteCentre);
        }

        void Spawn()
        {
            for (int i = 0; i < EnemyCount; i++)
            {
                // Ring layout so they are not stacked inside one another on spawn.
                float angle = i / (float)EnemyCount * Mathf.PI * 2f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                                 * Random.Range(SiteRadius * 0.4f, SiteRadius);

                Vector3 position = SiteCentre + offset + Vector3.up * 0.2f;

                var go = Instantiate(EnemyPrefab, position,
                                     Quaternion.LookRotation(-offset.normalized, Vector3.up));
                go.name = MissionId + "_enemy_" + i;

                // These are mission enemies, not police: strip anything that would make them
                // behave like law enforcement.
                var officer = go.GetComponent<PoliceOfficer>();
                if (officer != null) Destroy(officer);

                var hostile = go.GetComponent<HostileNpc>() ?? go.AddComponent<HostileNpc>();
                hostile.Target = Player;

                var health = go.GetComponent<Health>();
                if (health != null)
                {
                    health.MaxHealth = EnemyHealth;
                    health.MaxArmor = 0f;
                    health.StartingArmor = 0f;
                    health.Revive();
                }

                hostile.Died += OnEnemyDied;
                _enemies.Add(hostile);
            }
        }

        void OnEnemyDied(HostileNpc enemy)
        {
            _killed++;
            UpdateObjective();

            if (_killed >= _enemies.Count) Succeed();
        }

        protected override void OnTick(float deltaTime)
        {
            if (_engaged) return;

            if (DistanceToPlayer(SiteCentre) > ApproachRadius) return;

            _engaged = true;
            UpdateObjective();
        }

        void UpdateObjective()
        {
            int left = Mathf.Max(0, _enemies.Count - _killed);
            Vector3? marker = left > 0 ? NearestLivingEnemy() : null;
            SetObjective("Eliminate the crew  (" + left + " left)", marker);
        }

        Vector3? NearestLivingEnemy()
        {
            Vector3? best = null;
            float bestDistance = float.MaxValue;

            foreach (var e in _enemies)
            {
                if (e == null || e.IsDead) continue;

                float d = DistanceToPlayer(e.transform.position);
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = e.transform.position;
            }

            // Fall back to the site so the marker never vanishes mid-fight.
            return best ?? SiteCentre;
        }

        protected override void OnEnd(bool success)
        {
            foreach (var e in _enemies)
            {
                if (e == null) continue;
                e.Died -= OnEnemyDied;

                // Leave corpses to fade on success; clear the live ones out on failure so the
                // player is not shot by a mission they already lost.
                if (!e.IsDead) Destroy(e.gameObject);
            }

            _enemies.Clear();
            SetObjective(success ? "Site cleared" : "", null);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(SiteCentre, SiteRadius);
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f);
            Gizmos.DrawWireSphere(SiteCentre, ApproachRadius);
        }
    }
}
