using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Reports real frame rate and crowd state on a line that survives the trip to a phone.
    ///
    /// This exists because every performance number in Phase 14 so far is an Editor number on
    /// a desktop, and CLAUDE.md §5 is explicit that this does not count -- the project has
    /// already been burned once by treating Editor results as verification. The Editor also
    /// flatters: it has no IL2CPP, no mobile GPU, no thermal ceiling, and it renders at a
    /// desktop resolution.
    ///
    /// It logs rather than draws, so the numbers can be read straight out of `adb logcat`
    /// without needing a screenshot to be legible, and so reading them costs nothing on the
    /// device itself.
    ///
    /// Development builds and the Editor only. It compiles out of a release build entirely
    /// rather than merely going quiet, so there is no measurement scaffolding left running in
    /// a shipping APK.
    /// </summary>
    public class CrowdBenchmark : MonoBehaviour
    {
        [Tooltip("Seconds of frames averaged into each report.")]
        public float WindowSeconds = 5f;

        [Tooltip("Seconds to ignore at startup, while the crowd is still being built and the "
                 + "shader cache is still warming. Including them would report a frame rate "
                 + "the game never actually runs at.")]
        public float WarmupSeconds = 12f;

        /// <summary>Prefix every report carries, so `adb logcat` can filter on it.</summary>
        public const string Tag = "[BENCH]";

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        int _frames;
        float _elapsed;
        float _worst;
        float _warmup;
        int _reports;

        // ---------------------------------------------------------------- unattended run
        //
        // Driven from the command line so a measurement can be taken with no one at the
        // keyboard. The device is unavailable and the Editor's MCP bridge is blocked behind a
        // connection-approval prompt that needs a human click, so the only remaining way to
        // get a play-mode number is to let the game measure itself and quit.
        //
        // Numbers produced this way are Editor batch-mode numbers on a desktop. They are not
        // device numbers and every report says so.

        bool _auto;
        float _autoRemaining;
        float _autoWorst;
        int _autoFrames;
        float _autoElapsed;

        const string AutoArg = "-benchSeconds";

        void Start()
        {
            float seconds = ParseAutoSeconds();
            if (seconds <= 0f) return;

            _auto = true;
            _autoRemaining = seconds + WarmupSeconds;

            StageForMeasurement();

            Debug.Log(Tag + " AUTO-RUN begin: " + seconds.ToString("F0") + "s of sampling after "
                      + WarmupSeconds.ToString("F0") + "s warm-up. EDITOR BATCH MODE, NOT A DEVICE.");
        }

        static float ParseAutoSeconds()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == AutoArg && float.TryParse(args[i + 1], out float v)) return v;
            return 0f;
        }

        /// <summary>
        /// Puts the game into the state the measurement is supposed to describe.
        ///
        /// Without this the run measures the lobby: the menu holds Time.timeScale at zero and
        /// the main camera's culling mask at zero while the character stage is shown, so
        /// nothing animates and nothing renders. That is the trap that produced two false
        /// negatives during BUG-012, and an unattended run has nobody to notice it.
        /// </summary>
        void StageForMeasurement()
        {
            MenuState.ForceClear();
            if (Time.timeScale <= 0f) Time.timeScale = 1f;

            var stage = GameObject.Find("StageCamera");
            if (stage != null) stage.SetActive(false);

            var cam = Camera.main;
            if (cam != null) cam.cullingMask = ~0;

            var tuner = PerformanceTuner.Instance;
            if (tuner != null) tuner.Apply(QualityTier.High);

            // Uncapped: a capped reading measures the cap, not the load.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            // Put the player somewhere with roads, so the crowd lands on pavements rather
            // than scattering across open sand.
            var net = RoadNetwork.Instance;
            var player = GameObject.FindWithTag("Player");
            if (net != null && net.NodeCount > 0 && player != null)
            {
                var node = net.GetNode(net.NodeCount / 3);
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = node.Position + Vector3.up * 1.5f;
                if (cc != null) cc.enabled = true;
            }

            var crowd = FindAnyObjectByType<CrowdDirector>();
            if (crowd != null)
            {
                crowd.SpawnsPerFrame = 20;
                Debug.Log(Tag + " staged: crowd target = " + crowd.TargetPopulation);
            }
        }

        void AutoTick(float dt)
        {
            _autoRemaining -= dt;

            if (_warmup >= WarmupSeconds)
            {
                _autoFrames++;
                _autoElapsed += dt;
                if (dt > _autoWorst) _autoWorst = dt;
            }

            if (_autoRemaining > 0f) return;

            float fps = _autoFrames / Mathf.Max(0.0001f, _autoElapsed);

            Debug.Log(Tag + " AUTO-RUN SUMMARY"
                      + " pop=" + Pedestrian.Witnesses.Count
                      + " frames=" + _autoFrames
                      + " seconds=" + _autoElapsed.ToString("F1")
                      + " avgFps=" + fps.ToString("F1")
                      + " avgMs=" + (1000f * _autoElapsed / Mathf.Max(1, _autoFrames)).ToString("F2")
                      + " worstMs=" + (_autoWorst * 1000f).ToString("F1"));
            Debug.Log(Tag + " AUTO-RUN provenance: Editor batch mode, desktop. "
                      + "NOT an on-device measurement. Treat as provisional.");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (_auto) AutoTick(dt);

            if (_warmup < WarmupSeconds)
            {
                _warmup += dt;
                return;
            }

            _frames++;
            _elapsed += dt;
            if (dt > _worst) _worst = dt;

            if (_elapsed < WindowSeconds) return;

            Report();

            _frames = 0;
            _elapsed = 0f;
            _worst = 0f;
        }

        void Report()
        {
            float fps = _frames / Mathf.Max(0.0001f, _elapsed);
            float avgMs = 1000f * _elapsed / Mathf.Max(1, _frames);
            float worstMs = _worst * 1000f;

            int population = Pedestrian.Witnesses.Count;

            var dir = CrowdDirectorOrNull();
            string bands = dir != null
                ? dir.FullCount + "/" + dir.SimpleCount + "/" + dir.FrozenCount
                : "n/a";

            // The worst frame in the window matters as much as the average: a phone that
            // averages 30 fps with a 90 ms spike every few seconds reads as broken, and an
            // average alone hides that completely.
            Debug.Log(Tag
                      + " report=" + (++_reports)
                      + " pop=" + population
                      + " bands=" + bands
                      + " fps=" + fps.ToString("F1")
                      + " avgMs=" + avgMs.ToString("F2")
                      + " worstMs=" + worstMs.ToString("F1")
                      + " target=" + Application.targetFrameRate
                      + " drawn=" + DrawnCount());
        }

        static CrowdDirector CrowdDirectorOrNull() => FindAnyObjectByType<CrowdDirector>();

        /// <summary>
        /// Pedestrians with at least one renderer enabled -- what the GPU is actually being
        /// asked for, as opposed to how many exist.
        /// </summary>
        static int DrawnCount()
        {
            int n = 0;
            var all = Pedestrian.Witnesses;

            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null) continue;

                var renderers = p.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                    if (renderers[r].enabled) { n++; break; }
            }
            return n;
        }
#endif
    }
}
