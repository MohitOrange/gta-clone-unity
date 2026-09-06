using UnityEngine;

namespace MiniGTA
{
    public enum MissionState { Idle, Running, Succeeded, Failed }

    /// <summary>
    /// Base for every mission type.
    ///
    /// Missions live in the scene as disabled components rather than being spawned from data,
    /// so each type is a real class you can step through in a debugger and each instance can
    /// reference the actual world objects it cares about. The manager runs exactly one at a
    /// time and owns the rewards, so a mission subclass never touches money or XP -- it only
    /// declares success or failure.
    /// </summary>
    public abstract class MissionBase : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Stable id written into the save file. Never renumber these.")]
        public string MissionId = "mission.unnamed";
        public string Title = "Untitled Job";
        [TextArea(2, 4)] public string Briefing = "";

        [Header("Requirements")]
        public int RequiredLevel = 1;
        [Tooltip("Unlock id required to take this job, or empty.")]
        public string RequiredUnlock = "";
        public bool Repeatable;

        [Header("Reward")]
        public int RewardMoney = 250;
        public int RewardXp = 120;

        [Header("Limits")]
        [Tooltip("Seconds allowed. 0 means no limit.")]
        public float TimeLimit;

        public MissionState State { get; private set; } = MissionState.Idle;

        /// <summary>One line telling the player what to do right now.</summary>
        public string ObjectiveText { get; protected set; } = "";

        /// <summary>Where the objective marker should point, if anywhere.</summary>
        public bool HasObjectivePoint { get; protected set; }
        public Vector3 ObjectivePoint { get; protected set; }

        public float TimeRemaining { get; private set; }
        public bool IsTimed => TimeLimit > 0.01f;

        /// <summary>Why the mission failed, for the HUD banner.</summary>
        public string FailReason { get; private set; } = "";

        public event System.Action<MissionBase> Completed;
        public event System.Action<MissionBase, string> Failed;
        public event System.Action<MissionBase> ObjectiveChanged;

        protected Transform Player { get; private set; }
        protected PlayerVehicleController PlayerDriving { get; private set; }
        protected Health PlayerHealth { get; private set; }

        // ---------------------------------------------------------------- lifecycle

        /// <summary>Called by the manager. Do not call directly.</summary>
        public void Begin()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                Player = player.transform;
                PlayerDriving = player.GetComponent<PlayerVehicleController>();
                PlayerHealth = player.GetComponent<Health>();
            }

            State = MissionState.Running;
            TimeRemaining = TimeLimit;
            FailReason = "";

            enabled = true;
            OnBegin();
        }

        void Update()
        {
            if (State != MissionState.Running) return;

            // A wasted or busted player abandons whatever they were doing.
            var game = GameStateManager.Instance;
            if (game != null && !game.IsPlaying)
            {
                Fail("You didn't make it");
                return;
            }

            if (IsTimed)
            {
                TimeRemaining -= Time.deltaTime;
                if (TimeRemaining <= 0f)
                {
                    TimeRemaining = 0f;
                    Fail("Out of time");
                    return;
                }
            }

            OnTick(Time.deltaTime);
        }

        // ------------------------------------------------------------------ results

        protected void Succeed()
        {
            if (State != MissionState.Running) return;

            State = MissionState.Succeeded;
            SetObjective("Complete", null);
            OnEnd(true);
            enabled = false;

            Completed?.Invoke(this);
        }

        protected void Fail(string reason)
        {
            if (State != MissionState.Running) return;

            State = MissionState.Failed;
            FailReason = reason;
            OnEnd(false);
            enabled = false;

            Failed?.Invoke(this, reason);
        }

        /// <summary>Cancelled from outside, e.g. the player abandoned it.</summary>
        public void Abort(string reason) => Fail(reason);

        /// <summary>Return a finished mission to Idle so it can be offered again.</summary>
        public void ResetToIdle()
        {
            State = MissionState.Idle;
            enabled = false;
            HasObjectivePoint = false;
            ObjectiveText = "";
        }

        // --------------------------------------------------------------- objectives

        /// <summary>Update the current objective line and marker. Null point hides the marker.</summary>
        protected void SetObjective(string text, Vector3? point)
        {
            bool changed = ObjectiveText != text;

            ObjectiveText = text;

            if (point.HasValue)
            {
                if (!HasObjectivePoint || ObjectivePoint != point.Value) changed = true;
                HasObjectivePoint = true;
                ObjectivePoint = point.Value;
            }
            else
            {
                if (HasObjectivePoint) changed = true;
                HasObjectivePoint = false;
            }

            if (changed) ObjectiveChanged?.Invoke(this);
        }

        /// <summary>Flat distance from the player to a world point, ignoring height.</summary>
        protected float DistanceToPlayer(Vector3 point)
        {
            if (Player == null) return float.MaxValue;

            Vector3 delta = point - Player.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        protected bool PlayerIsDriving => PlayerDriving != null && PlayerDriving.IsDriving;

        // ------------------------------------------------------------------ hooks

        protected abstract void OnBegin();
        protected abstract void OnTick(float deltaTime);

        /// <summary>Tear down spawned objects and markers. Always runs, success or failure.</summary>
        protected abstract void OnEnd(bool success);
    }
}
