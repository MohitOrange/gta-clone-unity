using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// The whole city, big enough to read.
    ///
    /// It carries its own <see cref="Minimap"/> rather than scaling up the corner one. The
    /// minimap's blip logic is already generic over its rect, so a second instance at
    /// full-screen size costs one component and gets streets, police, jobs and the objective
    /// for free -- and the corner map keeps working underneath, unmodified.
    /// </summary>
    public class FullMapScreen : UiPanel
    {
        [Header("Wiring")]
        public Minimap Map;
        [Tooltip("The minimap tap-target that opens this screen. Wired at runtime because an "
                 + "editor-time onClick.AddListener is not serialised into the scene.")]
        public Button OpenButton;
        public Button CloseButton;
        [Tooltip("Removes the player's waypoint without selecting another one.")]
        public Button ClearWaypointButton;

        [Header("Readouts")]
        public Text ObjectiveLabel;
        public Text PositionLabel;

        UiPanel _returnTo;
        Transform _player;

        protected override void Awake()
        {
            base.Awake();
            if (OpenButton != null) OpenButton.onClick.AddListener(() => OpenFrom(null));
            if (CloseButton != null) CloseButton.onClick.AddListener(Hide);
            if (ClearWaypointButton != null)
                ClearWaypointButton.onClick.AddListener(MapWaypoint.Clear);
        }

        public void OpenFrom(UiPanel returnTo)
        {
            _returnTo = returnTo;
            Show();
        }

        protected override void OnShown()
        {
            MenuState.Enter();
            Refresh();
        }

        protected override void OnHidden()
        {
            MenuState.Exit();

            var back = _returnTo;
            _returnTo = null;
            back?.Show();
        }

        protected override void Update()
        {
            base.Update();
            if (IsOpen) Refresh();
        }

        void Refresh()
        {
            if (_player == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null) _player = player.transform;
            }

            if (PositionLabel != null && _player != null)
            {
                Vector3 p = _player.position;
                PositionLabel.text = "YOU  " + Mathf.RoundToInt(p.x) + ", " + Mathf.RoundToInt(p.z);
            }

            if (ObjectiveLabel == null) return;

            var mission = MissionManager.Instance != null ? MissionManager.Instance.Active : null;

            if (mission == null || mission.State != MissionState.Running)
            {
                ObjectiveLabel.text = "No active job.  Yellow markers are people with work.";
                return;
            }

            string distance = mission.HasObjectivePoint && _player != null
                ? "   " + Mathf.RoundToInt(Vector3.Distance(_player.position, mission.ObjectivePoint)) + "m"
                : "";

            ObjectiveLabel.text = mission.Title + ":  " + mission.ObjectiveText + distance;
        }
    }
}
