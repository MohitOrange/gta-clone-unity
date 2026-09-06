using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// A full-screen panel that fades in and out.
    ///
    /// Everything here runs on unscaled time, because every panel that uses it is shown with
    /// <c>Time.timeScale</c> at zero -- a menu that fades using scaled time never appears.
    ///
    /// Subclasses must do their wiring in an <c>Awake</c> override, not in <c>Start</c>: a
    /// panel that starts hidden is deactivated during Awake, and Unity does not run Start on a
    /// deactivated object -- it would fire the first time the panel opened, long after
    /// something else had already tried to use it.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UiPanel : MonoBehaviour
    {
        public float FadeSpeed = 6f;
        [Tooltip("Start hidden. Panels are built visible so they can be laid out in the editor.")]
        public bool HiddenOnStart = true;

        CanvasGroup _group;
        bool _open;

        public bool IsOpen => _open;

        /// <summary>Fired once the panel has been asked to close, not when the fade finishes.</summary>
        public event System.Action Closed;

        protected virtual void Awake()
        {
            _group = GetComponent<CanvasGroup>();

            if (HiddenOnStart)
            {
                _open = false;
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
                _group.interactable = false;
                // Deactivated as well as transparent: an invisible full-screen panel still
                // costs a canvas rebuild and still swallows taps if anything re-enables it.
                gameObject.SetActive(false);
            }
        }

        protected virtual void Update()
        {
            float target = _open ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, FadeSpeed * Time.unscaledDeltaTime);

            if (!_open && _group.alpha <= 0.001f) gameObject.SetActive(false);
        }

        public void Show()
        {
            if (_open) return;

            gameObject.SetActive(true);
            _open = true;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            OnShown();
        }

        public void Hide()
        {
            if (!_open) return;

            _open = false;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            OnHidden();
            Closed?.Invoke();
        }

        public void Toggle()
        {
            if (_open) Hide(); else Show();
        }

        /// <summary>Hook for subclasses to refresh their contents as they appear.</summary>
        protected virtual void OnShown() { }
        protected virtual void OnHidden() { }
    }
}
