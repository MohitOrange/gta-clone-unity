using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The character standing in the lobby: a display body on a small stage parked in dead
    /// air, rendered by its own camera into the RenderTexture the lobby shows.
    ///
    /// <b>Why a display body and not the player themselves.</b> The player is somewhere in the
    /// city -- possibly indoors, possibly at night, possibly in the rain, possibly at whatever
    /// position a save restored -- and none of that frames a portrait. The body here comes off
    /// the same <c>CharacterCatalog.AttachBody</c> call every character in the game comes off,
    /// wears the same <see cref="PlayerSkinSwapper"/>, and plays the same animator controller,
    /// so it is the same character by construction rather than by a second definition of one.
    ///
    /// <b>Why it takes the lighting over.</b> Only one directional light contributes on mobile
    /// -- the URP asset ships with additional lights disabled -- so a lamp aimed at the stage
    /// would render as nothing on a phone. Instead the lobby borrows the world's key light for
    /// as long as it is open, exactly as InteriorManager borrows the ambient when the player
    /// walks into a shop. Nothing else is being drawn at the time, so there is nothing to
    /// disturb, and closing the lobby hands it all straight back.
    /// </summary>
    public class LobbyPreview : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Renders the stage into the RenderTexture the lobby shows.")]
        public Camera StageCamera;
        [Tooltip("The body. Rotated for the idle sway; everything else hangs off the stage root.")]
        public Transform Subject;
        public Animator Body;
        public PlayerSkinSwapper Skin;

        [Header("Idle sway")]
        [Tooltip("Resting heading, degrees. A three-quarter view reads as a portrait; dead-on "
                 + "reads as a mugshot.")]
        public float RestHeading = 22f;
        [Tooltip("Degrees either side of the rest heading.")]
        public float SwayDegrees = 9f;
        [Tooltip("Seconds for one full sway. Slow enough to read as breathing, not as a turntable.")]
        public float SwaySeconds = 11f;

        [Header("Lighting while the lobby is open")]
        [Tooltip("Direction of the borrowed key light, as euler angles.")]
        public Vector3 KeyLightAngles = new Vector3(24f, 152f, 0f);
        public Color KeyLightColour = new Color(1f, 0.96f, 0.90f);
        public float KeyLightIntensity = 1.55f;
        [Tooltip("Flat ambient behind the key light, so the shadow side is not solid black.")]
        public Color Ambient = new Color(0.44f, 0.42f, 0.44f);

        bool _live;

        // --- what was borrowed, so it can be handed back exactly ---------------------
        DayNightCycle _cycle;
        bool _cycleWasEnabled;
        Light _sun;
        bool _sunWasEnabled;
        float _sunIntensity;
        Color _sunColour;
        Quaternion _sunRotation;
        Color _ambientWas;
        UnityEngine.Rendering.AmbientMode _ambientModeWas;
        bool _fogWas;

        Camera _worldCamera;
        int _worldCullingMask;
        bool _worldMaskBorrowed;

        void Awake()
        {
            // Off until the lobby asks for it: an always-on second camera would render the
            // stage every frame of the whole game for nobody.
            //
            // Guarded on _live because the lobby shows itself during Awake, and script Awake
            // order is not defined: without this, a lobby that woke first would switch the
            // stage on and this line would switch it straight back off.
            if (!_live && StageCamera != null) StageCamera.enabled = false;
        }

        void OnDisable() => Release();

        void Update()
        {
            if (!_live || Subject == null) return;

            // Unscaled: the lobby runs with Time.timeScale at zero, like every other menu.
            float phase = SwaySeconds > 0.01f
                ? Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / SwaySeconds))
                : 0f;

            Subject.localRotation = Quaternion.Euler(0f, RestHeading + phase * SwayDegrees, 0f);
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Show or hide the stage. Idempotent; safe to call either way twice.</summary>
        public void SetLive(bool live)
        {
            if (live == _live) return;

            if (live) Borrow();
            else Release();
        }

        void Borrow()
        {
            _live = true;

            if (StageCamera != null) StageCamera.enabled = true;

            // The city is behind an opaque lobby and cannot be seen. Rendering it anyway costs
            // the heaviest frame in the game for pixels nobody will ever look at, so the world
            // camera is emptied rather than disabled -- Camera.main only resolves an *enabled*
            // camera, and half the game re-resolves it lazily.
            _worldCamera = Camera.main;
            if (_worldCamera != null && _worldCamera != StageCamera)
            {
                _worldCullingMask = _worldCamera.cullingMask;
                _worldCamera.cullingMask = 0;
                _worldMaskBorrowed = true;
            }

            BorrowLighting();
            ApplyActiveSkin();
        }

        void Release()
        {
            if (!_live) return;
            _live = false;

            if (StageCamera != null) StageCamera.enabled = false;

            if (_worldMaskBorrowed && _worldCamera != null) _worldCamera.cullingMask = _worldCullingMask;
            _worldMaskBorrowed = false;
            _worldCamera = null;

            ReleaseLighting();
        }

        void BorrowLighting()
        {
            _ambientModeWas = RenderSettings.ambientMode;
            _ambientWas = RenderSettings.ambientLight;
            _fogWas = RenderSettings.fog;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            // A stage 3 m deep does not need fog, and linear fog set for a 900 m city would
            // otherwise wash the portrait toward the horizon colour at dusk.
            RenderSettings.fog = false;

            _cycle = FindAnyObjectByType<DayNightCycle>();
            if (_cycle == null) return;

            // The cycle re-applies the sun and the ambient every frame, timescale or not, so
            // it has to stand down rather than simply be overwritten once.
            _cycleWasEnabled = _cycle.enabled;
            _cycle.enabled = false;

            _sun = _cycle.Sun;
            if (_sun == null) return;

            _sunWasEnabled = _sun.enabled;
            _sunIntensity = _sun.intensity;
            _sunColour = _sun.color;
            _sunRotation = _sun.transform.rotation;

            _sun.enabled = true;
            _sun.intensity = KeyLightIntensity;
            _sun.color = KeyLightColour;
            _sun.transform.rotation = Quaternion.Euler(KeyLightAngles);
        }

        void ReleaseLighting()
        {
            if (_sun != null)
            {
                _sun.enabled = _sunWasEnabled;
                _sun.intensity = _sunIntensity;
                _sun.color = _sunColour;
                _sun.transform.rotation = _sunRotation;
            }

            RenderSettings.ambientMode = _ambientModeWas;
            RenderSettings.ambientLight = _ambientWas;
            RenderSettings.fog = _fogWas;

            if (_cycle != null)
            {
                _cycle.enabled = _cycleWasEnabled;
                // Re-enabling runs OnEnable -> Apply, but Refresh is explicit about it: the
                // cycle owns the sun and the ambient again from this line onward.
                if (_cycleWasEnabled) _cycle.Refresh();
            }

            _cycle = null;
            _sun = null;
        }

        // ---------------------------------------------------------------------- skin

        /// <summary>
        /// Repaints the display body to whatever outfit progress says is active.
        ///
        /// Called on opening and whenever the character select changes the selection, so the
        /// preview and the player are never showing two different outfits.
        /// </summary>
        public void ApplyActiveSkin()
        {
            if (Skin == null) return;

            var progress = PlayerProgress.Instance;
            string id = progress != null ? progress.ActiveSkin : "";

            if (!string.IsNullOrEmpty(id) && SkinLibrary.TryGetColour(id, out var colour))
                Skin.Apply(id, colour);
            else
                Skin.Clear();
        }
    }
}
