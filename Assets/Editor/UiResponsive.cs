using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Measures the interface against a matrix of real device shapes and reports, numerically,
    /// anything that leaves the safe area, overlaps something else, is too small to hit with a
    /// thumb, or is a text box its own text does not fit inside.
    ///
    /// <b>Why this exists rather than more screenshots.</b> Phase 13 verified layout by
    /// capturing three aspect ratios and looking at the pictures. That found real defects, but
    /// it missed a whole class of them, because an editor capture does not apply
    /// <see cref="Screen.safeArea"/> at all: <see cref="SafeAreaFitter"/> only runs in play
    /// mode, so every edit-mode capture this project has ever taken was of a canvas with no
    /// notch inset. On a phone that reports insets — which the Device Simulator does, and which
    /// any Android device does the moment <c>renderOutsideSafeArea</c> is turned on — the
    /// usable canvas is materially smaller than anything that was ever photographed.
    ///
    /// So this simulates both halves: the render size, which drives <see cref="CanvasScaler"/>,
    /// and the safe-area inset, which drives the container every UI root hangs off. Then it
    /// measures. A picture is still worth taking afterwards, and <see cref="Capture"/> takes
    /// them — but the sweep is what says which shapes are worth photographing.
    /// </summary>
    public static class UiResponsive
    {
        // ------------------------------------------------------------------ matrix

        /// <summary>One screen shape to test: pixels, plus the inset the OS reserves.</summary>
        public struct Shape
        {
            public string Name;
            public int Width, Height;
            /// <summary>Safe-area inset in pixels: left, bottom, right, top.</summary>
            public Vector4 Inset;
            /// <summary>Screen density, for the physical touch-target check.</summary>
            public float Dpi;

            public float Aspect => Width / (float)Height;
        }

        /// <summary>
        /// Landscape only, because the game is: <c>allowedAutorotateToPortrait</c> is false and
        /// both landscape orientations are true, so the OS never hands this UI a portrait
        /// window. A portrait layout would be dead code and the thumb clusters assume landscape.
        ///
        /// The insets are the interesting part. Android with <c>renderOutsideSafeArea = false</c>
        /// letterboxes the app itself and reports a full-screen safe area — that is the real
        /// test device, and it is the easy case. The rows with insets are the ones that matter:
        /// an iPhone-class cutout in landscape puts the notch on a long edge and the home
        /// indicator along the bottom, and Unity's Device Simulator reports exactly that.
        /// </summary>
        public static readonly Shape[] Matrix =
        {
            // --- Android, app letterboxed away from the cutout (the real test device) -----
            new Shape { Name = "vivo I2019 (project device)", Width = 2318, Height = 1080,
                        Inset = Vector4.zero, Dpi = 405f },
            new Shape { Name = "20:9 phone",                  Width = 2400, Height = 1080,
                        Inset = Vector4.zero, Dpi = 395f },
            new Shape { Name = "19.5:9 phone",                Width = 2340, Height = 1080,
                        Inset = Vector4.zero, Dpi = 400f },
            new Shape { Name = "16:9 phone",                  Width = 1920, Height = 1080,
                        Inset = Vector4.zero, Dpi = 320f },
            new Shape { Name = "16:9 low-end",                Width = 1280, Height =  720,
                        Inset = Vector4.zero, Dpi = 270f },
            new Shape { Name = "4:3 tablet",                  Width = 2048, Height = 1536,
                        Inset = Vector4.zero, Dpi = 264f },
            new Shape { Name = "21:9 ultrawide",              Width = 2560, Height = 1080,
                        Inset = Vector4.zero, Dpi = 385f },

            // --- reporting real insets: notch on a long edge, indicator along the bottom ---
            new Shape { Name = "iPhone-class notch (simulator)", Width = 2778, Height = 1284,
                        Inset = new Vector4(141f, 63f, 141f, 0f), Dpi = 458f },
            new Shape { Name = "punch-hole, one side",        Width = 2400, Height = 1080,
                        Inset = new Vector4(110f, 0f, 0f, 0f), Dpi = 395f },
            new Shape { Name = "gesture bar + cutout",        Width = 2340, Height = 1080,
                        Inset = new Vector4(90f, 48f, 90f, 0f), Dpi = 400f },
        };

        // --------------------------------------------------------------- entry point

        [MenuItem("Tools/Mini GTA/6c. Test UI Responsiveness", priority = 115)]
        public static void Run() { Debug.Log("[UI] " + Report()); }

        /// <summary>The sweep, returned rather than logged, so a caller can print it itself.</summary>
        public static string Report()
        {
            var report = new StringBuilder();
            int totalProblems = 0;

            report.AppendLine("UI responsiveness sweep — " + Matrix.Length + " screen shapes");
            report.AppendLine("face: " + UiTheme.FontSource);
            report.AppendLine();

            foreach (var shape in Matrix)
                totalProblems += Measure(shape, report);

            report.AppendLine();
            report.AppendLine(totalProblems == 0
                ? "PASS - no overflow, overlap, undersized target or clipped text on any shape."
                : "FAIL - " + totalProblems + " problems across the matrix.");

            return report.ToString();
        }

        // ------------------------------------------------------------------ harness

        /// <summary>
        /// Puts the canvas into a given screen shape, lets layout settle, measures, restores.
        ///
        /// The render size has to be simulated through a camera: <see cref="CanvasScaler"/>
        /// reads <c>Canvas.renderingDisplaySize</c>, which for a Screen Space - Overlay canvas
        /// is the Game view and cannot be overridden. Pointing the canvas at a camera with a
        /// target texture of the wanted size makes the scaler resolve exactly as it would on
        /// that display. The safe area is applied by hand for the same reason: SafeAreaFitter
        /// reads <see cref="Screen.safeArea"/>, which edit mode never changes.
        /// </summary>
        static int Measure(Shape shape, StringBuilder report)
        {
            var canvasGo = GameObject.Find("HUD");
            if (canvasGo == null) { report.AppendLine("no HUD canvas"); return 1; }

            var canvas = canvasGo.GetComponent<Canvas>();
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            var safe = canvasGo.transform.Find("SafeArea") as RectTransform;

            var rt = new RenderTexture(shape.Width, shape.Height, 0);
            var camGo = new GameObject("~UiMeasureCam");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.targetTexture = rt;

            var prevMode = canvas.renderMode;
            var prevCam = canvas.worldCamera;
            float prevPlane = canvas.planeDistance;
            Vector2 prevMin = safe.anchorMin, prevMax = safe.anchorMax;
            Vector2 prevOffMin = safe.offsetMin, prevOffMax = safe.offsetMax;

            int problems = 0;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 100f;

                // Force the scaler to recompute against the new display size.
                scaler.enabled = false;
                scaler.enabled = true;

                // Apply the shape's safe area the way SafeAreaFitter would at runtime.
                safe.anchorMin = new Vector2(shape.Inset.x / shape.Width, shape.Inset.y / shape.Height);
                safe.anchorMax = new Vector2(1f - shape.Inset.z / shape.Width,
                                             1f - shape.Inset.w / shape.Height);
                safe.offsetMin = Vector2.zero;
                safe.offsetMax = Vector2.zero;

                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(safe);
                Bleed(safe);

                problems = Audit(shape, canvas, safe, report);
            }
            finally
            {
                canvas.renderMode = prevMode;
                canvas.worldCamera = prevCam;
                canvas.planeDistance = prevPlane;
                safe.anchorMin = prevMin; safe.anchorMax = prevMax;
                safe.offsetMin = prevOffMin; safe.offsetMax = prevOffMax;

                scaler.enabled = false;
                scaler.enabled = true;

                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
                Canvas.ForceUpdateCanvases();
            }
            return problems;
        }

        // -------------------------------------------------------------------- audit

        /// <summary>The screens whose layout is checked, and the elements that must fit.</summary>
        static readonly string[] Screens =
        {
            "Lobby", "CharacterSelect", "ProfilePanel",
            "PauseMenu", "SettingsPanel", "FullMap", "DialoguePanel",
            "ShopUI", "StorePanel", "AdOfferPrompt",
            "StatusPanel", "MissionHud", "ActionButtons", "Minimap",
            "VehicleReadout", "StoreButton", "ClearWantedButton", "PauseButton",
        };

        /// <summary>
        /// A thumb target should be about 7 mm across — Apple's 44 pt and Google's 48 dp both
        /// land near it. Below this a control is technically present and practically a miss.
        /// </summary>
        const float MinTargetMillimetres = 7f;

        static int Audit(Shape shape, Canvas canvas, RectTransform safe, StringBuilder report)
        {
            float scale = canvas.scaleFactor;
            Rect safeRect = safe.rect;

            report.AppendLine(string.Format(
                "{0,-32} {1}x{2} ({3:F2}:1)  scale {4:F3}  canvas {5:F0}x{6:F0}  safe {7:F0}x{8:F0}",
                shape.Name, shape.Width, shape.Height, shape.Aspect, scale,
                ((RectTransform)canvas.transform).rect.width,
                ((RectTransform)canvas.transform).rect.height,
                safeRect.width, safeRect.height));

            var problems = new List<string>();

            // --- 1. does anything leave the safe area? --------------------------------
            foreach (string name in Screens)
            {
                var root = safe.Find(name) as RectTransform;
                if (root == null) continue;

                foreach (var element in Visible(root))
                {
                    Rect box = In(element, safe);
                    float over = Mathf.Max(
                        Mathf.Max(safeRect.xMin - box.xMin, box.xMax - safeRect.xMax),
                        Mathf.Max(safeRect.yMin - box.yMin, box.yMax - safeRect.yMax));

                    if (over > 1f)
                        problems.Add("  OVERFLOW  " + name + "/" + element.name
                                     + " leaves the safe area by " + over.ToString("F0") + " units");
                }
            }

            // --- 2. touch targets ------------------------------------------------------
            foreach (var control in safe.GetComponentsInChildren<Selectable>(true))
            {
                var image = control.GetComponent<Image>();
                if (image == null || image.color.a < 0.05f) continue;  // invisible hit areas

                Rect r = ((RectTransform)control.transform).rect;
                float mm = Mathf.Min(r.width, r.height) * scale / shape.Dpi * 25.4f;
                if (mm < MinTargetMillimetres)
                    problems.Add("  SMALL     " + control.name + " is " + mm.ToString("F1")
                                 + " mm (" + Mathf.Min(r.width, r.height).ToString("F0")
                                 + " units); floor is " + MinTargetMillimetres + " mm");
            }

            // --- 3. text that does not fit its own box ---------------------------------
            foreach (string name in Screens)
            {
                var root = safe.Find(name) as RectTransform;
                if (root == null) continue;

                foreach (var text in root.GetComponentsInChildren<Text>(true))
                {
                    if (string.IsNullOrEmpty(text.text)) continue;   // filled at runtime
                    if (!text.gameObject.activeInHierarchy) continue;

                    Rect r = text.rectTransform.rect;
                    // Overflow mode Wrap means a long line is meant to wrap, so only the
                    // height matters; Overflow means it is meant to spill and only a box far
                    // too small is a defect.
                    bool wraps = text.horizontalOverflow == HorizontalWrapMode.Wrap;
                    if (!wraps && text.preferredWidth > r.width + 1f)
                        problems.Add("  CLIPS     " + name + "/" + text.name + " '" + text.text
                                     + "' needs " + text.preferredWidth.ToString("F0")
                                     + " units, box is " + r.width.ToString("F0"));
                    if (text.preferredHeight > r.height + 1f)
                        problems.Add("  CLIPS-V   " + name + "/" + text.name + " '" + text.text
                                     + "' needs " + text.preferredHeight.ToString("F0")
                                     + " units tall, box is " + r.height.ToString("F0"));
                }
            }

            // --- 4. the lobby's own collisions ----------------------------------------
            problems.AddRange(LobbyCollisions(safe));

            foreach (string p in problems) report.AppendLine(p);
            if (problems.Count == 0) report.AppendLine("  ok");
            report.AppendLine();
            return problems.Count;
        }

        /// <summary>
        /// The lobby is edge-anchored, so its pieces are positioned from four different corners
        /// and nothing in the layout system compares them. These are the pairs that can only
        /// be checked by asking.
        /// </summary>
        static IEnumerable<string> LobbyCollisions(RectTransform safe)
        {
            var lobby = safe.Find("Lobby") as RectTransform;
            if (lobby == null) yield break;

            var rail = lobby.Find("Rail") as RectTransform;
            var content = lobby.Find("Content") as RectTransform;
            var topBar = lobby.Find("TopBar") as RectTransform;
            var stage = content != null ? content.Find("Stage") as RectTransform : null;
            var play = content != null ? content.Find("PlayButton") as RectTransform : null;

            if (rail != null && stage != null)
            {
                Rect r = In(rail, lobby), s = In(stage, lobby);
                if (r.xMax > s.xMin)
                    yield return "  COLLIDE   the rail and the character stage overlap by "
                                 + (r.xMax - s.xMin).ToString("F0") + " units";
            }

            if (topBar != null && stage != null)
            {
                Rect t = In(topBar, lobby), s = In(stage, lobby);
                if (s.yMax > t.yMin)
                    yield return "  COLLIDE   the top bar and the character stage overlap by "
                                 + (s.yMax - t.yMin).ToString("F0") + " units";
            }

            if (rail != null && play != null)
            {
                Rect r = In(rail, lobby), p = In(play, lobby);
                if (r.xMax > p.xMin && r.yMin < p.yMax && r.yMax > p.yMin)
                    yield return "  COLLIDE   the rail and PLAY overlap";
            }

            // The chips at the top right and the name chip at the top left share the bar.
            var chip = topBar != null ? topBar.Find("ProfileChip") as RectTransform : null;
            var level = topBar != null ? topBar.Find("LevelChip") as RectTransform : null;
            if (chip != null && level != null)
            {
                Rect c = In(chip, lobby), l = In(level, lobby);
                if (c.xMax > l.xMin)
                    yield return "  COLLIDE   the name chip and the level chip overlap by "
                                 + (c.xMax - l.xMin).ToString("F0") + " units";
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Leaf-ish elements worth measuring: anything that draws or takes a tap.</summary>
        static IEnumerable<RectTransform> Visible(RectTransform root)
        {
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.gameObject.activeInHierarchy) continue;
                if (graphic.color.a < 0.05f) continue;

                // A backdrop or a scrim is supposed to cover everything including the insets;
                // that is the whole point of SafeAreaBleed, so measuring it against the safe
                // area would report the fix as the bug.
                if (graphic.GetComponent<SafeAreaBleed>() != null) continue;

                var rt = graphic.rectTransform;
                if (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one
                    && rt.sizeDelta == Vector2.zero) continue;

                yield return rt;
            }
        }

        /// <summary>
        /// Applies every SafeAreaBleed under the container immediately.
        ///
        /// The component polls in Update, and no frame elapses inside an editor command -- so
        /// without this a measurement or a capture taken straight after moving the simulated
        /// safe area sees the backdrop still sized for the previous shape.
        /// </summary>
        public static void Bleed(RectTransform safe)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var bleed in safe.GetComponentsInChildren<SafeAreaBleed>(true))
                bleed.Apply();
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>A rect expressed in an ancestor's local space.</summary>
        static Rect In(RectTransform child, RectTransform space)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);

            float xMin = float.MaxValue, yMin = float.MaxValue;
            float xMax = float.MinValue, yMax = float.MinValue;
            foreach (var corner in corners)
            {
                Vector3 local = space.InverseTransformPoint(corner);
                xMin = Mathf.Min(xMin, local.x); xMax = Mathf.Max(xMax, local.x);
                yMin = Mathf.Min(yMin, local.y); yMax = Mathf.Max(yMax, local.y);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        // ----------------------------------------------------------------- captures

        /// <summary>
        /// Photographs one screen at every shape in the matrix, with that shape's safe area
        /// applied — which no capture in this project has done before.
        /// </summary>
        public static void Capture(string screen, string prefix)
        {
            var canvasGo = GameObject.Find("HUD");
            var canvas = canvasGo.GetComponent<Canvas>();
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            var safe = canvasGo.transform.Find("SafeArea") as RectTransform;

            Vector2 prevMin = safe.anchorMin, prevMax = safe.anchorMax;

            foreach (var shape in Matrix)
            {
                safe.anchorMin = new Vector2(shape.Inset.x / shape.Width, shape.Inset.y / shape.Height);
                safe.anchorMax = new Vector2(1f - shape.Inset.z / shape.Width,
                                             1f - shape.Inset.w / shape.Height);
                safe.offsetMin = Vector2.zero;
                safe.offsetMax = Vector2.zero;

                scaler.enabled = false; scaler.enabled = true;
                Canvas.ForceUpdateCanvases();
                Bleed(safe);

                string safeName = shape.Name.Replace(' ', '_').Replace('(', '_')
                                            .Replace(')', '_').Replace(':', '-').Replace(',', '_');
                PhaseCapture.UiShot(prefix + "_" + safeName, shape.Width, shape.Height);
            }

            safe.anchorMin = prevMin; safe.anchorMax = prevMax;
            safe.offsetMin = Vector2.zero; safe.offsetMax = Vector2.zero;
            scaler.enabled = false; scaler.enabled = true;
            Canvas.ForceUpdateCanvases();
        }
    }
}
