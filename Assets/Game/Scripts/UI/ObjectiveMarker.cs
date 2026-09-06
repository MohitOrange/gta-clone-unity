using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Points the player at the current objective two ways: a beam of light in the world, and
    /// a screen arrow that sticks to the edge when the objective is off-camera.
    ///
    /// The edge arrow matters more than it looks: on a phone the field of view is narrow and
    /// the objective is off-screen most of the time, so a world beam alone leaves the player
    /// turning in circles hunting for it.
    /// </summary>
    public class ObjectiveMarker : MonoBehaviour
    {
        [Header("World beam")]
        public Transform Beam;
        public float BeamSpinSpeed = 40f;

        [Header("Screen arrow")]
        public RectTransform Arrow;
        public Image ArrowImage;
        public Text ArrowDistance;
        [Tooltip("Keep the arrow this many pixels inside the screen edge.")]
        public float EdgePadding = 90f;
        [Tooltip("Hide the arrow once the objective is comfortably on screen and near.")]
        public float HideWhenCloserThan = 18f;

        [Tooltip("Colour of the direction arrow when it is following a player-placed map "
                 + "waypoint rather than a mission objective.")]
        public Color WaypointColour = new Color(0.62f, 0.87f, 0.97f);

        // The arrow's authored colour, so switching back from a waypoint to an objective
        // restores whatever the interface builder chose rather than a hard-coded white.
        Color _objectiveColour = Color.white;

        Camera _camera;
        Transform _player;
        Canvas _canvas;

        void Start()
        {
            _camera = Camera.main;
            _canvas = GetComponentInParent<Canvas>();

            var player = GameObject.FindWithTag("Player");
            if (player != null) _player = player.transform;

            if (ArrowImage != null) _objectiveColour = ArrowImage.color;
        }

        void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;

            var mission = MissionManager.Instance != null ? MissionManager.Instance.Active : null;
            bool hasMission = mission != null
                              && mission.State == MissionState.Running
                              && mission.HasObjectivePoint;

            // A mission objective outranks a player waypoint. The same arrow serves both, so
            // there is one direction indicator on screen rather than two competing ones -- the
            // player-set pin simply borrows it whenever no job is running.
            bool hasWaypoint = !hasMission && MapWaypoint.Active;

            if (!hasMission && !hasWaypoint)
            {
                if (Beam != null) Beam.gameObject.SetActive(false);
                if (Arrow != null) Arrow.gameObject.SetActive(false);
                return;
            }

            Vector3 target = hasMission ? mission.ObjectivePoint : MapWaypoint.Point;

            // Tinted so the two are never confused: the objective keeps the arrow's authored
            // colour, a personal waypoint is drawn in the theme accent.
            if (ArrowImage != null)
                ArrowImage.color = hasMission ? _objectiveColour : WaypointColour;

            UpdateBeam(target);
            UpdateArrow(target);
        }

        void UpdateBeam(Vector3 target)
        {
            if (Beam == null) return;

            Beam.gameObject.SetActive(true);
            Beam.position = target;
            Beam.Rotate(Vector3.up, BeamSpinSpeed * Time.deltaTime, Space.World);
        }

        void UpdateArrow(Vector3 target)
        {
            if (Arrow == null || _camera == null) return;

            float distance = _player != null
                ? Vector3.Distance(_player.position, target)
                : Vector3.Distance(_camera.transform.position, target);

            Vector3 viewport = _camera.WorldToViewportPoint(target);
            bool behind = viewport.z < 0f;
            bool onScreen = !behind
                            && viewport.x > 0.05f && viewport.x < 0.95f
                            && viewport.y > 0.05f && viewport.y < 0.95f;

            if (onScreen && distance < HideWhenCloserThan)
            {
                Arrow.gameObject.SetActive(false);
                return;
            }

            Arrow.gameObject.SetActive(true);

            // Behind the camera, the viewport point mirrors; flip it so the arrow points the
            // correct way round instead of swinging to the opposite edge.
            if (behind)
            {
                viewport.x = 1f - viewport.x;
                viewport.y = 1f - viewport.y;
            }

            var canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;
            Vector2 canvasSize = canvasRect != null ? canvasRect.rect.size
                                                    : new Vector2(Screen.width, Screen.height);

            Vector2 centred = new Vector2(
                (viewport.x - 0.5f) * canvasSize.x,
                (viewport.y - 0.5f) * canvasSize.y);

            Vector2 limit = canvasSize * 0.5f - Vector2.one * EdgePadding;

            if (behind || !onScreen)
            {
                // Push the point out to the rectangle edge along its own direction, so the
                // arrow slides around the border rather than jumping between corners.
                if (centred.sqrMagnitude < 0.001f) centred = Vector2.up;

                float scale = Mathf.Min(
                    limit.x / Mathf.Max(0.001f, Mathf.Abs(centred.x)),
                    limit.y / Mathf.Max(0.001f, Mathf.Abs(centred.y)));

                if (scale < 1f || behind) centred *= scale;
            }

            centred.x = Mathf.Clamp(centred.x, -limit.x, limit.x);
            centred.y = Mathf.Clamp(centred.y, -limit.y, limit.y);

            Arrow.anchoredPosition = centred;

            float angle = Mathf.Atan2(centred.y, centred.x) * Mathf.Rad2Deg;
            Arrow.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);

            if (ArrowDistance != null)
            {
                ArrowDistance.text = Mathf.RoundToInt(distance) + "m";
                // Keep the label upright while the arrow rotates.
                ArrowDistance.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -(angle - 90f));
            }
        }
    }
}
