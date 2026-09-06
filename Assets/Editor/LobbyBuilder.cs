using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Builds the lobby: the icon rail, the character stage, the PLAY button, and the two
    /// screens the rail opens.
    ///
    /// Its own file rather than more of <see cref="MenuBuilder"/> because it is a screen with
    /// a 3D rig behind it, and because the layout rules here are different from the rest of the
    /// front end. Every other screen in this project is a fixed-size card centred on the canvas.
    /// The lobby is edge-anchored -- rail on the left, bar along the top, hero column stretched
    /// between them -- so that a 20:9 phone and a 4:3 tablet both get a lobby rather than the
    /// same card with different amounts of space around it.
    ///
    /// Sizes below are in canvas reference units. That is not a pixel measurement: the Canvas
    /// Scaler maps them onto the device, and the Phase 8 safe-area container insets the whole
    /// canvas before any of it is drawn.
    /// </summary>
    public static class LobbyBuilder
    {
        // --------------------------------------------------------------- geometry
        //
        // Named rather than typed inline, because several of them have to agree: the rail's
        // width sets the hero column's left margin, and the top bar's height sets its top.

        const float ScreenMargin = 24f;
        const float TopBarHeight = 104f;
        const float RailButton = 112f;
        /// <summary>Caption strip under each icon, outside the button art.</summary>
        const float RailCaption = 28f;
        const float RailGap = 18f;
        const float RailInset = 30f;

        static float RailSlot => RailButton + RailCaption;

        /// <summary>Left margin of the hero column: past the rail, with air after it.</summary>
        static float ContentSide => RailInset + RailButton + 54f;

        /// <summary>
        /// How wide the hero column is as a fraction of its own height.
        ///
        /// Calibrated so the 1920x1080 reference reproduces the 600-unit plate this screen was
        /// designed with: the content box is 908 units tall there, and 0.66 x 908 = 599.
        /// </summary>
        const float HeroColumnAspect = 0.66f;

        /// <summary>Portrait render target. Portrait aspect, small enough to be nearly free.</summary>
        const int PortraitWidth = 640;
        const int PortraitHeight = 896;
        const string PortraitPath = "Assets/Game/UI/LobbyPortrait.renderTexture";

        /// <summary>Where the display character stands: dead air, 200 m above the interiors.</summary>
        static readonly Vector3 StageOrigin = new Vector3(-420f, 400f, 300f);

        const float StageFieldOfView = 32f;

        /// <summary>
        /// How much taller than the body the shot is.
        ///
        /// <b>Calibrated against a rendered capture, not derived.</b> The number it multiplies
        /// is <c>Renderer.bounds</c>, and a skinned mesh's bounds are the bind pose's, padded
        /// for bone spread: this model measures 2.02 m that way against a body that draws
        /// 1.79 m. Framing off the raw bounds left the character filling 65% of the plate with
        /// a band of empty air over its head. 1.04 puts it at about 78%, which is what the
        /// capture shows.
        /// </summary>
        const float FrameSlack = 1.04f;

        /// <summary>Floor visible below the boots, in metres. It is what the plate's contact
        /// shadow sits on; with none, the character stands on the edge of the picture.</summary>
        const float GroundDrop = 0.15f;

        static Font Font => UiTheme.Font;

        // ------------------------------------------------------------------ build

        /// <summary>
        /// Called by <see cref="MenuBuilder"/> once the rest of the front end exists, because
        /// the lobby has to be a later sibling than the pause button to draw over it, and has
        /// to exist before the safe-area container is assembled so it ends up inside it.
        /// </summary>
        public static LobbyScreen Build(GameObject canvas, SettingsPanel settings)
        {
            var preview = BuildStage();

            // Order is draw order. The lobby has to come after the HUD's round buttons so it
            // covers them, and its own overlays have to come after the lobby so they cover it.
            var lobby = BuildLobby(canvas, preview);
            var character = BuildCharacterSelect(canvas, preview);
            var profile = BuildProfile(canvas);

            lobby.CharacterSelect = character;
            lobby.Profile = profile;
            lobby.Settings = settings;
            profile.Lobby = lobby;

            return lobby;
        }

        /// <summary>Names this builder owns, removed before a rebuild so it does not stack.</summary>
        public static readonly string[] OwnedUiRoots = { "Lobby", "CharacterSelect", "ProfilePanel" };
        public const string StageRootName = "LobbyStage";

        // ------------------------------------------------------------------ stage

        /// <summary>
        /// The display character and the camera that photographs them.
        ///
        /// The body comes from <see cref="CharacterCatalog.AttachBody"/> with
        /// <see cref="CharacterCatalog.Player"/> -- the same call and the same look the player
        /// is built from -- so this cannot drift into being a different character. It carries
        /// its own non-primary <see cref="PlayerSkinSwapper"/>, which is the same component
        /// that recolours the player, so an outfit looks the same here as it does in the city.
        /// </summary>
        static LobbyPreview BuildStage()
        {
            var old = GameObject.Find(StageRootName);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(StageRootName);
            root.transform.position = StageOrigin;

            var subject = new GameObject("Subject");
            subject.transform.SetParent(root.transform, false);

            var body = CharacterCatalog.AttachBody(subject, CharacterCatalog.Player,
                                                   CharacterCatalog.LoadController(),
                                                   AnimatorCullingMode.AlwaysAnimate);

            var animator = body != null ? body.GetComponent<Animator>() : null;
            if (animator != null)
            {
                // The lobby runs at timeScale 0 like every other menu. A scaled animator on a
                // stopped clock is a photograph; unscaled, the character breathes.
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            var skin = subject.AddComponent<PlayerSkinSwapper>();
            skin.Primary = false;   // the player's own swapper keeps the singleton

            // --- camera, framed from the model rather than from a guess -----------------
            // Only the height is taken from the model. Its vertical placement is not: the
            // FBX's origin is at the soles, which AttachBody puts at the stage floor, so the
            // feet are at local zero by construction and do not need measuring.
            float height = 1.8f;

            if (body != null)
            {
                var bounds = MeasureBounds(body);
                if (bounds.size.y > 0.01f) height = bounds.size.y;
            }

            float framed = height * FrameSlack;
            float distance = framed * 0.5f / Mathf.Tan(StageFieldOfView * 0.5f * Mathf.Deg2Rad);
            // Bottom edge of the shot a hand's width below the floor the character stands on.
            float aim = framed * 0.5f - GroundDrop;

            var cameraGo = new GameObject("StageCamera");
            cameraGo.transform.SetParent(root.transform, false);
            // Level, not tilted down: a tilted camera foreshortens the legs, and a lobby
            // portrait is the one place the whole silhouette has to read honestly.
            cameraGo.transform.localPosition = new Vector3(0f, aim, distance);
            cameraGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            // Transparent, so the character is cut out onto the lobby's own plate rather than
            // sitting in a photograph of a grey box.
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.fieldOfView = StageFieldOfView;
            camera.nearClipPlane = 0.05f;
            // Short enough that nothing else in the world could ever wander into the shot,
            // whatever gets built near this corner of the map later.
            camera.farClipPlane = distance * 3f;
            camera.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
            camera.useOcclusionCulling = false;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.targetTexture = EnsurePortraitTexture();
            camera.enabled = false;

            var data = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.antialiasing = AntialiasingMode.None;

            var preview = root.AddComponent<LobbyPreview>();
            preview.StageCamera = camera;
            preview.Subject = subject.transform;
            preview.Body = animator;
            preview.Skin = skin;

            subject.transform.localRotation = Quaternion.Euler(0f, preview.RestHeading, 0f);

            Debug.Log("[Lobby] Stage built at " + StageOrigin + ". Body height "
                      + height.ToString("F2") + " m, camera " + distance.ToString("F2")
                      + " m back at " + StageFieldOfView + " deg.");
            return preview;
        }

        static Bounds MeasureBounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds();
            bool any = false;

            foreach (var renderer in renderers)
            {
                // Headgear the variant switched off is not part of the silhouette.
                if (!renderer.enabled) continue;

                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return any ? bounds : new Bounds(model.transform.position, Vector3.one * 1.8f);
        }

        static RenderTexture EnsurePortraitTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(PortraitPath);

            if (existing != null)
            {
                if (existing.width == PortraitWidth && existing.height == PortraitHeight
                    && existing.format == RenderTextureFormat.ARGB32)
                    return existing;

                // Size and format are only writable on a released texture.
                existing.Release();
                existing.width = PortraitWidth;
                existing.height = PortraitHeight;
                existing.format = RenderTextureFormat.ARGB32;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            Directory.CreateDirectory("Assets/Game/UI");

            var texture = new RenderTexture(PortraitWidth, PortraitHeight, 24,
                                            RenderTextureFormat.ARGB32)
            {
                name = "LobbyPortrait",
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                wrapMode = TextureWrapMode.Clamp,
            };

            AssetDatabase.CreateAsset(texture, PortraitPath);
            return texture;
        }

        // ------------------------------------------------------------------ lobby

        static LobbyScreen BuildLobby(GameObject canvas, LobbyPreview preview)
        {
            var root = Stretch(canvas, "Lobby", Vector2.zero, Vector2.zero);

            // The kit's own painted home screen, not a stretched panel: the lobby is not a
            // window onto the city, it is a room of its own, and this kit ships a 1920x1080
            // starfield authored for exactly this job.
            //
            // It goes on its own child carrying SafeAreaBleed, NOT on the lobby root. The root
            // lives inside the safe-area container so the rail and the top bar clear a notch;
            // the backdrop has to do the opposite and reach the physical screen edge, or the
            // inset is drawn as a bare band of the camera's clear colour down each side. That
            // is exactly how Phase 13 shipped it and it is the visible defect this pass fixes.
            //
            // Drawn Simple and allowed to stretch: a field of stars and nebula with no straight
            // edges or lettering in it, so stretching is invisible where letterboxing would not
            // be. It also carries the raycast block that used to sit on the root.
            var backdropGo = SceneAssembler.NewUi(root, "Backdrop");
            var backdrop = backdropGo.AddComponent<Image>();
            backdrop.sprite = UiTheme.LobbyBackdrop;
            backdrop.type = Image.Type.Simple;
            backdrop.color = Color.white;
            backdrop.raycastTarget = true;
            backdropGo.AddComponent<SafeAreaBleed>();
            backdropGo.transform.SetAsFirstSibling();

            root.AddComponent<CanvasGroup>();

            var lobby = root.AddComponent<LobbyScreen>();
            lobby.Preview = preview;

            BuildTopBar(root, lobby);
            BuildRail(root, lobby);
            BuildHeroColumn(root, lobby, preview);

            // Offset by half its own box on both axes. Caption() point-anchors with a centred
            // pivot, so a 300x30 label at (-24, 14) put 126 units of itself past the right edge
            // of the screen on every aspect ratio tested -- the one hard overflow in the sweep.
            lobby.VersionLabel = Caption(root, "Version", "", new Vector2(1f, 0f), new Vector2(1f, 0f),
                                         new Vector2(-(ScreenMargin + 150f), 14f + 15f),
                                         new Vector2(300f, 30f),
                                         UiTheme.Micro, TextAnchor.LowerRight, UiTheme.TextOnPanelDim);

            return lobby;
        }

        static void BuildTopBar(GameObject root, LobbyScreen lobby)
        {
            var bar = Stretch(root, "TopBar",
                              new Vector2(ScreenMargin, 0f), new Vector2(-ScreenMargin, 0f));
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(ScreenMargin, -TopBarHeight - 14f);
            rt.offsetMax = new Vector2(-ScreenMargin, -14f);

            // --- name chip, left ---------------------------------------------------------
            // 104 tall, not 84: at 84 units the chip measured 5.8 mm on a 20:9 phone against a
            // 7 mm touch floor, and the editable field inside it only 3.6 mm. It is the one
            // control on this screen that opens a keyboard, so it is the last one that should
            // be hard to hit.
            var chip = Fixed(bar, "ProfileChip", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                             new Vector2(0f, 0f), new Vector2(520f, 104f));

            var chipImage = chip.AddComponent<Image>();
            var chipButton = chip.AddComponent<Button>();
            UiTheme.StyleChipButton(chipButton, chipImage);
            lobby.ProfileChipButton = chipButton;

            var avatar = Fixed(chip, "Avatar", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                               new Vector2(16f, 0f), new Vector2(52f, 52f));
            var avatarImage = avatar.AddComponent<Image>();
            avatarImage.sprite = UiTheme.Icon("person");
            avatarImage.color = UiTheme.Accent;
            avatarImage.preserveAspect = true;
            avatarImage.raycastTarget = false;

            // No caption over the field. There is no room for one inside a chip this tall,
            // and above it the word sat on the screen's top edge and was cut in half.
            BuildNameField(chip);

            // --- level and money, right --------------------------------------------------
            lobby.MoneyLabel = StatChip(bar, "MoneyChip", UiTheme.Coin, 0f, 300f);
            lobby.LevelLabel = StatChip(bar, "LevelChip", UiTheme.Star, -316f, 210f);
        }

        /// <summary>
        /// The editable name, in the chip itself.
        ///
        /// A legacy <c>InputField</c> rather than TextMeshPro's, because the whole interface is
        /// still legacy uGUI text (DECISIONS D5) and mixing the two would put a second font
        /// atlas on the canvas -- which is precisely the cost D30 exists to bound.
        /// </summary>
        static void BuildNameField(GameObject chip)
        {
            // Fills the chip's interior rather than floating inside it, so the editable area is
            // as close to the chip's own 7.2 mm target as the plate's frame allows.
            var field = Fixed(chip, "NameField", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                              new Vector2(84f, 0f), new Vector2(414f, 88f));

            // The deeper of the kit's two field states, because this one sits *inside* the
            // chip: two copies of the same plate would have shown as one flat rectangle.
            var background = field.AddComponent<Image>();
            background.sprite = UiTheme.ChipFocused;
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 1f;
            background.color = Color.white;

            var viewport = Stretch(field, "TextArea", new Vector2(14f, 4f), new Vector2(-14f, -4f));
            var mask = viewport.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;

            var placeholder = Caption(viewport, "Placeholder", PlayerProgress.DefaultProfileName,
                                      Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                                      UiTheme.Body, TextAnchor.MiddleLeft, UiTheme.TextOnPanelDim,
                                      FontStyle.Italic);

            var text = Caption(viewport, "Text", "", Vector2.zero, Vector2.one,
                               Vector2.zero, Vector2.zero,
                               UiTheme.Body, TextAnchor.MiddleLeft, UiTheme.TextOnPanel, FontStyle.Bold);
            text.supportRichText = false;

            var input = field.AddComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = PlayerProgress.MaxProfileNameLength;
            input.lineType = InputField.LineType.SingleLine;
            input.selectionColor = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.45f);
            input.caretColor = UiTheme.Accent;
            input.customCaretColor = true;

            field.AddComponent<ProfileNameField>();
        }

        static Text StatChip(GameObject bar, string name, Sprite icon, float x, float width)
        {
            var chip = Fixed(bar, name, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                             new Vector2(x, 0f), new Vector2(width, 92f));

            var background = chip.AddComponent<Image>();
            background.sprite = UiTheme.Chip;
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 1f;
            background.color = Color.white;
            background.raycastTarget = false;

            var art = Fixed(chip, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                            new Vector2(12f, 0f), new Vector2(52f, 52f));
            var image = art.AddComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;

            // Inset vertically as well as horizontally: a stretched rect at the chip's full
            // height sits 8 units onto the plate's own lip top and bottom.
            return Caption(chip, "Value", "0", new Vector2(0f, 0f), new Vector2(1f, 1f),
                           new Vector2(70f, 12f), new Vector2(-16f, -12f),
                           UiTheme.Label, TextAnchor.MiddleRight, UiTheme.TextOnPanel, FontStyle.Bold);
        }

        static void BuildRail(GameObject root, LobbyScreen lobby)
        {
            const int slots = 4;
            float height = slots * RailSlot + (slots - 1) * RailGap;

            var rail = Fixed(root, "Rail", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                             new Vector2(RailInset, 0f), new Vector2(RailButton, height));

            // Positions are derived from the slot index, so adding a fifth icon is a change to
            // `slots` and one more line, not a re-typing of four coordinates.
            // All four icons now come from the kit's Picto set.
            //
            // D38 previously drew these by hand because the *old* kit's settings icon was a
            // painted three-quarter render that resolved to a gold smudge at rail size. This
            // kit ships a separate flat single-colour pictogram set authored for small use,
            // which is the thing D38 could not find -- so the rail goes back to kit art and
            // the interface has one icon vocabulary again instead of two. See D48.
            lobby.CharacterButton = RailButtonAt(rail, 0, slots, "Character", "CHARACTER",
                                                 UiTheme.Icon("person"));
            lobby.ProfileButton = RailButtonAt(rail, 1, slots, "Profile", "PROFILE",
                                               UiTheme.Icon("info"));
            lobby.SettingsButton = RailButtonAt(rail, 2, slots, "Settings", "SETTINGS",
                                                UiTheme.Icon("settings"));
            lobby.QuitButton = RailButtonAt(rail, 3, slots, "Quit", "EXIT",
                                            UiTheme.Icon("power"));
        }

        /// <summary>
        /// One rail slot: a square icon button with its word underneath.
        ///
        /// The caption is a sibling of the button, not a child of it. Inside, it landed on the
        /// button art's bottom bevel and read as a smudge at both test resolutions -- the word
        /// needs the plain backdrop behind it, not the painted edge of the button.
        /// </summary>
        static Button RailButtonAt(GameObject rail, int index, int slots, string name,
                                   string caption, Sprite icon)
        {
            float step = RailSlot + RailGap;
            float top = (slots - 1) * step * 0.5f;
            float centre = top - index * step;

            var go = Fixed(rail, "Rail_" + name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           new Vector2(0f, centre + RailCaption * 0.5f),
                           new Vector2(RailButton, RailButton));

            var image = go.AddComponent<Image>();
            var button = go.AddComponent<Button>();
            // Square art: these buttons are 112 units on a side, and the wide silhouette would
            // wrap a long-button frame around a square hole.
            UiTheme.StyleButton(button, image, UiTheme.ButtonTone.Quiet, UiTheme.ButtonShape.Square);

            var art = Fixed(go, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            Vector2.zero, new Vector2(58f, 58f));
            var glyph = art.AddComponent<Image>();
            glyph.sprite = icon;
            glyph.preserveAspect = true;
            glyph.raycastTarget = false;
            glyph.color = UiTheme.TextOnButton;

            Caption(rail, "Caption_" + name, caption,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, centre - RailButton * 0.5f - 2f),
                    new Vector2(RailButton + 40f, RailCaption),
                    UiTheme.Micro, TextAnchor.UpperCenter, UiTheme.TextOnPanel, FontStyle.Bold);

            return button;
        }

        static void BuildHeroColumn(GameObject root, LobbyScreen lobby, LobbyPreview preview)
        {
            // Symmetric margins so the column is centred on the screen, not on the space left
            // over beside the rail: PLAY has to be in the middle of the display.
            var content = Stretch(root, "Content",
                                  new Vector2(ContentSide, 34f),
                                  new Vector2(-ContentSide, -(TopBarHeight + 34f)));

            // --- the hero column ---------------------------------------------------------
            //
            // Everything in the middle of the lobby -- the plate, PLAY and the caption -- now
            // hangs off one column whose *width is derived from the height available to it*
            // rather than typed in pixels. That is what makes this screen responsive: the
            // stage was a fixed 600 units and PLAY a fixed 520, so on a short 21:9 phone the
            // plate kept its width while its height shrank and the character sat in a band of
            // dead plate, and on a 4:3 tablet the reverse.
            //
            // The ratio is calibrated, not arbitrary: at the 1920x1080 reference the content
            // box is 908 units tall, and 0.66 of that is 599 -- the 600 the screen was designed
            // at. So the reference aspect renders identically to before and every other aspect
            // now follows it instead of drifting.
            var column = SceneAssembler.NewUi(content, "Column");
            var columnRt = column.GetComponent<RectTransform>();
            columnRt.anchorMin = new Vector2(0.5f, 0f);
            columnRt.anchorMax = new Vector2(0.5f, 1f);
            columnRt.pivot = new Vector2(0.5f, 0.5f);
            columnRt.anchoredPosition = Vector2.zero;
            columnRt.sizeDelta = new Vector2(HeroColumnAspect * 908f, 0f);

            var columnFitter = column.AddComponent<AspectRatioFitter>();
            columnFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            columnFitter.aspectRatio = HeroColumnAspect;

            // --- the stage plate ---------------------------------------------------------
            // Full column width now, so the plate is as wide as the layout says rather than as
            // wide as a number typed in 2026.
            var stage = Relative(column, "Stage", new Vector2(0f, 0.285f), new Vector2(1f, 1f),
                                 Vector2.zero);
            var plate = stage.AddComponent<Image>();
            UiTheme.StylePanel(plate, UiTheme.Card);
            plate.raycastTarget = false;

            // A cyan frame glow behind the plate, from the kit's own selection highlight, so
            // the character stands in a lit bay rather than on a rectangle pasted onto the
            // starfield. First sibling so it sits behind the plate rather than over the body.
            var halo = Stretch(stage, "Halo", new Vector2(-26f, -26f), new Vector2(26f, 26f));
            halo.transform.SetAsFirstSibling();
            var haloImage = halo.AddComponent<Image>();
            haloImage.sprite = UiTheme.SlotHighlight;
            haloImage.type = Image.Type.Sliced;
            haloImage.pixelsPerUnitMultiplier = 1f;
            haloImage.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.55f);
            haloImage.raycastTarget = false;

            // A soft ellipse under the feet. Without it the character reads as floating in
            // front of the plate rather than standing on it.
            var shadow = Relative(stage, "Shadow", new Vector2(0.5f, 0.100f), new Vector2(0.5f, 0.170f),
                                  new Vector2(300f, 0f));
            var shadowImage = shadow.AddComponent<Image>();
            shadowImage.sprite = SceneAssembler.LoadSprite("ui_circle", SceneAssembler.MakeCircleSprite);
            shadowImage.color = new Color(UiTheme.PanelDeepest.r, UiTheme.PanelDeepest.g,
                                          UiTheme.PanelDeepest.b, 0.45f);
            shadowImage.raycastTarget = false;

            // The fitter drives its own rect from the parent's, so the plate's padding has to
            // be a container around it rather than an offset on it.
            var area = Stretch(stage, "PortraitArea", new Vector2(20f, 20f), new Vector2(-20f, -20f));

            var portrait = Stretch(area, "Portrait", Vector2.zero, Vector2.zero);
            var raw = portrait.AddComponent<RawImage>();
            raw.texture = EnsurePortraitTexture();
            raw.raycastTarget = false;

            // The render target is a fixed portrait shape; the plate is not. Fitting inside
            // rather than stretching is what keeps the character un-squashed on a 4:3 tablet.
            var fitter = portrait.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = PortraitWidth / (float)PortraitHeight;

            // --- PLAY --------------------------------------------------------------------
            // Also the column's width, so the primary action and the plate above it stay the
            // same size as each other on every screen instead of drifting apart.
            var play = Relative(column, "PlayButton", new Vector2(0f, 0.10f), new Vector2(1f, 0.25f),
                                Vector2.zero);
            var playImage = play.AddComponent<Image>();
            var playButton = play.AddComponent<Button>();
            UiTheme.StyleButton(playButton, playImage, UiTheme.ButtonTone.Strong,
                                UiTheme.ButtonShape.Wide);
            lobby.PlayButton = playButton;

            // Cream on the kit's brown button. UiTheme.EnforceContrast knows to leave a caption
            // on ButtonStrong alone -- it darkens light text on the kit's *cream* surfaces.
            Caption(play, "Label", "PLAY", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                    UiTheme.Title, TextAnchor.MiddleCenter, UiTheme.TextOnButton, FontStyle.Bold);

            // The stats line stays on `content`, not on the column: it is a sentence and wants
            // the full width of the screen to avoid wrapping, where the column is deliberately
            // narrow. Stretched between symmetric margins so it can never overhang an edge.
            lobby.PlayCaption = Caption(content, "PlayCaption", "",
                                        new Vector2(0f, 0.005f), new Vector2(1f, 0.085f),
                                        new Vector2(20f, 0f), new Vector2(-20f, 0f),
                                        UiTheme.Body, TextAnchor.MiddleCenter, UiTheme.TextOnPanelDim);
        }

        // -------------------------------------------------------- character select

        static CharacterSelectPanel BuildCharacterSelect(GameObject canvas, LobbyPreview preview)
        {
            var root = Overlay(canvas, "CharacterSelect");
            var panel = root.AddComponent<CharacterSelectPanel>();
            panel.Preview = preview;

            // A wardrobe tile is one of the kit's light painted buttons, so its three state
            // words take the on-button inks -- the cyan/pink/grey that read on a dark panel
            // are invisible here. The locked tint stays opaque: it multiplies painted art,
            // and a faded tint erases the tile rather than dimming it.
            panel.OwnedTint = Color.white;
            panel.LockedTint = new Color(0.72f, 0.72f, 0.78f, 1f);
            panel.WearingText = UiTheme.AccentOnButton;
            panel.OwnedText = UiTheme.MetallicOnButton;
            panel.LockedText = UiTheme.DangerOnButton;

            var card = Card(root, "Card", new Vector2(1300f, 920f));

            panel.TitleLabel = Caption(card, "Title", "CHARACTER",
                                       new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                       new Vector2(0f, -124f), new Vector2(880f, 60f),
                                       UiTheme.Heading, TextAnchor.MiddleCenter, UiTheme.TextOnPanel,
                                       FontStyle.Bold);

            var container = Fixed(card, "Cells", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                  new Vector2(0f, 10f), new Vector2(1160f, 440f));
            panel.CellContainer = container.GetComponent<RectTransform>();
            panel.CellTemplate = BuildCell(container);

            panel.StatusLabel = Caption(card, "Status", "", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                        new Vector2(0f, 196f), new Vector2(1080f, 34f),
                                        UiTheme.Small, TextAnchor.MiddleCenter, UiTheme.Accent);

            panel.CloseButton = PanelButton(card, "Close", "BACK", new Vector2(0f, 116f),
                                            new Vector2(380f, 74f), out _,
                                            UiTheme.ButtonTone.Quiet);
            return panel;
        }

        /// <summary>
        /// One wardrobe tile, hidden and cloned per outfit at runtime.
        ///
        /// Children are anchored as fractions of the tile rather than at fixed offsets, because
        /// the panel sizes tiles to fit however many outfits exist -- a tile laid out at fixed
        /// offsets would come apart the first time a sixth outfit was added.
        /// </summary>
        static RectTransform BuildCell(GameObject container)
        {
            var cell = Fixed(container, "CellTemplate", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             Vector2.zero, new Vector2(340f, 240f));

            var image = cell.AddComponent<Image>();
            var button = cell.AddComponent<Button>();
            UiTheme.StyleButton(button, image, UiTheme.ButtonTone.Quiet);

            var swatch = Relative(cell, "Swatch", new Vector2(0.5f, 0.71f), new Vector2(0.5f, 0.71f),
                                  new Vector2(104f, 104f));
            var swatchImage = swatch.AddComponent<Image>();
            swatchImage.sprite = SceneAssembler.LoadSprite("ui_circle", SceneAssembler.MakeCircleSprite);
            swatchImage.raycastTarget = false;

            var padlock = Relative(cell, "Lock", new Vector2(0.5f, 0.71f), new Vector2(0.5f, 0.71f),
                                   new Vector2(50f, 50f));
            var padlockImage = padlock.AddComponent<Image>();
            padlockImage.sprite = UiTheme.Icon("lock");
            // Ink, not cream. A locked swatch is the outfit's colour pulled two thirds of the
            // way toward the panel's light grey, so every one of them is pale -- a pale padlock
            // on the Ivory Set's swatch was two shades of white on top of each other.
            padlockImage.color = new Color(UiTheme.TextOnButton.r, UiTheme.TextOnButton.g,
                                           UiTheme.TextOnButton.b, 0.92f);
            padlockImage.preserveAspect = true;
            padlockImage.raycastTarget = false;

            Caption(cell, "Name", "Outfit", new Vector2(0.04f, 0.26f), new Vector2(0.96f, 0.44f),
                    Vector2.zero, Vector2.zero, UiTheme.Body, TextAnchor.MiddleCenter,
                    UiTheme.TextOnButton, FontStyle.Bold);

            Caption(cell, "Status", "", new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.24f),
                    Vector2.zero, Vector2.zero, UiTheme.Micro, TextAnchor.MiddleCenter,
                    UiTheme.TextOnButtonDim, FontStyle.Bold);

            // The "wearing" marker, at the very bottom edge and clear of the status line: the
            // two were overlapping, and the status line on the worn tile is green as well.
            var selected = Stretch(cell, "Selected", Vector2.zero, Vector2.zero);
            var srt = selected.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.12f, 0f);
            srt.anchorMax = new Vector2(0.88f, 0f);
            srt.offsetMin = new Vector2(0f, 7f);
            srt.offsetMax = new Vector2(0f, 13f);
            var bar = selected.AddComponent<Image>();
            bar.sprite = UiTheme.DividerGold;
            bar.type = Image.Type.Sliced;
            bar.pixelsPerUnitMultiplier = 1f;
            bar.color = UiTheme.Gold;
            bar.raycastTarget = false;

            cell.SetActive(false);
            return cell.GetComponent<RectTransform>();
        }

        // ---------------------------------------------------------------- profile

        static ProfilePanel BuildProfile(GameObject canvas)
        {
            var root = Overlay(canvas, "ProfilePanel");
            var panel = root.AddComponent<ProfilePanel>();

            var card = Card(root, "Card", new Vector2(1300f, 920f));

            Caption(card, "Title", "PROFILE", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -124f), new Vector2(880f, 60f),
                    UiTheme.Heading, TextAnchor.MiddleCenter, UiTheme.TextOnPanel, FontStyle.Bold);

            // --- name ---------------------------------------------------------------------
            Caption(card, "NameCaption", "NAME", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(ColumnLeft + 110f, 210f), new Vector2(220f, 40f),
                    UiTheme.Small, TextAnchor.MiddleLeft, UiTheme.TextOnPanelDim, FontStyle.Bold);

            panel.NameField = BuildProfileNameField(card);

            // --- level and experience -----------------------------------------------------
            panel.LevelLabel = Caption(card, "Level", "LEVEL 1",
                                       new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                       new Vector2(ColumnLeft + 180f, 128f), new Vector2(360f, 44f),
                                       UiTheme.Label, TextAnchor.MiddleLeft, UiTheme.TextOnPanel,
                                       FontStyle.Bold);

            panel.XpLabel = Caption(card, "Xp", "", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                    new Vector2(240f, 128f), new Vector2(620f, 40f),
                                    UiTheme.Small, TextAnchor.MiddleRight, UiTheme.TextOnPanelDim);

            // 32 tall, not 18: the kit's level bar paints an 11 px lip top and a 10 px lip
            // bottom, so at 18 units there was no channel left between them for a fill.
            var track = Fixed(card, "XpTrack", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                              new Vector2(0f, 88f), new Vector2(2f * -ColumnLeft, 32f));
            // The kit's level bar, which is what it is for. PHASE12 flagged this track as a
            // "heavy dark slab at exactly 0%"; the kit's empty bar is a framed channel rather
            // than a filled block, so an empty one reads as a container waiting to fill.
            var trackImage = track.AddComponent<Image>();
            trackImage.sprite = UiTheme.XpTrack;
            trackImage.type = Image.Type.Sliced;
            trackImage.pixelsPerUnitMultiplier = 1f;
            trackImage.color = Color.white;
            trackImage.raycastTarget = false;

            var fill = Stretch(track, "Fill", new Vector2(4f, 4f), new Vector2(-4f, -4f));
            var fillImage = fill.AddComponent<Image>();
            fillImage.sprite = UiTheme.XpFill;
            fillImage.color = UiTheme.Lilac;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.raycastTarget = false;
            panel.XpBar = fillImage;

            // --- statistics, two columns of four ------------------------------------------
            const int rows = 4;
            var captions = new Text[rows * 2];
            var values = new Text[rows * 2];

            // Two columns, each a caption pinned to its own left edge and a value pinned to
            // its own right edge. Both were previously laid out around a column *centre*, which
            // put the left column's value and the right column's caption on the same pixels --
            // "$2,850" printed through "UNLOCKS EARNED".
            for (int i = 0; i < captions.Length; i++)
            {
                int column = i / rows;
                int row = i % rows;

                float left = ColumnLeft + column * (ColumnWidth + ColumnGap);
                float y = 10f - row * 62f;

                captions[i] = Caption(card, "StatCaption" + i, "",
                                      new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                      new Vector2(left + 160f, y), new Vector2(320f, 36f),
                                      UiTheme.Small, TextAnchor.MiddleLeft, UiTheme.TextOnPanelDim,
                                      FontStyle.Bold);

                values[i] = Caption(card, "StatValue" + i, "",
                                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                    new Vector2(left + ColumnWidth - 105f, y),
                                    new Vector2(210f, 36f),
                                    UiTheme.Small, TextAnchor.MiddleRight, UiTheme.TextOnPanel,
                                    FontStyle.Bold);
            }

            panel.StatCaptions = captions;
            panel.StatValues = values;

            panel.StatusLabel = Caption(card, "Status", "", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                        new Vector2(0f, 196f), new Vector2(1080f, 34f),
                                        UiTheme.Small, TextAnchor.MiddleCenter, UiTheme.Accent);

            panel.NewGameButton = PanelButton(card, "NewGame", "NEW GAME", new Vector2(-250f, 116f),
                                              new Vector2(440f, 74f), out panel.NewGameLabel,
                                              UiTheme.ButtonTone.Strong);
            panel.CloseButton = PanelButton(card, "Close", "BACK", new Vector2(250f, 116f),
                                            new Vector2(440f, 74f), out _,
                                            UiTheme.ButtonTone.Quiet);
            return panel;
        }

        // --- profile column geometry ------------------------------------------------
        //
        // The card is 1240 wide; the kit's panel art keeps about 60 of that as its own frame,
        // so 560 either side of centre is the usable width. Two 540-wide columns with a 40 gap
        // fit inside it exactly.

        const float ColumnWidth = 520f;
        const float ColumnGap = 40f;
        const float ColumnLeft = -(ColumnWidth + ColumnGap * 0.5f);

        static InputField BuildProfileNameField(GameObject card)
        {
            var field = Fixed(card, "NameField", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                              new Vector2(120f, 208f), new Vector2(880f, 62f));

            var background = field.AddComponent<Image>();
            background.sprite = UiTheme.Chip;
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 1f;
            background.color = Color.white;

            var viewport = Stretch(field, "TextArea", new Vector2(18f, 6f), new Vector2(-18f, -6f));
            viewport.AddComponent<RectMask2D>();

            var placeholder = Caption(viewport, "Placeholder", "Tap to type a name",
                                      Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                                      UiTheme.Body, TextAnchor.MiddleLeft, UiTheme.TextOnPanelDim,
                                      FontStyle.Italic);

            var text = Caption(viewport, "Text", "", Vector2.zero, Vector2.one,
                               Vector2.zero, Vector2.zero,
                               UiTheme.Body, TextAnchor.MiddleLeft, UiTheme.TextOnPanel, FontStyle.Bold);
            text.supportRichText = false;

            var input = field.AddComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = PlayerProgress.MaxProfileNameLength;
            input.lineType = InputField.LineType.SingleLine;
            input.selectionColor = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.45f);
            input.caretColor = UiTheme.Accent;
            input.customCaretColor = true;

            field.AddComponent<ProfileNameField>();
            return input;
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>A dimmed full-screen overlay: the lobby stays visible and lit underneath.</summary>
        static GameObject Overlay(GameObject canvas, string name)
        {
            var go = Stretch(canvas, name, Vector2.zero, Vector2.zero);

            // The dimmer goes on a bleeding child rather than on the root: the root stays
            // inside the safe area so the card it holds clears a notch, but a dimmer that stops
            // at the safe area leaves a bright strip of lobby showing down each edge.
            var scrimGo = SceneAssembler.NewUi(go, "Scrim");
            var scrim = scrimGo.AddComponent<Image>();
            scrim.color = UiTheme.Scrim;
            scrim.raycastTarget = true;   // stops a tap reaching the lobby behind it
            scrimGo.AddComponent<SafeAreaBleed>();
            scrimGo.transform.SetAsFirstSibling();

            go.AddComponent<CanvasGroup>();
            return go;
        }

        static GameObject Card(GameObject parent, string name, Vector2 size)
        {
            var go = Fixed(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           Vector2.zero, size);

            var image = go.AddComponent<Image>();
            UiTheme.StylePanel(image, UiTheme.Panel);
            image.raycastTarget = false;
            return go;
        }

        static Button PanelButton(GameObject parent, string name, string caption,
                                  Vector2 position, Vector2 size, out Text label,
                                  UiTheme.ButtonTone tone = UiTheme.ButtonTone.Normal)
        {
            var go = Fixed(parent, "Btn_" + name, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                           position, size);

            var image = go.AddComponent<Image>();
            var button = go.AddComponent<Button>();
            UiTheme.StyleButton(button, image, tone);

            label = Caption(go, "Label", caption, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                            UiTheme.Label, TextAnchor.MiddleCenter, UiTheme.TextOnButton, FontStyle.Bold);
            return button;
        }

        // --- rect helpers ---------------------------------------------------------------
        //
        // Three shapes cover every element here: stretched to its parent with a margin,
        // pinned to a point, and stretched along one axis only. Everything is expressed in
        // anchors, so the layout follows the safe area and the aspect ratio rather than
        // sitting at a fixed offset from a corner that may not be on screen.

        static GameObject Stretch(GameObject parent, string name, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = SceneAssembler.NewUi(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return go;
        }

        static GameObject Fixed(GameObject parent, string name, Vector2 anchor, Vector2 pivot,
                                Vector2 position, Vector2 size)
        {
            var go = SceneAssembler.NewUi(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return go;
        }

        /// <summary>Fixed width, height taken as a fraction of the parent.</summary>
        static GameObject Relative(GameObject parent, string name, Vector2 anchorMin,
                                   Vector2 anchorMax, Vector2 size)
        {
            var go = SceneAssembler.NewUi(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            // With differing anchors on an axis, sizeDelta is a delta from the anchor span --
            // zero means "exactly the span", which is what makes these scale.
            rt.sizeDelta = new Vector2(anchorMin.x == anchorMax.x ? size.x : 0f,
                                       anchorMin.y == anchorMax.y ? size.y : 0f);
            return go;
        }

        static Text Caption(GameObject parent, string name, string content,
                            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
                            int size, TextAnchor alignment, Color colour,
                            FontStyle style = FontStyle.Normal)
        {
            var go = SceneAssembler.NewUi(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;

            if (anchorMin == anchorMax)
            {
                // Point-anchored: the two vectors are a position and a size, not offsets.
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = offsetMin;
                rt.sizeDelta = offsetMax;
            }
            else
            {
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = offsetMin;
                rt.offsetMax = offsetMax;
            }

            var text = go.AddComponent<Text>();
            UiTheme.StyleText(text, size, colour, style);
            text.text = content;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
