using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Builds the front end: title screen, pause menu, settings, the full-screen map, and the
    /// audio rig that goes with them.
    ///
    /// Separate from <see cref="SceneAssembler"/> because it is additive. The assembler
    /// destroys and rebuilds the world and the HUD; this only ever adds to what is already
    /// there, so it can be re-run on its own while iterating on a menu without touching the
    /// city, the player or the controls.
    /// </summary>
    public static class MenuBuilder
    {
        const string ScenePath = "Assets/Scenes/City.unity";

        // The menus take their colours from UiTheme rather than carrying a second palette.
        // These aliases are kept so the rest of the file reads the same.
        static Color Dim => UiTheme.Scrim;

        // Phase 13 inverted the polarity again, back the other way. Phase 11's Soft Touch kit
        // was cream panels needing dark text; the Space kit is deep-indigo panels needing
        // light text -- but the buttons *on* those panels are light painted art needing dark
        // text. So a caption's colour now depends on whether it sits on a panel or on a
        // button, and the two are opposite. `Ink` here means "on a panel"; anything drawn on
        // a button asks for UiTheme.TextOnButton explicitly.
        static Color Accent => UiTheme.Accent;
        static Color Ink => UiTheme.TextOnPanel;
        static Color Muted => UiTheme.TextOnPanelDim;

        /// <summary>
        /// The menus use the same face as everything else. This was the built-in
        /// LegacyRuntime font, which is why 41 of the 92 Text components were on a second
        /// font after the first pass of the Phase 11 re-skin -- a second face is a second
        /// runtime atlas, which is exactly the cost the type scale exists to bound.
        /// </summary>
        static Font Font => UiTheme.Font;

        [MenuItem("Tools/Mini GTA/10. Build Menus and Audio", priority = 130)]
        public static void Build()
        {
            var canvas = GameObject.Find("HUD");
            var systems = GameObject.Find("GameSystems");

            if (canvas == null || systems == null)
            {
                Debug.LogError("[Menus] No HUD or GameSystems in the scene. Run step 6 first.");
                return;
            }

            // Additive, but not cumulative: anything a previous run of this step made is
            // removed first so re-running does not stack two pause menus on top of each other.
            var owned = new System.Collections.Generic.List<string>
            {
                "MainMenu", "PauseMenu", "SettingsPanel", "FullMap", "HudLayoutEditor",
                "PauseButton", "MapButton", "AudioManager", "DialoguePanel",
                "CheatPanel", "CheatCorner",
            };
            owned.AddRange(LobbyBuilder.OwnedUiRoots);

            foreach (var name in owned)
            {
                var existing = Find(canvas, name) ?? GameObject.Find(name);
                if (existing != null) Object.DestroyImmediate(existing);
            }

            var circle = SceneAssembler.LoadSprite("ui_circle", SceneAssembler.MakeCircleSprite);

            BuildAudio(systems);
            var layout = BuildHudLayout(canvas);

            var settings = BuildSettingsPanel(canvas);
            var layoutEditor = BuildHudLayoutEditor(canvas);
            settings.LayoutEditor = layoutEditor;
            var map = BuildFullMap(canvas, circle);
            var pause = BuildPauseMenu(canvas);

            BuildPauseButton(canvas, circle, pause);
            BuildMinimapTapTarget(canvas, pause, map);

            BuildDialoguePanel(canvas);
            BuildCheatEntry(canvas, systems);

            // The lobby last of the screens, and after the pause button on purpose. Sibling
            // order is draw order, and the round pause button used to draw over the title
            // screen for exactly this reason -- it was built after it.
            var title = LobbyBuilder.Build(canvas, settings);

            pause.Settings = settings;
            pause.Map = map;
            pause.Title = title;

            // Click sounds last, so every button built above gets one.
            if (canvas.GetComponent<UiClickAudio>() == null) canvas.AddComponent<UiClickAudio>();
            if (canvas.GetComponent<MenuInput>() == null) canvas.AddComponent<MenuInput>();

            // Safe area last of all, once every UI root exists, so they all end up inside it.
            EnsureSafeArea(canvas);

            // After every screen exists: repaint any caption that ended up on a surface it
            // cannot be read on. See UiTheme.EnforceContrast for why this is a sweep.
            int repainted = UiTheme.EnforceContrast(canvas);

            // ...and check nothing was laid out underneath a panel's painted frame. This kit's
            // frames are two to three times thicker than the ones every card in this project
            // was sized against, and the result is invisible to every assertion.
            int overhangs = UiTheme.AuditFrames(canvas);

            EnsureSettingsObject(systems);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);

            Debug.Log("[Menus] Built lobby, pause, settings and map screens.\n"
                      + "  interface face: " + UiTheme.FontSource
                      + "\n  captions repainted for contrast: " + repainted
                      + "\n  elements overhanging a panel frame: " + overhangs
                      + "\n  mirrored widgets: " + layout.Mirrored.Length
                      + "\n  audio: procedural music, ambience, engine loop and 7 effects");
        }

        // ------------------------------------------------------------------ audio

        static void BuildAudio(GameObject systems)
        {
            var go = new GameObject("AudioManager");
            go.transform.SetParent(systems.transform, false);
            go.AddComponent<AudioManager>();
            go.AddComponent<VehicleAudio>();

            if (systems.GetComponent<AudioHooks>() == null) systems.AddComponent<AudioHooks>();
            if (systems.GetComponent<PerformanceTuner>() == null) systems.AddComponent<PerformanceTuner>();

            var player = GameObject.FindWithTag("Player");
            if (player != null && player.GetComponent<FootstepAudio>() == null)
                player.AddComponent<FootstepAudio>();
        }

        static void EnsureSettingsObject(GameObject systems)
        {
            // Added after the things it drives exist, but its own Start still runs after every
            // Awake, so ApplyAll finds a built AudioManager and PerformanceTuner.
            if (systems.GetComponent<GameSettings>() == null) systems.AddComponent<GameSettings>();
        }

        // ----------------------------------------------------------------- layout

        /// <summary>
        /// Slides a single full-screen "SafeArea" container between the canvas and every UI
        /// root, and puts <see cref="SafeAreaFitter"/> on it.
        ///
        /// One container rather than a fitter per screen: a notch does not care which panel is
        /// open, and this cannot be forgotten when the next screen is added. Idempotent, so
        /// re-running this build step does not nest a second container inside the first.
        /// </summary>
        static void EnsureSafeArea(GameObject canvas)
        {
            var existing = canvas.transform.Find("SafeArea");
            GameObject area;

            if (existing != null) area = existing.gameObject;
            else
            {
                area = SceneAssembler.NewUi(canvas, "SafeArea");
                var art = area.GetComponent<RectTransform>();
                art.anchorMin = Vector2.zero;
                art.anchorMax = Vector2.one;
                art.offsetMin = Vector2.zero;
                art.offsetMax = Vector2.zero;
            }

            if (area.GetComponent<SafeAreaFitter>() == null) area.AddComponent<SafeAreaFitter>();

            // Reparent every other canvas child, preserving sibling order (which is UI draw and
            // hit-test order -- shuffling it would put the look pad on top of the buttons).
            var move = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in canvas.transform)
                if (child.gameObject != area) move.Add(child);

            // Plain append, in the order they were collected. Do NOT read GetSiblingIndex()
            // inside this loop and re-apply it: by then the earlier children have already left
            // the canvas, every remaining one reports index 0, and the list comes out exactly
            // reversed -- which would put the full-screen look pad on top of every button.
            foreach (var child in move) child.SetParent(area.transform, false);

            area.transform.SetAsLastSibling();
            Debug.Log("[Menus] SafeArea container holds " + area.transform.childCount + " UI roots.");
        }

        static HudLayout BuildHudLayout(GameObject canvas)
        {
            var layout = canvas.GetComponent<HudLayout>() ?? canvas.AddComponent<HudLayout>();

            var stick = Find(canvas, "MoveStick");
            var look = Find(canvas, "LookZone");
            var cluster = Find(canvas, "ActionButtons");

            var mirrored = new System.Collections.Generic.List<RectTransform>();
            foreach (var root in new[] { stick, look, cluster })
                if (root != null) mirrored.Add((RectTransform)root.transform);

            // The buttons inside the cluster are right-anchored individually, so mirroring the
            // cluster alone would flip the group but leave the arc pointing the wrong way.
            if (cluster != null)
                foreach (var button in cluster.GetComponentsInChildren<HudButton>(true))
                    mirrored.Add((RectTransform)button.transform);

            layout.Mirrored = mirrored.ToArray();
            layout.ControlRoots = new[] { stick, look, cluster };
            return layout;
        }

        // ------------------------------------------------------------ pause menu

        static PauseMenu BuildPauseMenu(GameObject canvas)
        {
            var root = FullScreenPanel(canvas, "PauseMenu", Dim);
            var menu = root.AddComponent<PauseMenu>();

            // The kit's pause container, which is what it is named for. 800 tall, not 720:
            // its painted frame is 88 units, and the title and the status line at the far ends
            // of the old card were both laid out underneath it.
            var card = CardPanel(root, "Card", new Vector2(640f, 800f), UiTheme.Panel);

            CentreText(card, "Title", "PAUSED", new Vector2(0f, 270f), new Vector2(440f, 66f),
                       UiTheme.Title, FontStyle.Bold, Ink);
            Divider(card, "Title", 226f, 380f, gold: true);

            // RESUME is the primary action and takes the kit's gold family; the rest are blue.
            // MAIN MENU leaves the session, so it takes the quiet purple -- a destination, not
            // an action, and it should not compete with RESUME for the thumb.
            menu.ResumeButton = MenuButton(card, "Resume", "RESUME", new Vector2(0f, 150f),
                                           UiTheme.ButtonTone.Strong, "play");
            menu.MapButton = MenuButton(card, "Map", "MAP", new Vector2(0f, 54f),
                                        UiTheme.ButtonTone.Normal, "map");
            menu.SettingsButton = MenuButton(card, "Settings", "SETTINGS", new Vector2(0f, -42f),
                                             UiTheme.ButtonTone.Normal, "settings");
            menu.SaveButton = MenuButton(card, "Save", "SAVE NOW", new Vector2(0f, -138f),
                                         UiTheme.ButtonTone.Normal, "check");
            menu.TitleButton = MenuButton(card, "Title", "MAIN MENU", new Vector2(0f, -234f),
                                          UiTheme.ButtonTone.Quiet, "home");

            menu.StatusLabel = CentreText(card, "Status", "", new Vector2(0f, -294f),
                                          new Vector2(440f, 32f), UiTheme.Body,
                                          FontStyle.Normal, Accent);
            return menu;
        }

        static void BuildPauseButton(GameObject canvas, Sprite circle, PauseMenu menu)
        {
            // Left edge, between the vitals and the top of the movement stick.
            //
            // Measured the HUD rather than guessing at it, because both side edges are nearly
            // full (canvas units, 1920x1080, safe area applied):
            //   Minimap        x  124.. 328   y  799..1048
            //   vitals bars    x  124.. 330   y  776.. 825
            //   MoveStick zone x   97.. 822   y   53.. 690
            //   ActionButtons  x 1294..1787   y   96.. 631
            //   StoreButton    x 1691..1782   y  617.. 729
            //   Wallet         x 1518..1782   y  754.. 857
            // The right edge has no gap bigger than 25 units anywhere. The left edge had 86,
            // and this button is deliberately 104 -- see below -- so the stick zone gives up
            // its top strip to make room. That is the cheap side of the trade: the stick is a
            // floating one that re-centres wherever the thumb lands, thumbs rest low, and the
            // zone is still over half the screen.
            //
            // 104 units, not 78: 78 measured 5.4 mm on a 20:9 phone against a 7 mm floor, and
            // this is the control that gets you out of a chase.
            var button = SceneAssembler.MakeHudCircleButton(
                canvas, "PauseButton", circle, Font, "pause",
                new Vector2(0f, 1f), new Vector2(176f, -392f), 104f);

            // Assign the reference; PauseMenu.Awake does the wiring at runtime.
            //
            // Calling button.onClick.AddListener(menu.TogglePause) here looks equivalent and is
            // not: AddListener registers a *non-persistent* listener that lives in memory only.
            // It is never written to the scene, so it survives until the next domain reload in
            // the editor and does not exist at all in a player build -- which is why this button
            // worked every time it was tested in the editor and was dead on the phone.
            menu.OpenButton = button;
        }

        /// <summary>Makes the corner minimap open the full map when tapped.</summary>
        static void BuildMinimapTapTarget(GameObject canvas, PauseMenu pause, FullMapScreen map)
        {
            var minimap = Find(canvas, "Minimap");
            if (minimap == null) return;

            var go = SceneAssembler.NewUi(minimap, "MapButton");
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);   // invisible, still takes the tap

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            // Reference only -- FullMapScreen.Awake wires it. A lambda could never have been
            // serialised even in principle. See the note in BuildPauseButton.
            map.OpenButton = button;
        }

        // -------------------------------------------------------------- dialogue

        /// <summary>
        /// The talking-to-someone panel.
        ///
        /// Deliberately shorter than the other cards and anchored low: the person speaking is
        /// standing in front of the player, and a full-screen card would hide them. Same kit
        /// panel art, same button art, same seven-step type scale as everything else.
        /// </summary>
        static DialoguePanel BuildDialoguePanel(GameObject canvas)
        {
            var root = FullScreenPanel(canvas, "DialoguePanel", Dim);
            var panel = root.AddComponent<DialoguePanel>();

            // Bottom third, so the speaker stays visible above it.
            var card = SceneAssembler.NewUi(root, "Card");
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 40f);
            rt.sizeDelta = new Vector2(1280f, 420f);

            // The thin-framed dark plate, not the popup container: this card is deliberately
            // short so the speaker stays visible above it, and an 87 px frame on a 420-tall
            // card leaves 246 units for a name, a line, a hint and two buttons.
            var image = card.AddComponent<Image>();
            UiTheme.StylePanel(image, UiTheme.Card);
            image.raycastTarget = false;

            // The kit's selection glow around the outside, so the box still has a sci-fi frame
            // without one thick enough to eat the space inside it.
            var glow = SceneAssembler.NewUi(card, "Frame");
            var glowRt = glow.GetComponent<RectTransform>();
            glowRt.anchorMin = Vector2.zero;
            glowRt.anchorMax = Vector2.one;
            glowRt.offsetMin = new Vector2(-22f, -22f);
            glowRt.offsetMax = new Vector2(22f, 22f);
            glow.transform.SetAsFirstSibling();
            var glowImage = glow.AddComponent<Image>();
            glowImage.sprite = UiTheme.SlotHighlight;
            glowImage.type = Image.Type.Sliced;
            glowImage.pixelsPerUnitMultiplier = 1f;
            glowImage.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.6f);
            glowImage.raycastTarget = false;

            panel.SpeakerLabel = CentreText(card, "Speaker", "", new Vector2(-400f, 150f),
                                            new Vector2(400f, 46f), UiTheme.Heading,
                                            FontStyle.Bold, Accent);
            panel.SpeakerLabel.alignment = TextAnchor.MiddleLeft;

            // The kit's rule under the speaker's name, so the name reads as a heading over the
            // line rather than as the first sentence of it.
            Divider(card, "Speaker", 122f, 1180f);

            panel.LineLabel = CentreText(card, "Line", "", new Vector2(0f, 46f),
                                         new Vector2(1180f, 120f), UiTheme.Body,
                                         FontStyle.Normal, Ink);
            panel.LineLabel.alignment = TextAnchor.UpperLeft;
            panel.LineLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            panel.LineLabel.verticalOverflow = VerticalWrapMode.Truncate;

            panel.HintLabel = CentreText(card, "Hint", "", new Vector2(-320f, -78f),
                                         new Vector2(560f, 34f), UiTheme.Small,
                                         FontStyle.Normal, Muted);
            panel.HintLabel.alignment = TextAnchor.MiddleLeft;

            panel.NextButton = WideButton(card, "Next", "NEXT", new Vector2(330f, -142f),
                                          new Vector2(380f, 72f), out panel.NextLabel,
                                          UiTheme.ButtonTone.Strong, "next");
            panel.CloseButton = WideButton(card, "Close", "LEAVE", new Vector2(-90f, -142f),
                                           new Vector2(300f, 72f), out _,
                                           UiTheme.ButtonTone.Quiet, "cross");
            return panel;
        }

        // -------------------------------------------------------------- settings

        /// <summary>
        /// The cheat entry screen and the hidden gesture that opens it.
        ///
        /// <b>67 cheats were registered, verified, and unreachable.</b> `CheatConsole` types
        /// codes from `Keyboard.onTextInput`, which is the right mechanic and works on a
        /// desktop -- but the primary target is Android, which has no keyboard until something
        /// asks for one, and the console component was not in the scene at all. So the whole
        /// system existed and no player on the target platform could touch it.
        ///
        /// This is deliberately its own modal rather than a row on the settings card. That card
        /// already documents a tight vertical budget -- nine rows inside 724 usable units after
        /// the kit's 88-unit frame -- and Phase 13's frame audit found 91 layout defects the
        /// last time something was squeezed into a card that had no room for it.
        /// </summary>
        static void BuildCheatEntry(GameObject canvas, GameObject systems)
        {
            // The console itself, for the desktop type-anywhere path. Lives on Systems, not on
            // the canvas: it is an input listener, not a widget.
            if (systems.GetComponentInChildren<CheatConsole>() == null)
            {
                var consoleGo = new GameObject("CheatConsole");
                consoleGo.transform.SetParent(systems.transform, false);
                consoleGo.AddComponent<CheatConsole>();
            }

            var root = FullScreenPanel(canvas, "CheatPanel", Dim);
            var panel = root.AddComponent<CheatPanel>();

            // 1000 x 560 on pause-container-large, whose painted frame is 88 units a side --
            // so everything below lives inside +/-412 horizontally and +/-192 vertically. The
            // first pass ignored that and the build-time frame audit caught it immediately:
            // the title overhung by 26 units and the result line by 30. That audit has reported
            // 0 since Phase 13 and it stays at 0.
            var card = CardPanel(root, "Card", new Vector2(1000f, 560f), UiTheme.Panel);

            CentreText(card, "Title", "CHEAT CODE", new Vector2(0f, 158f), new Vector2(780f, 52f),
                       UiTheme.Title, FontStyle.Bold, Ink);
            Divider(card, "Title", 122f, 500f, gold: true);

            panel.GateLabel = CentreText(card, "Gate", "Locked", new Vector2(0f, 96f),
                                         new Vector2(780f, 32f), UiTheme.Small,
                                         FontStyle.Normal, Muted);

            panel.Field = BuildCheatField(card);

            panel.Result = CentreText(card, "Result", "", new Vector2(0f, -40f),
                                      new Vector2(780f, 36f), UiTheme.Body,
                                      FontStyle.Bold, UiTheme.Accent);

            panel.EnterButton = WideButton(card, "Enter", "ENTER", new Vector2(-176f, -142f),
                                           new Vector2(320f, 72f), out _);
            panel.CloseButton = WideButton(card, "Close", "CLOSE", new Vector2(176f, -142f),
                                           new Vector2(320f, 72f), out _);

            // The way in. Small, invisible, and in the top-left -- the one corner the thumb
            // cluster and the minimap both leave alone.
            var corner = SceneAssembler.NewUi(canvas, "CheatCorner");
            var rt = corner.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(6f, -6f);
            rt.sizeDelta = new Vector2(120f, 120f);

            var hit = corner.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);   // invisible, still raycastable
            hit.raycastTarget = true;

            var gesture = corner.AddComponent<CheatGesture>();
            gesture.Panel = panel;
        }

        /// <summary>The code field. A legacy InputField, matching the rest of this interface.</summary>
        static InputField BuildCheatField(GameObject card)
        {
            var field = SceneAssembler.NewUi(card, "CodeField");
            var rt = field.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 36f);
            rt.sizeDelta = new Vector2(800f, 76f);

            var background = field.AddComponent<Image>();
            background.sprite = UiTheme.Chip;
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 1f;
            background.color = Color.white;

            var viewport = SceneAssembler.NewUi(field, "TextArea");
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(20f, 6f);
            vrt.offsetMax = new Vector2(-20f, -6f);
            viewport.AddComponent<RectMask2D>();

            var placeholder = SceneAssembler.Label(viewport, "Placeholder", Vector2.zero,
                                                   Vector2.zero, Font, UiTheme.Body,
                                                   TextAnchor.MiddleCenter, Muted,
                                                   FontStyle.Italic);
            Stretch(placeholder.gameObject);
            placeholder.text = "type a code";

            var text = SceneAssembler.Label(viewport, "Text", Vector2.zero, Vector2.zero,
                                            Font, UiTheme.Body, TextAnchor.MiddleCenter,
                                            Ink, FontStyle.Bold);
            Stretch(text.gameObject);
            text.supportRichText = false;

            var input = field.AddComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = 24;
            input.lineType = InputField.LineType.SingleLine;
            input.characterValidation = InputField.CharacterValidation.None;
            input.selectionColor = new Color(UiTheme.Accent.r, UiTheme.Accent.g,
                                             UiTheme.Accent.b, 0.45f);
            input.caretColor = UiTheme.Accent;
            input.customCaretColor = true;
            return input;
        }

        static void Stretch(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static SettingsPanel BuildSettingsPanel(GameObject canvas)
        {
            var root = FullScreenPanel(canvas, "SettingsPanel", Dim);
            var panel = root.AddComponent<SettingsPanel>();

            // The quality row tints one button on and two off. Those tints multiply the kit's
            // painted button art, so they have to be opaque -- see UiTheme.ToggleOn/Off.
            panel.SelectedTint = UiTheme.ToggleOn;
            panel.UnselectedTint = UiTheme.ToggleOff;

            // NOT the kit's own `setting-container`, despite the name.
            //
            // That sprite's painted frame measures 165 px on every side, which on a 900-unit
            // card is 37% of the height gone before any control is placed -- the first pass
            // used it and the word SETTINGS was drawn across the top moulding. This is the
            // same container the pause menu uses; its frame is 88 units, which leaves the
            // 724 units of usable height this screen's nine rows actually need.
            //
            // Everything below is laid out inside +/-362 (half the card, less that frame).
            var card = CardPanel(root, "Card", new Vector2(1180f, 900f), UiTheme.Panel);

            CentreText(card, "Title", "SETTINGS", new Vector2(0f, 330f), new Vector2(1000f, 60f),
                       UiTheme.Title, FontStyle.Bold, Ink);
            Divider(card, "Title", 288f, 560f, gold: true);

            float y = 206f;
            const float step = 68f;

            panel.MasterSlider = SliderRow(card, "Master", "MASTER VOLUME", y, 0f, 1f, out panel.MasterValue);
            y -= step;
            panel.MusicSlider = SliderRow(card, "Music", "MUSIC", y, 0f, 1f, out panel.MusicValue);
            y -= step;
            panel.SfxSlider = SliderRow(card, "Sfx", "EFFECTS", y, 0f, 1f, out panel.SfxValue);
            y -= step;
            panel.SensitivitySlider = SliderRow(card, "Sensitivity", "LOOK SENSITIVITY", y,
                                                0.3f, 2.5f, out panel.SensitivityValue);
            y -= step - 6f;
            Divider(card, "Audio", y, 860f);
            y -= 42f;

            // --- Handedness and invert, side by side -------------------------------------
            panel.HandednessButton = WideButton(card, "Handedness", "LAYOUT:  RIGHT",
                                                new Vector2(-256f, y), new Vector2(480f, 62f),
                                                out panel.HandednessLabel);
            panel.InvertYButton = WideButton(card, "InvertY", "INVERT Y:  OFF",
                                             new Vector2(256f, y), new Vector2(480f, 62f),
                                             out panel.InvertYLabel);
            y -= 66f;

            // --- Quality tier ------------------------------------------------------------
            // Left edge at -480, not -560: the caption rect is centred on its position, so a
            // 320-wide box at -400 would start 60 units inside the card's own frame.
            CentreText(card, "QualityCaption", "GRAPHICS", new Vector2(-330f, y),
                       new Vector2(320f, 40f), UiTheme.Body, FontStyle.Bold, Muted).alignment =
                TextAnchor.MiddleLeft;

            y -= 56f;
            panel.LowButton = WideButton(card, "Low", "LOW", new Vector2(-336f, y),
                                         new Vector2(310f, 62f), out _);
            panel.MediumButton = WideButton(card, "Medium", "MEDIUM", new Vector2(0f, y),
                                            new Vector2(310f, 62f), out _);
            panel.HighButton = WideButton(card, "High", "HIGH", new Vector2(336f, y),
                                          new Vector2(310f, 62f), out _);

            y -= 54f;
            panel.QualityDetail = CentreText(card, "QualityDetail", "", new Vector2(0f, y),
                                             new Vector2(1000f, 34f), UiTheme.Small,
                                             FontStyle.Normal, Muted);

            panel.CustomiseHudButton = WideButton(card, "CustomiseHud", "CUSTOMISE CONTROLS",
                                                 new Vector2(0f, -256f), new Vector2(420f, 62f),
                                                 out _, UiTheme.ButtonTone.Normal, "settings");

            panel.BackButton = WideButton(card, "Back", "BACK", new Vector2(0f, -328f),
                                          new Vector2(420f, 62f), out _,
                                          UiTheme.ButtonTone.Quiet, "back");
            return panel;
        }

        // ------------------------------------------------------------- full map

        /// <summary>
        /// The touch-control layout editor.
        ///
        /// A deliberately thin strip along the top rather than a full card: the whole point is
        /// to see and reach the real controls underneath it while it is open, and a panel in
        /// the middle of the screen would sit exactly where the player needs to drag things.
        /// </summary>
        static HudLayoutEditor BuildHudLayoutEditor(GameObject canvas)
        {
            var root = SceneAssembler.NewUi(canvas, "HudLayoutEditor");
            var rrt = root.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.offsetMin = new Vector2(0f, -250f);
            rrt.offsetMax = new Vector2(0f, 0f);

            var panel = root.AddComponent<HudLayoutEditor>();

            // A flat panel, not one of the kit's framed tiles.
            //
            // Both framed sprites were tried and the build-time frame audit refused both, and
            // it was right to: shop-container-large paints a 165 px border and the card paints
            // 74, against a strip only 250 tall. There is no sensible way to lay a title, a
            // hint, a slider and two buttons inside 100 px of interior. A utility strip does
            // not need painted moulding -- it needs to be legible and out of the way -- so it
            // is a flat panel in the theme's own deep tone with a rule under it.
            var plate = root.AddComponent<Image>();
            plate.color = new Color(UiTheme.PanelDeepest.r, UiTheme.PanelDeepest.g,
                                    UiTheme.PanelDeepest.b, 0.94f);

            var edge = SceneAssembler.NewUi(root, "Edge");
            var ert = edge.GetComponent<RectTransform>();
            ert.anchorMin = new Vector2(0f, 0f);
            ert.anchorMax = new Vector2(1f, 0f);
            ert.pivot = new Vector2(0.5f, 0f);
            ert.offsetMin = Vector2.zero;
            ert.offsetMax = new Vector2(0f, 4f);
            var edgeImage = edge.AddComponent<Image>();
            edgeImage.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.75f);
            edgeImage.raycastTarget = false;

            CentreText(root, "Title", "CUSTOMISE CONTROLS", new Vector2(0f, -46f),
                       new Vector2(900f, 46f), UiTheme.Heading, FontStyle.Bold, Ink);

            CentreText(root, "Hint", "DRAG A CONTROL TO MOVE IT.  TOUCH ONE TO RESIZE IT.",
                       new Vector2(0f, -92f), new Vector2(1200f, 34f), UiTheme.Small,
                       FontStyle.Normal, Muted);

            panel.SelectedLabel = CentreText(root, "Selected", "", new Vector2(-430f, -150f),
                                             new Vector2(560f, 40f), UiTheme.Body,
                                             FontStyle.Bold, Accent);
            panel.SelectedLabel.alignment = TextAnchor.MiddleLeft;

            // Size slider. 0.6 to 1.8 is the range HudLayoutStore clamps to, so the slider
            // cannot ask for a size the store will refuse and silently snap back from.
            var sliderGo = SceneAssembler.NewUi(root, "SizeSlider");
            var srt = sliderGo.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(120f, -150f);
            srt.sizeDelta = new Vector2(420f, 40f);
            panel.SizeSlider = BuildSlider(sliderGo, 0.6f, 1.8f, 1f);

            panel.SizeValue = CentreText(root, "SizeValue", "--", new Vector2(380f, -150f),
                                         new Vector2(120f, 40f), UiTheme.Body,
                                         FontStyle.Bold, Ink);

            panel.ResetButton = WideButton(root, "ResetLayout", "RESET", new Vector2(600f, -150f),
                                           new Vector2(230f, 70f), out _,
                                           UiTheme.ButtonTone.Normal, "try-again");

            panel.DoneButton = WideButton(root, "LayoutDone", "DONE", new Vector2(850f, -150f),
                                          new Vector2(230f, 70f), out _,
                                          UiTheme.ButtonTone.Strong, "check");

            root.SetActive(false);
            return panel;
        }

        /// <summary>A themed slider, matching the audio sliders on the settings screen.</summary>
        static Slider BuildSlider(GameObject go, float min, float max, float value)
        {
            var slider = go.AddComponent<Slider>();

            var track = SceneAssembler.NewUi(go, "Track");
            var trt = track.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 0.5f);
            trt.offsetMin = new Vector2(0f, -8f);
            trt.offsetMax = new Vector2(0f, 8f);
            var trackImage = track.AddComponent<Image>();
            trackImage.sprite = UiTheme.BarTrack;
            trackImage.type = Image.Type.Sliced;
            trackImage.pixelsPerUnitMultiplier = 1f;

            var fillArea = SceneAssembler.NewUi(go, "FillArea");
            var frt = fillArea.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0.5f);
            frt.anchorMax = new Vector2(1f, 0.5f);
            frt.offsetMin = new Vector2(0f, -8f);
            frt.offsetMax = new Vector2(0f, 8f);

            var fill = SceneAssembler.NewUi(fillArea, "Fill");
            var fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImage = fill.AddComponent<Image>();
            fillImage.sprite = UiTheme.BarFill;
            fillImage.type = Image.Type.Sliced;
            fillImage.pixelsPerUnitMultiplier = 1f;

            var handle = SceneAssembler.NewUi(go, "Handle");
            var hrt = handle.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(34f, 34f);
            var handleImage = handle.AddComponent<Image>();
            handleImage.sprite = SceneAssembler.LoadSprite("ui_circle", SceneAssembler.MakeCircleSprite);
            handleImage.color = UiTheme.Accent;

            slider.fillRect = fillRt;
            slider.handleRect = hrt;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            return slider;
        }

        static FullMapScreen BuildFullMap(GameObject canvas, Sprite circle)
        {
            var root = FullScreenPanel(canvas, "FullMap", Dim);
            var screen = root.AddComponent<FullMapScreen>();

            const float size = 780f;

            var frame = SceneAssembler.NewUi(root, "MapFrame");
            var frt = frame.GetComponent<RectTransform>();
            frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
            frt.pivot = new Vector2(0.5f, 0.5f);
            frt.anchoredPosition = new Vector2(-120f, -10f);
            frt.sizeDelta = new Vector2(size, size);

            // The kit's thin-framed tile, not its wide `shop-container`. The wide one paints
            // a 165 px frame; the road grid is laid out against the MapArea rect, so a frame
            // that thick left the streets drawn straight across the panel's own moulding and
            // out onto the screen behind it.
            var bg = frame.AddComponent<Image>();
            UiTheme.StylePanel(bg, UiTheme.Card);
            bg.raycastTarget = false;

            // The frame that was lost by moving to the thin-bordered plate, put back *outside*
            // the map instead of inside it: the kit's selection glow, stretched around the
            // panel. Instruction 7 wants the kit's own line and frame art on borders, and this
            // is the way to get it without the frame eating the map's interior.
            var glow = SceneAssembler.NewUi(frame, "Frame");
            var glowRt = glow.GetComponent<RectTransform>();
            glowRt.anchorMin = Vector2.zero;
            glowRt.anchorMax = Vector2.one;
            glowRt.offsetMin = new Vector2(-24f, -24f);
            glowRt.offsetMax = new Vector2(24f, 24f);
            glow.transform.SetAsFirstSibling();

            var glowImage = glow.AddComponent<Image>();
            glowImage.sprite = UiTheme.SlotHighlight;
            glowImage.type = Image.Type.Sliced;
            glowImage.pixelsPerUnitMultiplier = 1f;
            glowImage.color = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.65f);
            glowImage.raycastTarget = false;

            var area = SceneAssembler.NewUi(frame, "MapArea");
            var art = area.GetComponent<RectTransform>();
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
            art.pivot = new Vector2(0.5f, 0.5f);
            art.sizeDelta = new Vector2(size - 48f, size - 48f);

            // Belt and braces: the roads are sized from the world bounds plus a margin, and a
            // blip for something outside those bounds would still draw past the frame.
            area.AddComponent<RectMask2D>();

            var minimap = frame.AddComponent<Minimap>();
            minimap.MapArea = art;
            SetIslandBounds(minimap);

            // --- terrain background, in place of the drawn grid ---------------------------
            //
            // The grid it replaces was nine even columns and rows standing in for streets. It
            // had three problems beyond being abstract: the columns were visibly unevenly
            // spaced and overran the frame, it showed only the city block grid so the beach and
            // the harbour did not exist on it at all, and the player arrow spent its time
            // clamped against the left edge because the player was standing somewhere the map
            // could not represent.
            //
            // This is a photograph of the real terrain, baked by MapBaker -- see there for why
            // it is a committed asset rather than a render at open time.
            var baked = MapBaker.Load();
            var background = SceneAssembler.NewUi(area, "Terrain");
            var brt = background.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            background.transform.SetAsFirstSibling();

            var bgImage = background.AddComponent<Image>();
            bgImage.sprite = baked;
            bgImage.raycastTarget = false;
            bgImage.preserveAspect = false;   // bounds are square and so is the bake
            if (baked == null)
            {
                bgImage.color = new Color(0.10f, 0.12f, 0.22f, 1f);
                Debug.LogWarning("[Map] No baked map texture. Run "
                                 + "'Tools/Mini GTA/18. Bake Map Texture' -- the full map will "
                                 + "show a flat panel until you do.");
            }

            // --- blips, one shape per category --------------------------------------------
            var arrow = SceneAssembler.LoadSprite("ui_arrow", SceneAssembler.MakeArrowSprite);

            minimap.ObjectiveBlip = SceneAssembler.MakeBlip(area, "ObjectiveBlip",
                                        UiTheme.Icon("location"), Accent, 30f);
            minimap.GiverBlipTemplate = SceneAssembler.MakeBlip(area, "GiverBlip",
                                        UiTheme.Icon("person"), UiTheme.Gold, 26f);
            minimap.WeaponBlipTemplate = SceneAssembler.MakeBlip(area, "WeaponBlip",
                                        UiTheme.Icon("gun"), UiTheme.Danger, 24f);
            minimap.PoliceBlipTemplate = SceneAssembler.MakeBlip(area, "PoliceBlip",
                                        UiTheme.Icon("shield"), UiTheme.Shield, 24f);
            minimap.ShopBlipTemplate = SceneAssembler.MakeBlip(area, "ShopBlip",
                                        UiTheme.Icon("shop"), UiTheme.Positive, 24f);
            minimap.PropertyBlipTemplate = SceneAssembler.MakeBlip(area, "PropertyBlip",
                                        UiTheme.Icon("home"), UiTheme.Lilac, 24f);
            minimap.BoatBlipTemplate = SceneAssembler.MakeBlip(area, "BoatBlip",
                                        UiTheme.Icon("send"), UiTheme.Metallic, 24f);
            minimap.HelicopterBlipTemplate = SceneAssembler.MakeBlip(area, "HelicopterBlip",
                                        UiTheme.Icon("rocket"), UiTheme.Metallic, 24f);
            minimap.DoorBlipTemplate = SceneAssembler.MakeBlip(area, "DoorBlip",
                                        UiTheme.Icon("door"), UiTheme.TextOnPanelDim, 20f);

            minimap.PlayerBlip = SceneAssembler.MakeBlip(area, "PlayerBlip", arrow,
                                                        UiTheme.TextOnPanel, 34f);

            // Every marker except the player's own becomes a waypoint target. Done on the
            // templates, because Minimap builds its pools by cloning them -- so one call here
            // makes all thirty-odd pooled copies tappable.
            foreach (var template in new[]
                     {
                         minimap.ObjectiveBlip, minimap.GiverBlipTemplate,
                         minimap.WeaponBlipTemplate, minimap.PoliceBlipTemplate,
                         minimap.ShopBlipTemplate, minimap.PropertyBlipTemplate,
                         minimap.BoatBlipTemplate, minimap.HelicopterBlipTemplate,
                         minimap.DoorBlipTemplate,
                     })
            {
                if (template == null) continue;
                var tap = template.gameObject.AddComponent<MapBlip>();
                tap.Selectable = true;

                var img = template.GetComponent<Image>();
                if (img != null)
                {
                    // MakeBlip turns raycasts off, which is right for the corner map and wrong
                    // here: on the full map a blip is the thing you aim at.
                    img.raycastTarget = true;
                    tap.Highlight = img;
                }
            }

            screen.Map = minimap;

            // --- Legend, right of the map -------------------------------------------------
            //
            // Ten categories now instead of four, so the column is taller and the rows are
            // spaced on a fixed pitch rather than at four hand-written offsets that had drifted
            // into different gaps. Each row shows the actual blip sprite, not a coloured dot
            // standing in for it, so the legend cannot disagree with the map.
            var legend = SceneAssembler.NewUi(root, "Legend");
            var lrt = legend.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(300f, 0f);
            lrt.sizeDelta = new Vector2(430f, 720f);

            CentreText(legend, "LegendTitle", "MAP", new Vector2(0f, 320f), new Vector2(400f, 50f),
                       UiTheme.Heading, FontStyle.Bold, Ink).alignment = TextAnchor.MiddleLeft;

            var rule = SceneAssembler.NewUi(legend, "Rule_Legend");
            var ruleRt = rule.GetComponent<RectTransform>();
            ruleRt.anchorMin = ruleRt.anchorMax = new Vector2(0.5f, 0.5f);
            ruleRt.pivot = new Vector2(0f, 0.5f);
            ruleRt.anchoredPosition = new Vector2(-205f, 292f);
            ruleRt.sizeDelta = new Vector2(150f, 6f);
            var ruleImage = rule.AddComponent<Image>();
            ruleImage.sprite = UiTheme.DividerGold;
            ruleImage.type = Image.Type.Sliced;
            ruleImage.pixelsPerUnitMultiplier = 1f;
            ruleImage.color = UiTheme.Gold;
            ruleImage.raycastTarget = false;

            // One pitch, one loop -- the old version placed four rows at 80/30/-20/-70, which
            // is a 50 gap written out three times and no room for a fifth.
            const float rowPitch = 42f;
            float rowY = 236f;

            LegendRow(legend, arrow, "YOU", UiTheme.TextOnPanel, rowY); rowY -= rowPitch;
            LegendRow(legend, UiTheme.Icon("location"), "OBJECTIVE", Accent, rowY); rowY -= rowPitch;
            LegendRow(legend, UiTheme.Icon("person"), "JOB AVAILABLE", UiTheme.Gold, rowY); rowY -= rowPitch;
            LegendRow(legend, UiTheme.Icon("gun"), "WEAPON", UiTheme.Danger, rowY); rowY -= rowPitch;
            LegendRow(legend, UiTheme.Icon("shield"), "POLICE", UiTheme.Shield, rowY); rowY -= rowPitch;
            LegendRow(legend, UiTheme.Icon("shop"), "SHOP", UiTheme.Positive, rowY); rowY -= rowPitch;
            LegendRow(legend, UiTheme.Icon("home"), "PROPERTY", UiTheme.Lilac, rowY); rowY -= rowPitch;
            LegendRow(legend, UiTheme.Icon("door"), "ENTRANCE", UiTheme.TextOnPanelDim, rowY); rowY -= rowPitch;
            LegendRow(legend, UiTheme.Icon("send"), "BOAT", UiTheme.Metallic, rowY); rowY -= rowPitch;
            LegendRow(legend, UiTheme.Icon("rocket"), "HELICOPTER", UiTheme.Metallic, rowY); rowY -= rowPitch;

            screen.ObjectiveLabel = CentreText(legend, "Objective", "", new Vector2(0f, -230f),
                                               new Vector2(400f, 76f), UiTheme.Body,
                                               FontStyle.Normal, Muted);
            screen.ObjectiveLabel.alignment = TextAnchor.UpperLeft;
            screen.ObjectiveLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

            screen.PositionLabel = CentreText(legend, "Position", "", new Vector2(0f, -292f),
                                              new Vector2(400f, 34f), UiTheme.Small,
                                              FontStyle.Normal, Muted);
            screen.PositionLabel.alignment = TextAnchor.MiddleLeft;

            // CLOSE, under the legend column and clear of it.
            //
            // The old one sat at (480, -206) in the panel while PositionLabel sat at (310+0,
            // -230) in the legend -- the two overlapped, and the position readout was drawn
            // half-behind the button. It is also built at the kit's own Wide proportions now:
            // the art is 356x96 and it was being drawn at 360x70, which squashed the painted
            // gradient bands out of step with every other button in the interface.
            // Clearing a waypoint without picking another. Tapping the same marker twice also
            // clears it, but that is a thing you have to be told; a button is not.
            var clear = WideButton(root, "ClearWaypoint", "CLEAR WAYPOINT", new Vector2(505f, -368f),
                                   new Vector2(356f, 76f), out _,
                                   UiTheme.ButtonTone.Normal, "cross");
            screen.ClearWaypointButton = clear;

            screen.CloseButton = WideButton(root, "Close", "CLOSE", new Vector2(505f, -470f),
                                            new Vector2(356f, 96f), out _,
                                            UiTheme.ButtonTone.Quiet, "cross");
            return screen;
        }

        /// <summary>
        /// One legend row: the category's own blip sprite and its caption.
        ///
        /// Takes the same sprite the map draws rather than a generic dot, so the legend and the
        /// map cannot drift apart -- the previous version showed four coloured circles for
        /// markers that were also all circles, which told the player nothing except the colour.
        /// </summary>
        static void LegendRow(GameObject parent, Sprite icon, string caption, Color colour, float y)
        {
            var dot = SceneAssembler.NewUi(parent, "Icon_" + caption);
            var drt = dot.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = new Vector2(-185f, y);
            drt.sizeDelta = new Vector2(28f, 28f);

            var image = dot.AddComponent<Image>();
            image.sprite = icon;
            image.color = colour;
            image.preserveAspect = true;
            image.raycastTarget = false;

            var label = CentreText(parent, "Label_" + caption, caption, new Vector2(24f, y),
                                   new Vector2(330f, 34f), UiTheme.Body, FontStyle.Normal,
                                   UiTheme.TextOnPanel);
            label.alignment = TextAnchor.MiddleLeft;
        }

        /// <summary>
        /// Bounds the full map to the whole island, not just the city block grid.
        ///
        /// The city-only bounds are why the player arrow lived clamped against the left edge:
        /// the beach, the harbour and everything west of the grid mapped to u &lt; 0 and got
        /// pinned to the boundary. The baked background is a picture of the entire terrain, so
        /// the bounds have to be the entire terrain for the two to line up.
        /// </summary>
        static void SetIslandBounds(Minimap minimap)
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain == null) { SetCityBounds(minimap); return; }

            var size = terrain.terrainData.size;
            Vector3 origin = terrain.transform.position;
            minimap.WorldMin = new Vector2(origin.x, origin.z);
            minimap.WorldMax = new Vector2(origin.x + size.x, origin.z + size.z);
        }

        /// <summary>The same bounds the corner minimap uses, from the city's own constants.</summary>
        static void SetCityBounds(Minimap minimap)
        {
            float gridSpan = CityBuilder.Blocks * CityBuilder.Pitch;
            float ox = TerrainBuilder.CityCenter.x - gridSpan * 0.5f;
            float oz = TerrainBuilder.CityCenter.y - gridSpan * 0.5f;
            const float margin = 30f;

            minimap.WorldMin = new Vector2(ox - margin, oz - margin);
            minimap.WorldMax = new Vector2(ox + gridSpan + margin, oz + gridSpan + margin);
        }

        // ----------------------------------------------------------------- pieces

        static GameObject FullScreenPanel(GameObject canvas, string name, Color background)
        {
            var go = SceneAssembler.NewUi(canvas, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // The dimmer is a bleeding child, not the root itself. The root stays inside the
            // safe-area container so the card centred on it clears a notch; the dimmer has to
            // reach the physical screen edge or the inset shows as an undimmed strip of the
            // world down each side while a modal is open.
            var scrimGo = SceneAssembler.NewUi(go, "Scrim");
            var image = scrimGo.AddComponent<Image>();
            image.color = background;
            // Raycastable on purpose: it is what stops a tap reaching the world behind it.
            image.raycastTarget = true;
            scrimGo.AddComponent<SafeAreaBleed>();

            go.AddComponent<CanvasGroup>();
            return go;
        }

        static GameObject CardPanel(GameObject parent, string name, Vector2 size, Sprite art = null)
        {
            var go = SceneAssembler.NewUi(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;

            var image = go.AddComponent<Image>();
            UiTheme.StylePanel(image, art != null ? art : UiTheme.Panel);
            image.raycastTarget = false;
            return go;
        }

        static Button MenuButton(GameObject parent, string name, string caption, Vector2 position,
                                 UiTheme.ButtonTone tone = UiTheme.ButtonTone.Normal,
                                 string icon = null)
            => WideButton(parent, name, caption, position, new Vector2(420f, 74f), out _, tone, icon);

        /// <summary>
        /// A menu button in the kit's painted art, optionally with one of the kit's flat
        /// pictograms on its left.
        ///
        /// The caption is <see cref="UiTheme.TextOnButton"/>, not <c>Ink</c>: the kit's buttons
        /// are light and its panels are dark, so a button caption and the heading above it are
        /// opposite colours. Getting this wrong is invisible-text, which is why UiTheme deleted
        /// the single shared `Ink` that used to serve both.
        /// </summary>
        static Button WideButton(GameObject parent, string name, string caption,
                                 Vector2 position, Vector2 size, out Text label,
                                 UiTheme.ButtonTone tone = UiTheme.ButtonTone.Normal,
                                 string icon = null)
        {
            var go = SceneAssembler.NewUi(parent, "Btn_" + name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var image = go.AddComponent<Image>();
            var button = go.AddComponent<Button>();
            UiTheme.StyleButton(button, image, tone);

            // The glyph sits in the button's left third and the caption shifts right by the
            // same amount, so the pair stays optically centred rather than the caption keeping
            // the middle and the icon hanging off the edge.
            float shift = 0f;
            if (!string.IsNullOrEmpty(icon))
            {
                float glyph = Mathf.Min(size.y * 0.52f, 40f);
                shift = glyph * 0.75f;

                var art = SceneAssembler.NewUi(go, "Icon");
                var art_rt = art.GetComponent<RectTransform>();
                art_rt.anchorMin = art_rt.anchorMax = new Vector2(0f, 0.5f);
                art_rt.pivot = new Vector2(0.5f, 0.5f);
                art_rt.anchoredPosition = new Vector2(glyph * 0.9f, 0f);
                art_rt.sizeDelta = new Vector2(glyph, glyph);

                var glyphImage = art.AddComponent<Image>();
                glyphImage.sprite = UiTheme.Icon(icon);
                glyphImage.color = UiTheme.TextOnButton;
                glyphImage.preserveAspect = true;
                glyphImage.raycastTarget = false;
            }

            label = CentreText(go, "Label", caption, new Vector2(shift, 0f),
                               new Vector2(size.x - shift * 2f, size.y),
                               UiTheme.Label, FontStyle.Bold, UiTheme.TextOnButton);
            return button;
        }

        /// <summary>
        /// One of the kit's sci-fi rules, stretched across a card.
        ///
        /// Instruction 7 of the re-skin brief: sections are separated by the kit's own line art
        /// rather than by whitespace or a flat rectangle.
        /// </summary>
        static void Divider(GameObject parent, string name, float y, float width, bool gold = false)
        {
            var go = SceneAssembler.NewUi(parent, "Rule_" + name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(width, gold ? 6f : 8f);

            var image = go.AddComponent<Image>();
            image.sprite = gold ? UiTheme.DividerGold : UiTheme.Divider;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = gold ? UiTheme.Gold : new Color(1f, 1f, 1f, 0.55f);
            image.raycastTarget = false;
        }

        static Text CentreText(GameObject parent, string name, string content, Vector2 position,
                               Vector2 size, int fontSize, FontStyle style, Color colour)
        {
            var text = SceneAssembler.Label(parent, name, Vector2.zero, size, Font, fontSize,
                                            TextAnchor.MiddleCenter, colour, style);
            text.text = content;
            SceneAssembler.CentreLabel(text, position, size);
            return text;
        }

        /// <summary>A labelled slider with a live percentage on the right.</summary>
        static Slider SliderRow(GameObject parent, string name, string caption, float y,
                                float min, float max, out Text valueLabel)
        {
            // All three pieces sit inside the card's usable width of +/-500: the caption box
            // reaches -500, the track runs -180 to 340, and the percentage ends at 495.
            var caption0 = CentreText(parent, name + "Caption", caption, new Vector2(-350f, y),
                                      new Vector2(300f, 40f), UiTheme.Body, FontStyle.Bold, Muted);
            caption0.alignment = TextAnchor.MiddleLeft;

            valueLabel = CentreText(parent, name + "Value", "", new Vector2(430f, y),
                                    new Vector2(130f, 40f), UiTheme.Body, FontStyle.Bold, Accent);
            valueLabel.alignment = TextAnchor.MiddleRight;

            // 44 tall, not 34. The kit's track art is a 44 px channel with a 7 px lip top and
            // bottom; drawn 14 units tall it is nothing but lip and renders as a hairline.
            return MakeSlider(parent, name + "Slider", new Vector2(80f, y),
                              new Vector2(520f, 44f), min, max);
        }

        /// <summary>
        /// A uGUI Slider assembled by hand.
        ///
        /// The hierarchy is not decorative -- Slider drives <c>fillRect</c>'s anchorMax and
        /// <c>handleRect</c>'s anchors directly, so both need their own padded parent or the
        /// handle slides half off each end of the track.
        /// </summary>
        static Slider MakeSlider(GameObject parent, string name, Vector2 position, Vector2 size,
                                 float min, float max)
        {
            float handle = size.y;

            var go = SceneAssembler.NewUi(parent, name);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            // Full height, so the kit's channel art is drawn at something like its authored
            // proportions rather than squashed into the middle 40% of the row.
            var background = SceneAssembler.NewUi(go, "Background");
            var brt = background.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            var bgImage = background.AddComponent<Image>();
            bgImage.sprite = UiTheme.SliderTrack;
            bgImage.type = Image.Type.Sliced;
            bgImage.pixelsPerUnitMultiplier = 1f;
            bgImage.color = Color.white;

            var fillArea = SceneAssembler.NewUi(go, "Fill Area");
            var fart = fillArea.GetComponent<RectTransform>();
            fart.anchorMin = new Vector2(0f, 0.22f);
            fart.anchorMax = new Vector2(1f, 0.78f);
            fart.offsetMin = new Vector2(handle * 0.5f, 0f);
            fart.offsetMax = new Vector2(-handle * 0.5f, 0f);

            var fill = SceneAssembler.NewUi(fillArea, "Fill");
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0f, 1f);
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            frt.sizeDelta = new Vector2(handle, 0f);
            var fillImage = fill.AddComponent<Image>();
            fillImage.sprite = UiTheme.SliderFill;
            fillImage.type = Image.Type.Sliced;
            fillImage.pixelsPerUnitMultiplier = 1f;
            fillImage.color = UiTheme.Accent;

            var handleArea = SceneAssembler.NewUi(go, "Handle Slide Area");
            var hart = handleArea.GetComponent<RectTransform>();
            hart.anchorMin = Vector2.zero;
            hart.anchorMax = Vector2.one;
            hart.offsetMin = new Vector2(handle * 0.5f, 0f);
            hart.offsetMax = new Vector2(-handle * 0.5f, 0f);

            var handleGo = SceneAssembler.NewUi(handleArea, "Handle");
            var hrt = handleGo.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0f, 0f);
            hrt.anchorMax = new Vector2(0f, 1f);
            hrt.offsetMin = hrt.offsetMax = Vector2.zero;
            hrt.sizeDelta = new Vector2(handle * 1.05f, 0f);
            var handleImage = handleGo.AddComponent<Image>();
            handleImage.sprite = UiTheme.SliderKnob;
            handleImage.type = Image.Type.Simple;
            handleImage.preserveAspect = true;
            handleImage.color = Color.white;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = frt;
            slider.handleRect = hrt;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = min;
            return slider;
        }

        static GameObject Find(GameObject parent, string name)
        {
            var t = parent.transform.Find(name);
            return t != null ? t.gameObject : null;
        }
    }
}
