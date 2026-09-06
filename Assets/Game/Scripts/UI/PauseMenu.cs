using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// The in-game menu: resume, map, settings, save, and the way back to the title.
    ///
    /// Saving is a button rather than something the menu does on its own. Autosave already
    /// runs on a timer and on losing focus, so a Save button here is for the player who wants
    /// to know for certain -- and telling them when it happened is most of its value.
    /// </summary>
    public class PauseMenu : UiPanel
    {
        static PauseMenu _instance;

        public static PauseMenu Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Buttons")]
        [Tooltip("The HUD pause button that opens this menu. Wired here, at runtime, on purpose: "
                 + "an onClick.AddListener call made by an editor build script is a non-persistent "
                 + "listener that is never serialised into the scene, so it exists only until the "
                 + "next domain reload and is absent entirely from a player build.")]
        public Button OpenButton;
        public Button ResumeButton;
        public Button MapButton;
        public Button SettingsButton;
        public Button SaveButton;
        public Button TitleButton;

        [Header("Panels")]
        public SettingsPanel Settings;
        public FullMapScreen Map;
        [Tooltip("The lobby, which replaced the Phase 7 title screen in Phase 12.")]
        public LobbyScreen Title;

        [Header("Readout")]
        public Text StatusLabel;

        float _statusTimer;

        protected override void Awake()
        {
            base.Awake();
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;

            if (OpenButton != null) OpenButton.onClick.AddListener(TogglePause);
            if (ResumeButton != null) ResumeButton.onClick.AddListener(Hide);
            if (MapButton != null) MapButton.onClick.AddListener(OpenMap);
            if (SettingsButton != null) SettingsButton.onClick.AddListener(OpenSettings);
            if (SaveButton != null) SaveButton.onClick.AddListener(SaveNow);
            if (TitleButton != null) TitleButton.onClick.AddListener(ReturnToTitle);
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        protected override void Update()
        {
            base.Update();

            if (_statusTimer <= 0f) return;

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f && StatusLabel != null) StatusLabel.text = "";
        }

        /// <summary>
        /// Handles Escape / the Android Back button. Driven by <see cref="MenuInput"/>, which
        /// keeps running while this panel is hidden.
        /// </summary>
        public void OnBackPressed()
        {
            // Back closes the deepest thing that is open, so the button never skips a level.
            if (Settings != null && Settings.IsOpen) { Settings.Hide(); return; }
            if (Map != null && Map.IsOpen) { Map.Hide(); return; }

            // The lobby has overlays of its own. Back closes those; from the lobby itself
            // there is nothing behind to go back to.
            if (Title != null && Title.IsOpen) { Title.OnBackPressed(); return; }

            TogglePause();
        }

        public void TogglePause()
        {
            if (IsOpen) { Hide(); return; }

            // Never over an ad: the overlay owns the timescale while it is up, and stacking a
            // second pause on it would leave the game frozen when the ad closed.
            if (AdService.Instance != null && AdService.Instance.AdShowing) return;
            if (Title != null && Title.IsOpen) return;

            Show();
        }

        protected override void OnShown()
        {
            MenuState.Enter();
            SetStatus("");
        }

        protected override void OnHidden() => MenuState.Exit();

        // ------------------------------------------------------------------ items

        void OpenMap()
        {
            if (Map == null) return;

            Hide();
            Map.OpenFrom(this);
        }

        void OpenSettings()
        {
            if (Settings == null) return;

            Hide();
            Settings.OpenFrom(this);
        }

        void SaveNow()
        {
            var save = SaveSystem.Instance;
            if (save == null) { SetStatus("Save system unavailable"); return; }

            save.Save();
            SetStatus("Progress saved");
        }

        void ReturnToTitle()
        {
            if (Title == null) return;

            // Leaving to the title is the one exit that is not autosaved for you elsewhere.
            SaveSystem.Instance?.Save();

            Hide();
            Title.Show();
        }

        void SetStatus(string text)
        {
            if (StatusLabel == null) return;

            StatusLabel.text = text;
            _statusTimer = string.IsNullOrEmpty(text) ? 0f : 2.5f;
        }
    }
}
