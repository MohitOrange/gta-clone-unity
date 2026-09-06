using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// The screen that lets a player actually enter a cheat code.
    ///
    /// <b>Why this exists.</b> 67 cheats are registered and verified, and until now there was no
    /// way to type one on the target platform. <see cref="CheatConsole"/> listens to
    /// <c>Keyboard.onTextInput</c>, which is the right GTA-style mechanic and works on desktop --
    /// but the PRIMARY target is Android, where there is no keyboard until something opens one.
    /// So the whole cheat system was unreachable on the platform the project is built for, and
    /// <c>CheatConsole</c> itself was attached to nothing in the scene besides.
    ///
    /// This panel is deliberately plain: one field, one confirm, one result line. It is a debug
    /// affordance, not a feature screen, and the gate below decides who ever sees it.
    /// </summary>
    public class CheatPanel : UiPanel
    {
        [Header("Wiring")]
        public InputField Field;
        public Button EnterButton;
        public Button CloseButton;
        public Text Result;
        public Text GateLabel;

        [Tooltip("Seconds the result line stays up before it clears itself.")]
        public float ResultSeconds = 4f;

        float _resultClearAt;

        protected override void Awake()
        {
            base.Awake();

            // Wired here rather than by the builder. A listener added from an editor script is
            // a non-persistent UnityEvent listener: it is never serialised into the scene, so it
            // works in the editor and the button is dead in the APK. That is BUG-004 from
            // PHASE8, and it cost a whole device pass -- every button in this project is wired
            // at runtime for that reason.
            if (EnterButton != null) EnterButton.onClick.AddListener(Submit);
            if (CloseButton != null) CloseButton.onClick.AddListener(Hide);
            if (Field != null) Field.onEndEdit.AddListener(_ => Submit());

            CheatRegistry.EnabledChanged += OnGateChanged;
            RefreshGate();
        }

        void OnDestroy() => CheatRegistry.EnabledChanged -= OnGateChanged;

        void OnGateChanged(bool on) => RefreshGate();

        // LateUpdate, not Update: UiPanel.Update drives the fade, and an Update here would
        // hide it -- the panel would never fade in or out.
        void LateUpdate()
        {
            if (_resultClearAt > 0f && Time.unscaledTime >= _resultClearAt)
            {
                _resultClearAt = 0f;
                if (Result != null) Result.text = "";
            }
        }

        /// <summary>
        /// Runs whatever is in the field.
        ///
        /// The unlock and lock phrases are handled here as well as in <see cref="CheatConsole"/>,
        /// because a phone player can only reach this panel -- if the gate could only be opened
        /// by typing IDDEV on a physical keyboard, the panel would be permanently useless on the
        /// platform it was built for.
        /// </summary>
        public void Submit()
        {
            if (Field == null) return;

            string code = Field.text != null ? Field.text.Trim().ToUpperInvariant() : "";
            Field.text = "";
            if (code.Length == 0) return;

            var console = Object.FindAnyObjectByType<CheatConsole>();
            string unlock = console != null ? console.UnlockPhrase.ToUpperInvariant() : "IDDEV";
            string lockPhrase = console != null ? console.LockPhrase.ToUpperInvariant() : "IDOFF";

            if (code == unlock) { CheatRegistry.SetEnabled(true); Say("Cheats ENABLED"); return; }
            if (code == lockPhrase) { CheatRegistry.SetEnabled(false); Say("Cheats DISABLED"); return; }

            if (!CheatRegistry.Enabled)
            {
                Say("Locked. Enter " + unlock + " first.");
                return;
            }

            var outcome = CheatRegistry.Try(code);
            switch (outcome)
            {
                case CheatResult.Applied: Say(code + " applied"); break;
                case CheatResult.Failed: Say(code + " did nothing here"); break;
                case CheatResult.Unknown: Say("No such code: " + code); break;
                case CheatResult.Disabled: Say("Cheats are locked"); break;
                case CheatResult.Unavailable: Say("Cheats are not available in this build"); break;
                default: Say(outcome.ToString()); break;
            }
        }

        void Say(string message)
        {
            if (Result == null) return;
            Result.text = message;
            _resultClearAt = Time.unscaledTime + ResultSeconds;
        }

        void RefreshGate()
        {
            if (GateLabel == null) return;
            GateLabel.text = CheatRegistry.Enabled
                           ? CheatRegistry.Count + " codes available"
                           : "Locked";
        }

        protected override void OnShown()
        {
            RefreshGate();
            if (Result != null) Result.text = "";
            if (Field != null) { Field.text = ""; Field.ActivateInputField(); }
        }
    }
}
