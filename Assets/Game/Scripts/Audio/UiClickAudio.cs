using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Gives every button on the canvas a click, without any of them knowing about audio.
    ///
    /// Attaching a listener from one place beats adding a call to each button's handler: new
    /// panels get their clicks for free, and no future button can be forgotten. The on-screen
    /// driving controls are excluded -- those fire continuously while held, and a click on
    /// every frame of acceleration would be intolerable.
    /// </summary>
    public class UiClickAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float Volume = 0.5f;

        void Start() => Attach();

        /// <summary>Hooks every button under this object. Safe to call again after new UI is built.</summary>
        public void Attach()
        {
            var buttons = GetComponentsInChildren<Button>(includeInactive: true);

            foreach (var button in buttons)
            {
                // A HudButton is a hold-capable gameplay control, not a menu item.
                if (button.GetComponent<HudButton>() != null) continue;

                var captured = button;
                captured.onClick.AddListener(() =>
                {
                    if (captured.interactable) AudioManager.Instance?.PlayUi(Sfx.UiClick, Volume);
                });
            }
        }
    }
}
