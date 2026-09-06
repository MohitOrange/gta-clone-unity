using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Runs one mission at a time and owns the reward payout.
    ///
    /// Rewards live here rather than in the mission subclasses so that every job pays out
    /// through the same path -- which is the only way XP, money, level-ups and the autosave
    /// stay consistent as mission types are added.
    /// </summary>
    public class MissionManager : MonoBehaviour
    {
        static MissionManager _instance;

        public static MissionManager Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<MissionManager>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Result banner")]
        [Tooltip("Seconds the success/failure banner stays up.")]
        public float ResultDisplayTime = 4f;

        [Header("Failure")]
        [Tooltip("Let a finished mission be offered again after this long.")]
        public float RetryCooldown = 6f;

        readonly List<MissionBase> _all = new List<MissionBase>();

        public MissionBase Active { get; private set; }
        public IReadOnlyList<MissionBase> AllMissions => _all;

        /// <summary>Fired when a mission is accepted.</summary>
        public event System.Action<MissionBase> MissionStarted;

        /// <summary>Mission, success, headline, detail. Drives the result banner.</summary>
        public event System.Action<MissionBase, bool, string, string> MissionEnded;

        public event System.Action<MissionBase> ObjectiveChanged;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            // Missions register themselves; catch any placed in the scene before this ran.
            foreach (var m in FindObjectsByType<MissionBase>(FindObjectsInactive.Include))
                Register(m);
        }

        public void Register(MissionBase mission)
        {
            if (mission == null || _all.Contains(mission)) return;
            _all.Add(mission);
            mission.enabled = false;
        }

        public MissionBase Find(string missionId)
        {
            foreach (var m in _all)
                if (m != null && m.MissionId == missionId) return m;
            return null;
        }

        // ------------------------------------------------------------------ gating

        /// <summary>Whether the player may take this job right now, with a reason if not.</summary>
        public bool CanAccept(MissionBase mission, out string reason)
        {
            reason = "";

            if (mission == null) { reason = "No job here"; return false; }
            if (Active != null) { reason = "Finish your current job first"; return false; }

            var game = GameStateManager.Instance;
            if (game != null && !game.IsPlaying) { reason = "Not right now"; return false; }

            var progress = PlayerProgress.Instance;
            if (progress != null)
            {
                if (progress.Level < mission.RequiredLevel)
                {
                    reason = "Requires level " + mission.RequiredLevel;
                    return false;
                }

                if (!progress.IsUnlocked(mission.RequiredUnlock))
                {
                    reason = "Locked";
                    return false;
                }

                if (!mission.Repeatable && progress.HasCompleted(mission.MissionId))
                {
                    reason = "Already done";
                    return false;
                }
            }

            if (mission.State == MissionState.Running) { reason = "In progress"; return false; }
            return true;
        }

        public bool CanAccept(MissionBase mission) => CanAccept(mission, out _);

        // ------------------------------------------------------------------ control

        public bool Accept(MissionBase mission)
        {
            if (!CanAccept(mission, out string reason))
            {
                Debug.Log("[Mission] Cannot accept " + (mission != null ? mission.MissionId : "null")
                          + ": " + reason);
                return false;
            }

            Active = mission;

            mission.Completed += OnMissionCompleted;
            mission.Failed += OnMissionFailed;
            mission.ObjectiveChanged += OnObjectiveChanged;

            mission.Begin();
            MissionStarted?.Invoke(mission);

            Debug.Log("[Mission] Started " + mission.MissionId + " (" + mission.Title + ")");
            return true;
        }

        public void AbandonActive()
        {
            if (Active == null) return;
            Active.Abort("Abandoned");
        }

        void OnObjectiveChanged(MissionBase mission) => ObjectiveChanged?.Invoke(mission);

        void OnMissionCompleted(MissionBase mission)
        {
            Unhook(mission);

            var progress = PlayerProgress.Instance;
            int money = mission.RewardMoney;
            int xp = mission.RewardXp;

            if (progress != null)
            {
                progress.MarkCompleted(mission.MissionId);
                progress.AddMoney(money);
                progress.AddXp(xp);
            }

            Active = null;

            string detail = "$" + money + "   +" + xp + " XP";
            MissionEnded?.Invoke(mission, true, "MISSION COMPLETE", detail);

            Debug.Log("[Mission] Completed " + mission.MissionId + " -> " + detail);

            SaveSystem.Instance?.Save();
            StartCoroutine(ReofferAfterCooldown(mission));
        }

        void OnMissionFailed(MissionBase mission, string reason)
        {
            Unhook(mission);
            Active = null;

            MissionEnded?.Invoke(mission, false, "MISSION FAILED", reason);
            Debug.Log("[Mission] Failed " + mission.MissionId + ": " + reason);

            StartCoroutine(ReofferAfterCooldown(mission));
        }

        void Unhook(MissionBase mission)
        {
            mission.Completed -= OnMissionCompleted;
            mission.Failed -= OnMissionFailed;
            mission.ObjectiveChanged -= OnObjectiveChanged;
        }

        System.Collections.IEnumerator ReofferAfterCooldown(MissionBase mission)
        {
            yield return new WaitForSeconds(RetryCooldown);
            if (mission != null && mission.State != MissionState.Running) mission.ResetToIdle();
        }

        /// <summary>Restore an in-progress mission after loading a save.</summary>
        public void RestoreActive(string missionId)
        {
            if (string.IsNullOrEmpty(missionId) || Active != null) return;

            var mission = Find(missionId);
            if (mission == null) return;

            // Missions are not resumable mid-objective; the player gets it offered again from
            // the start rather than being dropped into a half-finished state that was never
            // serialised. Honest, and avoids saving per-mission progress that would go stale.
            mission.ResetToIdle();
            Debug.Log("[Mission] " + missionId + " was active when saved; offering it again.");
        }
    }
}
