using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Sorts the city's geometry into what has to be drawn and what only has to be drawn
    /// nearby, so <see cref="PerformanceTuner"/> has something to cull.
    ///
    /// The single biggest win in this scene is the road markings: roughly seven hundred
    /// separate renderers, each a 32 cm dash that is illegible past about sixty metres. Putting
    /// them on their own layer and giving that layer a short far plane removes half the scene's
    /// renderers from the culling pass without changing anything a player can see.
    /// </summary>
    public static class PerformanceSetup
    {
        const string ScenePath = "Assets/Scenes/City.unity";

        /// <summary>
        /// Names that are decoration. Everything here must be collider-free -- culling the
        /// renderer of something you can walk into leaves an invisible wall.
        /// </summary>
        static readonly string[] DetailNames = { "M", "Parapet", "Path_NS", "Path_EW" };

        /// <summary>
        /// Phase 9 added several hundred pack props. Rather than list every prefab name here
        /// and have the list rot the first time someone adds a bin,
        /// <see cref="EnvironmentCatalog.Place"/> renames anything it builds collider-free with
        /// this prefix. The collider rule below still applies on top, so a prop that somehow
        /// kept a collider is still skipped -- the prefix is a hint, not an override.
        /// </summary>
        /// <summary>
        /// Name prefixes that mark cullable decoration.
        ///
        /// <c>Detail_</c> is applied by <see cref="EnvironmentCatalog.Place"/> to everything it
        /// builds collider-free. <c>Zebra_</c> is the crosswalk paint, which Phase 7 missed when
        /// it moved the lane markings onto this layer: the paint stayed on Default and so kept
        /// drawing all the way out to the tier's draw distance, which is exactly where depth
        /// precision fails and flat road decals start to tear. Culling it at 85-320 m like the
        /// lane markings removes the far-distance case entirely.
        /// </summary>
        static readonly string[] DetailPrefixes = { "Detail_", "Zebra_" };

        [MenuItem("Tools/Mini GTA/11. Sort Geometry for Culling", priority = 131)]
        public static void SortGeometry()
        {
            int layer = EnsureLayer(PerformanceTuner.DetailLayer);
            if (layer < 0)
            {
                Debug.LogError("[Perf] No free user layer for '" + PerformanceTuner.DetailLayer
                               + "'. Free one in Project Settings > Tags and Layers.");
                return;
            }

            var names = new HashSet<string>(DetailNames);
            int moved = 0, skipped = 0, total = 0;

            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                total++;

                var go = renderer.gameObject;
                bool byName = names.Contains(go.name);
                bool byPrefix = false;
                for (int i = 0; i < DetailPrefixes.Length && !byPrefix; i++)
                    byPrefix = go.name.StartsWith(DetailPrefixes[i]);
                if (!byName && !byPrefix) continue;

                // A collider means gameplay depends on this object existing, whatever it is
                // called. Never cull one of those.
                if (go.GetComponent<Collider>() != null) { skipped++; continue; }

                if (go.layer == layer) continue;

                go.layer = layer;
                EditorUtility.SetDirty(go);
                moved++;
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log("[Perf] Detail layer = " + layer + ".  Moved " + moved + " of " + total
                      + " renderers"
                      + (skipped > 0 ? ", skipped " + skipped + " with colliders" : "")
                      + ".\n  These stop drawing at the tier's detail distance "
                      + "(85 m low / 170 m medium / 320 m high).");
        }

        /// <summary>Finds or creates a named user layer, returning its index or -1.</summary>
        static int EnsureLayer(string name)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset == null || asset.Length == 0) return -1;

            var tagManager = new SerializedObject(asset[0]);
            var layers = tagManager.FindProperty("layers");
            if (layers == null) return -1;

            // 0-7 are Unity's own; only 8 and up are ours to assign.
            for (int i = 8; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;

            for (int i = 8; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return i;
            }

            return -1;
        }
    }
}
