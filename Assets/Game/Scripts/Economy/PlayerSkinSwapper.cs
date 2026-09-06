using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Changes the player's outfit colour.
    ///
    /// Recolours the existing Mixamo materials through a property block rather than swapping in
    /// a different character model. Swapping the model would mean re-binding the Animator,
    /// re-resolving PlayerAnimation and re-parenting anything attached to the rig -- a lot of
    /// moving parts for a cosmetic. Recolouring is instant, costs nothing, and keeps the
    /// character rig untouched.
    ///
    /// Swapping to a whole different Mixamo model is still possible later: the humanoid
    /// retargeting from Phase 1 means any model plays the same clips.
    /// </summary>
    public class PlayerSkinSwapper : MonoBehaviour
    {
        static PlayerSkinSwapper _instance;

        public static PlayerSkinSwapper Instance
        {
            get
            {
                if (_instance != null) return _instance;

                // Explicitly the Primary one. FindAnyObjectByType gives no ordering guarantee,
                // and picking the lobby's display body here would send every shop purchase to
                // the mannequin instead of to the player.
                foreach (var candidate in FindObjectsByType<PlayerSkinSwapper>(
                             FindObjectsInactive.Include))
                {
                    if (!candidate.Primary) continue;
                    _instance = candidate;
                    break;
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Tooltip("The player's own swapper. Clear it for a display copy -- the lobby's "
                 + "character preview carries this component too, and only one of them may "
                 + "answer to Instance or the shop would repaint the mannequin.")]
        public bool Primary = true;

        [Tooltip("How strongly the skin colour replaces the original texture colour.")]
        [Range(0f, 1f)] public float Strength = 0.7f;

        [Tooltip("Renderers to recolour. Leave empty to recolour the whole model.")]
        public Renderer[] Targets = new Renderer[0];

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Color[] _stockColours;
        Renderer[] _resolved;
        string _current = "";

        public string CurrentSkin => _current;

        /// <summary>
        /// Claims the singleton only if this is the player's swapper and nothing holds it yet.
        ///
        /// It used to destroy any second copy, which made a display character impossible: the
        /// lobby preview needs the same recolouring logic on a second body without becoming
        /// the thing the shop and the save system talk to.
        /// </summary>
        void Awake()
        {
            if (Primary && _instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;

            var progress = PlayerProgress.Instance;
            if (progress != null) progress.SkinChanged -= OnSkinChanged;
        }

        void Start()
        {
            Resolve();

            // Re-apply whatever was saved, once progress has loaded.
            var progress = PlayerProgress.Instance;
            if (progress != null)
            {
                progress.SkinChanged += OnSkinChanged;
                if (!string.IsNullOrEmpty(progress.ActiveSkin)) OnSkinChanged(progress.ActiveSkin);
            }
        }

        void Resolve()
        {
            _resolved = Targets != null && Targets.Length > 0
                ? Targets
                : GetComponentsInChildren<Renderer>(true);

            _stockColours = new Color[_resolved.Length];
            for (int i = 0; i < _resolved.Length; i++)
            {
                var mat = _resolved[i] != null ? _resolved[i].sharedMaterial : null;
                _stockColours[i] = mat != null && mat.HasProperty(BaseColorId)
                    ? mat.GetColor(BaseColorId)
                    : Color.white;
            }
        }

        /// <summary>
        /// Repaints to whatever outfit progress now says is active.
        ///
        /// The colour lives on the shop item that sells the outfit, which is why this used to
        /// record the id and change nothing: a skin bought in one session came back as the
        /// stock model in the next, because the id alone said nothing about what colour to
        /// paint. <see cref="SkinLibrary"/> resolves the id against that same shop stock, so
        /// there is still exactly one place an outfit's colour is authored.
        /// </summary>
        void OnSkinChanged(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) { Clear(); return; }

            if (SkinLibrary.TryGetColour(skinId, out var colour)) { Apply(skinId, colour); return; }

            // An id no shop sells any more. Stock colours, not black, and say so once.
            Debug.LogWarning("[Skins] No outfit sells id '" + skinId + "'; wearing stock colours.");
            Clear();
        }

        /// <summary>Apply a skin colour directly. Called by the shop at the point of purchase.</summary>
        public void Apply(string skinId, Color colour)
        {
            if (_resolved == null) Resolve();

            _current = skinId;

            var mpb = new MaterialPropertyBlock();
            for (int i = 0; i < _resolved.Length; i++)
            {
                var r = _resolved[i];
                if (r == null) continue;

                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, Color.Lerp(_stockColours[i], colour, Strength));
                r.SetPropertyBlock(mpb);
            }
        }

        /// <summary>Back to the model's original colours.</summary>
        public void Clear()
        {
            if (_resolved == null) return;

            _current = "";
            var mpb = new MaterialPropertyBlock();

            for (int i = 0; i < _resolved.Length; i++)
            {
                var r = _resolved[i];
                if (r == null) continue;

                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, _stockColours[i]);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
