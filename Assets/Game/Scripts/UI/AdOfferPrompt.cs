using System;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// The two-button "watch a video for X?" prompt.
    ///
    /// Times out on its own rather than waiting forever. An offer that blocks the game until
    /// the player answers turns an optional reward into a modal demand, which is exactly the
    /// behaviour the brief's frequency cap is trying to avoid elsewhere.
    /// </summary>
    public class AdOfferPrompt : MonoBehaviour
    {
        static AdOfferPrompt _instance;

        public static AdOfferPrompt Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<AdOfferPrompt>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Wiring")]
        public CanvasGroup Panel;
        public Text TitleLabel;
        public Text BodyLabel;
        public Button AcceptButton;
        public Text AcceptLabel;
        public Button DeclineButton;
        public Text DeclineLabel;
        public Image TimeoutFill;

        Action _onAccept;
        Action _onDecline;
        float _timeout;
        float _elapsed;
        bool _showing;

        public bool IsShowing => _showing;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            if (AcceptButton != null) AcceptButton.onClick.AddListener(Accept);
            if (DeclineButton != null) DeclineButton.onClick.AddListener(Decline);
            SetVisible(false);
        }

        void Update()
        {
            if (!_showing) return;

            // Unscaled, so the prompt still expires while the game is paused behind an ad.
            _elapsed += Time.unscaledDeltaTime;

            if (TimeoutFill != null)
                TimeoutFill.fillAmount = 1f - Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _timeout));

            if (_elapsed >= _timeout) Decline();
        }

        public void Offer(string title, string body, string acceptText, string declineText,
                          float timeout, Action onAccept, Action onDecline)
        {
            // Never stack prompts: the newer offer would orphan the older one's callbacks.
            if (_showing) Decline();

            _onAccept = onAccept;
            _onDecline = onDecline;
            _timeout = timeout;
            _elapsed = 0f;

            if (TitleLabel != null) TitleLabel.text = title;
            if (BodyLabel != null) BodyLabel.text = body;
            if (AcceptLabel != null) AcceptLabel.text = acceptText;
            if (DeclineLabel != null) DeclineLabel.text = declineText;

            SetVisible(true);
        }

        void Accept() => Close(true);
        void Decline() => Close(false);

        void Close(bool accepted)
        {
            if (!_showing) return;

            var accept = _onAccept;
            var decline = _onDecline;
            _onAccept = null;
            _onDecline = null;

            SetVisible(false);

            // Fire after hiding, so a callback that opens an ad does not fight this panel.
            if (accepted) accept?.Invoke();
            else decline?.Invoke();
        }

        void SetVisible(bool visible)
        {
            _showing = visible;

            if (Panel == null) return;
            Panel.alpha = visible ? 1f : 0f;
            Panel.blocksRaycasts = visible;
            Panel.interactable = visible;
        }
    }
}
