# Project State
Last updated: 2026-09-05 (release keystore, mobile textures, asset-gap audits)

## Done
- Phase 1: World, character, movement, camera, day/night cycle
- Phase 2: Vehicles, traffic, traffic lights, wanted-on-red-light
- Phase 3: NPCs, combat, wanted/heat system, police, arrest/game-over
- Phase 4: Missions, progression, minimap, save/load
- Phase 5: Economy, shops, vehicle upgrades, interiors, safehouse
- Phase 6: Ads & monetization (mock ad service, IAP stub)
- Phase 7: Menus, audio, performance tuning, signed Android APK built
- Phase 8: Fixed the critical on-device UI failure and ran a device
  verification pass. See PHASE8.md. Last build:
  Builds/Android/MiniGTA-0.2.0.apk (125.8 MB), installed and verified
  on the test phone. **That APK predates Phase 9 and no longer matches
  the project.**
- Phase 9: Full asset integration -- seven Asset Store packs replace the
  primitive-box art layer. See PHASE9.md. **Editor-verified only.**
- Phase 9b: Animation source replaced. Locomotion and gunplay now come
  from the Kevin Iglesias human pack, melee from the EEJANAI fighter
  pack; the Mixamo animation set is deleted apart from the swim clip.
  See PHASE9B.md. **Editor-verified only; the device pass has not been
  run for either 9 or 9b.**
- Phase 9b-fix: four defects found in Editor playtesting after 9b was
  reported complete -- zombie-arms pose, road striping, invisible animals,
  dead-looking FIRE. All four root-caused by reproduction, fixed and
  re-verified in Play mode; the full 9b checklist re-run. See the addendum
  in PHASE9B.md. **Editor-verified only.**
- Phase 11: UI re-skinned onto the Soft Touch kit; the city rebuilt from
  asset-pack buildings on a map 2.56x the size; traffic lights swapped to
  the Tarbo pack; weapon pickup, rain and a gameplay re-verification pass.
  See PHASE11.md. **Editor-verified only.**
- Phase 12: the bare Phase 7 title screen replaced by a full lobby -- left
  icon rail, the player's character standing lit on a plate, one large PLAY
  button, an editable profile name, and character-select / profile screens
  hanging off the rail. Save format v3 -> v4. See PHASE12.md.
  **Editor-verified only.**
- Phase 13: the whole interface re-skinned from Soft Touch onto the **Space
  Exploration GUI Kit**, and every text element moved onto one display face.
  Kit panels, four-state kit buttons, kit icons, kit bars and kit dividers
  across the HUD, the lobby, the menus, the shop and the map; a palette
  sampled out of the kit's own artwork. See PHASE13.md.
  **Editor- and Play-mode-verified; no device pass.**
  - **The requested "Fatality" font is not in the project and could not be
    fetched.** Everything else about that instruction is done: one face
    across all 129 Text components, and the face is a drop-in slot at
    `Assets/Game/UI/Fonts` that the build logs by name. Righteous (shipped
    inside the kit) stands in until the real file is imported. See D45.
- Phase 13b: the lobby made genuinely responsive. A new device-matrix sweep
  (`Tools > Mini GTA > 6c`) simulates render size **and** safe-area inset over
  ten landscape shapes; it found 10 elements outside the safe area, 20 text
  boxes clipping their own text, and the backdrop and all nine modal dimmers
  stopping at the safe area instead of the screen edge. All fixed; the sweep
  reports 0/0/0. See the addendum in PHASE13.md and D51-D54.
  **Editor-verified only, and the safe-area path specifically has never run on
  hardware** -- `renderOutsideSafeArea` is false, so the real Android build is
  letterboxed by the OS and reports a full-screen safe area.

## What Phase 8 fixed
1. **Vulkan pre-transform** (`vulkanEnablePreTransform` was on). Unity
   rendered into the display's native portrait swapchain and rotated;
   the overlay canvas did not get the matching transform, so the whole
   UI was squashed 0.466x horizontally and stretched 2.146x vertically.
   That is the oversized/cut-off menu AND the "vertical striping" text
   -- both gone once the setting was turned off. No editor can
   reproduce it (editor is D3D11 on desktop).
2. **Four HUD buttons had no click handler in the build.** They were
   wired by `onClick.AddListener` from an *editor* build script, which
   is a non-persistent listener and is never serialised into the scene.
   Pause, minimap/full-map, store and clear-wanted were all dead in the
   APK while working in the editor. Now wired at runtime in each
   panel's Awake, matching how every working button already did it.
3. **Safe area** implemented (was entirely absent): one `SafeArea`
   container under the canvas holding all 18 UI roots.

## In Progress
- **Combat / weapon / joystick brief.** Item 12 (single-stick vehicle control) is
  DONE and verified in Play mode -- see the section below. Items 2, 3, 3b, 4, 5,
  6, 7 and 8 (guns, inventory, pickup rules, animations, combat state machine,
  UI feedback, camera shake) are NOT started; `PlayerCombat` is still the Phase-7
  two-weapon version with no reload, no fire modes, no slots and no drop.
- The Phase 12 and Phase 13 passes are closed. Phases 9, 9b, 9b-fix, 11, 12
  and 13 are all awaiting one device pass, and Phase 11 makes it urgent: the
  triangle count has more than doubled and there are now 189 animated animals
  in the scene. Phase 12 adds a second reason -- the lobby's rename field
  opens the Android on-screen keyboard, a path the Editor cannot exercise at
  all. Phase 13 adds a third: **a display face at the 14-unit `Micro` step,
  used for the rail captions and the HUD control captions, is a readability
  question a desktop monitor cannot answer**, and 136 new UI textures of
  unmeasured ETC2 cost are now referenced by the scene.
- **Drop `Fatality.ttf` into `Assets/Game/UI/Fonts` when it is available**,
  then run `Tools > Mini GTA > 6b. Rebuild Interface Only`. That is the whole
  of the outstanding work on instruction 1 of the Phase 13 brief. Until then
  the build log reads
  `[Menus] interface face: Righteous (kit fallback -- Fatality not present)`.

## Next Step -- device pass, REQUIRED before 9 / 9b / 9b-fix / 11 / 12 can be called done

0. **RE-JUNCTION `Library/Bee` FIRST. THE NUMBERS IN THIS STEP WERE STALE AND
   THE SITUATION IS MUCH WORSE THAN IT SAID.** Measured 2026-09-02:

   | | Phase 12 said | Actually is now |
   |---|---|---|
   | `Library/Bee` | 0.21 GB | **5.45 GB** |
   | C: free | 5.8 GB | **4.61 GB** |
   | D: free | -- | 17.89 GB |

   Bee is a real directory on C:, not a junction, and it has grown 26x since
   it was last checked. **Moving it to D: is the single highest-value action
   available on this machine**: it frees 5.45 GB on C:, taking it from 4.6 GB
   to about 10 GB, which is more headroom than any build in this project's
   history has had. An IL2CPP build needs roughly 2.5 GB of Bee growth on top
   of whatever is free, and PHASE7 records three build failures from exactly
   this shortage -- at a point when there was *more* room than there is now.

   Do not attempt an APK before this. PHASE9 records that running out of disk
   here did not just fail the build, it left a **corrupt AssetDatabase entry**
   that took a file recreation at a different path to clear.

   Close the Editor, then:
   `robocopy "Library\Bee" "D:\unity_build_cache\gta_Bee" /MOVE /E`
   then `mklink /J "Library\Bee" "D:\unity_build_cache\gta_Bee"`.
1. **Build an APK and run the Phase 8 device matrix again.**
   `Tools > Mini GTA > 13. Build Android APK`, install, test. Phase 9 is
   Editor-verified only, and CLAUDE.md section 5 is explicit that this is
   not verification. The open question that matters most: whether a
   mid-range phone holds 30 fps at Low with **3,082 renderers and 2.46 M
   triangles** in the scene. That is +55% renderers and +115% triangles on
   the Phase 9 baseline, on a map 2.56x the area. 850 renderers are on the
   cullable Detail layer, so the *drawn* count at distance is lower than the
   raw total -- but this is by far the heaviest the scene has ever been.
2. **Watch disk.** C: has **4.7 GB** free as of 2026-09-02, down from 6.3 GB
   at Phase 9 and 5.8 GB at Phase 12. PHASE7 records the build failing three
   times on disk space alone, at a point when there was more room than there
   is now. See the Bee note in step 0 -- that move is no longer optional.
3. ~~Swim state transition untested~~ -- **done in Phase 9b.** InWater
   fires, the Swim state plays, the character swims. Retest on device.
4. Carried over from Phase 8, still outstanding:
   - The four in-world shops and the rewarded-ad overlay were never
     device-tested. A debug/cheat hook (spawn car, teleport to shop, grant
     heat) would make that repeatable.
   - **Delete `Assets/Game/Scripts/Core/DeviceDiagnostics.cs`** and its
     component before shipping. It is instrumentation.
   - Android Back button still does not open the pause menu.
   - TextMeshPro still not adopted. See DECISIONS.md D5.
