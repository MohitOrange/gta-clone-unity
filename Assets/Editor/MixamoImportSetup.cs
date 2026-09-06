using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click conversion of raw Mixamo FBX downloads into a retarget-ready Humanoid setup.
///
/// Mixamo exports every character on an identical skeleton, so importing each model as
/// Humanoid lets any humanoid clip play on any of them with no per-character work. Unity's
/// default is Generic + NoAvatar, which silently breaks that -- this fixes it.
///
/// <b>Scope after Phase 9b.</b> The animation library moved to the Kevin Iglesias and EEJANAI
/// packs, which ship correctly configured and need nothing from this script. What is still
/// Mixamo, and still needs this, is:
///   * the character <i>models</i> in /characters -- three bodies in the street crowd and the
///     four shopkeepers (Phase 9, DECISIONS D8), which also need their embedded textures
///     extracted or they render pure white; and
///   * exactly one clip, <c>Swimming.fbx</c>, because neither new pack contains a swim
///     animation (DECISIONS D14).
/// So this is no longer the project's animation pipeline -- it is the character-model
/// importer, plus one clip.
/// </summary>
public static class MixamoImportSetup
{
    const string CharactersDir = "Assets/characters";
    const string AnimationsDir = "Assets/animations";

    /// <summary>
    /// Clips that should play as a seamless cycle. Everything else is a one-shot.
    /// Matched case-insensitively against the source file name.
    ///
    /// Only the retained swim clip is left here; Phase 9b removed the rest of the Mixamo
    /// animation set.
    /// </summary>
    static readonly string[] LoopingClips =
    {
        "swimming",
    };

    /// <summary>
    /// Friendly clip names so the Animator refers to "Swim" rather than "mixamo.com".
    /// Any file not listed keeps its file name as the clip name.
    /// </summary>
    static readonly Dictionary<string, string> ClipRenames = new Dictionary<string, string>
    {
        { "swimming", "Swim" },
    };

    [MenuItem("Tools/Mini GTA/1. Configure Mixamo Assets", priority = 100)]
    public static void ConfigureAll()
    {
        var log = new StringBuilder();
        int changed = 0;

        var paths = AssetDatabase
            .FindAssets("t:Model", new[] { CharactersDir, AnimationsDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        if (paths.Count == 0)
        {
            Debug.LogWarning("[Mixamo] No FBX found under " + CharactersDir + " or " + AnimationsDir);
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (string path in paths)
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;

                bool isAnimationSource = path.StartsWith(AnimationsDir);
                if (Configure(mi, path, isAnimationSource, log))
                    changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        Debug.Log("[Mixamo] Configured " + changed + " of " + paths.Count + " FBX as Humanoid.\n" + log);
    }

    static bool Configure(ModelImporter mi, string path, bool isAnimationSource, StringBuilder log)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        string key = fileName.ToLowerInvariant();

        // --- Rig: Humanoid is what makes cross-model retargeting possible. ---
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.autoGenerateAvatarMappingIfUnspecified = true;
        mi.importAnimation = true;

        // Mixamo FBX carry their own unit scale; trusting the file lands them at ~1.7m.
        mi.useFileScale = true;
        mi.globalScale = 1f;

        if (isAnimationSource)
        {
            // Animation-only FBX ship a duplicate character mesh we never render.
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
        }
        else
        {
            mi.importCameras = false;
            mi.importLights = false;
            // Unity 6 keeps materials embedded in the FBX and maps them to URP/Lit via the
            // material description. InPrefab must be set explicitly -- the obsolete External
            // value warns on every import once it has been written into the .meta file.
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        // Mobile budget.
        mi.meshCompression = ModelImporterMeshCompression.Medium;
        mi.optimizeMeshPolygons = true;
        mi.optimizeMeshVertices = true;
        mi.weldVertices = true;
        mi.importNormals = ModelImporterNormals.Import;
        mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;

        // --- Clips ---
        var source = mi.defaultClipAnimations;
        if (source != null && source.Length > 0)
        {
            string clipName = ClipRenames.TryGetValue(key, out string nice) ? nice : fileName;
            bool loop = LoopingClips.Contains(key);

            var clips = new ModelImporterClipAnimation[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var c = source[i];
                c.name = source.Length == 1 ? clipName : clipName + "_" + i;

                c.loopTime = loop;
                c.loopPose = loop;
                c.cycleOffset = 0f;

                // Root motion is baked out entirely: the CharacterController owns movement,
                // so translation left in the clip would fight it and cause drift.
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalPositionXZ = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;

                clips[i] = c;
            }
            mi.clipAnimations = clips;

            log.AppendLine("  " + fileName + " -> clip " + clips[0].name + (loop ? " [looping]" : ""));
        }
        else
        {
            log.AppendLine("  " + fileName + " -> humanoid model (no clips)");
        }

        EditorUtility.SetDirty(mi);
        mi.SaveAndReimport();
        return true;
    }

    /// <summary>
    /// Pulls the textures Mixamo embeds inside each character FBX out into real project
    /// assets.
    ///
    /// Without this the characters render pure white: the material description importer
    /// creates correct URP/Lit materials but has nothing to bind to _BaseMap, because the
    /// image data is still trapped inside the FBX. Once the textures exist on disk, a
    /// reimport wires albedo, normal and smoothness maps automatically by name.
    /// </summary>
    [MenuItem("Tools/Mini GTA/1b. Extract Character Textures", priority = 102)]
    public static void ExtractCharacterTextures()
    {
        const string texDir = CharactersDir + "/Textures";
        Directory.CreateDirectory(texDir);

        var paths = AssetDatabase
            .FindAssets("t:Model", new[] { CharactersDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        int extracted = 0;
        foreach (string path in paths)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter mi) continue;
            if (mi.ExtractTextures(texDir)) extracted++;
        }

        AssetDatabase.Refresh();

        // Reimport after extraction so the importer can resolve the now-external textures.
        foreach (string path in paths)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        int textureCount = Directory.GetFiles(texDir)
            .Count(f => !f.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase));

        Debug.Log("[Mixamo] Extracted textures from " + extracted + " of " + paths.Count
                  + " characters -> " + textureCount + " texture assets in " + texDir);
    }

    [MenuItem("Tools/Mini GTA/2. Verify Retargeting", priority = 103)]
    public static void Verify()
    {
        var sb = new StringBuilder("=== RETARGET READINESS ===\n");
        int models = 0, clips = 0, bad = 0;

        foreach (string path in AssetDatabase
                     .FindAssets("t:Model", new[] { CharactersDir, AnimationsDir })
                     .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p))
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) continue;

            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            bool ok = mi.animationType == ModelImporterAnimationType.Human
                      && avatar != null && avatar.isValid && avatar.isHuman;

            if (!ok)
            {
                bad++;
                sb.AppendLine("  BAD  " + Path.GetFileName(path)
                              + " rig=" + mi.animationType
                              + " avatar=" + (avatar == null ? "none" : "invalid"));
                continue;
            }

            if (path.StartsWith(AnimationsDir)) clips++; else models++;
        }

        sb.AppendLine();
        sb.AppendLine("Humanoid models : " + models);
        sb.AppendLine("Humanoid clips  : " + clips);
        sb.AppendLine("Failures        : " + bad);
        sb.AppendLine(bad == 0
            ? "RESULT: OK - every clip will retarget onto every model."
            : "RESULT: FIX NEEDED - listed assets are not valid humanoids.");

        if (bad == 0) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
    }
}
