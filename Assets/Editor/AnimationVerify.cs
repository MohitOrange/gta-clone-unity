using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Play-mode sweep over the masked animation layers. Section 2.6.
    ///
    /// <b>Why this exists as a tool rather than a one-off check.</b> This project has shipped
    /// the same defect twice -- an Override layer at weight above zero whose active state has
    /// no motion, which writes the humanoid zero pose over every masked bone and puts the
    /// character into the arms-forward "zombie" stance (Phase 9b-fix) or the T-pose (BUG-023).
    /// Both times it passed every parameter assertion, because the assertion that would have
    /// caught it reads as healthy: <c>GetCurrentAnimatorClipInfoCount</c> returns 0, which was
    /// read as "this layer contributes nothing" when it is in fact the signature of the bug.
    ///
    /// So that exact condition is what this counts: frames where a masked layer carried weight
    /// while having no clip to play. The pass mark is zero, on every rig, over a whole sweep.
    ///
    /// It also bounds the per-frame weight change. "No popping" is otherwise a matter of
    /// opinion; here it has a number. <see cref="MiniGTA.PlayerAnimation"/> ramps every layer
    /// with MoveTowards at <c>deltaTime / LayerFade</c>, so any single-frame delta materially
    /// above that ceiling is a discontinuity -- a layer snapped rather than faded.
    ///
    /// Sampling runs off EditorApplication.update because no frames elapse inside one MCP
    /// command, so an instantaneous read can only ever catch a defect it happens to land on.
    /// </summary>
    public static class AnimationVerify
    {
        const string ControllerName = "PlayerLocomotion";
        const float WeightEpsilon = 0.01f;   // below this a layer writes nothing worth seeing
        const int MaxRigs = 60;              // a sweep, not a census; the crowd can be 1000
        const int RescanEvery = 30;          // frames; the crowd spawns in over time

        static bool _running;
        static int _frames;
        static int _rescanCountdown;

        static readonly List<Animator> _rigs = new List<Animator>();
        static readonly Dictionary<Animator, float> _prevAim = new Dictionary<Animator, float>();
        static readonly Dictionary<Animator, float> _prevUpper = new Dictionary<Animator, float>();

        // Findings
        static int _emptyAtWeight;           // the defect: weight carried with no clip
        static string _emptyWorst = "";
        static float _maxAimStep, _maxUpperStep;
        static float _stepCeiling;           // largest legitimate per-frame ramp seen
        static float _aimMax, _upperMax;
        static int _framesAimUpWhileMoving;  // 2.6's actual claim: stance blended over locomotion
        static int _framesAimUp;
        static float _fastestBaseSpeedWithAimUp;
        static readonly HashSet<string> _baseClipsWithAimUp = new HashSet<string>();
        static int _rigsSeen;

        // The player has to be sampled by name, not hoped for. The crowd is 300-1000 rigs on
        // the same controller and the cap below is 60, so a scan that simply takes the first
        // matches will sample sixty unarmed pedestrians, see an aim layer that is legitimately
        // flat at zero on every one of them, and report a confident PASS having never once
        // looked at the only character that carries a weapon. That happened on the first run of
        // this tool. A sweep that cannot say it saw the subject does not get to return PASS.
        static Animator _player;
        static int _playerFrames;
        static float _playerAimMax;

        // States that would genuinely write the humanoid zero pose if their layer carried
        // weight: no motion AND writeDefaultValues on. Resolved from the controller asset at
        // sweep start rather than guessed from runtime symptoms.
        //
        // The first version of this tool tested "layer has weight and no clip is playing",
        // which is the signature the 9b-fix wrote down -- and it reported 55 defect frames on a
        // rig that was demonstrably fine. Pinning the one-shot layer at full weight on its
        // empty resting state and photographing the result settled it: with
        // writeDefaultValues OFF an empty state contributes nothing at all, and this project
        // deliberately builds its resting state that way. The signature is necessary but not
        // sufficient; writeDefaultValues is the actual cause and is what gets tested here.
        static readonly HashSet<string> _unsafeStates = new HashSet<string>();
        static int _emptyButSafe;

        [MenuItem("Mini GTA/Verify/Animation stance sweep - Start", priority = 70)]
        public static void StartSweep()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[AnimVerify] Enter play mode first. Layer weights are driven in "
                               + "LateUpdate, which does not run in edit mode -- an edit-mode "
                               + "sweep would report a clean pass on a broken rig.");
                return;
            }

            ResetCounters();
            BuildUnsafeStateTable();
            _running = true;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log("[AnimVerify] Sweep started. Let the game run, then call Report.");
        }

        [MenuItem("Mini GTA/Verify/Animation stance sweep - Report", priority = 71)]
        public static void ReportSweep()
        {
            EditorApplication.update -= Tick;
            _running = false;

            if (_frames == 0)
            {
                Debug.LogWarning("[AnimVerify] No frames sampled. Was the sweep started, and did "
                                 + "time actually advance? Time.timeScale is 0 while any menu is "
                                 + "stacked -- call MenuState.ForceClear() first.");
                return;
            }

            bool sawPlayer = _playerFrames > 0;
            bool sawStance = _framesAimUpWhileMoving > 0;
            bool clean = _emptyAtWeight == 0;

            // Three separate questions, reported separately. "No defect found" is only a pass
            // if the sweep can also show it was looking at an armed player with the stance up
            // over live locomotion; otherwise it is INCONCLUSIVE, which is not a pass.
            string verdict = !clean ? "FAIL"
                           : (!sawPlayer || !sawStance) ? "INCONCLUSIVE - nothing to conclude from"
                           : "PASS";

            Debug.Log("[AnimVerify] === Section 2.6 stance sweep ===\n"
                + "  frames sampled       : " + _frames + " over " + _rigsSeen + " rigs\n"
                + "  player sampled       : " + (sawPlayer ? "yes, " + _playerFrames + " frames, peak AimPose " + _playerAimMax.ToString("0.00")
                       : "NO -- the subject was never looked at") + "\n"
                + "  EMPTY-STATE-AT-WEIGHT: " + _emptyAtWeight + "   <-- zombie/T-pose defect; pass = 0"
                + (_emptyAtWeight > 0 ? "\n      worst: " + _emptyWorst : "") + "\n"
                + "  empty-but-harmless   : " + _emptyButSafe
                + " frames (no motion, writeDefaultValues off -- contributes nothing)\n"
                + "  unsafe states known  : " + _unsafeStates.Count + "\n"
                + "  peak layer weight    : AimPose " + _aimMax.ToString("0.00")
                + " / UpperBody " + _upperMax.ToString("0.00") + "\n"
                + "  max per-frame ramp   : AimPose " + _maxAimStep.ToString("0.000")
                + " / UpperBody " + _maxUpperStep.ToString("0.000")
                + "   (legit ceiling " + _stepCeiling.ToString("0.000") + ")\n"
                + "  aim stance up        : " + _framesAimUp + " frames, of which "
                + _framesAimUpWhileMoving + " while the legs were moving\n"
                + "  fastest locomotion under the stance: "
                + _fastestBaseSpeedWithAimUp.ToString("0.00") + " (speed01)\n"
                + "  base clips under the stance: "
                + (_baseClipsWithAimUp.Count == 0 ? "(none seen)"
                   : string.Join(", ", new List<string>(_baseClipsWithAimUp)))
                + "\n  VERDICT: " + verdict);
        }

        static void ResetCounters()
        {
            _frames = 0; _rescanCountdown = 0; _rigsSeen = 0;
            _emptyAtWeight = 0; _emptyWorst = "";
            _maxAimStep = _maxUpperStep = _stepCeiling = 0f;
            _aimMax = _upperMax = 0f;
            _framesAimUp = _framesAimUpWhileMoving = 0;
            _fastestBaseSpeedWithAimUp = 0f;
            _rigs.Clear(); _prevAim.Clear(); _prevUpper.Clear();
            _baseClipsWithAimUp.Clear();
            _player = null; _playerFrames = 0; _playerAimMax = 0f;
            _unsafeStates.Clear(); _emptyButSafe = 0;
        }

        static void Tick()
        {
            if (!_running) return;
            if (!EditorApplication.isPlaying)
            {
                _running = false;
                EditorApplication.update -= Tick;
                return;
            }

            if (_rescanCountdown-- <= 0) { Rescan(); _rescanCountdown = RescanEvery; }
            if (_rigs.Count == 0) return;

            _frames++;

            // The legitimate ceiling on one frame's ramp, taken from PlayerAnimation's own fade
            // rather than typed here, so re-tuning the fade cannot silently invalidate the test.
            float fade = 0.12f;
            var driver = Object.FindAnyObjectByType<MiniGTA.PlayerAnimation>();
            if (driver != null && driver.LayerFade > 0.0001f) fade = driver.LayerFade;
            float ceiling = Mathf.Min(1f, Time.deltaTime / fade) + 0.02f;   // float slack
            if (ceiling > _stepCeiling) _stepCeiling = ceiling;

            for (int i = 0; i < _rigs.Count; i++)
            {
                var a = _rigs[i];
                if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;

                Sample(a, "AimPose", _prevAim, ref _maxAimStep, ref _aimMax);
                Sample(a, "UpperBody", _prevUpper, ref _maxUpperStep, ref _upperMax);

                if (a == _player)
                {
                    _playerFrames++;
                    int pl = a.GetLayerIndex("AimPose");
                    if (pl >= 0)
                    {
                        float pw = a.GetLayerWeight(pl);
                        if (pw > _playerAimMax) _playerAimMax = pw;
                    }
                }

                // 2.6's actual claim: the stance rides on top of locomotion, not instead of it.
                int aim = a.GetLayerIndex("AimPose");
                if (aim < 0 || a.GetLayerWeight(aim) <= 0.5f) continue;

                _framesAimUp++;

                float speed01 = 0f;
                foreach (var p in a.parameters)
                    if (p.name == "Speed") { speed01 = a.GetFloat("Speed"); break; }

                if (speed01 <= 0.05f) continue;

                _framesAimUpWhileMoving++;
                if (speed01 > _fastestBaseSpeedWithAimUp) _fastestBaseSpeedWithAimUp = speed01;

                var info = a.GetCurrentAnimatorClipInfo(0);
                if (info.Length > 0 && info[0].clip != null) _baseClipsWithAimUp.Add(info[0].clip.name);
            }
        }

        /// <summary>One layer, one frame. Counts the defect and bounds the ramp.</summary>
        static void Sample(Animator a, string layerName, Dictionary<Animator, float> prev,
                           ref float maxStep, ref float maxWeight)
        {
            int layer = a.GetLayerIndex(layerName);
            if (layer < 0) return;

            float w = a.GetLayerWeight(layer);
            if (w > maxWeight) maxWeight = w;

            // THE CHECK. Transitions are excused: mid-transition the clip info legitimately
            // empties out. An empty state only writes the zero pose when writeDefaultValues is
            // on, so that -- not the empty clip list -- is what decides defect vs. benign.
            if (w > WeightEpsilon && a.GetCurrentAnimatorClipInfoCount(layer) == 0
                && !a.IsInTransition(layer))
            {
                var st = a.GetCurrentAnimatorStateInfo(layer);
                if (_unsafeStates.Contains(layerName + ":" + st.shortNameHash))
                {
                    _emptyAtWeight++;
                    _emptyWorst = a.gameObject.name + " layer '" + layerName + "' w="
                                  + w.ToString("0.00") + " stateHash=" + st.shortNameHash;
                }
                else
                {
                    _emptyButSafe++;
                }
            }

            float p;
            if (prev.TryGetValue(a, out p))
            {
                float step = Mathf.Abs(w - p);
                if (step > maxStep) maxStep = step;
            }
            prev[a] = w;
        }

        /// <summary>
        /// Reads the controller asset and records which states would actually write the zero
        /// pose if their layer carried weight: no motion, and writeDefaultValues on.
        ///
        /// Done from the asset because the runtime AnimatorStateInfo does not expose
        /// writeDefaultValues, and that flag is the whole difference between the defect and the
        /// configuration this project ships on purpose.
        /// </summary>
        static void BuildUnsafeStateTable()
        {
            _unsafeStates.Clear();

            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                "Assets/Game/Animator/PlayerLocomotion.controller");
            if (ac == null)
            {
                Debug.LogWarning("[AnimVerify] Controller asset not found; the defect check "
                                 + "cannot distinguish a harmful empty state from a harmless "
                                 + "one and will report every empty state as safe.");
                return;
            }

            foreach (var layer in ac.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var cs in layer.stateMachine.states)
                {
                    if (cs.state.motion != null) continue;
                    if (!cs.state.writeDefaultValues) continue;
                    _unsafeStates.Add(layer.name + ":" + Animator.StringToHash(cs.state.name));
                    Debug.LogWarning("[AnimVerify] '" + layer.name + "/" + cs.state.name
                                     + "' has no motion and writeDefaultValues ON. If its layer "
                                     + "ever carries weight it writes the humanoid zero pose.");
                }
            }
        }

        static void Rescan()
        {
            _rigs.Clear();

            // The player goes in first and unconditionally. See the note on _player above.
            if (_player == null)
            {
                var combat = Object.FindAnyObjectByType<MiniGTA.PlayerCombat>();
                if (combat != null)
                {
                    var pa = combat.GetComponentInChildren<MiniGTA.PlayerAnimation>();
                    if (pa != null) _player = pa.Animator;
                }
            }
            if (_player != null && _player.runtimeAnimatorController != null) _rigs.Add(_player);

            var all = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude);
            foreach (var a in all)
            {
                if (a == _player) continue;
                if (a.runtimeAnimatorController == null) continue;
                if (a.runtimeAnimatorController.name != ControllerName) continue;
                _rigs.Add(a);
                if (_rigs.Count >= MaxRigs) break;
            }
            if (_rigs.Count > _rigsSeen) _rigsSeen = _rigs.Count;
        }
    }
}
