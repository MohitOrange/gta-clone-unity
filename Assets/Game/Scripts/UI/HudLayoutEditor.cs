using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// The screen the player customises their touch controls on.
    ///
    /// <b>It edits the real HUD, in place.</b> There is no mock-up of the controls to drag
    /// around: the actual joystick and the actual fire button are what move, at the size they
    /// will be during play, over the actual game. A preview would have to be kept in step with
    /// the real layout by hand, and would still not answer the only question the player is
    /// asking -- can my thumb reach it.
    ///
    /// That means this panel deliberately leaves the controls on screen while it is open, which
    /// is the opposite of what every other menu does. See <see cref="HudLayout"/>.
    /// </summary>
    public class HudLayoutEditor : UiPanel
    {
        [Header("Wiring")]
        public Slider SizeSlider;
        public Text SelectedLabel;
        public Text SizeValue;
        public Button ResetButton;
        public Button DoneButton;

        /// <summary>The control the size slider is currently pointed at.</summary>
        public static string Selected { get; private set; }

        /// <summary>Raised when the player picks a different control to adjust.</summary>
        public static event System.Action SelectionChanged;

        UiPanel _returnTo;

        /// <summary>Opens the editor, remembering which screen to go back to.</summary>
        public void OpenFrom(UiPanel returnTo)
        {
            _returnTo = returnTo;
            if (returnTo != null) returnTo.Hide();
            Show();
        }

        protected override void Awake()
        {
            base.Awake();

            if (DoneButton != null) DoneButton.onClick.AddListener(Hide);
            if (ResetButton != null) ResetButton.onClick.AddListener(ResetLayout);
            if (SizeSlider != null) SizeSlider.onValueChanged.AddListener(OnSizeChanged);
        }

        protected override void OnShown()
        {
            base.OnShown();

            // Nothing selected until the player touches a control, so the slider cannot resize
            // something they have not looked at.
            Select(null);
            HudCustomisable.SetEditMode(true);
        }

        protected override void OnHidden()
        {
            base.OnHidden();
            HudCustomisable.SetEditMode(false);

            // Write immediately rather than waiting for the autosave. Someone who spends two
            // minutes arranging their controls and then closes the app should not lose it.
            SaveSystem.Instance?.Save();

            if (_returnTo != null) { _returnTo.Show(); _returnTo = null; }
        }

        /// <summary>Called by a control when it is touched in edit mode.</summary>
        public static void Select(string id)
        {
            Selected = id;
            SelectionChanged?.Invoke();
        }

        void OnEnable() => SelectionChanged += Refresh;
        void OnDisable() => SelectionChanged -= Refresh;

        void Refresh()
        {
            bool has = !string.IsNullOrEmpty(Selected);

            if (SelectedLabel != null)
                SelectedLabel.text = has ? HudLayoutStore.Label(Selected)
                                         : "TOUCH A CONTROL TO SELECT IT";

            if (SizeSlider != null)
            {
                SizeSlider.interactable = has;
                // SetValueWithoutNotify, or selecting a control would immediately resize it to
                // whatever the slider happened to be showing for the last one.
                SizeSlider.SetValueWithoutNotify(has ? HudLayoutStore.Get(Selected).Scale : 1f);
            }

            UpdateSizeValue();
        }

        void OnSizeChanged(float value)
        {
            if (string.IsNullOrEmpty(Selected)) return;

            var entry = HudLayoutStore.Get(Selected);
            HudLayoutStore.Set(Selected, entry.OffsetX, entry.OffsetY, value);
            UpdateSizeValue();
        }

        void UpdateSizeValue()
        {
            if (SizeValue == null) return;
            SizeValue.text = string.IsNullOrEmpty(Selected)
                ? "--"
                : Mathf.RoundToInt(HudLayoutStore.Get(Selected).Scale * 100f) + "%";
        }

        void ResetLayout()
        {
            HudLayoutStore.ResetAll();
            Select(null);
        }
    }
}
