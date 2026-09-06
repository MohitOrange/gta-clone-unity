using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Screenshot and census helpers used to verify a phase in Play mode.
    ///
    /// This exists because "Editor-verified" has to mean a rendered frame was looked at, not
    /// that a parameter read came back with the expected string. Phase 9b was signed off on
    /// parameter reads and shipped a character stuck in an arms-forward pose that any single
    /// screenshot would have caught (see the PHASE9B.md addendum). Everything here renders
    /// through the real game camera stack, at a real resolution, into a file.
    ///
    /// Editor-only tooling. Nothing in Assets/Game depends on it and it ships in no build.
    /// </summary>
    public static class PhaseCapture
    {
        public const string ShotDir =
            @"C:\Users\Mohit\AppData\Local\Temp\claude\C--unity-games-gta-gta\65a53f8e-2b10-4bc2-bc72-0f1d94702a39\scratchpad";

        /// <summary>
        /// Renders whatever <see cref="Camera.main"/> currently sees to a PNG.
        ///
        /// Renders on demand rather than grabbing the back buffer, so it works while the Editor
        /// is backgrounded and does not depend on the Game view being visible or focused.
        /// </summary>
        public static string Shot(string name, int w = 1280, int h = 720, Camera cam = null)
        {
            cam = cam != null ? cam : Camera.main;
            if (cam == null) return "no camera";

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;

            Directory.CreateDirectory(ShotDir);
            string path = Path.Combine(ShotDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());

            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            return path;
        }

        /// <summary>
        /// Renders from an arbitrary vantage point, looking at a target.
        ///
        /// Uses a throwaway camera copied from the main one so post-processing, layer culling
        /// and the URP renderer asset all match what the player actually sees -- a bare
        /// <c>new Camera()</c> would render a different image and prove nothing.
        /// </summary>
        public static string ShotFrom(string name, Vector3 from, Vector3 lookAt,
                                      int w = 1280, int h = 720, float fov = 0f)
        {
            var main = Camera.main;
            if (main == null) return "no camera";

            var go = new GameObject("~PhaseCaptureCam");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.targetTexture = null;
            if (fov > 0.01f) cam.fieldOfView = fov;
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(lookAt - from, Vector3.up));

            string path = Shot(name, w, h, cam);
            Object.DestroyImmediate(go);
            return path;
        }

        /// <summary>
        /// Renders the world plus the interface at an arbitrary resolution.
        ///
        /// <b>Why this is not just a screenshot.</b> The HUD is a ScreenSpaceOverlay canvas,
        /// which is composited after everything and appears in no camera's render, and
        /// <c>ScreenCapture.CaptureScreenshot</c> is unavailable over the Editor bridge. So the
        /// canvas is temporarily moved to ScreenSpaceCamera against a throwaway camera whose
        /// pixel rect is the size being tested, drawn over a render of the main camera, and put
        /// back exactly as it was.
        ///
        /// The layout this produces is the real one: CanvasScaler derives its scale factor from
        /// the canvas's rendering display size either way, so "Scale With Screen Size" resolves
        /// identically. What it does <i>not</i> exercise is overlay compositing itself -- which
        /// is where PHASE8's Vulkan pre-transform bug lived, and that reproduces on device only.
        /// </summary>
        public static string UiShot(string name, int w = 1280, int h = 720)
        {
            var canvas = FindHudCanvas();
            if (canvas == null) return "no canvas";

            var main = Camera.main;

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("~UiCaptureCam");
            var uiCam = camGo.AddComponent<Camera>();

            // Remember everything before touching it.
            var prevMode = canvas.renderMode;
            var prevCam = canvas.worldCamera;
            float prevPlane = canvas.planeDistance;
            var prevMainTarget = main != null ? main.targetTexture : null;
            var prevActive = RenderTexture.active;

            try
            {
                if (main != null)
                {
                    main.targetTexture = rt;
                    main.Render();
                    main.targetTexture = prevMainTarget;
                }

                if (main != null) uiCam.CopyFrom(main);

                // Clear depth but keep colour: the world was drawn by a perspective camera and
                // this one is orthographic, so their depth values are not comparable and
                // leaving the buffer alone depth-rejects the whole canvas.
                uiCam.clearFlags = main != null ? CameraClearFlags.Depth : CameraClearFlags.SolidColor;

                // Every canvas graphic in this project sits on the Default layer, not on UI,
                // so filtering by layer here renders an empty frame. The orthographic box
                // below is what keeps world geometry out instead: the canvas is placed at the
                // origin in front of this camera, and the city is a kilometre away.
                uiCam.cullingMask = ~0;
                uiCam.orthographic = true;
                uiCam.targetTexture = rt;
                uiCam.transform.position = new Vector3(0f, 0f, -1000f);
                uiCam.transform.rotation = Quaternion.identity;

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCam;
                canvas.planeDistance = 100f;

                // Force the scaler to recompute against the new display size before drawing,
                // otherwise the first capture at a new resolution renders last frame's layout.
                Canvas.ForceUpdateCanvases();

                // Backdrops and scrims size themselves against the canvas in Update, and no
                // frame elapses inside an editor command -- so without this they keep the
                // offsets they were given at the *previous* resolution and a capture at a new
                // one shows the safe-area inset as a bare band down each edge.
                foreach (var bleed in canvas.GetComponentsInChildren<SafeAreaBleed>(true))
                    bleed.Apply();
                Canvas.ForceUpdateCanvases();

                uiCam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();

                Directory.CreateDirectory(ShotDir);
                string path = Path.Combine(ShotDir, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return path;
            }
            finally
            {
                canvas.renderMode = prevMode;
                canvas.worldCamera = prevCam;
                canvas.planeDistance = prevPlane;
                if (main != null) main.targetTexture = prevMainTarget;
                RenderTexture.active = prevActive;

                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
                Canvas.ForceUpdateCanvases();
            }
        }

        static Canvas FindHudCanvas()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                if (c.isRootCanvas) return c;
            return null;
        }

        /// <summary>Freezes the sky at a given time of day so captures are comparable.</summary>
        public static string SetTimeOfDay(float t, bool advance = false)
        {
            var dn = Object.FindAnyObjectByType<DayNightCycle>();
            if (dn == null) return "no DayNightCycle";
            dn.Advance = advance;
            dn.TimeOfDay = Mathf.Repeat(t, 1f);
            dn.Refresh();
            return "TimeOfDay=" + dn.TimeOfDay.ToString("F3") + " hour=" + dn.Hour.ToString("F1")
                 + " advance=" + dn.Advance + " night=" + dn.IsNight;
        }

        // ------------------------------------------------------------------ census

        /// <summary>
        /// The one scene census used for every before/after number in the phase docs.
        ///
        /// Counted the same way every time, because a renderer count is only meaningful
        /// against another one measured identically. Skinned and mesh renderers only:
        /// particle, line and trail renderers are not what the draw-call budget is about,
        /// and counting them would make the environment numbers move when the weather does.
        /// </summary>
        public static string Census()
        {
            var sb = new StringBuilder();
            int rend = 0;
            long tris = 0;
            int detail = 0;
            int detailLayer = LayerMask.NameToLayer("Detail");

            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                rend++;
                if (r.gameObject.layer == detailLayer) detail++;

                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += Tris(mf.sharedMesh);
                if (r is SkinnedMeshRenderer sk && sk.sharedMesh != null) tris += Tris(sk.sharedMesh);
            }

            sb.AppendLine("renderers=" + rend);
            sb.AppendLine("triangles=" + tris);
            sb.AppendLine("onDetailLayer=" + detail);
            sb.AppendLine("colliders=" + Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude).Length);
            sb.AppendLine("animals=" + Object.FindObjectsByType<AmbientAnimal>(FindObjectsInactive.Exclude).Length);
            sb.AppendLine("lights=" + Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).Length);

            var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);
            sb.AppendLine("packBuildings=" + all.Count(g => g.name.StartsWith("Bld_Pack")));
            sb.AppendLine("boxBuildings=" + all.Count(g => g.name == "Bld"));
            return sb.ToString();
        }

        /// <summary>
        /// Triangle count without allocating the whole index array.
        ///
        /// <c>mesh.triangles</c> allocates a managed int[] per call; over a few thousand
        /// renderers that is hundreds of megabytes of garbage and a visible editor stall.
        /// </summary>
        static long Tris(Mesh mesh)
        {
            long n = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                n += (long)mesh.GetIndexCount(i) / 3;
            return n;
        }
    }
}
