using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Bakes the full-screen map's background from the real world.
    ///
    /// <b>Why a bake and not a live render.</b> The map used to be a drawn grid: nine even
    /// columns and rows standing in for streets, with no coastline, no districts and no
    /// relationship to the terrain the player actually walks on. Redrawing it as nicer vector
    /// art would still be an abstraction that has to be kept in sync by hand every time the
    /// city changes. Photographing the world instead means the map is correct by construction
    /// -- the beach is where the beach is because that is a picture of the beach.
    ///
    /// <b>Why it is an asset and not a runtime RenderTexture.</b> Rendering the island needs a
    /// camera pass over the whole 1600 m terrain including every building. Doing that when the
    /// player opens the map is a visible hitch on a phone; doing it every frame is absurd. A
    /// PNG committed next to the scene costs nothing at runtime, and the map screen just points
    /// an Image at it. Re-run this step whenever the city layout changes.
    /// </summary>
    public static class MapBaker
    {
        public const string TexturePath = "Assets/Game/UI/MapBake.png";

        /// <summary>
        /// Pixels across the baked image.
        ///
        /// 2048 over a 1600 m island is 1.28 px per metre, which is enough to read the road
        /// grid and the shape of the coast at the size the map is displayed. 4096 doubles the
        /// APK cost of the texture for detail the player cannot see on a 6-inch screen.
        /// </summary>
        const int Resolution = 2048;

        [MenuItem("Tools/Mini GTA/18. Bake Map Texture", priority = 138)]
        public static void Bake()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[Map] No Terrain in the scene. Open City.unity first.");
                return;
            }

            var data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            float span = Mathf.Max(data.size.x, data.size.z);
            Vector3 centre = origin + new Vector3(data.size.x * 0.5f, 0f, data.size.z * 0.5f);

            // Bake at noon and in clear weather, whatever the scene happens to be set to.
            // A map photographed at midnight is a black square, and one photographed in rain
            // has the rain baked into it for the rest of the game.
            var cycle = Object.FindAnyObjectByType<DayNightCycle>();
            float restoreTime = 0f;
            bool restoreAdvance = false;
            if (cycle != null)
            {
                restoreTime = cycle.TimeOfDay;
                restoreAdvance = cycle.Advance;
                cycle.Advance = false;
                cycle.TimeOfDay = 0.5f;
                cycle.ClearWeather();
                cycle.Refresh();
            }

            var camGo = new GameObject("~MapBakeCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = span * 0.5f;
            cam.transform.position = centre + Vector3.up * (data.size.y + 200f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // straight down
            cam.nearClipPlane = 1f;
            cam.farClipPlane = data.size.y + 600f;
            cam.clearFlags = CameraClearFlags.SolidColor;

            // The sea reads as the background wherever the island is not, so the clear colour
            // is the deep-water tone rather than a UI grey -- the coastline then comes out of
            // the render as a real edge instead of needing to be masked in afterwards.
            cam.backgroundColor = new Color(0.06f, 0.12f, 0.24f, 1f);

            // Everything except the interface. The Detail layer is deliberately included: its
            // props are distance-culled in play, but in a top-down photograph they are the
            // texture of the parks and verges.
            cam.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));

            var rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
            };

            var readable = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);

            try
            {
                cam.targetTexture = rt;
                cam.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                readable.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                readable.Apply();
                RenderTexture.active = previous;

                Stylise(readable, terrain);

                Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
                File.WriteAllBytes(TexturePath, readable.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(readable);

                if (cycle != null)
                {
                    cycle.TimeOfDay = restoreTime;
                    cycle.Advance = restoreAdvance;
                    cycle.Refresh();
                }
            }

            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
            ConfigureImport();

            Debug.Log("[Map] Baked " + Resolution + "x" + Resolution + " map from the live world"
                      + " covering " + span + " m at " + (Resolution / span).ToString("F2")
                      + " px/m.\n  " + TexturePath
                      + "\n  Re-run this after any change to the city layout or terrain.");
        }

        /// <summary>
        /// Turns the photograph into a map.
        ///
        /// The raw render is correct but it is a screenshot: blown-out midday greens, white
        /// rooftops, and the ocean shader's wave pattern baked in as diagonal hatching. A map
        /// wants flat, legible, palette-consistent colour with the structure still readable.
        ///
        /// Three things decide each pixel, and none of them is the rendered colour alone:
        ///
        /// 1. <b>Water comes from the terrain height, not the render.</b> Anything below sea
        ///    level is painted from the depth, which both removes the hatching entirely and
        ///    gives a coastline that is exactly where the ground actually crosses the
        ///    waterline rather than wherever the water mesh happened to be drawn.
        ///
        /// 2. <b>Land tone comes from the splat map</b> -- the same sand/grass/rock weights the
        ///    terrain is painted with -- so the beach on the map is the beach in the world.
        ///
        /// 3. <b>Built surfaces are separated by saturation.</b> Roads, pavements and rooftops
        ///    are near-grey; terrain is not. Anything below a saturation threshold is drawn on
        ///    a neutral ramp instead of a natural one, which is what makes the street grid read
        ///    as streets rather than as dark grass.
        ///
        /// District tinting is applied last, from the same ring rule CityBuilder uses to decide
        /// what to build where, so the shading cannot drift out of step with the city.
        /// </summary>
        static void Stylise(Texture2D tex, Terrain terrain)
        {
            var data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;

            int hr = data.heightmapResolution;
            int ar = data.alphamapResolution;
            float[,] heights = data.GetHeights(0, 0, hr, hr);
            float[,,] alphas = data.GetAlphamaps(0, 0, ar, ar);
            int layers = data.terrainLayers.Length;

            float sea = TerrainBuilder.SeaLevel;

            // Palette, from UiTheme so the map belongs to the same interface as everything else.
            var deepWater = new Color(0.055f, 0.075f, 0.20f);
            var shallowWater = new Color(0.16f, 0.34f, 0.52f);
            var sandTone = new Color(0.78f, 0.71f, 0.50f);
            var grassTone = new Color(0.28f, 0.40f, 0.28f);
            var rockTone = new Color(0.38f, 0.37f, 0.40f);
            var roadTone = new Color(0.13f, 0.13f, 0.17f);
            var builtTone = new Color(0.62f, 0.63f, 0.70f);

            int res = tex.width;
            var pixels = tex.GetPixels();

            float centre = (CityBuilder.Blocks - 1) * 0.5f;
            var gridOrigin = CityBuilder.GridOrigin;
            float pitch = CityBuilder.Pitch;

            for (int y = 0; y < res; y++)
            {
                float v = y / (float)(res - 1);
                for (int x = 0; x < res; x++)
                {
                    float u = x / (float)(res - 1);
                    int i = y * res + x;

                    // Camera is Euler(90,0,0): screen +x is world +X, screen +y is world +Z.
                    float worldX = origin.x + u * data.size.x;
                    float worldZ = origin.z + v * data.size.z;

                    int hx = Mathf.Clamp(Mathf.RoundToInt(u * (hr - 1)), 0, hr - 1);
                    int hz = Mathf.Clamp(Mathf.RoundToInt(v * (hr - 1)), 0, hr - 1);
                    float height = heights[hz, hx] * data.size.y + origin.y;

                    Color source = pixels[i];
                    Color outColour;

                    if (height < sea)
                    {
                        // Depth shading, from the terrain rather than from the water shader.
                        float t = Mathf.Clamp01((sea - height) / 6f);
                        outColour = Color.Lerp(shallowWater, deepWater, t);
                    }
                    else
                    {
                        Color.RGBToHSV(source, out _, out float sat, out float val);

                        if (sat < 0.18f)
                        {
                            // Built: roads dark, pavement and rooftops light.
                            outColour = Color.Lerp(roadTone, builtTone, Mathf.Clamp01(val));
                        }
                        else
                        {
                            int axp = Mathf.Clamp(Mathf.RoundToInt(u * (ar - 1)), 0, ar - 1);
                            int azp = Mathf.Clamp(Mathf.RoundToInt(v * (ar - 1)), 0, ar - 1);

                            float sand = layers > 0 ? alphas[azp, axp, 0] : 0f;
                            float grass = layers > 1 ? alphas[azp, axp, 1] : 0f;
                            float rock = layers > 2 ? alphas[azp, axp, 2] : 0f;

                            outColour = sandTone * sand + grassTone * grass + rockTone * rock;

                            // Keep a little of the render's own shading so hills and prop
                            // shadows still read, without letting the midday exposure through.
                            outColour *= 0.82f + 0.30f * Mathf.Clamp01(val);
                        }

                        // District tint, from CityBuilder's own ring rule.
                        int bx = Mathf.FloorToInt((worldX - gridOrigin.x) / pitch);
                        int bz = Mathf.FloorToInt((worldZ - gridOrigin.y) / pitch);
                        if (bx >= 0 && bx < CityBuilder.Blocks && bz >= 0 && bz < CityBuilder.Blocks)
                        {
                            float ring = Mathf.Max(Mathf.Abs(bx - centre), Mathf.Abs(bz - centre));
                            Color tint = ring <= 1f ? new Color(0.62f, 0.56f, 0.95f)    // downtown
                                       : ring <= 3f ? new Color(0.55f, 0.78f, 0.95f)    // midtown
                                                    : new Color(0.70f, 0.90f, 0.72f);   // suburb
                            outColour = Color.Lerp(outColour, outColour * tint, 0.45f);
                        }
                    }

                    outColour.a = 1f;
                    pixels[i] = outColour;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
        }

        /// <summary>
        /// Imports the bake as a UI sprite.
        ///
        /// Compressed and mip-mapped: it is drawn at roughly a third of its native size on the
        /// map screen, so without mips it shimmers, and uncompressed 2048 RGB is 12 MB of APK
        /// for a picture of an island.
        /// </summary>
        static void ConfigureImport()
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;

            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;          // the map is never drawn larger than ~780
            android.format = TextureImporterFormat.ETC2_RGB4;
            importer.SetPlatformTextureSettings(android);

            importer.SaveAndReimport();
        }

        /// <summary>The baked sprite, or null if the bake has never been run.</summary>
        public static Sprite Load() => AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
    }
}