5. ~~Player's hand does not close around the grip~~ and ~~`Gunplay.fbx` is
   a truncated 0.20 s clip~~ -- **both closed by Phase 9b.** The `Armed`
   bool now has a producer and a consumer, and Gunplay is deleted in
   favour of a properly authored firing clip.
6. **Watch animation cost specifically on device.** The crowd is 16
   humanoid animators plus the player, the police and **189 animals**, and the
   new locomotion blend evaluates four clips where the old one evaluated
   three. Also check how the Low tier's 2-bone skin-weight limit treats
   these clips. The animals' animators are `CullCompletely`, so verify
   they actually stop costing anything off screen. See PHASE9B.md.
7. **Look at the characters on the device screen, not at a parameter dump.**
   The 9b-fix pass exists because a checklist was signed off from string
   comparisons that could not have caught the bug. Every animation state
   on device must be confirmed as a rendered frame.
8. **New in Phase 12, all of it device-only:**
   - **The rename field's on-screen keyboard.** uGUI `InputField` opens
     `TouchScreenKeyboard` on Android and the Editor exercises none of it:
     not the keyboard appearing, not the layout while it covers half a
     landscape screen, not Done, not losing focus to the Back gesture.
     This is the least-tested thing in the phase.
   - **Rail touch targets.** 112 reference units square. Confirm they are
     comfortable under a thumb and that the top-bar name chip is reachable
     one-handed in landscape.
   - **Lobby frame cost.** A second camera and a 640x896 RenderTexture draw
     a skinned body every frame the lobby is open. The world camera's
     culling mask is zero while it is up, so this should be far cheaper
     than the city -- measure it rather than assuming.
   - **Safe area against the 82 px cutout.** The lobby anchors content hard
     to the left and top edges; the old centred card never did.
9. **New in Phase 13, all of it device-only:**
   - **A display face at 14 units.** `UiTheme.Micro` sets the rail captions,
     the HUD control captions (JUMP / RUN / FIRE / ENTER...) and the version
     label. Righteous is wide and geometric and should hold up, but a
     display face at 14 pt is the single readability risk in the re-skin and
     a desktop monitor is the wrong instrument for it. Look at the phone.
   - **Whether the dynamic font atlas still repacks.** The bound is unchanged
     at one face and seven sizes (D30), but Righteous has different metrics
     from Carlito and PHASE8 observed a repack on device.
   - **UI texture cost.** 136 kit sprites are now referenced, several of them
     1128x512 and 1592x952. Nothing has measured the ETC2 footprint, the
     atlas count or the load time on a phone.
   - **The thumb cluster over the city, not over sand.** The play-mode
     composite that validated contrast was shot on the beach. The city puts
     dark asphalt and bright building faces in the same frame.
   - **Touch targets after the re-skin.** The thumb buttons kept their sizes
     (104 / 112 / 132) but now carry an icon *and* a caption inside them.
     Confirm the caption is still legible and the target still comfortable.
10. **New in Phase 13b, device-only:**
   - **The safe-area path has never run on hardware.** `renderOutsideSafeArea`
     is `false`, so Android letterboxes the app and reports a full-screen safe
     area -- the easy case, and the only one the device has ever seen. The
     inset handling was built and verified against a *simulated* inset. To
     exercise it for real, turn that setting on and re-run the device matrix,
     or test on a device that reports insets regardless.
   - **Card-screen touch targets are 4.3-5.1 mm** (pause, settings, shop,
     store, dialogue buttons at 62-74 units) against a 7 mm guideline. Known
     and deliberately not fixed this pass -- raising them means re-fitting
     five card layouts against the kit's frames. `6c` reports them every run.
     Decide on a phone whether they are actually a problem before doing that
     work.


## Phase 9 result summary (detail in PHASE9.md)
- Player, police, crowd and shopkeepers all now come through **one**
  `CharacterCatalog.AttachBody`. Vehicles are built from the Mobile
  Optimized car pack with wheel radius and track measured off the mesh.
  Weapons attach through a generic `WeaponSocket` whose hand rotation is
  *solved* from the Pistol Idle pose, not typed in. 43 animals and ~195
  wild plants are placed procedurally by terrain height and slope.
- **"Low Poly Characters Lite" could not be used for pedestrians.** Those
  FBX files contain no skeleton at all -- no Deformer, Cluster, LimbNode
  or Skin records -- and are modelled in a T-pose. They are static props
  shaped like people. They are used as shop mannequins in Threads instead.
  See DECISIONS.md D7.
- Scene now: **1,984 renderers** (was 1,426), **1,144,580 triangles**
  (was 1,010,043), 460 colliders, 1,017 renderers on the cullable Detail
  layer. Characters got much cheaper (pedestrians 583,907 -> 250,865 tris;
  player 35,196 -> ~5,000); the world got much busier.
- Drawn from a fixed camera, recorded so Phase 10 can compare like-for-like:
  city centre `(560,24,470)` rot `(8,20,0)` -> High 718 / Med 526 / Low 367;
  player spawn `(377,17,500)` rot `(6,90,0)` -> High 1,253 / Med 880 / Low 729.
- Two defects found and fixed during the pass, both worth remembering:
  a **19.5 m pistol** (the rig's armature carries a 100x scale that the
  skinned mesh hides but a bone-parented object inherits), and **43 animals
  throwing `InvalidOperationException` every frame** (the pack's demo
  scripts poll legacy `Input.GetAxis`, which throws under Input System).
- Zero console errors, zero broken/magenta materials, zero non-URP shaders,
  zero floating geometry, zero heavy mesh colliders left in the scene.


## Phase 9b result summary (detail in PHASE9B.md)
- **Clip source replaced, not layered.** Locomotion, gunplay and damage come
  from the Kevin Iglesias human pack; melee from the EEJANAI fighter pack.
  One controller (`PlayerLocomotion.controller`) drives the player, the
  police and the whole crowd, so this is the game's entire animation set.
- **13 Mixamo FBX files deleted**, 54 MB -> 688 KB. Only `Swimming.fbx`
  remains, because neither new pack has a swim clip (DECISIONS D14).
  Dependencies were checked across the scene, the controller and all 10
  game prefabs first; afterwards the controller had **0 broken clip refs**.
- **Both packs are Humanoid and retarget directly.** The Phase 1
  `MixamoImportSetup` pipeline does not apply to them and was not used.
  Kevin Iglesias ships root motion already locked, which is exactly the
  configuration PHASE1 had to apply by hand to Mixamo.
- **Trap worth remembering:** the EEJANAI *FBX* files are Generic with no
  avatar and will not retarget. The usable clips are the standalone `.anim`
  assets shipped alongside them, which are `humanMotion = true`.
- **The Phase 9 100x armature quirk does not affect these clips** -- every
  candidate was sampled onto both rigs and head height stayed in the
  1.38-1.65 m band against a 1.636 m bind pose. Measured, not assumed.
- **Blend thresholds are derived, not eyeballed** (D15). Each clip's
  authored speed was measured off its `[RM]` twin (2.00 / 4.00 / 6.00 m/s)
  and placed at the `speed01` where the character genuinely travels that
  fast: Walk 0.455, Run 0.750, Sprint 1.000. Residual foot-slide at full
  sprint: 3.4%.
- **Root motion confirmed locked at runtime**, not just at import: zero
  input for 15.39 s produced **0.00007 m** of movement.
- **Two Phase 9 rough edges closed.** The `Armed` bool now has a producer
  (`WeaponSocket`) and a consumer, so the hand closes around the grip --
  barrel alignment 0.94 -> 0.99. And the truncated 0.20 s `Gunplay.fbx` is
  gone. *(The consumer was `ArmedIdle`; 9b-fix replaced it with a
  fingers-only Hands layer. See D19.)*
- **The weapon socket reference clip had to move with the animation source**
  (D16). Phase 9 derives the socket rotation from a clip, and that clip was
  one of the deleted ones; the derived rotation changed from
  (282.2, 242.0, 217.8) to (290.7, 218.9, 319.9). Re-run steps 6 and 13
  after any future animation change or every weapon is silently mis-aimed.
- Masked upper-body layer still works: both attacks were fired while
  walking and the base layer kept blending Walk 0.85 / Run 0.15 throughout.
- Console: 0 errors, 0 warnings. Scene census unchanged from Phase 9
  (1,985 renderers, 1,144,660 tris, 460 colliders, 1,017 on Detail).
- **This summary was written before the defects below were found.** The
  checklist above was reported PASS on parameter reads, not on rendered
  frames. Read the 9b-fix section next.

