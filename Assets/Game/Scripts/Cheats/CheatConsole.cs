using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MiniGTA
{
    /// <summary>
    /// Listens for typed cheat codes, and for the hidden phrase that switches cheats on.
    ///
    /// <b>No UI, by design.</b> Item 9.4's standing resolution is that cheats are dev-gated
    /// and off by default, reachable through a hidden toggle -- so there is deliberately
    /// nothing on screen advertising that any of this exists. Typing is the whole interface,
    /// which is also how the genre has always done it.
    ///
    /// Two stages, matching the two gates in <see cref="CheatRegistry"/>. Until the unlock
    /// phrase is typed, every code is inert; a tester mashing letters cannot trip one, and a
    /// player who somehow got a development build still has to know the phrase. In a release
    /// build this component switches itself off in <c>Awake</c> and never reads input at all.
    ///
    /// Input comes through <see cref="Keyboard.onTextInput"/> rather than polling keys,
    /// because this project is Input System only -- the legacy <c>Input</c> class throws --
    /// and because text input already handles layout and repeat correctly.
    /// </summary>
    public class CheatConsole : MonoBehaviour
    {
        [Tooltip("Type this to arm the cheat system. Nothing else works until it is entered.")]
        public string UnlockPhrase = "IDDEV";

        [Tooltip("Type this to disarm again.")]
        public string LockPhrase = "IDOFF";

        [Tooltip("Longest code the buffer needs to hold. Codes longer than this cannot match.")]
        public int BufferLength = 24;

        [Tooltip("Seconds of no typing after which the buffer clears, so half-typed codes do "
                 + "not combine with whatever is typed next.")]
        public float IdleReset = 2.5f;

        readonly StringBuilder _buffer = new StringBuilder(32);
        float _lastKey;
        bool _listening;

        void Awake()
        {
            if (!CheatRegistry.Available)
            {
                // A release build carries this component but it never listens. Cheaper and
                // safer than relying on the component being stripped from the scene.
                enabled = false;
                return;
            }

            _listening = true;
        }

        void OnEnable()
        {
            if (_listening && Keyboard.current != null)
                Keyboard.current.onTextInput += OnText;
        }

        void OnDisable()
        {
            if (Keyboard.current != null)
                Keyboard.current.onTextInput -= OnText;
        }

        void Update()
        {
            // The keyboard can arrive after Awake (device connected later, or focus changes),
            // so the subscription is repaired rather than assumed.
            if (_listening && Keyboard.current != null && _buffer.Length == 0 && _lastKey == 0f)
            {
                Keyboard.current.onTextInput -= OnText;
                Keyboard.current.onTextInput += OnText;
                _lastKey = Time.unscaledTime;
            }

            if (_buffer.Length > 0 && Time.unscaledTime - _lastKey > IdleReset)
                _buffer.Clear();
        }

        void OnText(char c)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') return;

            _lastKey = Time.unscaledTime;
            _buffer.Append(char.ToUpperInvariant(c));

            if (_buffer.Length > BufferLength)
                _buffer.Remove(0, _buffer.Length - BufferLength);

            Evaluate();
        }

        void Evaluate()
        {
            string typed = _buffer.ToString();

            if (EndsWith(typed, UnlockPhrase))
            {
                CheatRegistry.SetEnabled(true);
                _buffer.Clear();
                return;
            }

            if (EndsWith(typed, LockPhrase))
            {
                CheatRegistry.SetEnabled(false);
                _buffer.Clear();
                return;
            }

            if (!CheatRegistry.Enabled) return;

            // Matched on suffix rather than equality so a code registers as soon as its last
            // character arrives, without needing Enter and without the buffer having to be
            // exactly the code -- which it never is, because the previous code is still in it.
            foreach (var cheat in CheatRegistry.All)
            {
                if (!EndsWith(typed, cheat.Code)) continue;

                CheatRegistry.Try(cheat.Code);
                _buffer.Clear();
                return;
            }
        }

        static bool EndsWith(string typed, string code)
            => !string.IsNullOrEmpty(code)
               && typed.Length >= code.Length
               && string.CompareOrdinal(typed, typed.Length - code.Length,
                                        code.ToUpperInvariant(), 0, code.Length) == 0;

        /// <summary>
        /// For a dev menu button to call, so the toggle does not depend on a keyboard at all.
        /// A phone has no keyboard, which is exactly where a hidden toggle is most needed.
        /// </summary>
        public void ToggleFromDevMenu() => CheatRegistry.SetEnabled(!CheatRegistry.Enabled);
    }
}
