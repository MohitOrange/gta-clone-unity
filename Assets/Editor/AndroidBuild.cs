using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// One place that knows how to produce a Mini GTA APK.
    ///
    /// This exists because the settings it asserts have been silently wrong before. The
    /// HelicopterAttack asset pack overwrote the build scene list and the application
    /// identifier on import (BUG-025), and the project sat that way through a whole phase --
    /// any APK built in that window would have been the asset pack's demo scenes under the
    /// pack's own package name. Nothing warned; the build would simply have succeeded and
    /// produced the wrong game.
    ///
    /// So the build re-asserts identity every time rather than trusting whatever is in
    /// ProjectSettings, and refuses to run at all if the game scene is missing. A build that
    /// cannot be wrong is worth more than a build that is convenient.
    ///
    /// Usable from the menu, or from the command line for CI:
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt; -executeMethod MiniGTA.EditorTools.AndroidBuild.BuildDevelopment
    /// </code>
    /// Batch mode needs the Editor closed, since it takes the project lock.
    /// </summary>
    public static class AndroidBuild
    {
        public const string GameScene = "Assets/Scenes/City.unity";
        public const string PackageName = "com.minigta.city";
        public const string ProductName = "Mini GTA";
        const string OutputDir = "Builds/Android";

        [MenuItem("Mini GTA/Build/Android APK (development)", priority = 10)]
        public static void BuildDevelopment() => Build(development: true);

        [MenuItem("Mini GTA/Build/Android APK (release)", priority = 11)]
        public static void BuildRelease() => Build(development: false);

        static void Build(bool development)
        {
            if (!File.Exists(GameScene))
                throw new FileNotFoundException("The game scene is missing: " + GameScene);

            ApplyIdentity();

            Directory.CreateDirectory(OutputDir);

            // Both configurations are labelled. A release APK used to come out simply as
            // "MiniGTA-0.3.0.apk", which says nothing about how it is signed -- and the one
            // thing that matters about an APK on disk is whether it carries the release key or
            // the debug one. Naming it makes that readable without running apksigner.
            string suffix = development ? "-dev" : "-release";
            string apk = Path.Combine(OutputDir,
                "MiniGTA-" + PlayerSettings.bundleVersion + suffix + ".apk");

            var options = new BuildPlayerOptions
            {
                // The scene list is passed explicitly rather than read from
                // EditorBuildSettings, so an asset pack rewriting that list cannot quietly
                // change what gets built. This is the whole of BUG-025.
                scenes = new[] { GameScene },
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            // Confirmed on every build, not only when something had to be corrected.
            //
            // BUG-025 was invisible precisely because a wrong configuration builds silently
            // and successfully. A pass-through that says nothing when all is well is the same
            // shape of trap: it proves nothing, and the one time it matters is the time
            // nobody looked. So the identity being built is stated out loud, every time.
            Debug.Log("[BUILD] ==== Mini GTA Android build ====");
            Debug.Log("[BUILD] package     = " + PlayerSettings.GetApplicationIdentifier(
                                                     NamedBuildTarget.Android));
            Debug.Log("[BUILD] product     = " + PlayerSettings.productName);
            Debug.Log("[BUILD] version     = " + PlayerSettings.bundleVersion
                      + " (versionCode " + PlayerSettings.Android.bundleVersionCode + ")");
            Debug.Log("[BUILD] backend     = "
                      + PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)
                      + "   arch = " + PlayerSettings.Android.targetArchitectures);
            Debug.Log("[BUILD] sdk         = min " + PlayerSettings.Android.minSdkVersion
                      + ", target " + PlayerSettings.Android.targetSdkVersion);
            Debug.Log("[BUILD] scenes      = " + options.scenes.Length);
            for (int i = 0; i < options.scenes.Length; i++)
                Debug.Log("[BUILD]   [" + i + "] " + options.scenes[i]);
            Debug.Log("[BUILD] configuration = " + (development ? "development" : "release"));
            Debug.Log("[BUILD] output      = " + apk);

            if (System.Array.IndexOf(options.scenes, GameScene) < 0)
                throw new BuildFailedException(
                    "The game scene " + GameScene + " is not in the build. Refusing to build "
                    + "something that is not Mini GTA. See BUG-025.");

            bool restoreCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            try
            {
                ApplySigning(development);

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                string line = "[BUILD] result=" + summary.result
                              + " time=" + summary.totalTime
                              + " sizeMB=" + (summary.totalSize / (1024f * 1024f)).ToString("F1")
                              + " errors=" + summary.totalErrors
                              + " out=" + summary.outputPath;

                if (summary.result == BuildResult.Succeeded) Debug.Log(line);
                else Debug.LogError(line);
            }
            finally
            {
                // Never leave a signing choice behind. A development build that quietly
                // turned custom signing off and left it off is how a release later goes out
                // debug-signed without anyone noticing.
                PlayerSettings.Android.useCustomKeystore = restoreCustomKeystore;
            }
        }

        /// <summary>
        /// Chooses how the APK is signed.
        ///
        /// <b>BUG-026.</b> The project is configured to sign with a custom keystore at
        /// <c>D:/JoySmashProjects/keystore/bundle.keystore</c> -- a path belonging to an
        /// entirely different project, on a drive with under 2 GB free, and the file is not
        /// there at all. The passwords are, correctly, not stored in the repository, so an
        /// interactive Editor session that had them typed in could build while a fresh batch
        /// process could not: the build failed with "Unable to sign the Android application"
        /// after five seconds.
        ///
        /// A development build does not need the release key. It is signed with the Android
        /// debug key, which installs on a device perfectly well and cannot be shipped -- which
        /// is exactly the right property for a benchmark build. Release keeps the custom
        /// keystore and fails loudly if it is not usable, because a release that silently
        /// falls back to a debug key is worse than one that does not build.
        /// </summary>
        static void ApplySigning(bool development)
        {
            if (development)
            {
                Debug.Log("[BUILD] signing     = Android debug key (development build)");
                PlayerSettings.Android.useCustomKeystore = false;
                return;
            }

            string keystore = PlayerSettings.Android.keystoreName;
            Debug.Log("[BUILD] signing     = custom keystore '" + keystore + "'");

            if (string.IsNullOrEmpty(keystore) || !File.Exists(keystore))
                throw new BuildFailedException(
                    "Release signing is configured to use '" + keystore + "', which does not "
                    + "exist. Set a real keystore and its passwords in Project Settings > "
                    + "Player > Publishing Settings, or build the development target, which "
                    + "is debug-signed. See BUG-026.");

            if (string.IsNullOrEmpty(PlayerSettings.Android.keystorePass)
                || string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
                throw new BuildFailedException(
                    "Release signing has no keystore password in this session. Passwords are "
                    + "deliberately not stored in the repository, so a release build has to be "
                    + "run from an Editor where they have been entered. See BUG-026.");
        }

        /// <summary>
        /// Re-asserts the things an asset pack import is known to overwrite.
        /// </summary>
        static void ApplyIdentity()
        {
            var android = NamedBuildTarget.Android;

            if (PlayerSettings.GetApplicationIdentifier(android) != PackageName)
            {
                Debug.LogWarning("[BUILD] application identifier was '"
                                 + PlayerSettings.GetApplicationIdentifier(android)
                                 + "', restoring '" + PackageName + "'. See BUG-025.");
                PlayerSettings.SetApplicationIdentifier(android, PackageName);
            }

            if (PlayerSettings.productName != ProductName)
            {
                Debug.LogWarning("[BUILD] product name was '" + PlayerSettings.productName
                                 + "', restoring '" + ProductName + "'.");
                PlayerSettings.productName = ProductName;
            }

            // Mobile-first, per CLAUDE.md 5.
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Pinned, not Auto.
            //
            // Every write this build makes lands on C: -- project, Library, Temp, Builds,
            // %TEMP%, ~/.gradle, the Editor log. D: holds only the toolchain (SDK, NDK,
            // OpenJDK, the Gradle distribution) and is read from, never written to.
            //
            // The single exception would be Unity deciding it needs an SDK platform that is
            // not installed and fetching one into the bundled SDK, which lives on D: -- where
            // there is under 2 GB free. Auto resolves to the highest *installed* platform so
            // it should never do that, but the directory is writable and "should never" is
            // not a guarantee worth a corrupted mid-build install. android-36 is installed
            // (alongside 34 and 37) with build-tools 36.0.0 and the NDK, so pinning removes
            // the possibility rather than relying on Auto behaving.
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GameScene, true) };
        }

        /// <summary>
        /// Bumps the version so a device never has to guess which build it is running.
        /// </summary>
        [MenuItem("Mini GTA/Build/Bump version code", priority = 20)]
        public static void BumpVersionCode()
        {
            PlayerSettings.Android.bundleVersionCode++;
            Debug.Log("[BUILD] versionCode is now " + PlayerSettings.Android.bundleVersionCode);
        }
    }
}
