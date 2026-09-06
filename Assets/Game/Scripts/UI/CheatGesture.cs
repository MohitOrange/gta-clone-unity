using UnityEngine;
using UnityEngine.EventSystems;

namespace MiniGTA
{
    /// <summary>
    /// The hidden way in to <see cref="CheatPanel"/>: a run of quick taps on an invisible
    /// corner target.
    ///
    /// <b>Why a gesture rather than a button.</b> Cheats are a debug affordance, and a visible
    /// CHEATS button on the HUD is both clutter and an invitation. A tap run is discoverable if
    /// you are told about it and invisible if you are not, which is the correct trade for
    /// something gated behind a passphrase anyway.
    ///
    /// The target is deliberately small and pinned to a screen corner the thumb cluster does
    /// not use, so it cannot swallow a tap meant for the game.
    /// </summary>
    public class CheatGesture : MonoBehaviour, IPointerDownHandler
    {
        [Tooltip("Taps needed to open the panel.")]
        public int TapsRequired = 5;

        [Tooltip("Seconds allowed between taps. A slow run resets rather than accumulating, so "
                 + "an accidental tap now and then over a long session never adds up to a hit.")]
        public float TapWindow = 0.7f;

        public CheatPanel Panel;

        int _taps;
        float _lastTap;

        public void OnPointerDown(PointerEventData eventData)
        {
            // Unscaled: the panel is reachable from the pause menu, where timeScale is 0.
            float now = Time.unscaledTime;
            if (now - _lastTap > TapWindow) _taps = 0;

            _lastTap = now;
            _taps++;

            if (_taps < TapsRequired) return;

            _taps = 0;
            if (Panel == null) Panel = Object.FindAnyObjectByType<CheatPanel>();
            if (Panel != null) Panel.Show();
        }
    }
}
