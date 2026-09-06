using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Volume, look sensitivity, handedness and the graphics tier.
    ///
    /// Every control applies immediately rather than on a Save button. Sensitivity in
    /// particular is unjudgeable from a number -- you have to move the camera and feel it --
    /// and a quality tier you cannot see the effect of is a guess.
    /// </summary>
    public class SettingsPanel : UiPanel
    {
        [Header("Sliders")]
        public Slider MasterSlider;
        public Slider MusicSlider;
        public Slider SfxSlider;
        public Slider SensitivitySlider;

        [Header("Slider readouts")]
        public Text MasterValue;
        public Text MusicValue;
        public Text SfxValue;
        public Text SensitivityValue;

        [Header("Toggling buttons")]
        public Button HandednessButton;
        public Text HandednessLabel;
        public Button InvertYButton;
        public Text InvertYLabel;

        [Header("Quality")]
        public Button LowButton;
        public Button MediumButton;
        public Button HighButton;
        public Text QualityDetail;

        [Header("Navigation")]
        public Button BackButton;

        [Header("Controls")]
        [Tooltip("Opens the touch-control layout editor.")]
        public Button CustomiseHudButton;
        [Tooltip("The editor itself. Assigned by the interface builder.")]
        public HudLayoutEditor LayoutEditor;

        [Header("Style")]
        public Color SelectedTint = new Color(0.30f, 0.85f, 0.55f, 0.55f);
        public Color UnselectedTint = new Color(1f, 1f, 1f, 0.14f);

        UiPanel _returnTo;
        bool _suppress;

        protected override void Awake()
        {
            base.Awake();

            Wire(MasterSlider, v => Apply(s => s.SetMasterVolume(v)));
            Wire(MusicSlider, v => Apply(s => s.SetMusicVolume(v)));
            Wire(SfxSlider, v => Apply(s => s.SetSfxVolume(v)));
            Wire(SensitivitySlider, v => Apply(s => s.SetLookSensitivity(v)));

            if (HandednessButton != null)
                HandednessButton.onClick.AddListener(() => Apply(s => s.SetLeftHanded(!s.LeftHanded)));

            if (InvertYButton != null)
                InvertYButton.onClick.AddListener(() => Apply(s => s.SetInvertLookY(!s.InvertLookY)));

            if (LowButton != null) LowButton.onClick.AddListener(() => Apply(s => s.SetTier(QualityTier.Low)));
            if (MediumButton != null) MediumButton.onClick.AddListener(() => Apply(s => s.SetTier(QualityTier.Medium)));
            if (HighButton != null) HighButton.onClick.AddListener(() => Apply(s => s.SetTier(QualityTier.High)));

            if (BackButton != null) BackButton.onClick.AddListener(Hide);

            // Reference only, wired at runtime -- an AddListener lambda is never serialised
            // into the scene, so a listener attached by the builder exists in the editor
            // session and nowhere in the APK. Same trap as the pause button.
            if (CustomiseHudButton != null)
                CustomiseHudButton.onClick.AddListener(OpenLayoutEditor);
        }

        void Wire(Slider slider, System.Action<float> handler)
        {
            if (slider == null) return;

            slider.onValueChanged.AddListener(value =>
            {
                // Refresh() writes the sliders, which fires this again. Without the guard the
                // first drag would fight the panel for control of the value.
                if (_suppress) return;
                handler(value);
                RefreshReadouts();
            });
        }

        void Apply(System.Action<GameSettings> change)
        {
            var settings = GameSettings.Instance;
            if (settings == null) return;

            change(settings);
            Refresh();
        }

        /// <summary>Hands off to the touch-control layout editor, and comes back after.</summary>
        void OpenLayoutEditor()
        {
            if (LayoutEditor == null)
            {
                LayoutEditor = FindAnyObjectByType<HudLayoutEditor>(FindObjectsInactive.Include);
                if (LayoutEditor == null) return;
            }
            LayoutEditor.OpenFrom(this);
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

        // ----------------------------------------------------------------- display

        public void Refresh()
        {
            var settings = GameSettings.Instance;
            if (settings == null) return;

            _suppress = true;

            if (MasterSlider != null) MasterSlider.value = settings.MasterVolume;
            if (MusicSlider != null) MusicSlider.value = settings.MusicVolume;
            if (SfxSlider != null) SfxSlider.value = settings.SfxVolume;
            if (SensitivitySlider != null) SensitivitySlider.value = settings.LookSensitivity;

            _suppress = false;

            if (HandednessLabel != null)
                HandednessLabel.text = settings.LeftHanded ? "LAYOUT:  LEFT" : "LAYOUT:  RIGHT";

            if (InvertYLabel != null)
                InvertYLabel.text = settings.InvertLookY ? "INVERT Y:  ON" : "INVERT Y:  OFF";

            Tint(LowButton, settings.Tier == QualityTier.Low);
            Tint(MediumButton, settings.Tier == QualityTier.Medium);
            Tint(HighButton, settings.Tier == QualityTier.High);

            if (QualityDetail != null)
            {
                var tuner = PerformanceTuner.Instance;
                QualityDetail.text = tuner != null ? tuner.Describe() : settings.Tier.ToString();
            }

            RefreshReadouts();
        }

        void RefreshReadouts()
        {
            var settings = GameSettings.Instance;
            if (settings == null) return;

            SetPercent(MasterValue, settings.MasterVolume);
            SetPercent(MusicValue, settings.MusicVolume);
            SetPercent(SfxValue, settings.SfxVolume);

            if (SensitivityValue != null)
                SensitivityValue.text = settings.LookSensitivity.ToString("0.0") + "x";
        }

        static void SetPercent(Text label, float value01)
        {
            if (label != null) label.text = Mathf.RoundToInt(value01 * 100f) + "%";
        }

        void Tint(Button button, bool selected)
        {
            if (button == null || button.targetGraphic == null) return;
            button.targetGraphic.color = selected ? SelectedTint : UnselectedTint;
        }
    }
}