## Phase 9b-fix result summary (detail in the PHASE9B.md addendum)
All four reported defects reproduced in Play mode, root-caused, fixed and
visually re-confirmed. **Pass on all four.** Ownership: one was a Phase 9b
regression, one a Phase 9 bug that Editor-only checks could not see, one
predates Phase 9 entirely, and one was a symptom rather than a defect.

- **Zombie-arms pose (hypothesis (c), confirmed).** The `UpperBody` layer is
  Override at `defaultWeight = 1`, masked over torso + head + both arms, and
  its default state has no motion. **An Override layer at weight 1 with an
  empty state does not contribute nothing -- it writes the humanoid zero
  pose over its whole mask.** That structure has been in the controller
  since Phase 1/3 and affected every character, not just the player. Phase
  9b's `ArmedIdle` compounded it by holding a two-handed aim pose
  permanently, since the player is armed from frame one.
  Fix: layer ships at weight 0 and `PlayerAnimation` drives it; `ArmedIdle`
  replaced by a fingers-only `Hands` grip layer (D19).
  Verified: **0 of 26 characters** with the upper layer raised while
  resting; arms hang naturally in rendered frames.
- **Road striping = z-fighting, not a texture bug.** Crosswalk paint sat at
  exactly the road's top face (both 15.1200) across all 64 junctions.
  Not the Phase 8 dynamic-atlas bug (that was UI text) and not the Cartoon
  City pack (it ships no road geometry -- roads are still Phase 1 boxes).
  Compounded by Phase 7 missing the zebras when it moved lane markings onto
  the cullable Detail layer, so they drew out to 420 m where depth
  precision is worst. Fix: paint raised to `+0.10..+0.12` (D20) and
  Detail-culled by prefix (D21). Verified: crisp bounded rectangles.
- **Animals were present, rendering, and hundreds of metres in the air.**
  `SampleGround` took the first downward ray hit, and the ray hits other
  animals; two animals near each other each read the other's back as
  ground and the pair climbed without limit -- sampled mid-session at
  **y = 78 to 255** in a city at y = 15. **A Phase 9 bug I wrote and did
  not catch, because Phase 9 measured animal height only on freshly-placed
  objects, never after simulation.** They were also all in the park, coast
  and mountains: nearest to spawn 190 m. Fix: probe filters out anything
  with an `AmbientAnimal` or `CharacterController` and clamps to the patch
  (D22); a City zone puts 20 strays on block pavements (D23);
  `SimulationRange` 140 -> 260 m. Verified: 59 animals, nearest **64 m**,
  **8 within 150 m**, worst rise after 18 s of simulation **0.31 m**.
- **FIRE was never broken.** It decremented ammo 48 -> 47 on the first
  press through its real `IPointerDownHandler` path -- it is a `HudButton`,
  so it was never exposed to the Phase 8 dead-listener bug. The attack
  plays on the same `UpperBody` layer that was pinned holding an aim pose,
  so a 0.3 s firing clip blended over it was near-invisible. Fixed by the
  layer-weight fix; **`PlayerCombat` was not modified.** Verified: layer
  rises 0.00 -> 1.00 and returns to 0.00, `Gun_Aim01_Shoot01 w1.00` plays.
- **Full Phase 9b checklist re-run, 12/12 PASS**, this time against
  rendered frames and asserted layer weights rather than parameter reads.
  Drift **0.00000 m** over 12.4 s; **0** animators scene-wide with
  `applyRootMotion`.
- Census after: 2,003 renderers, 1,163,656 tris, 477 colliders, 1,081 on
  Detail, 59 animals. Console 0 errors, 0 warnings. Phase 8 UI intact
  (SafeArea, 18 roots, 30 buttons).
- **Why the 9b checklist passed when the game was visibly broken:** it read
  `GetCurrentAnimatorClipInfo(layer)`, got an empty array for the upper
  body, and recorded "the layer contributes nothing". An empty array is the
  *signature* of this bug, not evidence of safety. Confirm animation states
  by looking at the character.

## Phase 11 result summary (detail in PHASE11.md)
- **The interface is entirely re-skinned onto the Soft Touch UI Kit.** A new
  `Assets/Editor/UiTheme.cs` owns the palette, the type scale, the sprite
  table and the nine-slice borders; MenuBuilder and SceneAssembler ask it for
  a button or a panel instead of setting colours inline. Re-skinning again is
  a change to one file.
- **Font atlas risk cut hard.** 22 distinct sizes across 2 fonts -> **7 sizes
  across 1 font** (Carlito), by snapping every requested size onto a fixed
  scale (D30). PHASE8 flagged the 22-size dynamic atlas as a live hazard and
  observed a repack on device; this is the mitigation short of TextMeshPro,
  which stays the right eventual answer (D5).
- **Phase 8's fixes still hold after the re-skin:** Canvas Scaler is still
  ScaleWithScreenSize 1920x1080 match 0.65, and there is still exactly one
  SafeAreaFitter holding all 18 UI roots. Captured at 2400x1080 and 1600x720
  with no overlap, clipping or cut-off text.
- **Map is 2.56x bigger, city is 3.24x bigger.** MapSize 1000 -> 1600,
  Blocks 5x5 -> 9x9, CityCenter (560,500) -> (896,800), CityExtents 215 -> 360.
- **Primitive-box buildings are gone: 417 buildings, 100% from asset packs,
  0 fallbacks.** The builder logs a warning if the fallback ever fires, so a
  regression cannot be silent. 50 models across three packs, placed
  procedurally with district-aware mixes and measured footprints.
- **Renderers 1,984 -> 2,950 (+49%) for a 3.24x larger city.** The reason is
  D27: lane-marking dashes were one GameObject each and would have cost about
  2,100 renderers on the new grid; batching them per road line gives 20.
  Without that the scene would be roughly 5,000 renderers.
  Triangles 1,144,580 -> 2,330,979 (+104%).
- **Every hardcoded world coordinate is now derived from the block grid** (D25):
  4 shop doors, 4 mission contacts, 8 mission checkpoints, the park mission
  site, the police fallback spawn, the boat and the ocean plane. None of them
  would have thrown an error when the grid moved -- three of the four shop
  doors would simply have been inside walls.
- **Traffic lights swapped to the Tarbo pack with zero logic change.**
  TrafficLightController and TrafficLightLamps untouched. The pack ships one
  mesh with no child objects, so three emissive quads are laid over its
  moulded lenses; their positions were measured by raycasting the mesh (D32).
- **The player starts unarmed** (D28). Six pistol pickups on block kerbs, the
  nearest 38 m from spawn; Interact takes one and the socket equips it.
- **Rain added as a state of the day cycle** (D29), not a separate always-on
  system: DayNightCycle rolls it once per day at 30%, damps the sun, cools the
  ambient and pulls the fog in; WeatherSystem only draws the particles.
- Attack verified end to end: 34 damage (exactly PistolDamage) on a stationary
  pedestrian, ammo decrementing, through the real HudButton pointer path.
- 104 pack materials converted to URP (70 SimplePoly, 34 Tarbo) -- every
  SimplePoly building would have rendered magenta otherwise.

## Phase 12 result summary (detail in PHASE12.md)
- **The Phase 7 title screen is gone.** In its place: a left icon rail
  (Character / Profile / Settings / Exit), the player's character standing
  lit on a plate in the centre, one large PLAY, and a top bar with an
  editable name plus level and money chips. `LobbyScreen` holds the Phase 7
  Continue / New Game logic **unchanged** -- it was moved, not rewritten.
- **PLAY is one button** (D36). No save -> new game, save -> continue, and
  the caption under it says which. Starting over lives behind a two-tap
  confirmation on the profile screen, because it erases the save.
- **The lobby character is a display body on its own stage** in dead air at
  (-420, 400, 300), rendered by its own camera into a 640x896 RenderTexture
  (D39). It comes from the same `CharacterCatalog.AttachBody` call the
  player does and wears the same `PlayerSkinSwapper`, so it cannot drift
  into being a different character. Cost: **+4 renderers, +6,101 triangles**
  (3,082 -> 3,086; 2,464,197 -> 2,470,298), and while the lobby is open
  they are the only things drawn at all -- the world camera's culling mask
  is set to zero (D41).
- **The lobby borrows the day/night key light** while it is open and hands
  it back on close (D42), the same trick InteriorManager uses. Necessary
  because mobile URP has additional lights disabled, so a lamp aimed at the
  stage would render as nothing on a phone.
- **The wardrobe is built from the Threads shop's own stock**, via a new
  runtime `SkinLibrary`. Prices, level gates and colours stay authored once
  in InteriorBuilder, and locked outfits use the shop's existing ownership
  rule -- the wardrobe cannot become a way around the till.
- **A latent Phase 5 bug fixed on the way.** `ActiveSkin` has been saved and
  restored since Phase 5 and `PlayerSkinSwapper.OnSkinChanged` recorded the
  id and changed nothing, because the colour lived on the shop item. An
  outfit bought in one session came back as the stock model in the next.
  Invisible until something displayed the character outside gameplay.
