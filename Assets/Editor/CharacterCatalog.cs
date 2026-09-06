using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// The one place that turns "which character should this be" into an actual model under a
    /// GameObject: the player, police officers, pedestrians and shopkeepers all come through
    /// <see cref="AttachBody"/>.
    ///
    /// It exists because Phase 9 swapped four different builders onto a new character pack at
    /// once. Each of them previously had its own near-identical copy of "instantiate the FBX,
    /// name it Body, add an Animator, assign the controller, turn root motion off, add
    /// PlayerAnimation" -- four copies that had already drifted apart on culling mode. One
    /// function means a fix to the character rig is a fix everywhere.
    /// </summary>
    public static class CharacterCatalog
    {
        /// <summary>Humanoid, 6,101 tris, URP materials. See PHASE9.md for the audit.</summary>
        public const string Apocalyptic = "Assets/Shady_3d/FBX/Apocalyptic character.fbx";

        public const string ControllerPath = "Assets/Game/Animator/PlayerLocomotion.controller";

        /// <summary>
        /// The three headgear combinations the pack README describes.
        ///
        /// The prefabs it ships for these are all identical -- every skinned renderer is
        /// enabled in all three, so the helmet and the gas mask render inside one another.
        /// The variant has to be applied, not loaded.
        /// </summary>
        public enum HeadGear { AsAuthored, Helmet, HelmetHood, GasMaskHood }

        const string GasMaskRenderer = "GAS MASK.001";
        const string HelmetRenderer = "HELMET";
        const string HoodRenderer = "MAIN HOOD";

        /// <summary>
        /// The Apocalyptic model is 1.95 m tall as authored, measured off the baked skin.
        /// Every character controller in this project is 1.75-1.80 m, so the model is brought
        /// down to match the collider it lives inside rather than the collider being grown to
        /// match the art -- the controller size is what doorways, seats and step offsets were
        /// tuned against in Phases 1-5.
        ///
        /// For reference, the model this replaces (Remy.fbx) is <b>4.15 m tall</b> in a 1.80 m
        /// controller. That was never right; it was just never visible from behind.
        /// </summary>
        public const float ApocalypticScale = 0.92f;

        /// <summary>One entry in a crowd: which model, which headgear, what colour.</summary>
        public struct Look
        {
            public string ModelPath;
            public HeadGear Gear;
            public Color Tint;
            public float TintStrength;
            public float Scale;

            public Look(string model, HeadGear gear, Color tint, float strength = 0.55f,
                        float scale = 1f)
            {
                ModelPath = model; Gear = gear; Tint = tint; TintStrength = strength; Scale = scale;
            }
        }

        static Look Apoc(HeadGear gear, Color tint, float strength = 0.55f) =>
            new Look(Apocalyptic, gear, tint, strength, ApocalypticScale);

        /// <summary>
        /// The street crowd.
        ///
        /// Every entry is a Humanoid rig, because a pedestrian has to walk, flinch and ragdoll.
        /// The Apocalyptic model carries most of the roster at 4.3-5.9k tris against the Mixamo
        /// characters' 35k, and three of the Mixamo bodies stay in the mix so the crowd is not
        /// six recolours of one silhouette -- different builds and heights read at distance in
        /// a way that a colour swap does not.
        /// </summary>
        /// <b>No HelmetHood in this table, on purpose.</b> That silhouette belongs to the
        /// player alone -- see the Player look below. Two entries that used it have been moved
        /// to Helmet and GasMaskHood, which keeps six distinct crowd variants while leaving the
        /// hood as the one shape on screen that is never a bystander.
        public static readonly Look[] Crowd =
        {
            Apoc(HeadGear.Helmet,      new Color(0.62f, 0.58f, 0.50f)),
            Apoc(HeadGear.GasMaskHood, new Color(0.30f, 0.36f, 0.42f)),
            Apoc(HeadGear.GasMaskHood, new Color(0.45f, 0.34f, 0.28f)),
            Apoc(HeadGear.Helmet,      new Color(0.28f, 0.42f, 0.34f)),
            Apoc(HeadGear.Helmet,      new Color(0.55f, 0.47f, 0.24f)),
            Apoc(HeadGear.GasMaskHood, new Color(0.36f, 0.30f, 0.40f)),
            new Look("Assets/characters/Ch02_nonPBR.fbx",        HeadGear.AsAuthored, Color.white, 0f),
            new Look("Assets/characters/Kachujin G Rosales.fbx", HeadGear.AsAuthored, Color.white, 0f),
            new Look("Assets/characters/Lola B Styperek.fbx",    HeadGear.AsAuthored, Color.white, 0f),

            // The remaining shipped Mixamo bodies, added so the street spans every human model
            // the project actually carries rather than four of them. These were previously
            // reserved for shopkeepers and mission NPCs, which kept "a counter is not another
            // passer-by" true -- that separation is now weaker by choice, and a shopkeeper is
            // told apart by standing behind a counter rather than by being a unique face.
            //
            // They are ~35k tris each against the Apocalyptic base's 4.3-5.9k, so CrowdDirector
            // weights the pool by mesh cost: every model appears, the cheap one appears most.
            new Look("Assets/characters/Ch07_nonPBR.fbx", HeadGear.AsAuthored, Color.white, 0f),
            new Look("Assets/characters/Ch08_nonPBR.fbx", HeadGear.AsAuthored, Color.white, 0f),
            new Look("Assets/characters/Ch16_nonPBR.fbx", HeadGear.AsAuthored, Color.white, 0f),
            new Look("Assets/characters/Ch22_nonPBR.fbx", HeadGear.AsAuthored, Color.white, 0f),
            new Look("Assets/characters/Ch31_nonPBR.fbx", HeadGear.AsAuthored, Color.white, 0f),
            new Look("Assets/characters/Arissa.fbx",      HeadGear.AsAuthored, Color.white, 0f),
        };

        /// <summary>Police: one silhouette, deliberately, so a uniform reads as a uniform.</summary>
        public static readonly Look Officer =
            Apoc(HeadGear.Helmet, new Color(0.20f, 0.26f, 0.44f), 0.72f);

        /// <summary>Shopkeepers. Distinct from the crowd so a counter is not another passer-by.</summary>
        public static readonly Look[] Keepers =
        {
            new Look("Assets/characters/Ch07_nonPBR.fbx", HeadGear.AsAuthored, Color.white, 0f),
            new Look("Assets/characters/Ch16_nonPBR.fbx", HeadGear.AsAuthored, Color.white, 0f),
            new Look("Assets/characters/Ch22_nonPBR.fbx", HeadGear.AsAuthored, Color.white, 0f),
            new Look("Assets/characters/Ch31_nonPBR.fbx", HeadGear.AsAuthored, Color.white, 0f),
        };

        /// <summary>
        /// The player. Hood up: it reads as a silhouette at chase-camera distance.
        ///
        /// <b>Distinct from the crowd on two axes, deliberately.</b> The audit found the player
        /// wearing the same model AND the same headgear as a crowd variant, separated only by a
        /// tint -- so the protagonist was, at a glance, one of the bystanders. The model stays
        /// (it is 4.3-5.9k tris against the Mixamo bodies' 35k, and that choice is what lets the
        /// crowd be 300 people at all), but HelmetHood is now the player's alone and the tint is
        /// a saturated signature colour where every crowd tint is a desaturated earth tone.
        ///
        /// The tint is the weaker of the two: PlayerSkinSwapper repaints the player from the
        /// equipped outfit, so a bought skin overrides it. The exclusive silhouette is what
        /// holds regardless of the wardrobe.
        /// </summary>
        public static readonly Look Player =
            new Look(Apocalyptic, HeadGear.HelmetHood,
                     new Color(0.74f, 0.16f, 0.20f), 0.62f, ApocalypticScale);

        public static RuntimeAnimatorController LoadController() =>
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

        /// <summary>
        /// Instantiates a character model under <paramref name="root"/> as "Body", wires the
        /// Animator, applies the headgear variant and adds the tint.
        ///
        /// Returns the model instance, or null if the FBX is missing -- callers that need a
        /// visible stand-in should check and fall back, exactly as they did before.
        /// </summary>
        public static GameObject AttachBody(GameObject root, Look look,
                                            RuntimeAnimatorController controller,
                                            AnimatorCullingMode culling)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(look.ModelPath);
            if (fbx == null)
            {
                Debug.LogWarning("[Characters] Missing model " + look.ModelPath);
                return null;
            }

            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model.name = "Body";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * (look.Scale <= 0f ? 1f : look.Scale);

            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            if (controller == null)
                Debug.LogWarning("[Characters] No animator controller; run 'Build Animator' first.");
            animator.runtimeAnimatorController = controller;
            // The CharacterController owns movement in this project, on every character.
            animator.applyRootMotion = false;
            animator.cullingMode = culling;

            ApplyHeadGear(model, look.Gear);

            if (look.TintStrength > 0.001f)
            {
                var tint = root.GetComponent<UniformTint>() ?? root.AddComponent<UniformTint>();
                tint.Tint = look.Tint;
                tint.Strength = look.TintStrength;
            }

            if (root.GetComponent<PlayerAnimation>() == null) root.AddComponent<PlayerAnimation>();

            return model;
        }

        /// <summary>
        /// Enables exactly the skinned renderers this variant wants.
        ///
        /// Skipped entirely for models that do not carry these renderers, so the same call is
        /// safe for a Mixamo body -- which is what lets one crowd table mix both packs.
        /// </summary>
        public static void ApplyHeadGear(GameObject model, HeadGear gear)
        {
            if (gear == HeadGear.AsAuthored) return;

            bool helmet = gear == HeadGear.Helmet || gear == HeadGear.HelmetHood;
            bool hood = gear == HeadGear.HelmetHood || gear == HeadGear.GasMaskHood;
            bool mask = gear == HeadGear.GasMaskHood;

            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                switch (smr.name)
                {
                    case HelmetRenderer: smr.enabled = helmet; break;
                    case HoodRenderer: smr.enabled = hood; break;
                    case GasMaskRenderer: smr.enabled = mask; break;
                }
            }
        }
    }
}
