using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Android player settings and mobile render-pipeline budgets for the game.
    ///
    /// Separated from the world builders because these are project-wide settings you set once
    /// and rarely revisit, and because switching the active build target is slow enough that it
    /// deserves to be an explicit, deliberate menu action rather than a side effect.
    /// </summary>
    public static class MobileSetup
    {
        const string ScenePath = "Assets/Scenes/City.unity";

        [MenuItem("Tools/Mini GTA/7. Configure Android Player Settings", priority = 120)]
        public static void ConfigureAndroid()
        {
            var android = NamedBuildTarget.Android;

            PlayerSettings.companyName = "MiniGTA";
            PlayerSettings.productName = "Mini GTA";
            // Must be set per-platform; the bare property only touches the active target.
            PlayerSettings.SetApplicationIdentifier(android, "com.minigta.city");
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;

            // Android 8.0 -- the floor this Unity version accepts.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // IL2CPP + ARM64 is mandatory for Play Store submission.
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            // ARM64 only. Adding ARMv7 makes IL2CPP compile every script to native code twice,
            // which doubles both the build's disk footprint and its time for the sake of phones
            // predating 2015 -- which would not run this game anyway. Google Play requires
            // 64-bit and treats 32-bit as optional. Re-add ARMv7 here if you ever need it.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetIl2CppCompilerConfiguration(android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);

            // Landscape only: the control layout assumes two thumbs on opposite edges.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Vulkan first, GLES3 as the fallback for older drivers.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            // Vulkan pre-transform OFF. This is the Phase 8 main-menu bug, and it is not a
            // preference -- it is a correctness fix.
            //
            // With it on, Unity renders into the display's *native* swapchain (portrait
            // 1080x2318 on the test device) and rotates during rendering. The 3D scene gets the
            // pre-transform matrix via UNITY_PRETRANSFORM_TO_DISPLAY_ORIENTATION, but on the
            // test device's Adreno driver the ScreenSpaceOverlay canvas does not, so the whole
            // UI is drawn with the swapchain's aspect instead of the screen's: squashed by
            // 1080/2318 = 0.466 horizontally and stretched by 2318/1080 = 2.146 vertically.
            // Measured on device: a 448.6 x 79.0 px button rendered at 205 x 168 px.
            //
            // No editor can reproduce this -- the editor renders D3D11 on a desktop GPU and
            // never takes the Vulkan pre-rotation path at all.
            PlayerSettings.vulkanEnablePreTransform = false;

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.MTRendering = true;
            PlayerSettings.Android.optimizedFramePacing = true;
            // Immersive fullscreen: hide the nav bar so it cannot swallow thumb input at the
            // screen edge, which is exactly where the joystick and action cluster live.
            PlayerSettings.Android.requestedVisibleInsets = AndroidWindowInsetsType.None;
            PlayerSettings.Android.renderOutsideSafeArea = false;
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.SetApiCompatibilityLevel(android, ApiCompatibilityLevel.NET_Standard);

            // 30 fps is the honest target for a mid-range phone with terrain + shadows.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;

            EnsureSceneInBuild();

            AssetDatabase.SaveAssets();
            Debug.Log("[Mobile] Android player settings configured.\n"
                      + "  package   = " + PlayerSettings.GetApplicationIdentifier(android) + "\n"
                      + "  minSdk    = " + PlayerSettings.Android.minSdkVersion + "\n"
                      + "  backend   = IL2CPP, ARM64 + ARMv7\n"
                      + "  graphics  = Vulkan, OpenGLES3\n"
                      + "  landscape = left + right only");
        }

        [MenuItem("Tools/Mini GTA/8. Apply Mobile Render Budgets", priority = 121)]
        public static void ApplyMobileRenderBudgets()
        {
            int touched = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null) continue;

                bool isMobileTier = path.Contains("Mobile");

                var so = new SerializedObject(asset);

                Set(so, "m_RequireDepthTexture", true);      // the ocean needs scene depth
                Set(so, "m_RequireOpaqueTexture", false);
                Set(so, "m_SupportsHDR", !isMobileTier);     // HDR costs bandwidth phones lack

                if (isMobileTier)
                {
                    SetFloat(so, "m_RenderScale", 0.85f);    // cheapest meaningful win on mobile
                    SetInt(so, "m_MainLightShadowmapResolution", 1024);
                    SetFloat(so, "m_ShadowDistance", 60f);
                    SetInt(so, "m_ShadowCascadeCount", 1);
                    SetInt(so, "m_AdditionalLightsRenderingMode", 0);  // disabled
                    SetInt(so, "m_MSAA", 1);
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                touched++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Mobile] Applied render budgets to " + touched + " URP assets. "
                      + "Mobile tier: 0.85 render scale, 1 cascade, 60m shadow distance, no MSAA, no HDR.");
        }

        [MenuItem("Tools/Mini GTA/9. Switch Platform to Android", priority = 122)]
        public static void SwitchToAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                Debug.Log("[Mobile] Already on Android.");
                return;
            }

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[Mobile] Android build support is not installed for this Unity version.");
                return;
            }

            Debug.Log("[Mobile] Switching to Android -- this reimports every texture and can take "
                      + "several minutes on first switch.");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        static void EnsureSceneInBuild()
        {
            var scenes = EditorBuildSettings.scenes.ToList();

            // The generated city scene must be index 0 or the build boots into an empty scene.
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));

            // Drop the template scene; it is not part of the game.
            scenes.RemoveAll(s => s.path == "Assets/Scenes/SampleScene.unity");

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void Set(SerializedObject so, string prop, bool value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.boolValue = value;
        }

        static void SetInt(SerializedObject so, string prop, int value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.intValue = value;
        }

        static void SetFloat(SerializedObject so, string prop, float value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.floatValue = value;
        }
    }
}