- **Save format v3 -> v4**, one field (`ProfileName`). The real v3 file on
  this machine was loaded and rewritten as v4 with everything intact.
- **Phase 8 holds:** scaler still ScaleWithScreenSize 1920x1080 match 0.65,
  still exactly one SafeAreaFitter, now holding 20 UI roots. Font atlas
  still 1 font / 7 sizes across 125 Text components.
- **Two genuinely different aspect ratios tested this time.** Phase 11's
  2400x1080 and 1600x720 are the *same* 2.222 aspect and the same 2220x999
  canvas -- two resolutions of one shape. This phase used 2400x1080 and
  2048x1536 (1593x1194 canvas), plus 1920x1080.
- **Five layout defects found by looking at captures, all fixed**, plus one
  found by assertion (the money chip read $0 on a first run). Each of the
  five had passed every assertion first. Details in PHASE12.md.
- **Two tooling traps recorded, neither a game bug:** runtime `onClick`
  listeners die on every MCP command's domain reload -- which mimics the
  Phase 8 dead-button bug exactly, and which the Phase 7 panels fail
  identically -- so a click journey has to run inside one command; and
  `PhaseCapture.UiShot` draws the interface but not the world behind it
  under URP. Read the PHASE12 tooling section before the next UI phase.
- Console at the end: **0 errors, 0 warnings.** Scene saved.
  `PlayerSettings.runInBackground` confirmed still `false`.

## Single-stick driving result (combat brief item 12)

**One stick now drives the car.** Y axis = throttle (forward accelerates, back brakes
then reverses, proportional to travel), X axis = steering. Gas and Brake buttons are
hidden. Rationale and reversal notes in DECISIONS.md D-041.

Files: `Assets/Game/Scripts/Input/InputHub.cs` (radial deadzone, dual-rate smoothing,
`Throttle` / `RawThrottle` / `Steer`, `TickThrottle` on unscaled time incl. the
unfocused branch), `Assets/Game/Scripts/UI/HudContext.cs` (`StickThrottle` flag).

Verified in Play mode, real car (`Police_1`), every number measured not assumed:

| Check | Result |
|---|---|
| Deadzone floor | stick 0.00-0.15 -> steer `0.000` |
| Smooth entry past the deadzone | stick 0.16 -> steer `0.012` (no 0.15 jump) |
| Full deflection | stick 1.00 -> steer `1.000` |
| Radial vs square zone | `(0.10, 0.10)` \|m\|=0.141 -> `0.000` both axes; a square zone would have leaked |
| Diagonal full push | `(1,1)` -> steer `0.707` |
| Resting-thumb drift | `(0.06, 0.09)` -> throttle `0.0000`, steer `0.0000` -- no creep at a standstill |
| Top speed on stick alone | **103.7 kph**, no pedals touched |
| Proportional throttle | half stick -> throttle `0.412` |
| Reversal bite | 69.2 kph forward, slam full back -> throttle `-1.000` at the faster 0.05 s rate |
| Exit at speed | 129.7 kph vs `MaxExitSpeedKph` 25 -> exit refused, still driving. PASS |
| Exit at rest | 0.0 kph -> clean exit, HUD returns to OnFoot. PASS |
| Combat after exiting | `HudContext` OnFoot, Attack button back. PASS |
| Pedal visibility | driving shows `Interact, Horn, Handbrake`; on foot `Jump, Sprint, Interact, Attack`. PASS |
| Flag is causal | `StickThrottle = false` -> `Gas, Brake` return. PASS |

Not yet tested on a device -- adds to the outstanding device-pass list.

## Phase 13 result summary (detail in PHASE13.md)
- **The interface is entirely on the Space Exploration GUI Kit.** `UiTheme`
  owns a palette of 17 values, all of them sampled out of the kit's own PNGs
  rather than picked by eye; a sprite table of 8 containers, 3 button
  families x 3 silhouettes x 4 states, 6 bars, 3 dividers and 24 icons; and
  the type scale. MenuBuilder, SceneAssembler and LobbyBuilder ask it for a
  panel or a button. Re-skinning again is still a change to one file.
- **Census:** 129 `Text` on **1** font at exactly **7** sizes (D30 holds);
  206 `Image` of which **136** are kit sprites; **37 of 38** buttons carry
  all four kit state sprites (the 38th is the invisible tap target over the
  corner minimap); 1 `SafeAreaFitter` holding 21 roots; scaler unchanged.
  Console 0 errors, 0 warnings. Scene saved.
- **The requested Fatality font is absent and could not be fetched.** The
  face is a drop-in slot (D45) and the build names whichever face it used, so
  this cannot be silently mis-reported. Righteous, from the kit, stands in.
- **The polarity of the interface inverted** -- dark panels carrying light
  buttons, where Soft Touch was light panels with dark text -- so `UiTheme.Ink`
  was **deleted** rather than redefined, making all ~30 call sites compile
  errors that had to be answered one at a time (D47). Redefining it would have
  compiled cleanly and produced invisible text on half the screens.
- **A new build-time frame audit found 91 layout defects that every existing
  assertion passed.** This kit's painted frames are 87-166 px against Soft
  Touch's 26-64, so every card in the project was mis-sized the moment the kit
  changed: the settings title was drawn across its own moulding, the shop's
  stock rows ran out past both sides of the sheet, the map's streets were
  painted over the panel and onto the screen behind it. `UiTheme.AuditFrames`
  now reports **0** and runs on every build.
- **Nine-slice borders are measured off the artwork, not typed** (D46), which
  matters because this phase swapped sprites between the kit's four size tiers
  repeatedly and a typed border would have gone stale silently.
- **The first true composite capture this project has taken.** PHASE12
  recorded that `UiShot` draws the interface with no world and `Shot` draws
  the world with no interface. Putting the overlay canvas on the main camera
  for one render gives the real thing, and it immediately produced three
  defects nothing else could have: the XP bar and the STORE button overlapping
  by 4 units, "LOSE HEAT" printing through "STORE", and the thumb cluster
  washing out over beach sand at 70% alpha.
- **`Tools > Mini GTA > 6b. Rebuild Interface Only` is new** (D50). It
  rebuilds the canvas against the existing world instead of re-running the
  17-step pipeline. A future screen that adds a fifth external HUD reference
  must add it there.
- **The five hand-drawn rail glyphs from D38 are gone**, replaced by the kit's
  flat pictogram set -- which is exactly the kind of asset D38 went looking for
  and could not find in the previous kit (D48).
- Verified in Play mode as well as the Editor: the lobby renders the real save
  (`Mohit`, level 10, $29,960) with the stage body playing `HumanM@Idle01`,
  and PLAY still takes `timeScale` 0 -> 1 and the culling mask 0 -> -1.

## Phase 13b result summary (detail in the PHASE13.md addendum)
- **The reported defect was real and Phase 13's verification could not have
  seen it.** `SafeAreaFitter` runs only in play mode, so every capture taken
  during the re-skin was of a canvas with no notch inset. With one applied,
  the lobby's starfield and all nine modal dimmers stopped at the safe area
  and the inset drew as flat grey bands down both edges and the bottom.
- **`SafeAreaBleed` splits the two jobs** (D51): controls stay inside the
  inset, backdrops and scrims expand back out to the full canvas. Ten in the
  scene. Verified numerically -- at the iPhone-class shape the safe rect is
  1960x959 and the backdrop is 2181x1008 -- and in captures.
- **New sweep, `Tools > Mini GTA > 6c`** (D52), over ten landscape shapes,
  simulating render size *and* safe-area inset, measuring overflow, text that
  clips its own box, lobby collisions, and touch targets **in millimetres**.
  First run 10 / 20 / 0 / 300; now **0 / 0 / 0** with touch targets as below.
- **Defects it found that no picture had:** the version label had hung 126
  units off the right edge of every screen since Phase 12 (it renders empty
  until a build number is set, so nothing ever photographed it); the ammo
  counter and the speedometer both clipped their own digits, because their
  boxes were sized for Carlito in Phase 11 and never re-measured after the
  face changed in Phase 13.
- **The hero column is now proportional** (D53). The plate and PLAY were fixed
  at 600 and 520 units on a screen whose height varies 22% across supported
  shapes; both now derive from an AspectRatioFitter calibrated so the
  1920x1080 reference renders exactly as before.
- **The reference resolution stays 1920x1080 / match 0.65, measured rather
  than argued** (D54). The suggested 1503x755 puts **185 elements off the
  screen**, because every layout here is authored in 1920x1080 units.
- **Touch targets on the main menu now pass**: PLAY 8.6 mm, rail icons 7.8 mm,
  name chip 7.2 mm (was 5.8), wardrobe tile 16.7 mm. Pause, store and
  lose-heat raised 78/96 -> 104 units. Card-screen buttons knowingly left at
  4.3-5.1 mm -- see step 10 above.
