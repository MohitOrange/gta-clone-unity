using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Converts imported-pack materials from Built-in shaders to URP, and reports the ones it
    /// cannot.
    ///
    /// <b>This is BUG-018's long tail.</b> Assigning the URP asset fixed everything that was
    /// already using a URP shader, which was every material in the scene at the time -- a
    /// sample of 401 renderers found zero broken. What that sample could not see was the
    /// content still sitting in the asset packs, unplaced. The moment the harbour put a
    /// CatBorg Pirates ship into the world it drew magenta, because its materials are on
    /// Built-in <c>Standard</c>, which has no valid pass under URP.
    ///
    /// A magenta object is not subtle, but nothing in the build or the scene audit reports it:
    /// the renderer count, the collider count and the bounds were all correct. It took a
    /// rendered frame. That is the whole argument for looking at pictures.
    /// </summary>
    public static class UrpMaterialFixer
    {
        /// <summary>Property pairs to carry across when swapping shader.</summary>
        static readonly (string from, string to)[] TextureMap =
        {
            ("_MainTex", "_BaseMap"),
            ("_BumpMap", "_BumpMap"),
            ("_EmissionMap", "_EmissionMap"),
        };

        static readonly (string from, string to)[] ColorMap =
        {
            ("_Color", "_BaseColor"),
            ("_EmissionColor", "_EmissionColor"),
        };

        [MenuItem("Mini GTA/World/Convert harbour materials to URP", priority = 42)]
        public static void FixHarbour()
        {
            var root = GameObject.Find("Harbor");
            if (root == null) { Debug.LogError("[URP] no Harbor object in the scene"); return; }
            Fix(root);
        }

        public static void Fix(GameObject root)
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) { Debug.LogError("[URP] URP/Lit shader not found"); return; }

            var seen = new HashSet<Material>();
            int ok = 0, converted = 0, failed = 0;

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !seen.Add(m)) continue;

                    if (m.shader == null)
                    {
                        Debug.LogWarning("[URP] '" + m.name + "' has no shader at all");
                        failed++;
                        continue;
                    }

                    string shaderName = m.shader.name;

                    if (shaderName.StartsWith("Universal Render Pipeline/")
                        || shaderName.StartsWith("Shader Graphs/")
                        || shaderName == "Sprites/Default")
                    {
                        ok++;
                        continue;
                    }

                    Debug.Log("[URP] converting '" + m.name + "' from '" + shaderName + "'");

                    // Read before the swap: changing shader discards properties the new one
                    // does not declare, so anything wanted afterwards has to be captured first.
                    var textures = new Dictionary<string, Texture>();
                    var colors = new Dictionary<string, Color>();

                    foreach (var (from, to) in TextureMap)
                        if (m.HasProperty(from)) textures[to] = m.GetTexture(from);

                    foreach (var (from, to) in ColorMap)
                        if (m.HasProperty(from)) colors[to] = m.GetColor(from);

                    Undo.RecordObject(m, "Convert material to URP");
                    m.shader = urpLit;

                    foreach (var kv in textures)
                        if (kv.Value != null && m.HasProperty(kv.Key)) m.SetTexture(kv.Key, kv.Value);

                    foreach (var kv in colors)
                        if (m.HasProperty(kv.Key)) m.SetColor(kv.Key, kv.Value);

                    EditorUtility.SetDirty(m);
                    converted++;
                }
            }

            AssetDatabase.SaveAssets();

            Debug.Log("[URP] materials: " + ok + " already URP, "
                      + converted + " converted, " + failed + " unfixable");
        }

        /// <summary>Reports without changing anything, so a check is cheap.</summary>
        public static void Audit(GameObject root, string label)
        {
            var seen = new HashSet<Material>();
            int ok = 0, bad = 0;

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || !seen.Add(m)) continue;

                bool good = m.shader != null
                            && (m.shader.name.StartsWith("Universal Render Pipeline/")
                                || m.shader.name.StartsWith("Shader Graphs/")
                                || m.shader.name == "Sprites/Default");

                if (good) ok++;
                else
                {
                    bad++;
                    Debug.LogWarning("[URP] " + label + ": '" + m.name + "' uses '"
                                     + (m.shader != null ? m.shader.name : "<null>")
                                     + "' -- will draw magenta");
                }
            }

            Debug.Log("[URP] " + label + ": " + ok + " URP materials, " + bad + " not");
        }
    }
}
