using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// The lobby: the first thing the game shows, and the only way into the city.
    ///
    /// Replaces the Phase 7 title screen -- a vertical stack of Continue / New Game /
    /// Settings / Quit -- with a side icon rail, a character standing on a lit stage, and one
    /// large PLAY button. <b>The session logic underneath is the Phase 7 logic, unchanged:</b>
    /// <see cref="StartNewGame"/> is the old New Game verbatim, and Play still enters the world
    /// by hiding this panel rather than by loading a scene.
    ///
    /// That last part is worth being explicit about. The city is already loaded -- the game is
    /// a single scene by design (PHASE7), because a separate menu scene would build the whole
    /// world twice, once as a backdrop and again when the player pressed Play, which on a phone
    /// is a long black screen for no gain. So "PLAY loads into City.unity" means the world this
    /// panel is sitting on top of starts running.
    ///
    /// Sub-screens open <i>over</i> the lobby rather than replacing it, so the MenuState depth
    /// goes 1 -> 2 -> 1 and the timescale is only handed back when the lobby itself closes.
    /// </summary>
    public class LobbyScreen : UiPanel
    {
        [Header("Primary action")]
        public Button PlayButton;
        [Tooltip("The line under PLAY: what pressing it is about to do.")]
        public Text PlayCaption;

        [Header("Side rail")]
        public Button CharacterButton;
        public Button ProfileButton;
        public Button SettingsButton;
        public Button QuitButton;

        [Header("Top bar")]
        [Tooltip("Opens the profile screen. The name itself is edited in place by the "
                 + "ProfileNameField on the chip, so this is the shoulder tap, not the name.")]
        public Button ProfileChipButton;
        public Text LevelLabel;
        public Text MoneyLabel;
        public Text VersionLabel;

        [Header("Panels")]
        public CharacterSelectPanel CharacterSelect;
        public ProfilePanel Profile;
        public SettingsPanel Settings;

        [Header("Stage")]
        public LobbyPreview Preview;

        [Header("Behaviour")]
        [Tooltip("Show the lobby on launch. Off drops straight into the world, for testing.")]
        public bool ShowOnLaunch = true;

        Vector3 _startPosition;
        Quaternion _startRotation;
        GameObject _player;

        protected override void Awake()
        {
            base.Awake();

            // Wired here, at runtime, on purpose. An onClick.AddListener made by an editor
            // build script is a non-persistent listener: it is never serialised into the scene,
            // so it survives until the next domain reload in the editor and does not exist at
            // all in a player build. Four HUD buttons shipped dead that way in Phase 8.
            if (PlayButton != null) PlayButton.onClick.AddListener(Play);
            if (CharacterButton != null) CharacterButton.onClick.AddListener(OpenCharacterSelect);
            if (ProfileButton != null) ProfileButton.onClick.AddListener(OpenProfile);
            if (ProfileChipButton != null) ProfileChipButton.onClick.AddListener(OpenProfile);
            if (SettingsButton != null) SettingsButton.onClick.AddListener(OpenSettings);
            if (QuitButton != null) QuitButton.onClick.AddListener(Quit);

            if (VersionLabel != null) VersionLabel.text = "v" + Application.version;

            // The lobby is built and shown during Awake, which is before SaveSystem.Start has
            // read the file -- so the first Refresh sees a freshly constructed PlayerProgress
            // and would advertise "level 1, $250" over a level 6 save. Loaded fires the moment
            // the real numbers are in, which is still before the first frame is drawn.
            var save = SaveSystem.Instance;
            if (save != null) save.Loaded += OnSaveLoaded;

            // Refresh the top bar as each overlay closes: buying nothing in the wardrobe still
            // changes what is worn, and the profile screen can start a whole new game.
            if (CharacterSelect != null) CharacterSelect.Closed += Refresh;
            if (Profile != null) Profile.Closed += Refresh;
            if (Settings != null) Settings.Closed += Refresh;

            _player = GameObject.FindWithTag("Player");
            if (_player != null)
            {
                // The scene's authored spawn, captured before a save can move the player off it.
                _startPosition = _player.transform.position;
                _startRotation = _player.transform.rotation;
            }

            if (ShowOnLaunch) Show();
        }

        void OnDestroy()
        {
            var save = SaveSystem.Instance;
            if (save != null) save.Loaded -= OnSaveLoaded;
        }

        void OnSaveLoaded(SaveData data) => Refresh();

        /// <summary>
        /// One more refresh once every Awake in the scene has run.
        ///
        /// Awake order is not defined, and PlayerProgress sets its starting money in its own
        /// Awake -- so a lobby that woke first read zero and a first-time player was shown
        /// "$0" instead of "$250". With a save, SaveSystem.Loaded corrects it; without one,
        /// nothing did. Start covers exactly that gap.
        /// </summary>
        void Start() => Refresh();

        protected override void OnShown()
        {
            MenuState.Enter();
            Preview?.SetLive(true);
            Refresh();
        }

        protected override void OnHidden()
        {
            // The stage goes down before the depth is handed back, so the world camera has its
            // culling mask again by the time anything reacts to the game being unpaused.
            Preview?.SetLive(false);
            MenuState.Exit();
        }

        /// <summary>
        /// Escape / Android Back, routed here by <see cref="PauseMenu.OnBackPressed"/>.
        ///
        /// Closes the deepest overlay. There is deliberately nothing to go back to from the
        /// lobby itself -- it is the root of the front end.
        /// </summary>
        public void OnBackPressed()
        {
            if (Settings != null && Settings.IsOpen) { Settings.Hide(); return; }
            if (CharacterSelect != null && CharacterSelect.IsOpen) { CharacterSelect.Hide(); return; }
            if (Profile != null && Profile.IsOpen) Profile.Hide();
        }

        // ---------------------------------------------------------------- readouts

        void Refresh()
        {
            var progress = PlayerProgress.Instance;
            var save = SaveSystem.Instance;
            bool hasSave = save != null && save.SaveExists;

            if (LevelLabel != null)
                LevelLabel.text = progress != null ? progress.Level.ToString() : "1";

            if (MoneyLabel != null)
                MoneyLabel.text = progress != null ? progress.Money.ToString("N0") : "0";

            if (PlayCaption == null) return;

            PlayCaption.text = !hasSave
                ? "Start a new game"
                : progress == null
                    ? "Continue"
                    : "Continue  -  level " + progress.Level
                      + "  -  $" + progress.Money.ToString("N0")
                      + "  -  " + progress.CompletedMissions.Count + " jobs done";
        }

        // ------------------------------------------------------------------ items

        /// <summary>
        /// Into the city.
        ///
        /// One button rather than the old Continue / New Game pair: with no save it is a new
        /// game, with a save it is a continue, and the caption underneath says which. Starting
        /// a fresh run over an existing save is a destructive act and lives behind a
        /// confirmation on the profile screen, not next to the button people press every time.
        /// </summary>
        void Play()
        {
            var save = SaveSystem.Instance;
            bool hasSave = save != null && save.SaveExists;

            // Hide first. It puts the world camera's culling mask and the day/night cycle back
            // before anything below asks for Camera.main -- which is disabled-camera-sensitive
            // and is exactly what AlignBehindTarget needs.
            Hide();

            if (!hasSave) ResetToNewGame();
        }

        /// <summary>
        /// Wipes the save and puts the world back to its opening state.
        ///
        /// The Phase 7 New Game, unchanged. The scene is not reloaded: everything a run
        /// accumulates lives in PlayerProgress, the mission manager and the garage, so
        /// resetting those three and moving the player home is the whole of a new game -- and
        /// it avoids a reload the player would sit through on a phone.
        ///
        /// Public because the profile screen offers it behind a confirmation; it hides the
        /// lobby itself, so a caller does not have to remember to.
        /// </summary>
        public void StartNewGame()
        {
            Hide();
            ResetToNewGame();
        }

        void ResetToNewGame()
        {
            MissionManager.Instance?.AbandonActive();
            PlayerProgress.Instance?.ResetProgress();
            Garage.Instance?.Restore(System.Array.Empty<OwnedVehicleData>());

            SaveSystem.Instance?.Delete();

            if (_player != null)
            {
                var driving = _player.GetComponent<PlayerVehicleController>();
                if (driving != null && driving.IsDriving) driving.Exit();

                var controller = _player.GetComponent<PlayerController>();
                if (controller != null) controller.Warp(_startPosition);
                else _player.transform.position = _startPosition;

                _player.transform.rotation = _startRotation;

                var health = _player.GetComponent<Health>();
                if (health != null) health.Revive(1f);

                var combat = _player.GetComponent<PlayerCombat>();
                if (combat != null)
                {
                    combat.HasPistol = false;
                    combat.Ammo = 0;
                    combat.Mode = WeaponMode.Unarmed;
                }
            }

            HeatSystem.Instance?.Clear();
            PoliceDispatcher.Instance?.StandDown();

            Camera.main?.GetComponent<ThirdPersonCamera>()?.AlignBehindTarget();
        }

        // ------------------------------------------------------------- sub-screens

        /// <summary>
        /// Opens an overlay without closing the lobby.
        ///
        /// The pause menu hides itself before opening settings, because it is a modal over
        /// live gameplay. The lobby is the opposite: the rail icons are shortcuts into parts of
        /// the lobby, and the character standing behind a half-transparent panel is the point.
        /// MenuState is a counter, so nesting simply takes the depth to 2 and the timescale
        /// stays where it is.
        /// </summary>
        void OpenCharacterSelect() => Raise(CharacterSelect)?.Show();

        void OpenProfile()
        {
            if (Profile == null) return;

            Profile.Lobby = this;
            Raise(Profile).Show();
        }

        void OpenSettings()
        {
            // OpenFrom re-shows its caller on close. The lobby was never hidden, so that call
            // lands on an already-open panel and does nothing -- which is the behaviour wanted.
            Raise(Settings)?.OpenFrom(this);
        }

        /// <summary>
        /// Brings a panel to the front of the canvas before it opens.
        ///
        /// Sibling order is draw order, and the settings panel is built early -- it is shared
        /// with the pause menu, which hides itself before opening it and so never cared. The
        /// lobby does not hide, so an overlay that stays where it was built is drawn
        /// underneath the lobby and is invisible. Asking last wins.
        /// </summary>
        static T Raise<T>(T panel) where T : UiPanel
        {
            if (panel != null) panel.transform.SetAsLastSibling();
            return panel;
        }

        void Quit()
        {
            SaveSystem.Instance?.Save();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
