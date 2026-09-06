using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Who the player is: the name they chose, and what the save file knows about them.
    ///
    /// Every figure on this screen is read from something that was already being tracked --
    /// <see cref="PlayerProgress"/>, <see cref="Garage"/>, <see cref="SaveSystem"/>. Nothing
    /// here counts anything new, and where a statistic a profile screen would normally show
    /// does not exist in this game (time played, kills, distance driven, arrests), it is
    /// absent rather than invented. See DECISIONS.md D40.
    /// </summary>
    public class ProfilePanel : UiPanel
    {
        [Header("Identity")]
        public InputField NameField;
        public Text LevelLabel;
        public Text XpLabel;
        public Image XpBar;

        [Header("Stats")]
        [Tooltip("One caption/value pair per row, filled in order by Refresh.")]
        public Text[] StatCaptions = new Text[0];
        public Text[] StatValues = new Text[0];

        [Header("Actions")]
        public Button NewGameButton;
        public Text NewGameLabel;
        public Button CloseButton;
        public Text StatusLabel;

        [Header("Links")]
        [Tooltip("Asked to start the new game, so the reset lives in exactly one place.")]
        public LobbyScreen Lobby;

        [Header("Confirmation")]
        [Tooltip("Seconds the New Game button stays armed after the first tap.")]
        public float ConfirmSeconds = 4f;

        float _confirmTimer;
        float _statusTimer;

        readonly List<(string caption, string value)> _stats = new List<(string, string)>();

        protected override void Awake()
        {
            base.Awake();

            if (NewGameButton != null) NewGameButton.onClick.AddListener(NewGamePressed);
            if (CloseButton != null) CloseButton.onClick.AddListener(Hide);
        }

        protected override void OnShown()
        {
            MenuState.Enter();
            Disarm();
            SetStatus("");
            Refresh();
        }

        protected override void OnHidden()
        {
            MenuState.Exit();
            Disarm();
        }

        protected override void Update()
        {
            base.Update();

            if (_confirmTimer > 0f)
            {
                _confirmTimer -= Time.unscaledDeltaTime;
                if (_confirmTimer <= 0f) Disarm();
            }

            if (_statusTimer <= 0f) return;

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f) SetStatus("");
        }

        // --------------------------------------------------------------- readouts

        public void Refresh()
        {
            var progress = PlayerProgress.Instance;
            if (progress == null) return;

            if (LevelLabel != null) LevelLabel.text = "LEVEL " + progress.Level;

            if (XpLabel != null)
                XpLabel.text = progress.Level >= progress.MaxLevel
                    ? progress.Xp.ToString("N0") + " XP   (max level)"
                    : progress.Xp.ToString("N0") + " XP   -   "
                      + progress.XpToNextLevel.ToString("N0") + " to level " + (progress.Level + 1);

            if (XpBar != null) XpBar.fillAmount = progress.LevelProgress;

            _stats.Clear();
            _stats.Add(("MONEY", "$" + progress.Money.ToString("N0")));
            _stats.Add(("MISSIONS COMPLETED", progress.CompletedMissions.Count.ToString()));
            _stats.Add(("OUTFITS OWNED", OwnedSkinCount(progress) + " / " + (SkinLibrary.Skins.Count + 1)));
            _stats.Add(("ITEMS BOUGHT", progress.OwnedItems.Count.ToString()));
            _stats.Add(("UNLOCKS EARNED", progress.Unlocked.Count.ToString()));
            _stats.Add(("PROPERTIES", progress.OwnedProperties.Count.ToString()));

            var garage = Garage.Instance;
            _stats.Add(("VEHICLES IN GARAGE", garage != null ? garage.Owned.Count.ToString() : "0"));
            _stats.Add(("LAST SAVED", LastSaved()));

            for (int i = 0; i < StatCaptions.Length; i++)
            {
                bool has = i < _stats.Count;

                if (StatCaptions[i] != null)
                {
                    StatCaptions[i].text = has ? _stats[i].caption : "";
                    StatCaptions[i].gameObject.SetActive(has);
                }

                if (i < StatValues.Length && StatValues[i] != null)
                {
                    StatValues[i].text = has ? _stats[i].value : "";
                    StatValues[i].gameObject.SetActive(has);
                }
            }
        }

        static int OwnedSkinCount(PlayerProgress progress)
        {
            int owned = 1;   // the model as authored is always available
            foreach (var item in SkinLibrary.Skins)
                if (progress.Owns(item.Id)) owned++;

            return owned;
        }

        /// <summary>
        /// When the save file was last written, in local time.
        ///
        /// Read off the file rather than tracked in memory, because "last saved" has to be
        /// true across an app kill, which is the only time the answer is interesting.
        /// </summary>
        static string LastSaved()
        {
            var save = SaveSystem.Instance;
            if (save == null || !save.SaveExists) return "never";

            try
            {
                var stamp = System.IO.File.GetLastWriteTime(save.SavePath);
                return stamp.ToString("d MMM, HH:mm");
            }
            catch (System.Exception)
            {
                // A readable save whose timestamp cannot be read is not worth an error dialog.
                return "unknown";
            }
        }

        // ---------------------------------------------------------------- new game

        /// <summary>
        /// Two taps, not one.
        ///
        /// This erases the save file. A confirmation step on the button itself is cheaper than
        /// a modal, and it is the only destructive control anywhere in the front end.
        /// </summary>
        void NewGamePressed()
        {
            if (_confirmTimer <= 0f)
            {
                _confirmTimer = ConfirmSeconds;
                if (NewGameLabel != null) NewGameLabel.text = "TAP AGAIN TO ERASE";
                SetStatus("This deletes your saved game.");
                return;
            }

            Disarm();

            if (Lobby == null) { SetStatus("Lobby unavailable"); return; }

            Lobby.StartNewGame();
            Hide();
        }

        void Disarm()
        {
            _confirmTimer = 0f;
            if (NewGameLabel != null) NewGameLabel.text = "NEW GAME";
        }

        void SetStatus(string text)
        {
            if (StatusLabel != null) StatusLabel.text = text;
            _statusTimer = string.IsNullOrEmpty(text) ? 0f : 4f;
        }
    }
}
