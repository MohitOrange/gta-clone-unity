using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MiniGTA
{
    /// <summary>
    /// Watches for the Back key on behalf of the menus.
    ///
    /// It cannot live on the pause menu itself: a hidden panel is deactivated, so its Update
    /// stops running, and Escape would be able to close the pause menu but never open it. This
    /// sits on the canvas, which is always on.
    /// </summary>
    public class MenuInput : MonoBehaviour
    {
        void Update()
        {
            if (!BackPressed()) return;

            var pause = PauseMenu.Instance;
            if (pause != null) pause.OnBackPressed();
        }

        /// <summary>Escape on desktop. Android maps its hardware Back button to the same key.</summary>
        static bool BackPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return false;
#endif
        }
    }
}
