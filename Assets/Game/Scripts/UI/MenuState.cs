using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Whether any menu is open, and everything that follows from that.
    ///
    /// A counter rather than a flag because menus nest: the pause menu opens settings, which
    /// closes the pause menu, which would otherwise unpause the game underneath the settings
    /// panel the player is still reading. Time only starts again when the last one closes.
    /// </summary>
    public static class MenuState
    {
        static int _depth;
        static float _resumeTimeScale = 1f;

        public static bool AnyOpen => _depth > 0;

        /// <summary>Raised when the game pauses for a menu and when it resumes.</summary>
        public static event System.Action<bool> Changed;

        /// <summary>
        /// Statics outlive a Play mode session when domain reloading is off, which would leave
        /// the game convinced a menu from the last run is still open.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            _depth = 0;
            _resumeTimeScale = 1f;
            Changed = null;
        }

        public static void Enter()
        {
            _depth++;
            if (_depth != 1) return;

            // Remember what time was running at rather than assuming 1. An ad overlay or a
            // slow-motion effect could own the timescale when the player hits pause.
            _resumeTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;

            HudLayout.Instance?.SetControlsVisible(false);
            Changed?.Invoke(true);
        }

        public static void Exit()
        {
            if (_depth == 0) return;

            _depth--;
            if (_depth != 0) return;

            Time.timeScale = _resumeTimeScale;

            HudLayout.Instance?.SetControlsVisible(true);
            Changed?.Invoke(false);
        }

        /// <summary>Force everything closed, for a state change that must not leave time stopped.</summary>
        public static void ForceClear()
        {
            if (_depth == 0) return;

            _depth = 0;
            Time.timeScale = _resumeTimeScale;

            HudLayout.Instance?.SetControlsVisible(true);
            Changed?.Invoke(false);
        }
    }
}