- **No portrait layout, on purpose**: `allowedAutorotateToPortrait` is false
  and both landscape orientations are true, so the OS never hands this UI a
  portrait window.
- Frame audit still 0 at zero tolerance. Console 0 errors, 0 warnings.

## Blockers
- None for the work itself. Everything written this phase is compiled, built
  into the scene and saved, with a clean console.
- **`C:` free space is a hard blocker for the next APK**, not for this phase.
  4.61 GB free, with a 5.45 GB `Library/Bee` sitting on it un-junctioned --
  and step 0's note that Bee was "only 0.21 GB" was 26x out of date. No APK
  was built this phase for that reason: PHASE7 records three build failures
  from this shortage and PHASE9 records it corrupting the AssetDatabase.
  The fix frees 5.45 GB and needs the Editor closed, so it is the user's
  call to make rather than something to do under a running Editor. See step 0.


## Environment gotchas (cost real time this session -- do not rediscover)
- **adb is NOT on PATH.** It lives at
  `C:\platformtools\platform-tools\adb.exe`.
- **Unity is at `D:\Unity\6000.5.8f1\Editor\Unity.exe`.** The Editor is
  usually open and holds the project lock, so batch-mode builds fail.
  The Unity MCP bridge drives the open Editor instead and works well;
  `EditorApplication.ExecuteMenuItem("Tools/Mini GTA/13. Build Android
  APK")` called synchronously is the reliable way to build.
  `EditorApplication.delayCall` does NOT fire while the Editor is
  backgrounded, and is cleared by the domain reload that follows a
  script import.
- **After editing a builder, force the reimport before calling it.** The
  MCP bridge compiles its own throwaway assembly, which does not pull in
  your edit, and `AssetDatabase.Refresh()` alone often does not either.
  Use `AssetDatabase.ImportAsset(path, ForceUpdate | ForceSynchronousImport)`
  then `CompilationPipeline.RequestScriptCompilation()`, and check the
  timestamp on `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll`
  before trusting the run. A whole rebuild silently used stale code once.
- **Play mode over the MCP bridge is fragile.** Each dynamic compile can
  restart the play session (watch `Time.frameCount` reset), and
  `Application.runInBackground` is reset to the PlayerSettings value on
  every entry -- which Phase 7 deliberately set to false. Set it again
  after entering play mode or the player loop does not tick at all while
  the Editor is backgrounded. Also: the game boots into the main menu with
  `timeScale = 0`, so nothing moves until MainMenu's Continue is invoked.
- **`Library\Bee` must stay junctioned to D:. It was lost AGAIN.** At the
  start of the Phase 9 session Bee was a real 5.5 GB directory on C: and
  C: had **1 GB free**, which made the build backend fail with "Internal
  build system error" and left a corrupt AssetDatabase entry -- a new .cs
  file Unity refused to add to Assembly-CSharp until it was recreated at a
  different path. Moved back and re-junctioned; C: now 6.3 GB free.
  Check this first if scripts mysteriously will not compile.
  `robocopy "Library\Bee" "D:\unity_build_cache\gta_Bee" /MOVE /E` then
  `mklink /J "Library\Bee" "D:\unity_build_cache\gta_Bee"`.
- **The Bee build backend can wedge and silently keep the OLD assembly.**
  It logs "Internal build system error ... backend process appears to still
  be running", the Scene view says "All compiler errors have to be fixed
  before you can enter playmode", and yet the console lists **no CS errors**
  -- because there are none. Compilation simply never completes, so code
  edits appear to have no effect and play mode is blocked. Confirm by
  comparing timestamps: `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll`
  against the .cs you just edited. The fix that worked was deleting Bee's
  build state (all regenerable) while the Editor stayed open:
  `Library/Bee/TundraBuildState.state*`, `tundra.digestcache`,
  `bee_backend.info` and `*.dag*`, then requesting a compile again.
  Do NOT assume your edit had a syntax error -- check for CS errors first.
- **Runtime `onClick` listeners die on every MCP `RunCommand`.** The bridge
  compiles a throwaway assembly per call, and the domain reload that follows
  wipes non-persistent UnityEvent listeners while leaving the GameObjects
  and their serialised references intact -- so every button wired in `Awake`
  is dead from the second command onward, and `Awake` never runs again to
  re-add them. **This looks exactly like the Phase 8 dead-button bug and is
  not it.** Confirm by adding a probe listener in the same command (it
  fires) or by testing a Phase 7 panel (PauseMenu's RESUME and Settings'
  BACK fail identically). Drive a whole click journey inside ONE command.
  No frames elapse inside a command either, so force `CanvasGroup.alpha = 1`
  before capturing an opened panel or you photograph a transparent one.
- **No editor capture applies the safe area, so no capture ever showed a
  notch.** `SafeAreaFitter` runs in `Awake`/`Update`, neither of which exists
  in edit mode, so every screenshot this project took before Phase 13b was of
  a canvas with no inset -- which is how a backdrop that stopped at the safe
  area survived a whole re-skin. `Tools > Mini GTA > 6c` simulates the inset
  by hand; use it, or a capture is only testing the easy case.
- **Nothing that polls in `Update` has run by the time an editor command
  captures.** No frame elapses inside an MCP command, so `SafeAreaBleed` (and
  anything like it) has to be driven by hand -- `PhaseCapture.UiShot` now calls
  `Apply()` on every one before it renders. Same class of trap as the dead
  `onClick` listeners below.
- **`PhaseCapture.UiShot` does not render the world behind the UI** under
  URP. Its overlay camera clears depth-only to preserve the colour the main
  camera wrote, and the URP render graph clears colour regardless, so the
  backdrop comes back as the camera's flat background colour. Harmless for
  every opaque screen. Use `PhaseCapture.Shot` to photograph the world -- it
  is correct, but it draws no UI.
- **To photograph the HUD over the city -- what the player actually sees --
  put the overlay canvas on the main camera for one render** (found in Phase
  13; before that no capture in this project had ever shown both at once):

  ```csharp
  var mode = canvas.renderMode;
  try {
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = Camera.main;
      canvas.planeDistance = 0.5f;
      Canvas.ForceUpdateCanvases();
      PhaseCapture.Shot("city_hud", 2400, 1080);
  } finally { canvas.renderMode = mode; Canvas.ForceUpdateCanvases(); }
  ```

  Run it in play mode, inside one command, in a `try/finally`. It found three
  defects in its first frame that no editor capture and no assertion could
  have. The canvas plane does not cover the whole frustum, so expect a band
  of unmasked world at the edges -- that is the technique, not a UI bug.
- **In edit mode every modal sits at `CanvasGroup.alpha = 1`.** The builders
  add the group; each panel's `Awake` is what drives it to 0. So an edit-mode
  capture shows every screen stacked on top of every other unless the script
  opens exactly one -- two Phase 13 captures caught three panels at once
  before it was noticed. Not a defect, and the play-mode captures are the
  ones that prove the runtime state.
- **Git Bash mangles adb device paths** (`/sdcard/x.png` becomes
  `C:/Program Files/Git/sdcard/x.png`). Use PowerShell for adb.
- **The test phone sleeps and locks.** Device verification needs it
  unlocked with Developer options > Stay awake on.

## Device Info
- Test device: vivo I2019, Android 14 (API 34), arm64-v8a, 8 cores, 7.4 GB
- Display 1080x2400, 82px centred cutout; game renders 2318x1080 landscape
- Canvas on device: scaleFactor 1.0682, 2170 x 1011 reference units
- APK: Builds/Android/MiniGTA-0.2.0.apk (125.8 MB, ARM64, versionCode 6)

---


---

## Session update — 2026-09-03 (Phase 14, sections 0-2)

### Done and verified this session
- **BUG-012** death animation — `RagdollLite.Collapse` was disabling the Animator in the same
  frame `Dead` was raised. Two-stage collapse now plays the clip, then goes physical. `3dbd563`
- **BUG-014** armed NPCs had no melee at any range. `HostileNpc` now swings inside
  `MeleeRange`, mirroring `PlayerCombat.Melee` exactly. `38c5286`
- **BUG-017** `WeatherSystem` threw a NullReferenceException *every frame* from a cached
  `ParticleSystem.EmissionModule`. `553839f`
- **BUG-018** — **no render pipeline was assigned at all.** See below. `80c4282`
- **2.4** shared muzzle flash / tracer / impact VFX, pooled, procedural, no new assets. `c8e68eb`
- **PHASE14.md** created — the bug log now has a durable home. `7c7a222`, `9e1ae62`

### READ THIS BEFORE SECTION 3
Section 3 is NPC scaling to 300-1000, which is a pure performance section. **Its premises just
changed.** `PerformanceTuner` resolves its URP asset as
`QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline`
(`PerformanceTuner.cs:160`, `:184`). Both were null, so **every PHASE7 tier setting -- shadow
distance, render scale, MSAA, cascades -- has been a no-op.** All the numbers in PHASE7's tier
table were measured against settings that were never applied.

