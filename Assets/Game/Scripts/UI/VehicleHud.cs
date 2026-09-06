using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Speed and condition readout, shown only while driving.
    ///
    /// Deliberately minimal: on a phone the centre of the screen is the game, and a driver
    /// needs exactly two numbers -- how fast, and how broken.
    /// </summary>
    public class VehicleHud : MonoBehaviour
    {
        [Header("Wiring")]
        public CanvasGroup Group;
        public Text SpeedLabel;
        public Text NameLabel;
        public Image HealthFill;

        [Header("Health colours")]
        public Color Healthy = new Color(0.45f, 0.85f, 0.45f);
        public Color Damaged = new Color(0.95f, 0.75f, 0.25f);
        public Color Critical = new Color(0.95f, 0.30f, 0.25f);

        public float FadeSpeed = 8f;

        Vehicle _vehicle;
        bool _visible;

        void Awake()
        {
            if (Group == null) Group = GetComponent<CanvasGroup>();
            if (Group != null) Group.alpha = 0f;
        }

        public void SetVisible(bool visible) => _visible = visible;

        public void Bind(Vehicle vehicle)
        {
            _vehicle = vehicle;
            if (NameLabel != null && vehicle != null) NameLabel.text = vehicle.DisplayName.ToUpperInvariant();
        }

        void Update()
        {
            if (Group != null)
            {
                float target = _visible && _vehicle != null ? 1f : 0f;
                Group.alpha = Mathf.MoveTowards(Group.alpha, target, FadeSpeed * Time.unscaledDeltaTime);
                Group.blocksRaycasts = false;   // a readout must never eat a thumb tap
            }

            if (_vehicle == null || !_visible) return;

            if (SpeedLabel != null)
                SpeedLabel.text = Mathf.RoundToInt(_vehicle.SpeedKph).ToString();

            if (HealthFill != null)
            {
                float h = Mathf.Clamp01(_vehicle.Health / Mathf.Max(1f, _vehicle.MaxHealth));
                HealthFill.fillAmount = h;
                HealthFill.color = h > 0.6f ? Healthy
                    : h > 0.3f ? Color.Lerp(Damaged, Healthy, (h - 0.3f) / 0.3f)
                    : Color.Lerp(Critical, Damaged, h / 0.3f);
            }
        }
    }
}
