using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Makes the imported Asset Store packs usable by this project.
    ///
    /// Two of the seven packs ship built-in-render-pipeline materials. Under URP those
    /// render magenta -- not subtly wrong, completely invisible as art. This step rewrites
    /// them onto URP/Lit in place, keeping the textures and colours they already reference.
    ///
    /// It is deliberately idempotent and deliberately narrow: it only touches materials whose
    /// shader is one of the known built-in ones, and only inside the pack folders listed here.
    /// Running it twice is a no-op, and it can never touch a project material.
    /// </summary>
    public static class AssetPackSetup
    {
        /// <summary>Pack folders this step is allowed to modify.</summary>
        static readonly string[] PackRoots =
        {
            "Assets/PolygonalAssets",
            "Assets/Palmov Island",
            "Assets/Awbmecreations",
            "Assets/ithappy",
            "Assets/Shady_3d",

            // Phase 11 packs. SimplePoly City ships all 70 of its materials on
            // Legacy Shaders/Diffuse and every one of its 40 buildings renders magenta
            // untouched; Tarbo ships two on Standard.
            "Assets/SimplePoly City - Low Poly Assets",
            "Assets/Tarbo-CITY-TrafficLights",

            // Deliberately NOT listed: Assets/SoftTouch_UI. Its materials are GUI/Text
            // Shader, which is correct for canvas rendering -- forcing them onto URP/Lit
            // would light the interface. UI materials are not surface materials.
        };

        /// <summary>Built-in shaders that must become URP/Lit.</summary>
        static readonly HashSet<string> LegacyShaders = new HashSet<string>
        {
            "Standard",
            "Standard (Specular setup)",
            "Autodesk Interactive",
            "Legacy Shaders/Diffuse",
            "Legacy Shaders/Bumped Diffuse",
            "Mobile/Diffuse",
        };

        [MenuItem("Tools/Mini GTA/1c. Convert Asset Packs to URP", priority = 104)]
        public static void ConvertAll()
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                Debug.LogError("[Packs] URP/Lit shader not found. Is the URP package installed?");
                return;
            }

            int converted = 0, alreadyOk = 0, instanced = 0;

            var seen = new HashSet<string>();
            foreach (var root in PackRoots)
            {
                if (!AssetDatabase.IsValidFolder(root)) { Debug.LogWarning("[Packs] Missing " + root); continue; }

                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { root }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!seen.Add(path)) continue;

                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null || mat.shader == null) continue;

                    // Skyboxes are not surface materials and have no URP/Lit equivalent.
                    if (mat.shader.name.StartsWith("Skybox/")) continue;

                    if (LegacyShaders.Contains(mat.shader.name))
                    {
                        ConvertToUrpLit(mat, urpLit);
                        converted++;
                    }
                    else
                    {
                        alreadyOk++;
                    }

                    // Every pack material is used by many instances of the same prop. Without
                    // this the city clutter cannot instance and each bin is its own draw call.
                    if (!mat.enableInstancing) { mat.enableInstancing = true; instanced++; }
                    EditorUtility.SetDirty(mat);
                }
            }

            AssetDatabase.SaveAssets();

            Debug.Log("[Packs] URP conversion: " + converted + " converted, " + alreadyOk
                      + " already URP, " + instanced + " had GPU instancing switched on.");
        }

        /// <summary>
        /// Rewrites one built-in material onto URP/Lit.
        ///
        /// The property values have to be read *before* the shader is swapped: assigning a new
        /// shader drops any property the new shader does not declare, and the built-in names
        /// (_Color, _MainTex, _Glossiness) are exactly the ones URP renamed.
        /// </summary>
        static void ConvertToUrpLit(Material mat, Shader urpLit)
        {
            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Texture main = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Vector2 tiling = main != null ? mat.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = main != null ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
            Texture bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            Texture emis = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
            Color emisColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
            float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
            // Built-in calls it Glossiness; Autodesk Interactive uses the URP name already.
            float smoothness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness")
                             : mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness")
                             : 0.2f;
            bool emissive = emis != null || emisColor.maxColorComponent > 0.001f;

            mat.shader = urpLit;

            mat.SetColor("_BaseColor", color);
            if (main != null)
            {
                mat.SetTexture("_BaseMap", main);
                mat.SetTextureScale("_BaseMap", tiling);
                mat.SetTextureOffset("_BaseMap", offset);
            }
            if (bump != null)
            {
                mat.SetTexture("_BumpMap", bump);
                mat.EnableKeyword("_NORMALMAP");
            }
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);

            if (emissive)
            {
                mat.SetColor("_EmissionColor", emisColor);
                if (emis != null) mat.SetTexture("_EmissionMap", emis);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }

        /// <summary>
        /// Reports what the packs look like after conversion. Cheap sanity check to run before
        /// blaming a builder for magenta geometry.
        /// </summary>
        [MenuItem("Tools/Mini GTA/1d. Report Asset Pack Shaders", priority = 105)]
        public static void Report()
        {
            foreach (var root in PackRoots)
            {
                if (!AssetDatabase.IsValidFolder(root)) continue;

                var mats = AssetDatabase.FindAssets("t:Material", new[] { root })
                    .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(m => m != null).ToArray();

                Debug.Log("[Packs] " + root + " (" + mats.Length + " materials): "
                    + string.Join(" | ", mats
                        .GroupBy(m => m.shader != null ? m.shader.name : "NULL")
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key + " x" + g.Count())));
            }
        }
    }
}
