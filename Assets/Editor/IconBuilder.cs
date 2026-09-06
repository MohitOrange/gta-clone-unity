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
    }
}