Re-measure the PHASE7 baseline *before* adding NPCs, or Section 3's numbers will be
uninterpretable.

### Exact next step
1. Re-run the PHASE7 renderer/frame baseline now that URP is actually active.
2. Then Section 2 remainder: 2.6 (aim-movement blend), 2.10 (end-to-end pass).
3. Then Section 3.

### Still open
- BUG-009 (attack input destroyed during cooldown), BUG-013 (`Health.Heal` accepts negatives),
  BUG-016 (24 `Bld_Pack` animators with no controller, benign).
- **Asset gap:** no equip/draw/holster/switch clip exists in the pack, so 2.2/2.7 cannot play
  one. Currently substituted with timed `EquipSeconds` states. Needs an asset or sign-off.
- Nothing in Phase 14 has been on the device. Per CLAUDE.md 5 none of it is verified.

### Disk (measured this session)
`Library` 12 GB, `Builds` 3.7 GB, `Temp` 60 MB, project total 20 GB. `.gitignore` excludes
15.7 GB of that. Moving `Library` to D: frees 12 GB and is still the highest-value disk action
before an IL2CPP build.

## Session update — 2026-09-03 (later): PHASE7 re-baseline + Section 3

### PHASE7 re-baseline — done, `fad9063`
- **BUG-019**: the `Detail` layer never existed (layer 8 was still named `MainBall`), so
  `ApplyCamera` returned early and detail culling never ran either. Renaming layer 8 fixed 850
  renderers with no scene edits. PHASE7's "31% reduction" is real once it actually runs:
  Low 273→183, Medium 1123→803, High 1692→1246.
- `PerformanceTuner.ResolvePipeline()` now logs a CRITICAL error naming the fix instead of
  returning silently. **Verified by removing the pipeline on purpose** — it fires on the real
  startup path. Pipeline restored and confirmed against git.
- **PHASE7.md now carries a VOID banner.** Do not use any number in it.

### Section 3 — done, `749ffa0`
**300 pedestrians now cost less than 66 did before the work** (13.61 ms vs 14.38 ms).
1000 runs at 29.23 ms with 416 drawn.

The blocker was never rendering: `Pedestrian.Separation()` was O(n²) — a million distance
checks a frame at 1000. `CrowdGrid` (spatial hash) plus `CrowdDirector` (capped LOD bands,
pavement placement, recycling) fixed it.

**Recommended shipping default: `TargetPopulation = 300`.** 1000 is stable but ~34 fps in the
Editor on desktop, which will not hold on a phone.

### The lesson worth carrying forward
Six bugs this session were **silent early-returns or stale cached state** — BUG-018, BUG-019,
BUG-022, BUG-024, plus the two in my own first drafts of the director. Every one of them
reported healthy counters while doing nothing. Two (BUG-023 T-pose, BUG-024 invisible crowd)
were only ever going to be caught by rendering a frame and looking at it.

### Exact next step
Section 4 (police/crime), then 5-10. Section 2 still owes 2.6 (aim-movement blend) and 2.10.

### Still open
BUG-009, BUG-013, BUG-016, BUG-020 (unused Toon shaders fail to compile, benign).
Crowd limitations: every clone shares one character model; recycling converges slowly at the
shipping rate of 3/frame.

**Nothing in Phase 14 has been on the device.** Per CLAUDE.md §5 none of it is verified, and
the headroom is now known to be thin.

## Session update — 2026-09-03 (later still): first device build

### Built, not yet measured
`Builds/Android/MiniGTA-0.3.0-dev.apk` — 169.7 MB, IL2CPP, ARM64, 19m51s, **development
build** (debug-signed). Verified by extracting `AndroidManifest.xml` from the APK itself:
package is `com.minigta.city`, no HelicopterAttack. Build reported 2 errors, both pre-existing
and harmless (batch-mode licensing handshake, the URP FilmGrain package-cache complaint).

**The on-device measurement did not happen — the phone was never connected.** `adb devices`
stayed empty for 40 minutes and Windows reported no Android USB device present at all, so it
is a cable/USB-debugging matter rather than an authorisation one.

### Two more config bugs, same family as BUG-025
Both were invisible in an interactive Editor and only a clean batch process exposed them.

- **BUG-025** — the build contained the HelicopterAttack demo scenes, not `City.unity`.
- **BUG-026** — signing pointed at `D:/JoySmashProjects/keystore/bundle.keystore`: another
  project's keystore, on the nearly-full D: drive, and **the file does not exist**.

`Assets/Editor/AndroidBuild.cs` now owns builds. It passes the scene list explicitly, re-asserts
identity every time, prints what it is building on every run, refuses to build without
`City.unity`, debug-signs development builds and fails loudly on release signing rather than
silently falling back.

### The D: question, answered
**Every write lands on C:** — project, `Library`, `Temp`, `Builds`, `%TEMP%`, `~/.gradle`, the
Editor log, GI cache. **D: is read-only**, holding only the toolchain (SDK, NDK, OpenJDK,
Gradle 9.1.0), confirmed against the build log's own tool-path dump. `targetSdk` is pinned to
android-36 so Auto can never fetch a platform onto D:. C: sits at ~33 GB free.

### Blocker for shipping (not for measurement)
**There is no usable release keystore.** One has to be created for `com.minigta.city` or the
original located, and Publishing Settings pointed at it. If the original is lost and the app
was ever published, the package cannot be updated under that identity.

### Exact next step
1. Connect the vivo I2019 (data-capable cable, USB debugging, authorise, Stay awake).
2. `adb install -r -d Builds/Android/MiniGTA-0.3.0-dev.apk`
3. Launch, wait past the 12 s warm-up, read `[BENCH]` from `adb logcat`.
4. If 300 holds, Section 4. If not, stop before police scaling.

## Session update — 2026-09-03 (final): Sections 6, 7, 8, 9

Section 4 remains **gated** on a real on-device 300-population confirmation, as instructed.

### Done
- **Section 9 — PASS.** 67 cheat codes, 0 duplicates, both gates verified. `e81478d`
- **Section 6 — PASS, visually verified.** Harbour: explorable ship, wreck, two piers, bridge.
- **Section 7 — code complete, UNVERIFIED.** `HelicopterController`; one stick, altitude
  buttons, auto-yaw. Needs to be flown.
- **Section 8 — finding + tooling.** BUG-028; needs an icon from the project owner.

### Three new bugs
- **BUG-027** asset-pack materials still on Built-in shaders draw magenta under URP. BUG-018's
  long tail — the 401-renderer sample only saw what was *already in the scene*. `UrpMaterialFixer`
  converts them.
- **BUG-028** the application icon was the HelicopterAttack pack's store icon, all 18 Android
  slots empty. Cleared; a real icon is still needed.
- **BUG-026** (keystore) deliberately left OPEN pending the owner's decision.

Three settings — scene list + package, keystore, icon — were all silently overwritten by one
asset pack import. Worth assuming there are more.

### THE MEASUREMENT WAS NOT TAKEN
No fresh Editor number and **no worst-frame figure at all**. Four routes, all blocked:
1. MCP bridge needs a human to approve the connection.
2. `-batchmode` accepts `EnterPlaymode()` then idles; it does not pump the Play loop.
3. PlayMode tests need an asmdef, which cannot reference `Assembly-CSharp`.
4. Emulator would need a system image on D:, which has 1.2 GB free.

The only figure available is **300 pedestrians at 73.5 fps / 13.61 ms**, Editor, desktop,
measured earlier in this session. **Provisional. Not a pass.**

### What batch mode can and cannot do
Edit-mode `-executeMethod` works, with two traps: a launch that recompiles scripts **silently
skips the method** (run it twice), and batch mode opens an **empty scene**, not the game. Camera
captures *do* work in edit mode, which is how the harbour was checked by eye.

### Exact next step when the laptop is back
1. Connect the vivo I2019, install `Builds/Android/MiniGTA-0.3.0-dev.apk` (already built,
   debug-signed, verified as `com.minigta.city`).
2. Read `[BENCH]` from logcat — average **and worst frame**.
3. If 300 holds, Section 4. If not, stop before police scaling.
4. Fly the helicopter and exercise the cheat codes in Play mode — neither is verified.
5. Supply `Assets/Game/UI/Branding/app_icon.png`, then `Mini GTA ▸ Icons ▸ Apply game icon`.
6. Decide BUG-026 (keystore) — check backups before assuming it is lost.

## Session update — 2026-09-05: Sections 2.6, 5 and 4

### The thing that changed everything this session
**The Unity MCP bridge is live.** The previous two sessions were blocked on it needing a human to
approve the connection, which is why Section 7 was never flown and no measurement was taken. Play
mode can now be driven end to end. Everything below was verified in play mode, with rendered
frames, not parameter dumps.

