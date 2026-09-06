using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// The one place that decides what the interface looks like.
    ///
    /// Phase 13 re-skinned every screen from the Soft Touch kit onto the <b>Space Exploration
    /// GUI Kit</b>. MenuBuilder, SceneAssembler and LobbyBuilder ask this class for a panel, a
    /// button, a bar or a label; the look lives here and nowhere else. Re-skinning again is a
    /// change to this file.
    ///
    /// <b>Three things about this pass are not cosmetic and are easy to get wrong.</b>
    ///
    /// 1. <b>The polarity inverted.</b> Soft Touch was cream panels with dark text. This kit is
    ///    deep-indigo panels with <i>light</i> painted buttons on them, so text on a panel must
    ///    be light and text on a button must be dark -- opposite inks on the same screen. The
    ///    old single <c>Ink</c> colour cannot express that, so it is gone: use
    ///    <see cref="TextOnPanel"/> or <see cref="TextOnButton"/>. Anything still saying
    ///    <c>UiTheme.Ink</c> is a compile error on purpose, because the failure mode otherwise
    ///    is invisible text that no assertion catches (see PHASE12's five capture-only defects).
    ///
    /// 2. <b>The borders are measured, not typed.</b> The kit ships every sprite with a zero
    ///    nine-slice border, and its art is used here at sizes it was not authored for -- a
    ///    356x96 button becomes a 520x96 PLAY, a 1128x512 container becomes a 560x700 card.
    ///    <see cref="PrepareSprites"/> reads each PNG off disk and derives the border from the
    ///    opaque silhouette's corner radius, so the table cannot go stale when a sprite is
    ///    swapped for a different size. See DECISIONS D46.
    ///
    /// 3. <b>The type scale is still bounded.</b> PHASE8 measured 22 distinct font sizes forcing
    ///    a dynamic-atlas repack on device. D30 snapped that to seven steps on one face and this
    ///    pass keeps exactly that bound -- one font, seven sizes -- even though the face changed.
    /// </summary>
    public static class UiTheme
    {
        const string Kit = "Assets/Space_Exploration_GUI_Kit";

        // ------------------------------------------------------------------ palette
        //
        // Every value below was sampled out of the kit's own PNGs rather than picked by eye:
        // the colour histogram of each piece of art, taken over its opaque pixels. The hex
        // in each comment is the pixel the value came from, so a future pass can check it.

        // --- surfaces ---------------------------------------------------------------

        /// <summary>The containers' translucent indigo body. #463577 at alpha 194.</summary>
        public static readonly Color PanelFill = Rgb(0x46, 0x35, 0x77, 194);

        /// <summary>The kit's solid dark plate: rows, chips, bar tracks. #271C47.</summary>
        public static readonly Color PanelDeep = Rgb(0x27, 0x1C, 0x47);

        /// <summary>Deeper still -- an input field with focus. #161438.</summary>
        public static readonly Color PanelDeepest = Rgb(0x16, 0x14, 0x38);

        // --- ink ---------------------------------------------------------------------
        //
        // Two inks, and which one a caption needs depends on what it is drawn on. Panels are
        // dark; the kit's painted buttons are light. EnforceContrast is the safety net.

        /// <summary>Primary text on one of the kit's dark panels. #DFF1F7, the kit's own white.</summary>
        public static readonly Color TextOnPanel = Rgb(0xDF, 0xF1, 0xF7);

        /// <summary>Secondary text on a dark panel: captions, hints, units. Metallic #A3AAC1.</summary>
        public static readonly Color TextOnPanelDim = Rgb(0xA3, 0xAA, 0xC1);

        /// <summary>Text on one of the kit's light painted buttons. #271C47 -- the outline
        /// colour the kit draws every one of those buttons with, so it can never clash.</summary>
        public static readonly Color TextOnButton = Rgb(0x27, 0x1C, 0x47);

        /// <summary>The same, softened for a sub-caption inside a button.</summary>
        public static readonly Color TextOnButtonDim = Rgb(0x27, 0x1C, 0x47, 160);

        // --- accents -----------------------------------------------------------------

        /// <summary>The kit's neon cyan: headings, selection glow, live values. #9FDFF7.</summary>
        public static readonly Color Accent = Rgb(0x9F, 0xDF, 0xF7);

        // --- the same accents, darkened for text sitting on a light button face --------
        //
        // A shop row and a wardrobe tile are buttons, so their painted face is light, and the
        // status word on them cannot be the same cyan/pink/grey that works on a dark panel.
        // These are those three hues taken down in value until they read at 18pt on the kit's
        // lilac. They exist so the state colours stay recognisably the same colours rather
        // than collapsing to black.

        /// <summary>Cyan dark enough to read as text on a light button face.</summary>
        public static readonly Color AccentOnButton = Rgb(0x1B, 0x4E, 0x66);
        /// <summary>Danger pink, likewise.</summary>
        public static readonly Color DangerOnButton = Rgb(0x8C, 0x1E, 0x5C);
        /// <summary>Inert metallic, likewise.</summary>
        public static readonly Color MetallicOnButton = Rgb(0x4A, 0x4E, 0x63);

        /// <summary>Coin gold. Money, rewards, XP figures. #F7E2A6.</summary>
        public static readonly Color Gold = Rgb(0xF7, 0xE2, 0xA6);

        /// <summary>Affordable, owned, succeeded. The kit's progress-bar cyan, #ABDCF4.</summary>
        public static readonly Color Positive = Rgb(0xAB, 0xDC, 0xF4);

        /// <summary>Blocked, damaged, failed. The kit's heart/notification pink pushed hot.</summary>
        public static readonly Color Danger = Rgb(0xFF, 0x6F, 0xC8);

        /// <summary>Inert: owned-but-not-worn, unavailable stock, disabled rows. #A3AAC1.</summary>
        public static readonly Color Metallic = Rgb(0xA3, 0xAA, 0xC1);

        /// <summary>The kit's lilac. XP fill, secondary emphasis. #BB8FE2.</summary>
        public static readonly Color Lilac = Rgb(0xBB, 0x8F, 0xE2);

        /// <summary>The kit's own steel blue, for armour and shields. #9ABBF4.</summary>
        public static readonly Color Shield = Rgb(0x9A, 0xBB, 0xF4);

        /// <summary>Screen dimmer behind a modal. The kit's background-overlay, #130E2B.</summary>
        public static readonly Color Scrim = Rgb(0x13, 0x0E, 0x2B, 209);

        // --- HUD controls over the world ---------------------------------------------

        /// <summary>
        /// Tint on the HUD's translucent controls at rest.
        ///
        /// 0.80, not 0.70: checked in a play-mode frame over the beach, which is the brightest
        /// ground in the game, and at 0.70 the thumb cluster was washing into the sand.
        /// </summary>
        public static readonly Color HudIdle = new Color(1f, 1f, 1f, 0.80f);

        /// <summary>...and while held. Warms toward the kit's cyan rather than to white.</summary>
        public static readonly Color HudPressed = new Color(0.80f, 0.96f, 1f, 1f);

        /// <summary>
        /// Tints for a button showing an on/off state (the quality tier row).
        ///
        /// Both opaque. They multiply the kit's painted button art, and a faded tint makes that
        /// art vanish rather than dim -- the Phase 11 note about the tier row rendering as two
        /// captions floating on nothing applies to this kit identically.
        /// </summary>
        public static readonly Color ToggleOn = Rgb(0xAB, 0xDC, 0xF4);
        public static readonly Color ToggleOff = Rgb(0x7E, 0x86, 0x9C);

        static Color Rgb(int r, int g, int b, int a = 255) =>
            new Color(r / 255f, g / 255f, b / 255f, a / 255f);

        // ------------------------------------------------------------------- type
        //
        // Seven steps, and nothing may use a size that is not one of them. See D30.

        public const int Micro = 14;
        public const int Small = 18;
        public const int Body = 22;
        public const int Label = 26;
        public const int Heading = 32;
        public const int Title = 44;
        public const int Display = 64;

        static readonly int[] Scale = { Micro, Small, Body, Label, Heading, Title, Display };

        /// <summary>
        /// Rounds any requested point size to the nearest step on the scale.
        ///
        /// Deliberately silent rather than an error: callers all over three large files ask for
        /// sizes hand-tuned against an older look, and snapping them is exactly the wanted
        /// behaviour. What matters is that the set of sizes in the atlas stays bounded.
        /// </summary>
        public static int SnapSize(int requested)
        {
            int best = Scale[0];
            int bestGap = Mathf.Abs(requested - best);

            for (int i = 1; i < Scale.Length; i++)
            {
                int gap = Mathf.Abs(requested - Scale[i]);
                if (gap >= bestGap) continue;
                best = Scale[i];
                bestGap = gap;
            }
            return best;
        }

        // -------------------------------------------------------------------- font

        /// <summary>Where a licensed display face is dropped in. See <see cref="Font"/>.</summary>
        public const string FontDir = "Assets/Game/UI/Fonts";

        /// <summary>
        /// The interface face, resolved in priority order at build time.
        ///
        /// <b>The brief for this pass asked for the "Fatality" FPS gaming font, and that font is
        /// not in the project</b> -- there is no copy anywhere on this machine and it is not
        /// something this project may generate or fetch. So the lookup is a slot rather than a
        /// path: drop any <c>.ttf</c>/<c>.otf</c> into <see cref="FontDir"/> and the whole
        /// interface picks it up on the next build, no code change. A file whose name contains
        /// "fatality" wins over any other file there, so the intended face takes over the moment
        /// it is imported even if something else is already sitting in the folder.
        ///
        /// Until then the fallback is <b>Righteous</b>, which ships inside this GUI kit: a
        /// geometric display face, wide and heavy, and the closest thing in the project to the
        /// aggressive esports look the pass is after. Whichever face wins, the build logs it by
        /// name, so a report can never claim the intended font is in use when it is not.
        /// </summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;

                _font = FindDroppedFont(out _fontSource)
                        ?? Load<Font>(Kit + "/Fonts/Font Sources/static/Righteous-Regular.ttf");

                if (_font != null && _fontSource == null)
                    _fontSource = "Righteous (kit fallback -- Fatality not present)";

                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    _fontSource = "LegacyRuntime (nothing else resolved)";
                    Debug.LogWarning("[UI] No themed font found; fell back to LegacyRuntime.ttf.");
                }
                return _font;
            }
        }
        static Font _font;
        static string _fontSource;

        /// <summary>Which face <see cref="Font"/> actually resolved to, for the build log.</summary>
        public static string FontSource
        {
            get { var _ = Font; return _fontSource; }
        }

        /// <summary>
        /// Looks in <see cref="FontDir"/> for a dropped-in face, preferring one named for the
        /// font this pass was asked to use.
        /// </summary>
        static Font FindDroppedFont(out string source)
        {
            source = null;
            if (!Directory.Exists(FontDir)) return null;

            Font best = null;
            bool bestIsNamed = false;

            foreach (string path in Directory.GetFiles(FontDir))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf") continue;

                string asset = path.Replace('\\', '/');
                var font = Load<Font>(asset);
                if (font == null) continue;

                bool named = Path.GetFileNameWithoutExtension(path)
                                 .ToLowerInvariant().Contains("fatality");

                // A file named for the requested face beats anything else in the folder; among
                // equals the first found wins, which keeps the choice stable across rebuilds.
                if (best != null && (bestIsNamed || !named)) continue;

                best = font;
                bestIsNamed = named;
                source = Path.GetFileName(path) + (named ? "" : " (dropped in, not Fatality)");
            }
            return best;
        }

        // ---------------------------------------------------------------- sprites
        //
        // Keys, not paths, at the call sites: which tier of the kit's four a piece comes from
        // is a decision about pixel density and belongs here. Containers come from `large`
        // because they are drawn at up to 1240 canvas units; buttons and bars from `medium`
        // because they are drawn near their authored size; square buttons from `large` because
        // the medium square is only 60x56 and the rail draws it at 112.

        const string Buttons = Kit + "/Button_Images";
        const string Menu = Kit + "/Settings_&_Menu_Components/Medium/";
        const string Grid = Kit + "/Grid_Components/Medium/";
        const string Boxes = Kit + "/Containers/Large/";

        // --- panels and frames (instruction 2) ---------------------------------------

        /// <summary>Standard menu card: pause, character select, profile.</summary>
        public static Sprite Panel => Get(Boxes + "pause-container-large.png");
        /// <summary>A tall portrait card.</summary>
        public static Sprite PanelTall => Get(Boxes + "login-remove-ads-container-large.png");
        /// <summary>Wide sheet: the shop, the full map.</summary>
        public static Sprite Sheet => Get(Boxes + "shop-container-large.png");
        /// <summary>The settings frame, which carries its own header tab.</summary>
        public static Sprite SettingsFrame => Get(Boxes + "setting-container-large.png");
        /// <summary>Short landscape popup: dialogue, prompts, result banners.</summary>
        public static Sprite Popup => Get(Boxes + "victory-defeat-container-large.png");
        /// <summary>Solid dark plate for a tile or an inset block.</summary>
        public static Sprite Card => Get(Boxes + "shop-item-container-large.png");
        /// <summary>Solid dark bar: a list row, a HUD strip.</summary>
        public static Sprite Row => Get(Boxes + "inventory-menu-container-large.png");
        /// <summary>Small square frame for an icon.</summary>
        public static Sprite IconFrame => Get(Boxes + "homepage-icon-container-large.png");
        /// <summary>Rounded dark chip: money and level readouts, the name field.</summary>
        public static Sprite Chip => Get(Menu + "username-password-container-selected-deselected-medium.png");
        /// <summary>The same chip with focus.</summary>
        public static Sprite ChipFocused => Get(Menu + "username-password-container-selected-medium.png");
        /// <summary>Translucent lilac frame, for an item slot over a busy background.</summary>
        public static Sprite Slot => Get(Grid + "inventory-item-container copy-medium.png");
        /// <summary>Cyan selection glow, drawn over a slot.</summary>
        public static Sprite SlotHighlight => Get(Grid + "inventory-highlight-medium.png");

        // --- borders, dividers and separators (instruction 7) ------------------------

        /// <summary>Full-width sci-fi rule between sections.</summary>
        public static Sprite Divider => Get(Menu + "settings-divider-medium.png");
        /// <summary>The kit's gold accent line, for a heading underline.</summary>
        public static Sprite DividerGold => Get(Menu + "homepage-gui-line-medium.png");
        /// <summary>Short lilac underline, for a caption.</summary>
        public static Sprite Underline => Get(Menu + "inventory-underline-medium.png");

        // --- progress and loading bars (instruction 6) -------------------------------

        /// <summary>Thin bar track: health, armour, vehicle condition, loading.</summary>
        public static Sprite BarTrack => Get(Menu + "mission-bar-empty-medium.png");
        /// <summary>...and its cyan fill.</summary>
        public static Sprite BarFill => Get(Menu + "mission-bar-full-medium.png");
        /// <summary>Fill for a bar that has completed.</summary>
        public static Sprite BarFillDone => Get(Menu + "mission-bar-completed-medium.png");
        /// <summary>Chunky XP track.</summary>
        public static Sprite XpTrack => Get(Menu + "level-bar-empty-medium.png");
        /// <summary>...and its lilac fill.</summary>
        public static Sprite XpFill => Get(Menu + "level-bar-full-medium.png");
        public static Sprite SliderTrack => Get(Menu + "sound-bar-container-medium.png");
        /// <summary>
        /// The slider's fill is the mission bar, not the kit's own `sound-bar-full`.
        ///
        /// That sprite is a row of separate segments with transparent gaps between them --
        /// its centre pixel is fully transparent -- which is right for the kit's stepped
        /// volume control and wrong for a continuous slider: nine-slicing it stretches one
        /// gap across the whole bar. The mission bar is a solid cyan fill of the same family.
        /// </summary>
        public static Sprite SliderFill => Get(Menu + "mission-bar-full-medium.png");
        public static Sprite SliderKnob => Get(Menu + "circle-button-blank-medium.png");

        // --- backgrounds --------------------------------------------------------------

        /// <summary>The lobby's own room: the kit's painted home screen.</summary>
        public static Sprite LobbyBackdrop => Get(Kit + "/Background_Images/large/home-background-large.png");

        // --- icons (instruction 4) ----------------------------------------------------

        /// <summary>
        /// A flat white pictogram from the kit's Picto set, tinted by the caller.
        ///
        /// These are the ones to use small. PHASE12 D38 dropped the previous kit's settings
        /// icon because it was a painted three-quarter render that resolved to a smudge at rail
        /// size -- this set is the opposite, drawn as flat single-colour silhouettes precisely
        /// for small use, so the rail can go back to kit art rather than generated glyphs.
        /// </summary>
        public static Sprite Icon(string name) => Get(Kit + "/Picto_Icons/White/" + name + "-128.png");

        /// <summary>A painted colour icon from the kit's Icons set. Use at 40 units and up.</summary>
        public static Sprite Emblem(string name) => Get(Kit + "/Icons/" + name + "-128.png");

        /// <summary>The wanted meter's star, and the lobby's level chip.</summary>
        public static Sprite Star => Emblem("star");
        /// <summary>Money.</summary>
        public static Sprite Coin => Emblem("coin");
        /// <summary>Health.</summary>
        public static Sprite Heart => Emblem("heart");
        public static Sprite HeartEmpty => Emblem("empty-heart");

        static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        static Sprite Get(string path)
        {
            if (_cache.TryGetValue(path, out var cached) && cached != null) return cached;

            var sprite = Load<Sprite>(path);
            if (sprite == null) Debug.LogWarning("[UI] Missing themed sprite " + path);

            _cache[path] = sprite;
            return sprite;
        }

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        // ---------------------------------------------------------------- buttons
        //
        // The kit ships four separate sprite sets -- normal, highlighted, pressed and disabled
        // -- in five shapes and four colours. Instructions 3 and 10 want all four states used
        // and visibly distinct, which this kit gives directly: no tinting needed, and tinting
        // would in fact hide it, because a tint multiplies painted art rather than replacing it.

        /// <summary>Which painted colour family a button belongs to.</summary>
        public enum ButtonTone
        {
            /// <summary>Blue. Everything ordinary.</summary>
            Normal,
            /// <summary>Gold. The one primary action on a screen -- PLAY, BUY, WATCH.</summary>
            Strong,
            /// <summary>Purple. Secondary and over-the-world controls.</summary>
            Quiet,
        }

        /// <summary>Which of the kit's silhouettes a button uses.</summary>
        public enum ButtonShape
        {
            /// <summary>356x96 -- a normal wide menu button.</summary>
            Wide,
            /// <summary>120x112 -- a square icon button: the rail, the HUD cluster.</summary>
            Square,
            /// <summary>304x52 -- a slim row or chip.</summary>
            Slim,
        }

        static string Colour(ButtonTone tone) => tone switch
        {
            ButtonTone.Strong => "yellow",
            ButtonTone.Quiet => "purple",
            _ => "blue",
        };

        // Shape name, and which of the kit's four size tiers it comes from. Note the tier
        // folder is spelled lower-case under Source_Image_Sprites and Highlighted_Sprite and
        // Capitalised under Pressed_Sprites and Disabled_Sprites; that is the kit's own
        // inconsistency, not a typo here.
        static (string shape, string lower, string upper) Geometry(ButtonShape shape) => shape switch
        {
            ButtonShape.Square => ("square", "large", "Large"),
            ButtonShape.Slim => ("extra-long", "medium", "Medium"),
            _ => ("large", "medium", "Medium"),
        };

        public static Sprite ButtonNormal(ButtonTone tone, ButtonShape shape = ButtonShape.Wide)
        {
            var g = Geometry(shape);
            return Get($"{Buttons}/Source_Image_Sprites/{g.lower}/{g.shape}-{Colour(tone)}-{g.lower}.png");
        }

        public static Sprite ButtonHighlighted(ButtonTone tone, ButtonShape shape = ButtonShape.Wide)
        {
            var g = Geometry(shape);
            return Get($"{Buttons}/Highlighted_Sprite/{g.lower}/{g.shape}-{Colour(tone)}-highlight-{g.lower}.png");
        }

        public static Sprite ButtonPressed(ButtonTone tone, ButtonShape shape = ButtonShape.Wide)
        {
            var g = Geometry(shape);
            return Get($"{Buttons}/Pressed_Sprites/{g.upper}/{g.shape}-{Colour(tone)}-pressed-{g.upper}.png");
        }

        /// <summary>Disabled art is shared across the colour families -- the kit paints one.</summary>
        public static Sprite ButtonDisabled(ButtonShape shape = ButtonShape.Wide)
        {
            var g = Geometry(shape);
            return Get($"{Buttons}/Disabled_Sprites/{g.upper}/{g.shape}-disabled-{g.upper}.png");
        }

        /// <summary>The resting art of an ordinary button, for callers that only need one sprite.</summary>
        public static Sprite Button => ButtonNormal(ButtonTone.Normal);

        // ------------------------------------------------------------ import setup

        /// <summary>
        /// Every themed sprite, and whether it is stretched.
        ///
        /// A nine-sliced entry gets a border derived from its own artwork; the rest -- icons,
        /// backgrounds, knobs -- are drawn whole and must keep a zero border, because a border
        /// on a sprite drawn Simple is harmless but a border on one drawn Sliced when it should
        /// not be turns a round knob into a rounded rectangle.
        /// </summary>
        static IEnumerable<(string path, bool sliced)> AllSprites()
        {
            foreach (ButtonTone tone in new[] { ButtonTone.Normal, ButtonTone.Strong, ButtonTone.Quiet })
                foreach (ButtonShape shape in new[] { ButtonShape.Wide, ButtonShape.Square, ButtonShape.Slim })
                {
                    var g = Geometry(shape);
                    yield return ($"{Buttons}/Source_Image_Sprites/{g.lower}/{g.shape}-{Colour(tone)}-{g.lower}.png", true);
                    yield return ($"{Buttons}/Highlighted_Sprite/{g.lower}/{g.shape}-{Colour(tone)}-highlight-{g.lower}.png", true);
                    yield return ($"{Buttons}/Pressed_Sprites/{g.upper}/{g.shape}-{Colour(tone)}-pressed-{g.upper}.png", true);
                    yield return ($"{Buttons}/Disabled_Sprites/{g.upper}/{g.shape}-disabled-{g.upper}.png", true);
                }

            yield return (Boxes + "pause-container-large.png", true);
            yield return (Boxes + "login-remove-ads-container-large.png", true);
            yield return (Boxes + "shop-container-large.png", true);
            yield return (Boxes + "setting-container-large.png", true);
            yield return (Boxes + "victory-defeat-container-large.png", true);
            yield return (Boxes + "shop-item-container-large.png", true);
            yield return (Boxes + "inventory-menu-container-large.png", true);
            yield return (Boxes + "homepage-icon-container-large.png", true);
            yield return (Menu + "username-password-container-selected-deselected-medium.png", true);
            yield return (Menu + "username-password-container-selected-medium.png", true);
            yield return (Grid + "inventory-item-container copy-medium.png", true);
            yield return (Grid + "inventory-highlight-medium.png", true);

            yield return (Menu + "settings-divider-medium.png", true);
            yield return (Menu + "homepage-gui-line-medium.png", true);
            yield return (Menu + "inventory-underline-medium.png", true);

            yield return (Menu + "mission-bar-empty-medium.png", true);
            yield return (Menu + "mission-bar-full-medium.png", true);
            yield return (Menu + "mission-bar-completed-medium.png", true);
            yield return (Menu + "level-bar-empty-medium.png", true);
            yield return (Menu + "level-bar-full-medium.png", true);
            yield return (Menu + "sound-bar-container-medium.png", true);

            yield return (Menu + "circle-button-blank-medium.png", false);
            yield return (Kit + "/Background_Images/large/home-background-large.png", false);

            foreach (string n in PictoIcons) yield return (Kit + "/Picto_Icons/White/" + n + "-128.png", false);
            foreach (string n in Emblems) yield return (Kit + "/Icons/" + n + "-128.png", false);
        }

        /// <summary>Flat pictograms the interface uses. Named here so PrepareSprites imports
        /// exactly the ones that ship in the build and no more.</summary>
        public static readonly string[] PictoIcons =
        {
            "gun", "bolt", "up-arrow", "door", "pause", "map", "shop", "power", "settings",
            "person", "play", "cross", "back", "next", "check", "heart", "shield", "star",
            "menu", "caution", "megaphone", "stop", "fast-forward", "redo", "skull",
            "location", "lock", "exclamation", "home", "trophy", "info",
        };

        /// <summary>Painted icons, used at 40 canvas units and above.</summary>
        public static readonly string[] Emblems =
        {
            "star", "coin", "heart", "empty-heart", "gun", "shop", "map", "skull", "gem-1",
        };

        /// <summary>
        /// Applies borders and mobile-friendly import settings to every sprite the theme uses.
        ///
        /// Runs before anything is built. Idempotent, and only reimports what is actually wrong,
        /// so a rebuild does not churn 60 textures through the importer.
        /// </summary>
        [MenuItem("Tools/Mini GTA/1e. Prepare UI Kit Sprites", priority = 106)]
        public static void PrepareSprites()
        {
            int changed = 0, correct = 0, missing = 0;

            foreach (var (path, sliced) in AllSprites())
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning("[UI] Missing themed sprite " + path);
                    missing++;
                    continue;
                }

                Vector4 border = sliced ? MeasureBorder(path) : Vector4.zero;

                bool dirty = importer.textureType != TextureImporterType.Sprite
                             || importer.spriteImportMode != SpriteImportMode.Single
                             || importer.spriteBorder != border
                             || !importer.alphaIsTransparency
                             || importer.mipmapEnabled;

                if (!dirty) { correct++; continue; }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = border;
                importer.alphaIsTransparency = true;
                // No mipmaps on UI: the canvas draws these at roughly 1:1, and mips only cost
                // memory and blur the edges the borders exist to keep crisp.
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
                changed++;
            }

            _cache.Clear();
            Debug.Log($"[UI] Space kit sprites prepared: {changed} reimported, {correct} already "
                      + $"correct, {missing} missing. Borders measured from the artwork.");
        }

        /// <summary>
        /// Derives a nine-slice border from a sprite's own opaque silhouette.
        ///
        /// The border has to cover the transparent padding around the art <i>plus</i> the
        /// rounded corner, because those are the pixels that must not be stretched. Both are
        /// read off the PNG on disk rather than through the importer, so this works no matter
        /// what state the texture's Read/Write flag is in.
        ///
        /// Corners are taken as one radius for the whole sprite and applied symmetrically:
        /// every piece in this kit is a rounded rectangle, so a border that comes out lopsided
        /// is a measurement artefact and would render as a visibly off-centre frame.
        /// </summary>
        static Vector4 MeasureBorder(string path)
        {
            if (!File.Exists(path)) return Vector4.zero;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(tex);
                return Vector4.zero;
            }

            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;

            int x0 = w, x1 = -1, y0 = h, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[y * w + x].a <= 16) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }

            Object.DestroyImmediate(tex);
            if (x1 < 0) return Vector4.zero;

            // Corner radius: how far along an edge of the bounding box the silhouette stays
            // transparent before it squares off.
            int radius = 0;
            radius = Mathf.Max(radius, RunIn(px, w, x0, y0, y1, 1, true));
            radius = Mathf.Max(radius, RunIn(px, w, x0, y1, y0, -1, true));
            radius = Mathf.Max(radius, RunIn(px, w, y0, x0, x1, 1, false));
            radius = Mathf.Max(radius, RunIn(px, w, y0, x1, x0, -1, false));

            // +2 so the slice lands just inside the art rather than exactly on its edge, where
            // bilinear filtering would smear the corner into the stretched middle.
            int l = x0 + radius + 2;
            int r = (w - 1 - x1) + radius + 2;
            int b = y0 + radius + 2;
            int t = (h - 1 - y1) + radius + 2;

            // The two borders on an axis must leave a stretchable middle, or Unity draws the
            // corners overlapping and the sprite collapses.
            if (l + r > w - 6) { int k = (w - 6) / 2; l = Mathf.Min(l, k); r = Mathf.Min(r, k); }
            if (b + t > h - 6) { int k = (h - 6) / 2; b = Mathf.Min(b, k); t = Mathf.Min(t, k); }

            return new Vector4(Mathf.Max(0, l), Mathf.Max(0, b), Mathf.Max(0, r), Mathf.Max(0, t));
        }

        /// <summary>Counts transparent pixels along one edge of the bounding box.</summary>
        static int RunIn(Color32[] px, int w, int fixedAxis, int from, int to, int step, bool vertical)
        {
            int n = 0;
            for (int i = from; step > 0 ? i <= to : i >= to; i += step)
            {
                int index = vertical ? i * w + fixedAxis : fixedAxis * w + i;
                if (px[index].a > 16) break;
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ styling

        /// <summary>
        /// Puts the kit's look on a uGUI Button: sliced art for all four interaction states.
        ///
        /// Sprite swap rather than colour tint, because these are painted sprites -- a tint
        /// multiplies them, so a "pressed" tint darkens the paint instead of showing the kit's
        /// pressed art, and a faded "disabled" tint makes the button disappear rather than look
        /// unavailable. The kit draws all four; instruction 10 asks for all four; this uses them.
        /// </summary>
        public static void StyleButton(Button button, Image image,
                                       ButtonTone tone = ButtonTone.Normal,
                                       ButtonShape shape = ButtonShape.Wide)
        {
            if (button == null || image == null) return;

            image.sprite = ButtonNormal(tone, shape);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = Color.white;

            button.transition = Selectable.Transition.SpriteSwap;
            button.targetGraphic = image;

            var state = button.spriteState;
            state.highlightedSprite = ButtonHighlighted(tone, shape);
            state.pressedSprite = ButtonPressed(tone, shape);
            state.selectedSprite = ButtonHighlighted(tone, shape);
            state.disabledSprite = ButtonDisabled(shape);
            button.spriteState = state;

            // White across the board: with SpriteSwap the art carries the state, and any tint
            // here would fight it. Only the disabled colour is left slightly down, so a
            // disabled button reads as unavailable even where the swap is not visible.
            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = Color.white;
            colours.pressedColor = Color.white;
            colours.selectedColor = Color.white;
            colours.disabledColor = new Color(1f, 1f, 1f, 0.75f);
            colours.fadeDuration = 0.06f;
            button.colors = colours;

            Register(image.sprite, Surface.Light);
        }

        /// <summary>
        /// A button that is a dark chip rather than one of the kit's painted faces.
        ///
        /// The lobby's top bar carries three chips side by side -- the name, the level and the
        /// money -- and only the first is tappable. Styled as a painted button it came out a
        /// pale lilac slab next to two dark plates and read as a different kind of object
        /// entirely. This keeps the chip art and takes its states from the kit's own
        /// focused/unfocused pair, so it still answers instruction 10.
        /// </summary>
        public static void StyleChipButton(Button button, Image image)
        {
            if (button == null || image == null) return;

            image.sprite = Chip;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = Color.white;

            button.transition = Selectable.Transition.SpriteSwap;
            button.targetGraphic = image;

            var state = button.spriteState;
            state.highlightedSprite = ChipFocused;
            state.pressedSprite = ChipFocused;
            state.selectedSprite = ChipFocused;
            state.disabledSprite = Chip;
            button.spriteState = state;

            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = Color.white;
            colours.pressedColor = new Color(0.82f, 0.90f, 1f, 1f);
            colours.selectedColor = Color.white;
            colours.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            colours.fadeDuration = 0.06f;
            button.colors = colours;

            Register(Chip, Surface.Dark);
            Register(ChipFocused, Surface.Dark);
        }

        /// <summary>Puts the kit's panel art on a background Image.</summary>
        public static void StylePanel(Image image, Sprite sprite = null)
        {
            if (image == null) return;
            image.sprite = sprite != null ? sprite : Panel;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = Color.white;

            Register(image.sprite, Surface.Dark);
        }

        /// <summary>Applies the theme font, a snapped size and a palette colour to a Text.</summary>
        public static void StyleText(Text text, int size, Color colour, FontStyle style = FontStyle.Normal)
        {
            if (text == null) return;
            text.font = Font;
            text.fontSize = SnapSize(size);
            text.color = colour;
            text.fontStyle = style;
        }

        // ------------------------------------------------------------ contrast sweep

        enum Surface { Dark, Light }

        /// <summary>
        /// Which sprites are dark plates and which are light painted buttons.
        ///
        /// Filled as the builders style things, so it cannot drift out of step with the sprite
        /// table -- a sprite that is never styled is never swept.
        /// </summary>
        static readonly Dictionary<Sprite, Surface> _surfaces = new Dictionary<Sprite, Surface>();

        static void Register(Sprite sprite, Surface surface)
        {
            if (sprite != null) _surfaces[sprite] = surface;
        }

        /// <summary>
        /// Repaints any caption that ended up unreadable on whatever it was drawn on.
        ///
        /// <b>This kit inverted the polarity of the interface.</b> Soft Touch was cream panels
        /// with dark text; this one is deep-indigo panels carrying <i>light</i> painted buttons,
        /// so a screen has both kinds of surface on it at once and the correct ink is opposite
        /// on each. The Phase 11 sweep only darkened light text and would leave every panel
        /// caption black on indigo.
        ///
        /// So this compares luma both ways. A caption whose brightness is too close to the
        /// surface behind it gets moved to the far side: a neutral one snaps to the surface's
        /// ink, and a deliberately coloured one -- money gold, a damage warning -- keeps its hue
        /// and only has its value pushed, so the palette survives the sweep.
        ///
        /// Captions over the world (the ammo counter, the speedometer, the banner) have no kit
        /// surface behind them and are left alone.
        /// </summary>
        public static int EnforceContrast(GameObject canvasRoot)
        {
            if (canvasRoot == null) return 0;
            int changed = 0;

            foreach (var text in canvasRoot.GetComponentsInChildren<Text>(true))
            {
                if (!OnKitSurface(text.transform, out Surface surface)) continue;

                Color c = text.color;
                float ink = Luma(c);
                float plate = surface == Surface.Dark ? Luma(PanelFill) : Luma(Rgb(0x9A, 0xBB, 0xF4));

                // Rec. 601 luma is what the eye reads as brightness. A gap under 0.30 is the
                // point at which a caption stops separating from its background at HUD sizes.
                if (Mathf.Abs(ink - plate) >= 0.30f) continue;

                Color.RGBToHSV(c, out float h, out float s, out float v);

                if (s < 0.18f)
                {
                    // Neutral: snap to the surface's ink, keeping the alpha the builders use to
                    // distinguish a heading from a hint.
                    Color target = surface == Surface.Dark
                        ? (c.a < 0.8f ? TextOnPanelDim : TextOnPanel)
                        : (c.a < 0.8f ? TextOnButtonDim : TextOnButton);
                    text.color = new Color(target.r, target.g, target.b, Mathf.Max(c.a, 0.75f));
                }
                else
                {
                    // Coloured on purpose: keep the hue, move the value to the readable side.
                    float lifted = surface == Surface.Dark ? Mathf.Max(v, 0.92f) : Mathf.Min(v, 0.42f);
                    Color moved = Color.HSVToRGB(h, s, lifted);
                    text.color = new Color(moved.r, moved.g, moved.b, c.a);
                }
                changed++;
            }

            return changed;
        }

        static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        // -------------------------------------------------------------- frame audit

        /// <summary>
        /// Reports any caption or control that has been laid out underneath the painted frame
        /// of the panel it sits on.
        ///
        /// <b>This is the characteristic failure of this kit and it is worth a build-time
        /// check.</b> The Soft Touch panels this interface was laid out against had frames of
        /// 26 to 64 px; these have frames of 87 to 166. Every card in the project was sized
        /// against the old numbers, so on the first pass the settings title was drawn across
        /// the top moulding, the shop's stock rows ran out past both sides of the sheet, and
        /// the map's streets were painted over the panel and onto the screen behind it. All
        /// three passed every assertion and were found by looking at pictures.
        ///
        /// A picture will still find the next one -- but a picture has to be taken, of the
        /// right screen, at the right size. This does not: it measures each nine-sliced kit
        /// panel's border, converts it into canvas units, and checks every Text and Selectable
        /// under it against the rectangle that leaves. Returns the number of intrusions and
        /// logs each one with the overlap in units, so the fix is a number rather than a hunt.
        /// </summary>
        public static int AuditFrames(GameObject canvasRoot, float tolerance = 8f)
        {
            if (canvasRoot == null) return 0;

            var canvas = canvasRoot.GetComponentInParent<Canvas>();
            float reference = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            int problems = 0;

            foreach (var image in canvasRoot.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null || image.type != Image.Type.Sliced) continue;
                if (!AssetDatabase.GetAssetPath(image.sprite).StartsWith(Kit)) continue;

                // A button is not a panel. Its border is a painted bevel a couple of units
                // deep, and its caption is deliberately stretched over the whole face so that
                // centred text stays centred at any size -- flagging that would bury the real
                // findings under one warning per button on the canvas.
                //
                // Tested by "does this thing take a press", not by component type: the HUD's
                // thumb controls are HudButton, which handles pointers itself and is not a
                // uGUI Selectable at all.
                if (IsControl(image.gameObject)) continue;

                Vector4 border = image.sprite.border;
                if (border == Vector4.zero) continue;

                // How a sliced Image scales its border: the sprite's own pixels-per-unit
                // against the canvas's reference, times the component's multiplier.
                float scale = image.pixelsPerUnitMultiplier
                              / Mathf.Max(0.0001f, image.sprite.pixelsPerUnit / reference);

                var rect = image.rectTransform.rect;
                var safe = Rect.MinMaxRect(rect.xMin + border.x * scale,
                                           rect.yMin + border.y * scale,
                                           rect.xMax - border.z * scale,
                                           rect.yMax - border.w * scale);

                // A frame wider than the panel is its own bug and would flag everything.
                if (safe.width <= 0f || safe.height <= 0f)
                {
                    Debug.LogWarning($"[UI] '{image.name}' is smaller than {image.sprite.name}'s "
                                     + $"own frame ({border.x}/{border.y}/{border.z}/{border.w} px). "
                                     + "Nothing can be laid out inside it.");
                    problems++;
                    continue;
                }

                foreach (var child in image.GetComponentsInChildren<RectTransform>(true))
                {
                    if (child == image.rectTransform) continue;
                    if (child.GetComponent<Text>() == null && child.GetComponent<Selectable>() == null)
                        continue;

                    // An invisible full-bleed tap target -- the one over the corner minimap --
                    // is supposed to cover the frame. It has nothing to draw on it.
                    var graphic = child.GetComponent<Graphic>();
                    if (graphic != null && graphic.color.a < 0.05f
                        && child.GetComponent<Text>() == null) continue;
                    // Only judge a child against the nearest panel above it, or a caption deep
                    // inside a button would be measured against the screen behind it as well.
                    if (Nearest(child, image.rectTransform) != image.rectTransform) continue;

                    var box = Box(child, image.rectTransform);
                    float over = Mathf.Max(Mathf.Max(safe.xMin - box.xMin, box.xMax - safe.xMax),
                                           Mathf.Max(safe.yMin - box.yMin, box.yMax - safe.yMax));
                    if (over <= tolerance) continue;

                    Debug.LogWarning($"[UI] '{child.name}' overhangs {image.sprite.name}'s frame "
                                     + $"on '{image.name}' by {over:F0} units.");
                    problems++;
                }
            }

            return problems;
        }

        /// <summary>
        /// Whether a GameObject is something the player presses, whatever it is built from.
        ///
        /// <see cref="Selectable"/> covers uGUI's buttons and sliders; the pointer interfaces
        /// cover this project's own <c>HudButton</c>, which implements them directly.
        /// </summary>
        static bool IsControl(GameObject go)
        {
            if (go.GetComponent<Selectable>() != null) return true;

            foreach (var behaviour in go.GetComponents<MonoBehaviour>())
                if (behaviour is UnityEngine.EventSystems.IPointerDownHandler
                    || behaviour is UnityEngine.EventSystems.IPointerClickHandler)
                    return true;

            return false;
        }

        /// <summary>The closest ancestor of <paramref name="child"/> that paints a kit panel.</summary>
        static RectTransform Nearest(RectTransform child, RectTransform stopAt)
        {
            for (var p = child.parent as RectTransform; p != null; p = p.parent as RectTransform)
            {
                var img = p.GetComponent<Image>();
                if (img != null && img.sprite != null && img.type == Image.Type.Sliced
                    && img.sprite.border != Vector4.zero
                    && AssetDatabase.GetAssetPath(img.sprite).StartsWith(Kit))
                    return p;
                if (p == stopAt) return p;
            }
            return null;
        }

        /// <summary>A child's rect expressed in an ancestor's local space.</summary>
        static Rect Box(RectTransform child, RectTransform space)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);

            float xMin = float.MaxValue, yMin = float.MaxValue;
            float xMax = float.MinValue, yMax = float.MinValue;

            foreach (var corner in corners)
            {
                Vector3 local = space.InverseTransformPoint(corner);
                xMin = Mathf.Min(xMin, local.x); xMax = Mathf.Max(xMax, local.x);
                yMin = Mathf.Min(yMin, local.y); yMax = Mathf.Max(yMax, local.y);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>
        /// Finds the surface a caption is drawn on, walking up to the nearest ancestor that
        /// actually paints something, and reports which kind it is.
        /// </summary>
        static bool OnKitSurface(Transform t, out Surface surface)
        {
            surface = Surface.Dark;

            for (var p = t; p != null; p = p.parent)
            {
                var img = p.GetComponent<Image>();
                if (img == null || img.sprite == null) continue;
                if (img.color.a < 0.05f) continue;   // an invisible tap target is not a surface

                if (_surfaces.TryGetValue(img.sprite, out surface)) return true;

                // A kit sprite nothing registered: treat it as dark, which every unregistered
                // piece in this kit is. Anything outside the kit stops the walk.
                if (AssetDatabase.GetAssetPath(img.sprite).StartsWith(Kit))
                {
                    surface = Surface.Dark;
                    return true;
                }
                return false;
            }
            return false;
        }
    }
}
