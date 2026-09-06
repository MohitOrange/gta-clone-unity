using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Owns the application icon, and refuses to let the build wear someone else's badge.
    ///
    /// <b>Section 8's actual finding.</b> The configured application icon was
    /// <c>helicopter-attack-3d-game-template-icon.png</c> -- the HelicopterAttack asset pack's
    /// own store icon -- and all eighteen Android icon slots were empty. The APK built earlier
    /// in this phase therefore carries that pack's artwork on the launcher. That is the same
    /// import-clobber family as BUG-025 (scene list and package name) and BUG-026 (keystore):
    /// an asset pack wrote itself into project settings and nothing warned.
    ///
    /// <b>No icon is generated here.</b> CLAUDE.md §6 reserves art for the project owner, and
    /// an application icon is branding, which is precisely the kind of thing that should not be
    /// invented by a tool. What this does is remove the wrong one, state exactly what is needed
    /// to replace it, and make assigning the replacement a single call.
    /// </summary>
    public static class IconBuilder
    {
        /// <summary>Where a supplied icon is expected to live.</summary>
        public const string ExpectedIconPath = "Assets/Game/UI/Branding/app_icon.png";

        /// <summary>Sizes Android asks for, largest first.</summary>
        static readonly int[] AndroidSizes = { 432, 324, 216, 162, 108, 81 };

        [MenuItem("Mini GTA/Icons/Audit", priority = 70)]
        public static void Audit()
        {
            var android = NamedBuildTarget.Android;

            var generic = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            Debug.Log("[ICON] default icons assigned = " + CountNonNull(generic));
            foreach (var t in generic)
                if (t != null)
                    Debug.Log("[ICON]   default -> " + AssetDatabase.GetAssetPath(t));

            // Android's icon slots are not IconKind -- that enum only knows Application and
            // Settings. The launcher's adaptive, round and legacy sets live behind the
            // platform-icon API instead.
            foreach (var kind in AndroidKinds())
            {
                var icons = PlayerSettings.GetPlatformIcons(android, kind);
                int assigned = 0;
                foreach (var icon in icons)
                    if (icon.GetTextures().Length > 0 && icon.GetTexture(0) != null) assigned++;

                Debug.Log("[ICON] Android " + kind + ": " + icons.Length
                          + " slots, " + assigned + " assigned");
            }

            bool supplied = AssetDatabase.LoadAssetAtPath<Texture2D>(ExpectedIconPath) != null;
            Debug.Log("[ICON] game icon at " + ExpectedIconPath + " = "
                      + (supplied ? "present" : "MISSING"));

            if (!supplied)
            {
                Debug.LogWarning("[ICON] No Mini GTA application icon exists. See the asset "
                                 + "specification in PHASE14.md. Until one is supplied the "
                                 + "build has no icon of its own.");
            }
        }

        /// <summary>
        /// Removes any icon that is not the game's own.
        ///
        /// Leaving the Unity default is not good, but it is honest. Shipping an asset pack's
        /// store icon as the application icon is worse: it misrepresents the app on the
        /// launcher and in any store listing.
        /// </summary>
        [MenuItem("Mini GTA/Icons/Clear borrowed icons", priority = 71)]
        public static void ClearBorrowed()
        {
            var generic = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            int cleared = 0;

            for (int i = 0; i < generic.Length; i++)
            {
                if (generic[i] == null) continue;

                string path = AssetDatabase.GetAssetPath(generic[i]);
                if (path == ExpectedIconPath) continue;   // the game's own, keep it

                Debug.LogWarning("[ICON] clearing borrowed icon: " + path);
                generic[i] = null;
                cleared++;
            }

            if (cleared > 0)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, generic, IconKind.Any);
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[ICON] cleared " + cleared + " borrowed icon(s)");
        }

        /// <summary>
        /// Assigns the supplied icon to every Android slot and the default slot.
        /// Does nothing, loudly, if no icon has been supplied.
        /// </summary>
        [MenuItem("Mini GTA/Icons/Apply game icon", priority = 72)]
        public static void Apply()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(ExpectedIconPath);
            if (icon == null)
            {
                Debug.LogError("[ICON] nothing to apply: " + ExpectedIconPath + " does not "
                               + "exist. Supply an icon there first; the specification is in "
                               + "PHASE14.md.");
                return;
            }

            var android = NamedBuildTarget.Android;

            foreach (var kind in AndroidKinds())
            {
                var slots = PlayerSettings.GetPlatformIcons(android, kind);
                foreach (var slot in slots) slot.SetTexture(icon, 0);
                PlayerSettings.SetPlatformIcons(android, kind, slots);
                Debug.Log("[ICON] Android " + kind + ": filled " + slots.Length + " slots");
            }

            var generic = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            for (int i = 0; i < generic.Length; i++) generic[i] = icon;
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, generic, IconKind.Any);

            AssetDatabase.SaveAssets();
            Debug.Log("[ICON] applied " + ExpectedIconPath);
        }

        /// <summary>
        /// The Android launcher icon sets. Adaptive is what modern launchers actually use;
        /// Round and Legacy still matter on older devices, and minSdk here is 26.
        /// </summary>
        static PlatformIconKind[] AndroidKinds() => new[]
        {
            AndroidPlatformIconKind.Adaptive,
            AndroidPlatformIconKind.Round,
            AndroidPlatformIconKind.Legacy,
        };

        static int CountNonNull(IReadOnlyList<Texture2D> icons)
        {
            int n = 0;
            for (int i = 0; i < icons.Count; i++) if (icons[i] != null) n++;
            return n;
        }

        /// <summary>Prints the specification for the icon the project still needs.</summary>
        [MenuItem("Mini GTA/Icons/Print required asset spec", priority = 73)]
        public static void PrintSpec()
        {
            Debug.Log("[ICON] Mini GTA application icon -- required asset\n"
                      + "  path        : " + ExpectedIconPath + "\n"
                      + "  format      : PNG, 32-bit, no alpha needed for the legacy icon\n"
                      + "  source size : 1024 x 1024 (Unity downsamples to "
                      + string.Join(", ", AndroidSizes) + ")\n"
                      + "  safe area   : keep artwork inside the centre 66% -- Android's\n"
                      + "                adaptive mask crops to a circle on many launchers\n"
                      + "  import      : Texture Type 'Sprite (2D and UI)' or 'Default',\n"
                      + "                Alpha Is Transparency on, mipmaps off\n"
                      + "  must not    : reuse artwork from an imported asset pack");
        }

        // ------------------------------------------------------------------- splash

        /// <summary>
        /// Configures the launch splash.
        ///
        /// <b>The Unity logo stays, and that is a licence term rather than a preference.</b>
        /// This project is on a Personal licence -- InternalEditorUtility.HasPro() is false and
        /// HasFreeLicense() is true -- and Unity's terms require the Unity splash on Personal.
        ///
        /// The trap worth recording: PlayerSettings.SplashScreen.show and .showUnityLogo are
        /// both *writable* through the scripting API on this licence. Writing false and reading
        /// it back returns false, so it looks like it worked. That is not permission; it is an
        /// API that does not enforce the licence. Removing the splash here would be working
        /// around a licensing restriction, so it is not done.
        ///
        /// What is done instead is everything the licence does allow: the game's own logo is
        /// added to the sequence and given the main position, with the Unity logo underneath
        /// it, on the game's own background colour. The player sees our branding immediately
        /// and prominently, and Unity's requirement is met honestly.
        /// </summary>
        [MenuItem("Tools/Mini GTA/1f. Configure Launch Splash", priority = 107)]
        public static void ConfigureSplash()
        {
            bool pro = UnityEditorInternal.InternalEditorUtility.HasPro();

            // Assert the licence-correct state rather than inheriting whatever was set.
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = true;

            var sprite = LoadBrandingSprite();
            if (sprite == null)
            {
                Debug.LogError("[SPLASH] No branding sprite at " + ExpectedIconPath
                               + "; the splash will show Unity's logo alone.");
                return;
            }

            // Ours above, Unity's below. UnityLogoBelow is what puts the game's logo in the
            // main slot -- the alternative draws them all in one row at equal weight.
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.UnityLogoBelow;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Dolly;
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;

            // The interface's deepest panel tone, so the splash reads as this game rather than
            // as a default Unity screen with a picture on it.
            PlayerSettings.SplashScreen.backgroundColor = new Color(0.086f, 0.078f, 0.220f, 1f);

            // 2 seconds is Unity's floor for a logo entry; anything less is clamped up.
            PlayerSettings.SplashScreen.logos = new[]
            {
                PlayerSettings.SplashScreenLogo.Create(2.5f, sprite),
            };

            AssetDatabase.SaveAssets();

            Debug.Log("[SPLASH] licence = " + (pro ? "Pro/Plus" : "PERSONAL (free)")
                      + "\n  Unity logo: " + (pro ? "could be disabled" : "REQUIRED, kept")
                      + "\n  game logo : " + ExpectedIconPath + " shown for 2.5 s, above Unity's"
                      + "\n  This changes the launch splash only. The launcher icon is a"
                      + " separate setting, applied by 'Apply Game Icon'.");
        }

        /// <summary>
        /// The branding image as a Sprite, importing it as one if it is not already.
        ///
        /// Re-imports rather than duplicating the file: a second copy is a second thing to keep
        /// in step. A Sprite-imported texture is still returned by LoadAssetAtPath&lt;Texture2D&gt;,
        /// so the icon pipeline that reads this same file is unaffected -- verified, not assumed.
        /// </summary>
        static Sprite LoadBrandingSprite()
        {
            var importer = AssetImporter.GetAtPath(ExpectedIconPath) as TextureImporter;
            if (importer == null) return null;

            if (importer.textureType != TextureImporterType.Sprite
                || importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(ExpectedIconPath);
        }
    }
}