### Done and verified
- **Section 2.6 — PASS.** The weapon stance blends over locomotion on its own `AimPose` layer,
  placed between the base layer and the one-shots. 5,326 sampled frames, 0 defect frames, stance
  up over Idle/Walk/Run/Jump/Fall, per-frame ramp 0.833 against a 0.853 ceiling. `3ff761e`
- **Section 5 — PASS.** Carjacking, with a driver who was visibly in the seat first. Driver
  ejects and flees, 14 bystanders panic, heat +22.0 exactly, and a parked player-owned car now
  drifts 0.01 m instead of being teleported away. `edbd09b`
- **Section 4 — PARTIAL.** 4.1, 4.3 and 4.4 pass; 4.2 deliberately not done, see below. `33279c5`
- **New tool: `Mini GTA ▸ Verify ▸ Animation stance sweep`**, a play-mode sampler for the
  zombie-arms / T-pose bug class that has now shipped here twice.

### Four new bugs
- **BUG-029** rebuilding the animator controller nulls every `Animator` in the *open scene*.
  526 animators, 0 on `PlayerLocomotion`, player with no controller — and the scene on disk is
  perfectly fine. Reopen `City.unity` and all 79 references come back. **Read this before
  concluding the animation system is broken.**
- **BUG-030** `TrafficSpawner` teleported a parked player-owned car to a fresh lane node the
  moment the player walked 260 m away. Fixed.
- **BUG-031** `Destroy` is deferred to end of frame, so components stripped off a cloned body
  stayed live and kept writing animator parameters. Disable first, destroy second.
- Not a bug but it cost time: **`MenuState.ForceClear()` does not restore the world camera's
  culling mask.** The lobby zeroes it (D41) and only PLAY restores it, so every play-mode capture
  after a `ForceClear` renders sky and bare ground. Set `Camera.main.cullingMask = ~0` first.

### The lesson worth carrying forward
The verification tool I wrote to catch the zombie-arms bug **returned a confident PASS on its
first run having never once sampled the player** — it capped at 60 rigs and filled them with
unarmed pedestrians whose aim layer is legitimately flat at zero. Then, once fixed, it reported
55 defect frames on a rig that was demonstrably fine, because it tested the bug's *signature*
(a layer at weight with no clip) rather than its *cause* (`writeDefaultValues` on). Settling that
took pinning the layer at full weight and photographing the result.

Both failure modes are the same one this project keeps hitting: **a measurement that cannot say
what it looked at is not evidence.** The tool now reports INCONCLUSIVE rather than PASS when it
cannot show it saw the subject.

### Needs your decision (blocked, not guessed)
1. **Section 8 app icon** — still needs `Assets/Game/UI/Branding/app_icon.png` from you.
2. **Fatality font** — still absent.
3. **No equip/draw/holster clip** in the pack (2.2/2.7). Timed states substitute.
4. **NEW: no seated clip either.** `Sit` plays `HumanM@MilitaryIdle01`, a standing idle — nothing
   in this project can sit down. The carjack driver is therefore a standing figure in a car seat.
5. **BUG-026 keystore** — release blocker, check backups before assuming it is lost.
6. **4.2, Military FREE has no police vehicle and no police character** — a Hummer, a tank and a
   helicopter. Forcing a tank into a two-star city response would look wrong, so it is flagged
   rather than forced, as the brief asks. Proposal for sign-off: the Hummer as a five-star
   military escalation unit, and the pack's barriers/sandbags/crates for the 3B.6 checkpoint zone.
   The pack is referenced **0 times** in `City.unity` today.

### Exact next step
1. **Section 3B** — character variety. This is the most visible remaining gap: every crowd
   screenshot this session shows 300 copies of the same person. Raw material is better than the
   docs suggested: `Assets/characters/` holds **13 Mixamo humanoid FBX** (Arissa, Remy, Kachujin,
   Lola, Ch02–Ch31) plus 3 Shady_3d characters and the Kevin Iglesias soldier bodies. The 5
   PolygonalAssets "lite" models stay excluded — no skeleton at all (D7).
2. **Section 10** — global environment pass, which overlaps 3B.5 and 3B.6.
3. **Section 2.10** — the end-to-end weapon pass.
4. **Section 7** — fly the helicopter. Now actually possible with the bridge live.
5. **Still no device pass.** `adb devices` was empty all session; the phone was never connected.
   Per CLAUDE.md §5 nothing in Phase 14 is verified until it runs on the vivo I2019.

### Disk
C: 15.4 GB free, D: 1.2 GB free. No build attempted this session.

## Session update — 2026-09-05 (later): the audit fix-up pass

Driven from `PROJECT_ANALYSIS.md` / `asset_inventory.csv` (this session's earlier read-only
audit, both now in the repo root). Six commits, `f112631` through `96183a0`.

### The headline: three systems existed and were attached to nothing
- **`WeaponController`** — the whole Section 2 state machine, on zero GameObjects. The game ran
  the Phase-7 two-field weapon and **five cheat codes returned early every time**. Now on the
  player, owning ammunition, slots and legality; `PlayerCombat` keeps the ray, damage, tracer
  and crime. `FireBallistics` is shared by both paths so they cannot drift.
- **`CheatConsole`** — 67 verified cheats with no way to enter one on Android, which has no
  keyboard until something asks for one. New `CheatPanel` + `CheatGesture`: five taps on an
  invisible top-left corner opens a plain modal.
- **`HelicopterController`** — left alone this pass, as instructed. Still attached to nothing.

### The mistake worth reading before the next session
**I committed a broken `City.unity` in Part 1, and BUG-029 is exactly why.** Rebuilding the
animator controller nulls every `Animator` reference in the *open* scene. The documented rule is
to reopen before trusting a reading — I did that. What I did not do is reopen before **saving**,
and the Part 1 save wrote 78 null controller references into the scene. Measured across commits:
79 refs before the session, 1 in Part 1, 1 in Part 2.

Repaired in `bb7b042` by restoring the scene from the last good commit and re-applying all three
changes from their builders. **The rule is now: after ANY animator rebuild, reopen the scene
before touching it — reading or writing. A read gives a wrong answer; a write makes it permanent.**

### A device-only bug caught before it shipped
The crowd's cost weighting used `mesh.triangles`, which returns an **empty array on a
non-readable mesh in a player build**. All ten character meshes import with Read/Write disabled.
The editor keeps a CPU copy so it read correctly here — correct on this machine, silently uniform
on the phone, putting six times the intended triangle load on the device and nowhere else. Now
`vertexCount`, which needs no CPU copy.

### Known regression, accepted and reported
**Crowd variety costs ~22% of worst-frame headroom at 300**: ~34.7 ms → ~42.5 ms in the Editor.
Average still holds the 30 fps cap. Nine models simply cost more than one; cost weighting
(cheap bodies 78% of the crowd) reduced it but cannot erase it. Knobs are exposed:
`CrowdDirector.VarietySeed`, `NoImmediateRepeat`, and the one-line cost function.

### A control change you may want to veto
The 2D locomotion blend was correct and **had nothing to do**: `PlayerController` rotates the
body to face travel, so local velocity is always straight ahead and this game had no strafing at
all. The 21 directional clips were reachable only in principle. So while the weapon stance is up
the body now faces the camera and the stick strafes. One boolean:
`PlayerController.StrafeWhileAiming = false` restores the old behaviour.

### Cleanup
5,653 files / 2,344.6 MB → **5,429 files / 1,801.9 MB**. Deleted the four unused Mixamo
characters, `_Recovery`, `SoftTouch_UI`, `Low Poly Weapons VOL.1`, 69 demo scenes and
`DeviceDiagnostics.cs`. Moved the pirate pack out of `Assets/Scenes` via `AssetDatabase.MoveAsset`
so its GUIDs survived. **City.unity's dependency count is 674 before and 674 after**, the only
difference being ten files at their new path, and 0 unresolvable.

### Still open, unchanged
- **BUG-026 keystore** — release blocker, needs your original file.
- **No seated pose, no equip/holster clip, no app icon, no Fatality font** — all need assets.
- **Female animation set** — 26 `HumanF@` clips unused; female characters run male animation.
- **`HelicopterController` and the HelicopterAttack pack** — still unwired, by instruction.
- **Toon City Pack** — 475 files, still unplaced, by instruction.
- **`characters/Textures`** is still ~590 MB of source art for a phone game.
- **Nothing in Phase 14 has run on the device.** `adb devices` empty all session.

### Exact next step
1. Device pass. Everything above is Editor-only, and the crowd-variety frame cost specifically
   needs a phone number, not a desktop one.
2. Then Section 10 (environment) or the helicopter, once you have decided on assets.

## Session update — 2026-09-05 (later still): device attempt + BUG-026 resolved as a question

**No code changed this session.** Diagnosis and investigation only.

