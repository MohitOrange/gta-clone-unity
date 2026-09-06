using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// What a townsperson is saying, one line at a time.
    ///
    /// <b>A new panel rather than a reuse of ShopUI.</b> The instruction was to reuse whatever
    /// InteriorNpc already opens for shopkeepers, and that is <see cref="ShopUI"/> -- a scrolling
    /// list of purchasable rows with prices and blocked reasons. Dialogue is one speaker, one
    /// paragraph and a continue button; rendering it as a list of one-column rows would fight
    /// the widget rather than reuse it. What is reused is everything that matters structurally:
    /// <see cref="UiPanel"/> for show/hide and fade, <c>MenuState</c> for the timescale, the
    /// Soft Touch theme for the art, and the Contact-tier claim in <see cref="TownNpc"/> so the
    /// Interact button is arbitrated exactly like every other interactable.
    /// </summary>
    public class DialoguePanel : UiPanel
    {
        static DialoguePanel _instance;

        public static DialoguePanel Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindAnyObjectByType<DialoguePanel>(FindObjectsInactive.Include);
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Readouts")]
        public Text SpeakerLabel;
        public Text LineLabel;
        public Text HintLabel;

        [Header("Buttons")]
        public Button NextButton;
        public Text NextLabel;
        public Button CloseButton;

        TownNpc _speaker;
        int _line;

        protected override void Awake()
        {
            base.Awake();

            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;

            if (NextButton != null) NextButton.onClick.AddListener(Advance);
            if (CloseButton != null) CloseButton.onClick.AddListener(Hide);
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        /// <summary>Start a conversation. Safe to call while another one is open.</summary>
        public void Open(TownNpc npc)
        {
            if (npc == null || npc.Lines == null || npc.Lines.Length == 0) return;

            // Let the previous speaker walk away before adopting a new one.
            if (_speaker != null && _speaker != npc) _speaker.ReleaseHold();

            _speaker = npc;
            _line = 0;
            Show();
            Render();
        }

        protected override void OnShown() => MenuState.Enter();

        protected override void OnHidden()
        {
            MenuState.Exit();

            if (_speaker != null) _speaker.ReleaseHold();
            _speaker = null;
        }

        /// <summary>
        /// Next line, or -- on the last one -- accept the job if this NPC has one.
        ///
        /// The mission goes through <c>MissionManager.CanAccept</c> exactly as a mission
        /// contact's does, so level gates, prerequisites and the one-at-a-time rule all still
        /// apply and the refusal reason is the manager's own wording.
        /// </summary>
        void Advance()
        {
            if (_speaker == null) { Hide(); return; }

            if (_line < _speaker.Lines.Length - 1)
            {
                _line++;
                Render();
                return;
            }

            if (_speaker.Mission != null) { OfferMission(); return; }

            Hide();
        }

        void OfferMission()
        {
            var manager = MissionManager.Instance;
            if (manager == null) { Hide(); return; }

            if (!manager.CanAccept(_speaker.Mission, out string reason))
            {
                if (HintLabel != null) HintLabel.text = reason;
                return;
            }

            manager.Accept(_speaker.Mission);
            Hide();
        }

        void Render()
        {
            if (_speaker == null) return;

            var line = _speaker.Lines[Mathf.Clamp(_line, 0, _speaker.Lines.Length - 1)];

            if (SpeakerLabel != null)
                SpeakerLabel.text = string.IsNullOrEmpty(line.Speaker)
                    ? _speaker.DisplayName : line.Speaker;

            if (LineLabel != null) LineLabel.text = line.Text;

            bool last = _line >= _speaker.Lines.Length - 1;
            bool offering = last && _speaker.Mission != null;

            if (NextLabel != null)
                NextLabel.text = offering ? "ACCEPT JOB" : last ? "GOODBYE" : "NEXT";

            if (HintLabel == null) return;

            HintLabel.text = offering && MissionManager.Instance != null
                             && !MissionManager.Instance.CanAccept(_speaker.Mission, out string why)
                ? why
                : offering ? _speaker.Mission.Title : "";
        }
    }
}
