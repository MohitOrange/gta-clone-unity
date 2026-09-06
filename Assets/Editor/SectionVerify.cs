using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Batch entry points for verifying section work without a human at the keyboard.
    ///
    /// Edit-mode only. Anything needing Play mode cannot run this way -- see the note in
    /// PHASE14.md about batch mode not pumping the Play loop -- so these check the things
    /// that are true before the game runs: that a system registered what it claims to, that
    /// its gates hold, and that what a builder placed is actually in the scene.
    /// </summary>
    public static class SectionVerify
    {
        [MenuItem("Mini GTA/Verify/Cheat system", priority = 60)]
        public static void Cheats()
        {
            Debug.Log("[VERIFY] === Section 9: cheat codes ===");
            Debug.Log("[VERIFY] registered = " + CheatRegistry.Count + " (item 9 asks for 50+)");
            Debug.Log("[VERIFY] Available in this build = " + CheatRegistry.Available
                      + "   Enabled = " + CheatRegistry.Enabled);

            var counts = new System.Collections.Generic.Dictionary<CheatCategory, int>();
            var seen = new System.Collections.Generic.HashSet<string>();
            int duplicates = 0;

            foreach (var c in CheatRegistry.All)
            {
                counts.TryGetValue(c.Category, out int n);
                counts[c.Category] = n + 1;
                if (!seen.Add(c.Code)) duplicates++;
            }

            foreach (var kv in counts) Debug.Log("[VERIFY]   " + kv.Key + " : " + kv.Value);
            Debug.Log("[VERIFY] duplicate codes = " + duplicates + " (want 0)");

            // The gate is the whole point of 9.4, so it is tested rather than assumed.
            Debug.Log("[VERIFY] --- gate, cheats still disabled ---");
            Debug.Log("[VERIFY] HEAL          -> " + CheatRegistry.Try("HEAL"));
            Debug.Log("[VERIFY] NOT_A_CODE    -> " + CheatRegistry.Try("NOT_A_CODE"));

            CheatRegistry.SetEnabled(true);
            Debug.Log("[VERIFY] --- gate opened ---");
            Debug.Log("[VERIFY] NOT_A_CODE    -> " + CheatRegistry.Try("NOT_A_CODE")
                      + "   (want Unknown, proving the table is being consulted)");
            Debug.Log("[VERIFY] CHEAT_LIST    -> " + CheatRegistry.Try("CHEAT_LIST")
                      + "   (want Applied; it needs nothing from the scene)");
            Debug.Log("[VERIFY] HEAL          -> " + CheatRegistry.Try("HEAL")
                      + "   (want Failed in edit mode: no player to heal)");

            CheatRegistry.SetEnabled(false);
            Debug.Log("[VERIFY] re-locked. Enabled = " + CheatRegistry.Enabled);
        }

        [MenuItem("Mini GTA/Verify/Build and audit harbour", priority = 61)]
        public static void Harbor()
        {
            Debug.Log("[VERIFY] === Section 6: harbour ===");
            if (!OpenGameScene()) return;

            HarborBuilder.Build();

            var harbour = GameObject.Find("Harbor");
            if (harbour != null)
            {
                UrpMaterialFixer.Audit(harbour, "harbour before");
                UrpMaterialFixer.Fix(harbour);
                UrpMaterialFixer.Audit(harbour, "harbour after");
            }

            HarborBuilder.Audit();
            CaptureHarbour();
        }

        /// <summary>
        /// Renders the harbour to a file so it can actually be looked at.
        ///
        /// The standing instruction after BUG-023 and BUG-024 is that anything with a visual
        /// component is not PASS on logs alone. Both of those bugs reported healthy counters
        /// while the screen was wrong, and this section is entirely about whether a thing
        /// looks like a ship you could board.
        ///
        /// Edit-mode capture, because Play mode cannot be driven unattended here. A camera
        /// renders to a RenderTexture perfectly well outside Play mode, so the frame is real
        /// even though nothing is simulating.
        /// </summary>
        static void CaptureHarbour()
        {
            var root = GameObject.Find("Harbor");
            if (root == null) { Debug.LogWarning("[VERIFY] no harbour to capture"); return; }

            var ship = root.transform.Find("Ship_Explorable");
            if (ship == null) { Debug.LogWarning("[VERIFY] no ship to capture"); return; }

            var b = new Bounds(ship.position, Vector3.zero);
            foreach (var r in ship.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);

            // Framed from the object's own size rather than a guessed distance, so the shot
            // stays useful if the scale is changed again.
            float span = Mathf.Max(b.size.x, b.size.z, 8f);
            Vector3 from = b.center + new Vector3(span * 1.1f, span * 0.75f, span * 1.1f);

            Debug.Log("[VERIFY] ship world size = " + b.size.ToString("F1")
                      + "   (a 1.8 m player should look small against this)");
            Debug.Log("[VERIFY] captured -> "
                      + PhaseCapture.ShotFrom("phase14_harbor", from, b.center, 1600, 900));
        }

        /// <summary>
        /// Opens City.unity.
        ///
        /// Batch mode starts on an empty untitled scene, not on the game -- which is why the
        /// first harbour run reported "no WaterVolume in the scene" and built nothing. It was
        /// looking at an empty scene and saying so correctly; the caller was the one making an
        /// assumption. Anything that touches scene contents from batch mode has to open the
        /// scene itself.
        /// </summary>
        static bool OpenGameScene()
        {
            const string path = AndroidBuild.GameScene;

            var active = EditorSceneManager.GetActiveScene();
            if (active.path == path)
            {
                Debug.Log("[VERIFY] game scene already open");
                return true;
            }

            if (!System.IO.File.Exists(path))
            {
                Debug.LogError("[VERIFY] game scene missing: " + path);
                return false;
            }

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Debug.Log("[VERIFY] opened " + scene.path
                      + "  rootObjects=" + scene.rootCount);
            return scene.IsValid();
        }

        [MenuItem("Mini GTA/Verify/Icons", priority = 62)]
        public static void Icons()
        {
            Debug.Log("[VERIFY] === Section 8: icons ===");
            IconBuilder.Audit();
            IconBuilder.ClearBorrowed();
            IconBuilder.Audit();
            IconBuilder.PrintSpec();
        }

        /// <summary>Everything that can be checked without Play mode, in one batch run.</summary>
        public static void All()
        {
            Cheats();
            Icons();
            Harbor();
        }
    }
}