### Device: still not connected, and now precisely diagnosed
`adb devices` empty. adb itself is fine (1.0.41, platform-tools 37.0.1, at the documented
`C:\platformtools\platform-tools\adb.exe`). Server killed and restarted — still empty.

Windows PnP tells the rest of the story:
- **No Android device is present.** The only `Status = OK` USB devices are a webcam
  (VID_5986, enumerating with the confusing name "APP Mode"), a Dell keyboard, Lenovo and
  Foxconn Bluetooth. Nothing with an Android VID.
- **The phone HAS been connected before and its drivers are installed.** Twelve remembered
  (`Status = Unknown`, i.e. not present) entries under vivo's `VID_2D95`, all sharing serial
  `1394498210000I9`, across four USB modes: `600A` RNDIS, `600B` RNDIS+ADB, `6012`
  MTP+MassStorage, `6013` MTP+MassStorage+**ADB**. `6013` is the mode a device pass needs.
- PnP names the device **iQOO 9 SE**, which is vivo's name for the I2019 the docs record. Same
  phone, two names -- not a discrepancy.

So this is not a driver problem and not an authorisation problem. **The phone is simply not
plugged in, or is on a charge-only cable.** Nothing on this machine can fix that.

No emulator fallback either: no `~/.android/avd` directory exists, and D: has 1.21 GB free,
which will not hold a system image.

**Everything in Phase 14 remains Editor-only. The crowd-variety worst-frame regression
(~34.7 ms -> ~42.5 ms at 300) is still an unvalidated desktop number.**

### BUG-026 -- answered. It is the EASY case.

The long-standing open question was "lost keystore for a published app" (serious) versus
"keystore never set up" (trivial). It is decisively the second.

Evidence:
- `AndroidKeystoreName = D:/JoySmashProjects/keystore/bundle.keystore`,
  `AndroidKeyaliasName = 1`, `androidUseCustomKeystore = 1`.
  **Passwords are not set** -- `keystorePass` and `keyaliasPass` are both empty.
- `D:\JoySmashProjects` **does not exist at all** -- not the file, not the folder, not the
  parent. Nothing of that name is anywhere on D:.
- Filesystem search of C:\unity_games, D:\ and the whole user profile for
  `*.keystore *.jks *.p12 *.pfx` found **no release keystore anywhere**. The only real one is
  `C:\Users\Mohit\.android\debug.keystore`.
- **Every APK this project has ever produced is debug-signed.** All seven builds (0.1.0
  through 0.3.0-dev) carry `C=US, O=Android, CN=Android Debug`, SHA-256
  `6CF78D7D...C569ECF4` -- which is an exact match for this machine's `debug.keystore`,
  created 25 Aug 2026, one day before the first APK on 26 Aug.
- No keystore file was **ever tracked in git**, and no `.md`/`.txt`/log in the repo records a
  backup location. Every mention of the keystore is this project documenting the problem.
- The setting appears exactly once in git history, in the initial snapshot `5fb49fc`, and has
  never changed since. (Git history only starts 2026-09-03; the project predates it.)

**Conclusion: nothing has ever been signed for release from this machine, so nothing could
have been uploaded to a store from it.** `com.minigta.city` is almost certainly unpublished.
The `JoySmashProjects` path is a leftover from another project -- the same import-clobber
family as BUG-025 (scene list, package name) and BUG-028 (app icon): three settings
overwritten by one asset-pack import.

The residual uncertainty is the one this machine cannot answer: if the app were ever published
from a **different** computer, the key would matter. Only the project owner knows that.

**Recommendation: generate a fresh keystore for `com.minigta.city` and back it up.** Not done
-- deliberately left for the owner's decision, as instructed.

### Exact next step
1. Plug the phone in (data-capable cable, USB debugging on, authorise the prompt, Stay awake).
   `adb devices` should then show `1394498210000I9  device`.
2. Then the whole Part 2 device pass: build via `AndroidBuild`, install, `[BENCH]` from logcat
   at population 300, and the smoke tests (strafing, WeaponController fire/reload, the 5-tap
   cheat gesture on a real touchscreen, vehicle enter/exit).
3. Keystore: owner's decision. If "generate new", it is a ten-minute job and unblocks release.

## Session update — 2026-09-05 (final): keystore closed, mobile textures, gap audits

No device this session, by instruction. Editor-side only.

### BUG-026 is CLOSED
A real release keystore exists and is proven by the artefact, not by the setting.

  alias      minigta-release
  owner      CN=Mini GTA, OU=Development, O=MiniGTA, C=IN
  valid      05 Sep 2026 -> 27 Aug 2061 (~35 years)
  SHA-256    D1:CF:9E:38:...:42:75:F1:B2
  location   C:\unity_games\minigta-keystore\   (OUTSIDE the repo)

`MiniGTA-0.3.0.apk` now verifies as `CN=Mini GTA` / `d1cf9e38...4275f1b2`, against
`CN=Android Debug` / `6cf78d7d...c569ecf4` on every previous build. Read off the APK with
apksigner, the same way the bug was originally diagnosed.

**Passwords are not in the repository and not in ProjectSettings.asset** -- verified by searching
every file in the commit for the password string. Unity holds them for the Editor session only,
so a release build after an Editor restart needs them re-entered in Publishing Settings. That is
the pre-existing design; `AndroidBuild.ApplySigning` already fails loudly rather than falling
back to the debug key. See `RELEASE_KEYSTORE_README.md`.

**The owner must back up `C:\unity_games\minigta-keystore\` before the first release.** Losing it
means a published app can never be updated.

### Mobile texture compression -- 23.3% off the APK
The audit was the finding: 143 textures under `Assets/characters`, all at maxTextureSize 2048,
and **not one had an Android override**. Nor did anything else -- 2,668 other textures scanned,
zero overrides. The ETC2 setting CLAUDE.md section 5 requires had never been applied to anything.

Applied to all 143: Android override, ETC2, max 1024 (127 ETC2_RGB4 opaque, 16 ETC2_RGBA8 with
real alpha, detected not assumed). Source files untouched -- import settings only.

  release APK before : 120.30 MB
  release APK after  :  92.22 MB
  saved              :  28.08 MB (23.3%)

Both builds are the same code and the same scene, minutes apart, so the delta is the textures.

**ETC2 rather than ASTC deliberately.** ASTC is very likely better on an ARM64-only target, but
CLAUDE.md section 5 names ETC2 as the project standard and that is not a call to make inside a
texture-import commit. **Open question for the owner.**

Verified: 143/143 overridden, 0 unresolvable textures, 601 skinned material slots all still
bound, 0 materials on the error shader, dependency closure **674 -- exactly the baseline**.

Honest limit: the Editor capture that confirmed nothing broke does NOT exercise ETC2. Android
overrides only apply on an Android build target and this Editor is D3D11, so what was
photographed is still the 2048 default. Reference integrity verified; compression *quality* is
unverified and needs a device or an Android-target Editor.

### Two asset gaps scoped, not faked -- `ASSET_GAPS.md`
- **Seated pose**: visible in 2 player vehicle types (Bike, Boat -- both `HideOccupant=false`)
  and up to 10 concurrent carjack drivers. **Not** a gap for cars: 7 of 9 vehicle prefabs hide
  the occupant entirely. Plus 216 seat-like props no NPC can use, because `Pedestrian` has no
  sitting state at all.
- **Equip/holster**: 3 `Equipping` entry points, 1 `Switching`, 1 holster path -- all timer-only.
  Firing and Reloading DO animate; the gap is specific to getting the weapon in and out of hand.
  Dead time a clip would fill: 0.30 s (knife) to 0.90 s (sniper).

Neither was approximated from existing clips. An approximation would look wrong in a way that is
harder to catch in review than an obvious absence, and would hide the gap from the next audit.

### Noticed, not actioned
`Assets/characters/Textures/` still holds textures for **Ch15, Ch21, Ch29 and Remy** -- the four
characters deleted last session. The FBX files went; the shared Textures folder kept its copies.
They are unreferenced. Not deleted this session because cleanup was not in scope.

### Still open
- **Toon City Pack** -- 475 files, unplaced. Owner's decision, explicitly out of scope.
- **Seated pose, equip/holster, app icon, Fatality font, female animation set** -- all need assets.
- **ASTC vs ETC2** -- new question, above.
- **No device pass.** Everything in Phase 14 remains Editor-only, including the crowd-variety
  worst-frame regression (~34.7 -> ~42.5 ms at 300) and now the ETC2 quality question.

### Exact next step
1. Back up `C:\unity_games\minigta-keystore\`.
2. Device pass when the phone is available: `adb devices` should show `1394498210000I9`.
   Install `Builds/Android/MiniGTA-0.3.0.apk` (release-signed, 92.22 MB) or a fresh dev build.
3. On device: `[BENCH]` at 300, the ETC2 quality check, and the smoke tests still outstanding
   (strafing, WeaponController fire/reload, the 5-tap cheat gesture, vehicle enter/exit).
