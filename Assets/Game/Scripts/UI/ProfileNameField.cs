using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// Binds one text box to the player's profile name.
    ///
    /// A component rather than code inside the lobby because the name is editable in two
    /// places -- the chip in the lobby's top bar and the profile screen -- and two copies of
    /// "read it, clean it, write it, save it" is exactly how those two boxes end up disagreeing
    /// about what the name is. Both carry this; <see cref="PlayerProgress"/> is the only
    /// thing that holds the value.
    /// </summary>
    [RequireComponent(typeof(InputField))]
    public class ProfileNameField : MonoBehaviour
    {
        InputField _field;
        bool _suppress;

        void Awake()
        {
            _field = GetComponent<InputField>();
            _field.characterLimit = PlayerProgress.MaxProfileNameLength;
            _field.lineType = InputField.LineType.SingleLine;
            _field.onEndEdit.AddListener(Commit);
        }

        void OnEnable()
        {
            var progress = PlayerProgress.Instance;
            if (progress != null) progress.ProfileNameChanged += OnNameChanged;
            Refresh();
        }

        void OnDisable()
        {
            var progress = PlayerProgress.Instance;
            if (progress != null) progress.ProfileNameChanged -= OnNameChanged;
        }

        void OnNameChanged(string name) => Refresh();

        void Refresh()
        {
            if (_field == null) return;

            var progress = PlayerProgress.Instance;
            string name = progress != null ? progress.ProfileName : PlayerProgress.DefaultProfileName;
            if (_field.text == name) return;

            // Writing text fires onValueChanged, never onEndEdit, so this cannot loop back
            // through Commit -- but the guard costs nothing and says the intent out loud.
            _suppress = true;
            _field.text = name;
            _suppress = false;
        }

        /// <summary>
        /// Fires when the box loses focus or the player presses Enter / Done.
        ///
        /// The typed value goes through <see cref="PlayerProgress.CleanProfileName"/> and the
        /// box is then rewritten with the result, so an all-spaces name visibly becomes the
        /// default rather than silently reverting the next time the screen opens.
        /// </summary>
        void Commit(string typed)
        {
            if (_suppress) return;

            var progress = PlayerProgress.Instance;
            if (progress == null) return;

            progress.SetProfileName(typed);
            Refresh();

            // Written straight through, but only over a save that already exists. Creating one
            // here would turn a first-ever run into a "continue", because the lobby decides
            // between New Game and Continue on whether a file is present.
            var save = SaveSystem.Instance;
            if (save != null && save.SaveExists) save.Save();
        }
    }
}
