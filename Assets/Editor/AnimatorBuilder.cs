using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Generates the shared character AnimatorController: a 1D locomotion blend tree plus
    /// airborne, landing, swimming and seated states, with a masked upper-body layer carrying
    /// the combat one-shots.
    ///
    /// One controller drives the player, the police and the whole crowd, so a change here is a
    /// change everywhere. It is generated rather than hand-authored so the wiring can be
    /// reviewed as code; the output is a normal .controller asset and nothing in the runtime
    /// depends on how it is wired beyond the parameter names.
    ///
    /// Phase 9b replaced the clip source. Everything except the swim clip now comes from the
    /// Kevin Iglesias human pack (locomotion, gunplay, damage) and the EEJANAI fighter pack
    /// (melee). Both are Humanoid with `humanMotion` clips, so they retarget onto the
    /// Apocalyptic Survivor rig and the Mixamo NPC bodies through the same avatar path the
    /// Mixamo clips used -- verified by sampling, see PHASE9B.md.
    /// </summary>
    public static class AnimatorBuilder
    {
        const string OutPath = "Assets/Game/Animator/PlayerLocomotion.controller";

        const string KI = "Assets/Kevin Iglesias/Human Animations/Animations/Male";
        const string Movement = KI + "/Movement";
        const string MaskedPoses = "Assets/Kevin Iglesias/Human Animations/Animations/Masked Poses";
        const string Fighter = "Assets/EEJANAI_Team/FreeFighterAnimations/Animations";

        /// <summary>Layer names PlayerAnimation looks up to drive the weights.</summary>
        public const string UpperBodyLayerName = "UpperBody";
        public const string HandsLayerName = "Hands";
        /// <summary>Section 2.6. Held weapon stance, blended under the one-shot layer.</summary>
        public const string AimPoseLayerName = "AimPose";

        /// <summary>
        /// The one clip still sourced from Mixamo.
        ///
        /// Neither new pack contains a swimming animation. Keeping this one is the single
        /// exception to the Phase 9b clean replacement, and it is deliberate rather than
        /// leftover -- see DECISIONS.md D14.
        /// </summary>
        const string SwimClip = "Assets/animations/Swimming.fbx";

        // --- Locomotion blend thresholds -----------------------------------------------
        //
        // Derived, not eyeballed. Each clip's authored ground speed was measured by sampling
        // its [RM] variant start-to-end (Walk 2.00, Run 4.00, Sprint 6.00 m/s). PlayerController
        // encodes speed01 as 0 -> 0 m/s, 0.5 -> WalkSpeed (2.2), 1.0 -> RunSpeed (5.8). Placing
        // each clip at the speed01 where the character genuinely travels at that clip's authored
        // speed is what stops the feet sliding:
        //
        //   Walk   2.0 m/s -> 2.0 / (2 x 2.2)                = 0.455
        //   Run    4.0 m/s -> 0.5 + (4.0 - 2.2) / (2 x 3.6)  = 0.750
        //   Sprint 6.0 m/s -> 0.5 + (6.0 - 2.2) / (2 x 3.6)  = 1.028, clamped to 1.0
        //
        // Sprint is the only inexact one: the game tops out at 5.8 m/s against the clip's 6.0,
        // a 3% overrun that is not visible. If PlayerController's speeds change, recompute
        // these -- they are tied to WalkSpeed and RunSpeed, not to taste.
        const float WalkThreshold = 0.455f;
        const float RunThreshold = 0.750f;
        const float SprintThreshold = 1.000f;

        // --- Combat clip playback rates -------------------------------------------------
        //
        // Each attack clip is played at the rate that makes its useful length match the
        // cooldown PlayerCombat already gates it by, so the animation finishes exactly as the
        // player regains the ability to attack again. Neither number is arbitrary:
        //   melee : 1.17 s / 1.5 x 0.70 exit ~= 0.55 s = PlayerCombat.MeleeCooldown
        //   shoot : 0.80 s / 1.6 x 0.60 exit  = 0.30 s = PlayerCombat.PistolCooldown
        /// <summary>
        /// Playback speed of the punch.
        ///
        /// 2.4 is derived, not tuned by eye: "charge fist" is 2.00 s long and its strike peaks
        /// at t=0.65, so at 2.4x the fist reaches full extension 0.54 s after the button --
        /// which is PlayerCombat.MeleeCooldown (0.55 s). The punch therefore lands exactly as
        /// the next one becomes legal, instead of the player being able to throw three before
        /// the first one visually connects.
        /// </summary>
        const float MeleeSpeed = 2.4f;
        const float ShootSpeed = 1.6f;

        [MenuItem("Tools/Mini GTA/5. Build Player Animator", priority = 112)]
        public static AnimatorController Build()
        {
            System.IO.Directory.CreateDirectory("Assets/Game/Animator");

            var idle = Clip(KI + "/Idles/HumanM@Idle01.fbx");
            var walk = Clip(KI + "/Movement/Walk/HumanM@Walk01_Forward.fbx");
            var run = Clip(KI + "/Movement/Run/HumanM@Run01_Forward.fbx");
            var sprint = Clip(KI + "/Movement/Sprint/HumanM@Sprint01_Forward.fbx");
            var jump = Clip(KI + "/Movement/Jump/HumanM@Jump01 - Begin.fbx");
            var fall = Clip(KI + "/Movement/Jump/HumanM@Fall01.fbx");
            var land = Clip(KI + "/Movement/Jump/HumanM@Jump01 - Land.fbx");
            var swim = Clip(SwimClip);
            // No seated clip exists in either pack. A braced standing idle is the closest
            // reasonable substitute and is only ever visible on the bike and the boat, because
            // CarController hides the occupant. See DECISIONS.md D14.
            var sit = Clip(KI + "/Idles/HumanM@MilitaryIdle01.fbx");

            if (idle == null || walk == null || run == null || sprint == null)
            {
                Debug.LogError("[Animator] Missing a core locomotion clip from "
                               + KI + ". Is the Kevin Iglesias pack imported?");
                return null;
            }

            AssetDatabase.DeleteAsset(OutPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(OutPath);

            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            // Section 7: local-space movement direction, for the 2D locomotion blend.
            ac.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ac.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            // Turn-in-place.
            ac.AddParameter("Turn", AnimatorControllerParameterType.Float);
            // Idle variation, randomised per character so a crowd does not breathe in unison.
            ac.AddParameter("IdleVariant", AnimatorControllerParameterType.Float);
            ac.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ac.AddParameter("InWater", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("InVehicle", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Punch", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            // Phase 14 Section 2.
            ac.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            // Section 10: NPC conversation gesture.
            ac.AddParameter("Talk", AnimatorControllerParameterType.Trigger);
            // Section 11: which weapon stance the held weapon uses.
            ac.AddParameter("Stance", AnimatorControllerParameterType.Float);

            SetDefault(ac, "Grounded", true);

            var sm = ac.layers[0].stateMachine;

            // --- Locomotion: speed outside, direction inside ----------------------------
            //
            // A 1D tree on Speed whose Walk / Run / Sprint children are themselves 2D
            // directional trees on MoveX / MoveY.
            //
            // <b>What this fixes.</b> The old tree was Speed only, with one forward clip per
            // tier, so moving sideways or backwards played the forward walk cycle while the
            // character slid in another direction. The pack has shipped the full directional
            // set since Phase 9b and 21 of those clips had never been referenced by anything.
            //
            // Speed stays on the outside because it is the axis the thresholds were *measured*
            // against in Phase 9b (each clip's authored speed read off its [RM] twin, placed at
            // the speed01 where the character genuinely travels that fast). Putting direction
            // outside would throw those measurements away.
            var locomotion = ac.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            BuildIdleSlot(tree);

            Directional(tree, "Walk", WalkThreshold, Movement + "/Walk/HumanM@Walk01_", true);
            Directional(tree, "Run", RunThreshold, Movement + "/Run/HumanM@Run01_", true);
            // Sprint ships no backward set, which is correct -- nobody sprints backwards.
            Directional(tree, "Sprint", SprintThreshold, Movement + "/Sprint/HumanM@Sprint01_", false);

            sm.defaultState = locomotion;

            // --- Airborne / swim states ------------------------------------------------
            var jumpState = AddState(sm, "Jump", jump, new Vector3(320, -60, 0));
            var fallState = AddState(sm, "Fall", fall, new Vector3(320, 40, 0));
            var landState = AddState(sm, "Land", land, new Vector3(60, 140, 0));
            var swimState = AddState(sm, "Swim", swim, new Vector3(-220, 40, 0));

            // Jump: fired by trigger, only from the ground.
            var toJump = locomotion.AddTransition(jumpState);
            toJump.hasExitTime = false;
            toJump.duration = 0.06f;
            toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

            // Walking off a ledge drops straight into the fall loop.
            var toFall = locomotion.AddTransition(fallState);
            toFall.hasExitTime = false;
            toFall.duration = 0.15f;
            toFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            // Apex of the jump hands over to the fall loop.
            var jumpToFall = jumpState.AddTransition(fallState);
            jumpToFall.hasExitTime = false;
            jumpToFall.duration = 0.12f;
            jumpToFall.AddCondition(AnimatorConditionMode.Less, 0f, "VerticalSpeed");

            // Safety net: if the jump clip ends while still airborne, do not stall in it.
            // The new takeoff clip is 0.67 s where the Mixamo one was 1.90 s, so this fires
            // much sooner -- which is why Fall01 has to be a clean loop, and it is.
            var jumpEnd = jumpState.AddTransition(fallState);
            jumpEnd.hasExitTime = true;
            jumpEnd.exitTime = 0.90f;
            jumpEnd.duration = 0.12f;

            var fallToLand = fallState.AddTransition(landState);
            fallToLand.hasExitTime = false;
            fallToLand.duration = 0.08f;
            fallToLand.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            // Leave the landing clip early -- playing it out feels sluggish on a phone.
            var landToLoco = landState.AddTransition(locomotion);
            landToLoco.hasExitTime = true;
            landToLoco.exitTime = 0.55f;
            landToLoco.duration = 0.18f;

            // Bail out of landing immediately if the player is already running again.
            var landRunOut = landState.AddTransition(locomotion);
            landRunOut.hasExitTime = false;
            landRunOut.duration = 0.12f;
            landRunOut.AddCondition(AnimatorConditionMode.Greater, 0.35f, "Speed");

            // --- Swimming overrides everything ------------------------------------------
            var anyToSwim = sm.AddAnyStateTransition(swimState);
            anyToSwim.hasExitTime = false;
            anyToSwim.duration = 0.25f;
            anyToSwim.canTransitionToSelf = false;
            anyToSwim.AddCondition(AnimatorConditionMode.If, 0f, "InWater");

            var swimOut = swimState.AddTransition(locomotion);
            swimOut.hasExitTime = false;
            swimOut.duration = 0.25f;
            swimOut.AddCondition(AnimatorConditionMode.IfNot, 0f, "InWater");

            // --- Seated in a vehicle, highest priority of all ---------------------------
            var sitState = AddState(sm, "Sit", sit, new Vector3(-220, 150, 0));

            var anyToSit = sm.AddAnyStateTransition(sitState);
            anyToSit.hasExitTime = false;
            anyToSit.duration = 0.2f;
            anyToSit.canTransitionToSelf = false;
            anyToSit.AddCondition(AnimatorConditionMode.If, 0f, "InVehicle");

            var sitOut = sitState.AddTransition(locomotion);
            sitOut.hasExitTime = false;
            sitOut.duration = 0.2f;
            sitOut.AddCondition(AnimatorConditionMode.IfNot, 0f, "InVehicle");

            // Swimming must not hijack a driver crossing water in a boat.
            anyToSwim.AddCondition(AnimatorConditionMode.IfNot, 0f, "InVehicle");

            // --- Death, above everything ------------------------------------------------
            //
            // BUG-012: a pedestrian punched to zero health carried on playing
            // HumanM@Walk01_Forward, verified live -- it died walking on the spot. The pack has
            // shipped Death01-03 since Phase 9b and nothing was ever wired to them.
            //
            // Full body, not the upper-body layer: a corpse does not keep running. No exit
            // transition either -- Health.Revive is what clears the bool, so the state is left
            // only by an explicit revive rather than by a clip ending.
            var death = Clip(KI + "/Combat/HumanM@Death01.fbx");
            var deathState = AddState(sm, "Death", death, new Vector3(-220, 240, 0));
            StopLooping(death);

            var anyToDeath = sm.AddAnyStateTransition(deathState);
            anyToDeath.hasExitTime = false;
            anyToDeath.duration = 0.12f;
            anyToDeath.canTransitionToSelf = false;
            anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

            var deathOut = deathState.AddTransition(locomotion);
            deathOut.hasExitTime = false;
            deathOut.duration = 0.2f;
            deathOut.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            // Layer order matters. The aim pose sits *under* the one-shots so that a shot, a
            // punch or a reload overrides the held stance rather than fighting it for the same
            // bones, and both sit under Hands so the grip always wins on the fingers.
            var upperMask = BuildUpperBodyMask();
            BuildAimPoseLayer(ac, upperMask);
            BuildUpperBodyLayer(ac, upperMask);
            BuildHandsLayer(ac);

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();

            Debug.Log("[Animator] Built " + OutPath
                      + "\n  Locomotion blend: Idle 0 / Walk " + WalkThreshold
                      + " / Run " + RunThreshold + " / Sprint " + SprintThreshold
                      + "\n  States: Jump, Fall, Land, Swim, Sit"
                      + "\n  AimPose (weight 0, driven): AimIdle"
                      + "\n  UpperBody (weight 0, driven): PunchUpper, ShootUpper, HitUpper, ReloadUpper"
                      + "\n  Hands (weight 0, driven): Grip"
                      + "\n  Clip source: Kevin Iglesias + EEJANAI; swim retained from Mixamo.");
            return ac;
        }

        /// <summary>Tag on the upper-body resting state. <see cref="PlayerAnimation"/> reads it.</summary>
        public const string RestTag = "Rest";

        /// <summary>
        /// Adds the masked upper-body layer carrying the combat one-shots.
        ///
        /// A separate layer rather than full-body states so the player can punch or fire while
        /// still running -- on a twin-stick mobile layout, freezing locomotion to play an
        /// attack would feel like the controls had stopped responding.
        ///
        /// <b>This layer ships at weight 0 and must stay that way until something plays.</b>
        /// An Override layer at weight 1 whose active state has no motion does not "contribute
        /// nothing" -- it writes the humanoid zero-muscle pose over every bone the mask covers,
        /// which here is the whole torso, head and both arms. That is what produced the
        /// arms-forward "zombie" pose on the player and on all sixteen pedestrians: the resting
        /// state was empty, the layer was at full weight, and the empty state won. The weight is
        /// now driven by <see cref="PlayerAnimation"/>, which raises it only while an action is
        /// actually playing.
        /// </summary>
        static void BuildUpperBodyLayer(AnimatorController ac, AvatarMask mask)
        {
            // "charge fist", not "back fist".
            //
            // The clip was wrong, and it is measurable rather than a matter of taste. Sampling
            // the right hand against the hips in the character's own facing, across the three
            // punch clips the pack ships (Z+ is where PlayerCombat puts the melee sphere):
            //
            //   t          0.0   0.1   0.2   0.3   0.4   0.5   0.6   0.7
            //   back fist -0.07  0.47  0.06 -0.46 -0.38 -0.38 -0.12 -0.06
            //   5 inch     0.36  0.28  0.17  0.12  0.08  0.01 -0.07 -0.18
            //   charge     0.36  0.30  0.17  0.13  0.13  0.13  0.61  0.73
            //
            // "back fist" flicks forward for a single frame and then spends the whole rest of
            // the clip BEHIND the character -- it is a spinning back-hand strike, which is what
            // the name says. "5 inch punch" only ever retracts. "charge fist" chambers the fist
            // to 0.13 and then drives it out to 0.73, which is the one trajectory that is a
            // forward punch.
            //
            // The hit detection was never the problem: PlayerCombat.MeleeForward() uses
            // transform.forward, so the sphere has always been in front (that was BUG-008).
            // The animation was striking away from the damage.
            var punch = Clip(Fighter + "/charge fist.anim");
            var shoot = Clip(KI + "/Combat/Gun/HumanM@Gun_Aim01_Shoot01.fbx");
            var hit = Clip(KI + "/Combat/HumanM@Damage01.fbx");
            // Section 2.5. Upper body, so a reload can be walked through.
            var reload = Clip(KI + "/Combat/Gun/HumanM@Gun_Reload01.fbx");
            // Section 10. Shipped since Phase 9b and referenced by nothing, so every
            // conversation in the game happened between two people standing perfectly still.
            var talk = Clip(KI + "/Social/Conversation/HumanM@Talk01.fbx");

            // The fighter pack ships every clip flagged as looping, including its single
            // strikes. A looping punch on a one-shot state is a stall waiting to happen if the
            // exit transition is ever interrupted, so the flag is corrected on the clip we use.
            StopLooping(punch);


            var sm = new AnimatorStateMachine
            {
                name = "UpperBody",
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(sm, ac);

            var none = sm.AddState("None", new Vector3(250, 0, 0));
            none.writeDefaultValues = false;
            none.tag = RestTag;
            sm.defaultState = none;

            var punchState = AddState(sm, "PunchUpper", punch, new Vector3(500, -80, 0));
            var shootState = AddState(sm, "ShootUpper", shoot, new Vector3(500, 0, 0));
            // Section 11. The alternate stance has its own fire and reload; the three carry
            // poses do not, so they fall back to the pistol clips rather than to nothing.
            shootState.motion = StanceTree(ac, "ShootStance", new[]
            {
                KI + "/Combat/Gun/HumanM@Gun_Aim01_Shoot01.fbx",
                KI + "/Combat/Gun/HumanM@Gun_Aim02_Shoot01.fbx",
            });
            var hitState = AddState(sm, "HitUpper", hit, new Vector3(500, 80, 0));
            var reloadState = AddState(sm, "ReloadUpper", reload, new Vector3(500, 160, 0));
            reloadState.motion = StanceTree(ac, "ReloadStance", new[]
            {
                KI + "/Combat/Gun/HumanM@Gun_Reload01.fbx",
                KI + "/Combat/Gun/HumanM@Gun_Reload02.fbx",
            });
            var talkState = AddState(sm, "TalkUpper", talk, new Vector3(500, 240, 0));

            punchState.speed = MeleeSpeed;
            shootState.speed = ShootSpeed;

            AddOneShot(sm, punchState, none, "Punch", 0.08f, 0.70f);
            AddOneShot(sm, shootState, none, "Shoot", 0.05f, 0.60f);
            AddOneShot(sm, hitState, none, "Hit", 0.06f, 0.80f);
            // Longer exit than the strikes: a reload that snaps away half-finished reads as a
            // dropped magazine. WeaponController holds the lock for the clip's full length.
            AddOneShot(sm, reloadState, none, "Reload", 0.10f, 0.92f);
            // Gentler in and out than a strike: a conversational gesture that snaps reads as a
            // twitch. Exits late so the hands settle before the arms drop.
            AddOneShot(sm, talkState, none, "Talk", 0.20f, 0.95f);

            ac.AddLayer(new AnimatorControllerLayer
            {
                name = UpperBodyLayerName,
                defaultWeight = 0f,      // see the note above; PlayerAnimation drives this
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = sm,
            });
        }

        /// <summary>
        /// Section 2.6. Adds the held-weapon stance: a masked upper-body aim pose that blends
        /// over whatever the legs are doing, so an armed character walks, runs and sprints
        /// normally while still carrying the weapon up.
        ///
        /// <b>Why this is its own layer rather than a state on UpperBody.</b> Phase 9b put an
        /// <c>ArmedIdle</c> state on the one-shot layer and it produced the zombie-arms report.
        /// The mechanism is worth stating exactly, because the obvious fix reintroduces it: an
        /// Override layer writes its active state over every masked bone at the layer's weight,
        /// and the one-shot layer's resting state is deliberately <i>empty</i>. Any scheme that
        /// raises that layer for a stance has to cross the empty state on the way in and on the
        /// way out, and every frame of that crossing is the humanoid zero pose fading in over
        /// the character's arms.
        ///
        /// A dedicated layer never holds an empty state -- the aim pose is the only thing on it
        /// -- so its weight can ramp from 0 to 1 and back with nothing to cross. That is the
        /// whole of "no popping or T-pose frames between states": there is no frame in which
        /// this layer has nothing to say.
        ///
        /// Ships at weight 0 like the others. <see cref="PlayerAnimation"/> raises it, and only
        /// while the character is both armed and actually in a firing stance -- holding the gun
        /// up permanently is the other half of what 9b-fix removed.
        /// </summary>
        static void BuildAimPoseLayer(AnimatorController ac, AvatarMask mask)
        {
            var aim = Clip(KI + "/Combat/Gun/HumanM@Gun_Aim01.fbx");
            if (aim == null)
            {
                Debug.LogWarning("[Animator] No aim pose found; skipping the AimPose layer. "
                                 + "Armed characters will carry the weapon with relaxed arms.");
                return;
            }

            // The stance is held for as long as the player stays in combat, so it has to loop.
            // A one-shot aim clip would play once and then hold its last frame, which happens to
            // look identical -- until someone re-authors the clip with a settle at the end.
            StartLooping(aim);

            var sm = new AnimatorStateMachine
            {
                name = AimPoseLayerName,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(sm, ac);

            // Section 11. A blend on Stance rather than one clip, so a rifle is carried like a
            // rifle. Everything previously reused the pistol aim, which is why a shotgun and a
            // sniper were both held one-handed at chest height.
            var aimState = sm.AddState("AimIdle", new Vector3(250, 0, 0));
            aimState.writeDefaultValues = false;
            aimState.motion = StanceTree(ac, "AimStance", new[]
            {
                KI + "/Combat/Gun/HumanM@Gun_Aim01.fbx",
                KI + "/Combat/Gun/HumanM@Gun_Aim02.fbx",
                MaskedPoses + "/HumanM@WeaponHold_Rifle01.fbx",
                MaskedPoses + "/HumanM@WeaponHold_AssaultRifle01.fbx",
                MaskedPoses + "/HumanM@WeaponHold_Bazooka01.fbx",
            });
            sm.defaultState = aimState;

            ac.AddLayer(new AnimatorControllerLayer
            {
                name = AimPoseLayerName,
                defaultWeight = 0f,      // driven by PlayerAnimation; see the note above
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = sm,
            });
        }

        /// <summary>
        /// Section 11. A 1D blend over the weapon stances, indexed by the Stance parameter.
        ///
        /// The three WeaponHold_* poses are the pack's own masked carry poses -- authored for
        /// exactly this, and referenced by nothing until now. A missing entry is skipped rather
        /// than fatal, and the blend simply has one fewer stop; a weapon pointing at a stance
        /// that was skipped falls between its neighbours, which looks odd but plays.
        /// </summary>
        static Motion StanceTree(AnimatorController ac, string name, string[] clipPaths)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Stance",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, ac);

            int added = 0;
            for (int i = 0; i < clipPaths.Length; i++)
            {
                var clip = Clip(clipPaths[i]);
                if (clip == null)
                {
                    Debug.LogWarning("[Animator] Stance clip missing, index " + i
                                     + " left empty: " + clipPaths[i]);
                    continue;
                }
                tree.AddChild(clip, i);
                added++;
            }

            if (added == 0)
            {
                Debug.LogError("[Animator] Stance tree '" + name + "' has no clips at all.");
                return null;
            }
            return tree;
        }

        /// <summary>
        /// Adds a fingers-only layer holding a closed grip, for whenever a weapon is out.
        ///
        /// This replaces Phase 9b's <c>ArmedIdle</c> state, which held a full two-handed gun
        /// <i>aim</i> pose on the upper-body layer for as long as the player was armed -- and
        /// the player is armed from the first frame. Walking around permanently aiming is the
        /// second half of the zombie-arms report.
        ///
        /// The intent behind that state was right: PHASE9 correctly flagged that the pistol sat
        /// in a relaxed open hand. But closing a hand around a grip is a job for the fingers,
        /// not for the shoulders. A fingers-only mask leaves the arms entirely to locomotion,
        /// so the character walks normally and still holds the weapon properly.
        /// </summary>
        static void BuildHandsLayer(AnimatorController ac)
        {
            var grip = Clip(MaskedPoses + "/Human@ObjectGripHands01.fbx")
                    ?? Clip(MaskedPoses + "/Human@HandsClosed01.fbx");
            if (grip == null)
            {
                Debug.LogWarning("[Animator] No grip pose found; skipping the Hands layer.");
                return;
            }

            var sm = new AnimatorStateMachine
            {
                name = UpperBodyLayerName + "Hands",
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(sm, ac);

            // One state, always current. The layer's weight is the switch, not a transition --
            // an empty state on an Override layer is exactly the trap this phase fixed.
            var gripState = AddState(sm, "Grip", grip, new Vector3(250, 0, 0));
            sm.defaultState = gripState;

            ac.AddLayer(new AnimatorControllerLayer
            {
                name = HandsLayerName,
                defaultWeight = 0f,      // PlayerAnimation raises it while armed
                avatarMask = BuildHandsMask(),
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = sm,
            });
        }

        /// <summary>
        /// One-shot: Any State -> action on trigger, then back to the resting state.
        ///
        /// There is no longer an "armed rest" branch, because the armed pose moved to its own
        /// fingers-only layer and no longer lives here.
        /// </summary>
        static void AddOneShot(AnimatorStateMachine sm, AnimatorState state, AnimatorState rest,
                               string trigger, float inDuration, float exitTime)
        {
            var enter = sm.AddAnyStateTransition(state);
            enter.hasExitTime = false;
            enter.duration = inDuration;
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);

            var exit = state.AddTransition(rest);
            exit.hasExitTime = true;
            exit.exitTime = exitTime;
            exit.duration = 0.2f;
        }

        static AvatarMask BuildHandsMask()
        {
            const string maskPath = "Assets/Game/Animator/Hands.mask";
            AssetDatabase.DeleteAsset(maskPath);

            var mask = new AvatarMask();
            foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                mask.SetHumanoidBodyPartActive(part, false);
            }

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);

            AssetDatabase.CreateAsset(mask, maskPath);
            return mask;
        }

        static AvatarMask BuildUpperBodyMask()
        {
            const string maskPath = "Assets/Game/Animator/UpperBody.mask";
            AssetDatabase.DeleteAsset(maskPath);

            var mask = new AvatarMask();

            // Start from nothing, then enable only what should override the base layer. Leaving
            // Root and the legs off is what lets locomotion keep running underneath.
            foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                mask.SetHumanoidBodyPartActive(part, false);
            }

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);

            AssetDatabase.CreateAsset(mask, maskPath);
            return mask;
        }

        /// <summary>
        /// The standing-still slot: turn-in-place on one axis, idle variation on the other.
        ///
        /// Both live inside the blend tree rather than as states with transitions, and that is
        /// the point. A TurnInPlace state entered on "Speed is nearly zero and the body is
        /// rotating" has to decide, every frame, which side of a threshold it is on -- and a
        /// player nudging the stick sits exactly on that threshold, which is where a state
        /// machine flickers. A blend has no threshold to sit on.
        ///
        /// Nested two deep: Turn on the outside (left / centre / right), and at centre a second
        /// blend on IdleVariant between the pack's two idles.
        /// </summary>
        static void BuildIdleSlot(BlendTree parent)
        {
            var idle01 = Clip(KI + "/Idles/HumanM@Idle01.fbx");
            var idle02 = Clip(KI + "/Idles/HumanM@Idle02.fbx");
            var turnLeft = Clip(Movement + "/Turn/HumanM@Turn01_Left.fbx");
            var turnRight = Clip(Movement + "/Turn/HumanM@Turn01_Right.fbx");

            var turn = parent.CreateBlendTreeChild(0f);
            turn.name = "Idle";
            turn.blendType = BlendTreeType.Simple1D;
            turn.blendParameter = "Turn";
            turn.useAutomaticThresholds = false;

            if (turnLeft != null) turn.AddChild(turnLeft, -1f);
            else Debug.LogWarning("[Animator] No Turn01_Left; turning left will not animate.");

            // The idle pair sits at the centre of the turn axis.
            var variant = turn.CreateBlendTreeChild(0f);
            variant.name = "IdleVariant";
            variant.blendType = BlendTreeType.Simple1D;
            variant.blendParameter = "IdleVariant";
            variant.useAutomaticThresholds = false;
            variant.AddChild(idle01, 0f);
            if (idle02 != null) variant.AddChild(idle02, 1f);
            else Debug.LogWarning("[Animator] No Idle02; every character will share one idle.");

            if (turnRight != null) turn.AddChild(turnRight, 1f);
            else Debug.LogWarning("[Animator] No Turn01_Right; turning right will not animate.");
        }

        /// <summary>
        /// Adds one speed tier to the locomotion tree as a 2D directional blend.
        ///
        /// Freeform Directional rather than Cartesian: the children sit on a unit circle around
        /// the character and the blend should follow the *angle* between them. Cartesian
        /// interpolates in straight lines across that circle, which makes a half-left input
        /// blend forward and left in equal parts and land visibly short of the diagonal clip
        /// that exists precisely for that input.
        ///
        /// A missing clip is skipped rather than fatal. The pack ships no backward sprint, and a
        /// tier with a hole in it still blends -- it just has nothing to play in that quadrant,
        /// which for sprinting backwards is the right answer anyway.
        /// </summary>
        static void Directional(BlendTree parent, string name, float threshold,
                                string prefix, bool includeBackward)
        {
            var child = parent.CreateBlendTreeChild(threshold);
            child.name = name;
            child.blendType = BlendTreeType.FreeformDirectional2D;
            child.blendParameter = "MoveX";
            child.blendParameterY = "MoveY";

            const float D = 0.7071f;   // cos/sin 45 degrees: the diagonals sit on the circle

            AddDirection(child, prefix + "Forward.fbx", 0f, 1f);
            AddDirection(child, prefix + "ForwardLeft.fbx", -D, D);
            AddDirection(child, prefix + "ForwardRight.fbx", D, D);
            AddDirection(child, prefix + "Left.fbx", -1f, 0f);
            AddDirection(child, prefix + "Right.fbx", 1f, 0f);

            if (!includeBackward) return;

            AddDirection(child, prefix + "Backward.fbx", 0f, -1f);
            AddDirection(child, prefix + "BackwardLeft.fbx", -D, -D);
            AddDirection(child, prefix + "BackwardRight.fbx", D, -D);
        }

        static void AddDirection(BlendTree tree, string path, float x, float y)
        {
            var clip = Clip(path);
            if (clip == null)
            {
                Debug.LogWarning("[Animator] Directional clip missing, quadrant left empty: " + path);
                return;
            }
            tree.AddChild(clip, new Vector2(x, y));
        }

        static AnimatorState AddState(AnimatorStateMachine sm, string name, AnimationClip clip, Vector3 pos)
        {
            var st = sm.AddState(name, pos);
            st.motion = clip;              // null is tolerated: the state just holds the last pose
            st.writeDefaultValues = false; // avoids parameters bleeding between layers later
            if (clip == null)
                Debug.LogWarning("[Animator] State '" + name + "' has no clip.");
            return st;
        }

        static void SetDefault(AnimatorController ac, string param, bool value)
        {
            var ps = ac.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == param) ps[i].defaultBool = value;
            ac.parameters = ps;
        }

        /// <summary>Clears the loop flag on a standalone .anim asset. No-op if already off.</summary>
        static void StopLooping(AnimationClip clip)
        {
            if (clip == null) return;

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (!settings.loopTime) return;

            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            Debug.Log("[Animator] Cleared the loop flag on '" + clip.name + "'.");
        }

        /// <summary>Sets the loop flag on a standalone .anim asset. No-op if already on, and on
        /// an FBX sub-asset, where the flag belongs to the importer rather than the clip.</summary>
        static void StartLooping(AnimationClip clip)
        {
            if (clip == null) return;

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.loopTime) return;

            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            Debug.Log("[Animator] Set the loop flag on '" + clip.name + "'.");
        }

        /// <summary>
        /// Loads a clip from either an FBX (first non-preview sub-asset) or a standalone .anim.
        /// The two packs use both forms, so callers should not have to care which.
        /// </summary>
        static AnimationClip Clip(string path)
        {
            if (path.EndsWith(".anim"))
            {
                var direct = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (direct == null) Debug.LogWarning("[Animator] Not found: " + path);
                return direct;
            }

            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            if (all == null || all.Length == 0)
            {
                Debug.LogWarning("[Animator] Not found: " + path);
                return null;
            }

            var clip = all.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip == null) Debug.LogWarning("[Animator] No clip inside: " + path);
            return clip;
        }
    }
}
