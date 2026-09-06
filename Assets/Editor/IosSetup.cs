using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// iOS player settings, configured but not built.
    ///
    /// A Unity iOS build produces an Xcode project, and turning that into an .ipa needs Xcode,
    /// a Mac and a signing identity -- none of which exist on this machine. Everything that
    /// can be decided from Windows is set here so that opening the project on a Mac later is
    /// a build step rather than a configuration session.
    /// </summary>
    public static class IosSetup
    {
        [MenuItem("Tools/Mini GTA/12. Configure iOS Player Settings", priority = 132)]
        public static void ConfigureIos()
        {
            var ios = NamedBuildTarget.iOS;

            PlayerSettings.SetApplicationIdentifier(ios, "com.minigta.city");

            // iOS 15: the floor that still gets Metal 3 and covers everything Apple supports.
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;

            // IL2CPP is the only backend on iOS; ARM64 is the only architecture Apple accepts.
            PlayerSettings.SetScriptingBackend(ios, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(ios, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetManagedStrippingLevel(ios, ManagedStrippingLevel.Low);
            PlayerSettings.SetApiCompatibilityLevel(ios, ApiCompatibilityLevel.NET_Standard);

            // Metal only. There is no GLES fallback on any iOS version this targets.
            //
            // Only settable with the iOS module installed. Without it Unity rejects the list as
            // empty and substitutes its default -- which is Metal anyway -- while logging a
            // warning that reads like a failure when nothing has gone wrong.
            bool moduleInstalled =
                BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS);

            if (moduleInstalled)
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
            }

            // Same landscape-only rule as Android: the control layout puts a thumb on each edge.
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.statusBarHidden = true;

            PlayerSettings.iOS.requiresFullScreen = true;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.hideHomeButton = true;

            // Every string below is a privacy prompt. An App Store build is rejected if a
            // permission is requested without one, and rejected again if one is declared for a
            // permission the app never uses -- this game uses none of them, so they stay empty.
            PlayerSettings.iOS.locationUsageDescription = "";
            PlayerSettings.iOS.cameraUsageDescription = "";
            PlayerSettings.iOS.microphoneUsageDescription = "";

            AssetDatabase.SaveAssets();

            Debug.Log("[iOS] Player settings configured (no build -- that needs a Mac).\n"
                      + "  bundle id  = " + PlayerSettings.GetApplicationIdentifier(ios) + "\n"
                      + "  min iOS    = " + PlayerSettings.iOS.targetOSVersionString + "\n"
                      + "  devices    = " + PlayerSettings.iOS.targetDevice + "\n"
                      + "  backend    = IL2CPP, Metal only\n"
                      + "  landscape  = left + right only, full screen\n"
                      + "  iOS module = " + (moduleInstalled ? "installed"
                          : "NOT installed -- the settings above are saved, but no "
                            + "iOS build can be produced until iOS Build Support is "
                            + "added on a Mac") + "\n"
                      + "\n  To build on a Mac: switch platform to iOS, Build to an Xcode "
                      + "project, then Archive from Xcode with a signing team selected.\n"
                      + "  Before shipping, add an ATT consent flow -- iOS requires it before "
                      + "any ad SDK requests a tracking identifier.");
        }
    }
}
