# Decisions

Assumptions made and choices taken without asking, per the Autonomy Rule in CLAUDE.md.
Newest last. Each entry says what was decided, why, and what would reverse it.

---

## Phase 8 — 2026-08-31

### D1. Vulkan kept, pre-transform turned off
`vulkanEnablePreTransform = false` rather than dropping Vulkan for GLES3.

Pre-transform was the root cause of the broken main menu (see [PHASE8](PHASE8.md)). Removing
Vulkan entirely would also have fixed it, but costs the Vulkan backend on every device to work
around a bug in one driver's handling of one setting. Turning the setting off keeps Vulkan and
loses only the pre-rotation optimisation, whose value is a small amount of composition work.

*Reverse it by* setting it back to `true` in `MobileSetup.ConfigureAndroid` — but only after
re-testing the main menu on a real phone, because no editor reproduces the failure.

### D2. HUD buttons wired at runtime, not with persistent listeners
The three dead buttons were fixed by adding an `OpenButton` reference field to `PauseMenu`,
`FullMapScreen` and `StorePanel` and wiring `onClick` in each panel's `Awake`.

The alternative was `UnityEventTools.AddPersistentListener` in the editor builders, which would
serialise properly. Runtime wiring was chosen because every button in the project that already
worked is wired that way, so this makes the codebase consistent rather than adding a second
mechanism — and because a serialised listener is invisible in a diff, whereas a line in `Awake`
is reviewable.

*Reverse it by* switching the builders to `AddPersistentListener`. Do not go back to plain
`onClick.AddListener` from an editor script under any circumstances: that is the bug.

### D3. Safe area as one container, not per screen
A single `SafeArea` RectTransform between the canvas and all 18 UI roots, carrying
`SafeAreaFitter`, rather than a fitter on each screen.

A notch does not care which panel is open, and one inherited container cannot be forgotten when
screen 19 is added. Verified as a no-op on the test device (Android letterboxes the cutout away
because `renderOutsideSafeArea = false`), and verified not to regress anything.

*Reverse it by* deleting the container and reparenting the roots to the canvas — but note that
sibling order is hit-test order and must be preserved exactly.

### D4. Hardcoded pixel sizes left as they are
The task asked for all UI to be re-anchored relatively. Measured instead: across the entire
landscape aspect range the game can present (4:3 through 21:9), **zero** elements overflow the
canvas. The app is landscape-locked, so nothing narrower than 4:3 can occur.

Rewriting 92 centre-anchored elements would have been a large refactor, unverifiable in its
benefit and with real regression risk, to fix a problem that does not occur on any reachable
configuration. Recorded in PHASE8 as residual risk rather than silently marked done.

*Reverse it by* re-anchoring if portrait is ever enabled, or if a reference resolution change
shrinks the canvas below ~1200 units wide.

### D5. Legacy `UI.Text` kept; TextMeshPro not adopted
The text striping was caused by the pre-transform bug, not by the font atlas, and disappeared
when the transform was fixed with the font untouched.

The dynamic atlas is still a genuine latent risk (92 labels, 22 sizes, one runtime-rebuilt
atlas). Converting to TMP is the correct long-term fix, but TMP Essentials are not imported and
the conversion touches all 92 components — too large a change to make on the back of a bug it
turned out not to have caused.

*Reverse it by* importing TMP Essentials, generating an SDF asset for the project font, and
converting the labels. Worth doing before adding substantially more on-screen text.

### D6. Diagnostics deliberately left in the 0.2.0 build
`DeviceDiagnostics` logs screen/canvas geometry, tap raycasts and font-atlas rebuilds to logcat.
It is instrumentation, not a feature, and stays only so the next device session starts with the
same visibility. Flagged in PHASE8 as a TODO to delete before shipping — noted here so the
sign-off is explicit rather than implied.

---

## Phase 9 — 2026-09-01

### D7. "Low Poly Characters Lite" not used for pedestrians or police
Street NPCs stay on humanoid rigs (the Apocalyptic model plus three Mixamo bodies). The Lite
pack is used only as shop-window mannequins in the Threads clothing store.

The task asked for the Lite pack to become the crowd. It cannot: the FBX files contain **no
Deformer, Cluster, LimbNode or Skin records at all**, import as Generic with no avatar and zero
SkinnedMeshRenderers, and are modelled in a T-pose. They are static props shaped like people.
A pedestrian in this project has to walk, flinch and ragdoll, and none of that is reachable
without a skeleton — this is impossible, not merely inadvisable.

Automatic skinning to a borrowed skeleton is a real technique, but it is art authoring, it
produces poor joints, and CLAUDE.md §6 says not to generate art. A clothing shop is the one
place in the game where a motionless figure with its arms out is the intended effect, so that
is where they went — all four models, on plinths.

*Reverse it by* buying the full (non-Lite) version of that pack if it ships rigged models, or
by replacing the crowd table in `CharacterCatalog.Crowd` with any other humanoid pack.

### D8. Crowd is a mix, not one model recoloured
Six Apocalyptic entries (three headgear variants × colour tints) plus three Mixamo bodies.

A colour swap does not read at distance; a different build and height does. Six recolours of
one silhouette would have satisfied "multiple colours" while still looking like one person
repeated. The Mixamo bodies cost ~35 k triangles each against ~5 k, which is why they are the
minority rather than the majority — the crowd still dropped from 583,907 to 250,865 triangles.

*Reverse it by* removing the three Mixamo entries from `CharacterCatalog.Crowd` for another
~190 k triangles, accepting a more uniform crowd.

### D9. The Apocalyptic model is scaled to the collider, not the collider to the model
`CharacterCatalog.ApocalypticScale = 0.92`, bringing the 1.95 m model to 1.79 m inside the
existing 1.80 m character controller.

The controller size is what doorways, vehicle seats, step offsets and interiors were tuned
against across Phases 1–5. Growing it to fit the art would have quietly re-opened all of those.
Scaling the art is one number in one place.

*Reverse it by* setting it to 1.0 and re-tuning `CharacterController.height` on the player,
the officer prefab and both pedestrian builders together — they must move as a set.

### D10. The weapon socket is solved from a pose, not typed in
`WeaponSetup.ComputeHandSocket` samples the `Pistol Idle` clip and solves for the rotation that
makes the weapon point where the character points.

Hardcoding hand axes does not survive a rig change — the Mixamo rigs and this one disagree on
every hand axis, and this one's bind pose faces −Z. What they agree on is what aiming looks
like, because that is exactly what the humanoid avatar normalises. Re-running the builder on a
different rig recomputes the number instead of needing someone to notice it is wrong.

*Reverse it by* setting `WeaponSocket.SocketPosition/SocketEuler` by hand in the inspector;
they are plain serialised fields and the derivation only writes them at build time.

### D11. Pack buildings on about a third of lots, not all of them
`CityBuilder.BuildLot` places a Cartoon City building on ~34 % of quarter-lots and keeps the
primitive box elsewhere.

The pack has **four** buildings at 4,486–8,578 triangles each, against ~90 lots. Using them
everywhere costs roughly half a million triangles *and* produces a skyline of four repeated
shapes — worse on both axes at once. A third is enough to break up the boxes. Scale is uniform
and derived from whichever horizontal axis is the tighter fit, and yaw is quantised to 90° so a
footprint cannot overhang the pavement.

*Reverse it by* changing the 0.34 threshold; raising it trades triangles for repetition in both
directions, so it is not a free knob.

### D12. Small props are collider-free so that culling them stays safe
Bins, bushes, grass and small stones are built with no collider and a `Detail_` name prefix;
lampposts, benches, bus stops, fountains, trees, rocks and buildings keep theirs.

Phase 7's rule — never distance-cull anything with a collider, because that leaves an invisible
wall — is a good rule and is **unchanged**. Rather than weaken it to accommodate ~350 new props,
the props were built to qualify: you can walk through a bush, and you cannot walk through a
lamppost. `EnvironmentCatalog.Place` is the single place that decides, so the collider and the
cullability cannot disagree.

*Reverse it by* flipping a prop's `Solid` flag in `EnvironmentCatalog`; it changes the collider
and the culling together, which is the point.

### D13. `DayNightCycle` left untouched
The only sky asset across all seven packs is a static `Skybox/Cubemap`. The scene currently uses
`Skybox/Procedural`, which follows the sun transform automatically. Adopting the cubemap would
freeze the sky at one time of day while the lighting kept moving underneath it — a regression,
not an improvement.

What was added instead is `NightLights`, which switches the Cartoon City buildings' emissive
window shells on after dusk. One component owns all 22, because the decision is the same for
all of them.

*Reverse it by* deleting the `NightLights` object; the shells simply stay off.

---

## Phase 9b — 2026-09-01

### D14. Two clips kept or substituted rather than sourced from the new packs
Neither new pack contains a swim animation or a seated animation.

**Swim** keeps Mixamo `Swimming.fbx`. This is the exception the task explicitly authorised, and
it is the only Mixamo animation left in the project. `MixamoImportSetup` is kept alive partly
to configure it.

**Sit** substitutes `HumanM@MilitaryIdle01`, a braced standing idle with the hands forward at
chest height. There is no seated clip to be had, and the task requires no state be left without
a clip. It is a standing pose used seated and it is wrong if you look hard — but `CarController`
sets `HideOccupant = true`, so it is only ever visible on the motorbike and the boat, where
hands-forward reads as gripping bars or a wheel. The honest alternative was leaving `Sit`
empty, which would T-pose every rider.

*Reverse it by* importing any pack with a seated/driving clip and changing the one line in
`AnimatorBuilder.Build`.

### D15. Locomotion blend thresholds derived from measured clip speed
Idle 0, Walk 0.455, Run 0.750, Sprint 1.000 — not the obvious 0 / 0.33 / 0.66 / 1.

Each clip's authored ground speed was measured by sampling its `[RM]` twin start-to-end
(2.00 / 4.00 / 6.00 m/s). `PlayerController` maps `speed01` 0 → 0, 0.5 → 2.2, 1.0 → 5.8 m/s.
Placing each clip where the character genuinely moves at that clip's authored speed is the
whole anti-foot-slide argument; evenly spaced thresholds would have the character running a
4 m/s cycle while travelling 2.9 m/s. Residual at full sprint is 3.4 %, which is the 6.0 → 5.8
clamp.

*Reverse it by* recomputing from `WalkSpeed`/`RunSpeed` if those change — the constants in
`AnimatorBuilder` carry the arithmetic in a comment. They are not taste.

### D16. The weapon socket's reference pose moved with the animation source
`WeaponSetup.ComputeHandSocket` now solves against `HumanM@Gun_Aim01` instead of Mixamo
`Pistol Idle`.

This was forced, not optional: Phase 9 derives the socket rotation *from a clip*, and that clip
was one of the ones this phase deletes. The new pack poses the hand differently and the derived
rotation moved from `(282.2, 242.0, 217.8)` to `(290.7, 218.9, 319.9)`. Leaving the reference
pointed at a deleted asset would have silently unrotated every weapon in the game; leaving it
pointed at a non-aiming clip would have mis-aimed them, which is worse because it looks
plausible.

*Reverse it by* changing `ReferenceAimClip()`. The fallback chain is deliberate — the aim hold
first, the firing pose second — because both are poses of a character actually aiming.

### D17. `Armed` finally given a producer and a consumer  *(consumer superseded by D19, see below)*
`WeaponSocket` now calls `PlayerAnimation.SetArmed`, and the upper-body layer has an
`ArmedIdle` state playing `HumanM@Gun_Aim01`.

The `Armed` bool was declared by the Phase 1 controller and then set by nothing and consumed by
nothing — dead wiring that PHASE9 correctly identified as the reason the pistol sat in a relaxed
open hand. The socket is the right producer because it is the component that put the weapon
there and already tracks the state every frame. Measured improvement: barrel-vs-forward
alignment 0.94 → 0.99.

This is the one place Phase 9b touched behaviour rather than assets, and it is confined to two
lines in a component Phase 9 created. `PlayerCombat` was not modified.

*Reverse it by* deleting the `ArmedIdle` state; `PushArmedPose` then feeds a bool nothing reads,
which is where it started.

### D18. `MixamoImportSetup` kept, not deleted
The task asked for now-unused Mixamo-specific scripts to be removed. This one is not unused: it
imports the character *models* in `Assets/characters` — three bodies in the street crowd and the
four shopkeepers (D8) — and extracts their embedded textures, without which they render pure
white. Its animation tables were cut to the single retained swim clip and its documentation
rewritten to describe what it actually is now.

Deleting it would have broken the crowd the next time anyone reimported a character.

*Reverse it by* replacing the remaining Mixamo character models, at which point the whole script
and `Assets/characters` (1.8 GB) can go.

---

## Phase 9b-fix — playtest defect fixes (2026-09-01)

### D19. Masked layer weights are driven from code, not serialised on the layer
`UpperBody` ships at `defaultWeight = 0` and `PlayerAnimation.LateUpdate` raises it. The
`ArmedIdle` state added by D17 is deleted; holding a weapon is now a fingers-only `Hands` layer
playing `Human@ObjectGripHands01`, its weight driven by `Armed`.

The alternative was to give the empty `None` state a motion — an idle clip — so the layer had
something to write at weight 1. Rejected: that puts the arms under two authorities at once
(locomotion underneath, a redundant idle on top) and every future locomotion clip has to be
duplicated onto the masked layer to keep them agreeing. Driving the weight means the upper body
has exactly one owner at any instant.

The weight is decided by asking the animator, not by a timer: the resting state carries a `Rest`
tag, so *not resting, or mid-transition* is exactly *an action is on screen*. A timer would need
re-tuning every time a clip length or exit time changes; this does not.

D17's premise still stands — the hand did need to close around the grip — but the shoulders were
the wrong joint to spend on it. Fingers only, so locomotion keeps the arms.

*Reverse it by* setting `defaultWeight = 1` in `AnimatorBuilder` and deleting `LateUpdate`, which
restores the arms-forward pose exactly. Do not.

### D20. Ground decals get 4 cm of clearance, not 1 cm
Crosswalk paint moved from `centre.y + 0.05` to `+0.11`, so it spans `+0.10..+0.12` against a
road top of `+0.06` and lane markings at `+0.06..+0.08`.

The minimum that stops z-fighting on a desktop depth buffer is far smaller, but the target is a
phone with a lower-precision depth buffer at a much longer draw distance, and the value has to
survive the lane markings running straight through every crossing. 4 cm clears both and is still
invisible at any camera height the game uses.

Considered and rejected: URP decal projectors (a per-decal draw cost across 64 junctions on a
mobile tier), and polygon offset via a custom shader (a new material variant to maintain for
paint that is four boxes).

*Reverse it by* changing the one `y` in `RoadNetworkBuilder.BuildCrosswalks`. Anything under
`+0.09` reintroduces the tearing.

### D21. `Detail` culling is prefix-matched, not name-listed
`PerformanceSetup` gained a `DetailPrefixes` list (`Detail_`, `Zebra_`) alongside its exact-name
set.

The crosswalk paint was missed by Phase 7 because the layer assignment was an explicit list of
names and nobody added the zebras to it. A list of names is a list that will be incomplete again
the next time someone adds scenery. Prefixes make the naming convention the contract. The
existing rule that anything with a collider is never culled is unchanged, so this cannot make
something solid disappear.

*Reverse it by* removing entries from `DetailPrefixes`.

### D22. A ground probe ignores anything that can walk
`AmbientAnimal.SampleGround` uses `RaycastNonAlloc`, discards hits belonging to an
`AmbientAnimal` or a `CharacterController`, takes the highest survivor, and clamps it to
`MaxGroundRise` (3 m) either side of the patch the animal was placed on.

A layer mask would have been the tidier fix, but the animal prefabs come from a third-party pack
and re-layering every collider in it is a change that gets lost the next time the pack is
reimported. Component-type filtering asks the question directly and keeps working through a
reimport. The clamp is belt-and-braces: it bounds the damage from any future collider nobody
anticipated, without needing to know what it is.

`SimulationRange` also went 140 → 260 m, because 140 was shorter than the draw distance on every
quality tier and animals were visibly frozen on screen.

*Reverse it by* restoring the single `Physics.Raycast`, which restores animals levitating into
the sky over a few minutes of play.

### D23. Twenty animals live in the city, dealt from a shuffled block deck
A fourth `Zone.City` places strays on the pavement band around city blocks, choosing blocks by
dealing from a shuffled deck rather than picking at random each time.

Phase 9's zones were thematically right and practically useless: everything was in the park, on
the coast or up the mountains, and the nearest animal to the player's spawn was 190 m away, so
in an hour of ordinary play you would never see one. Random-with-replacement block choice was
what clustered them; a shuffled deck guarantees twenty strays land on twenty different blocks.

Total population 43 → 59. The added 16 are animators set to `CullCompletely`, and the census
after the change is 2,003 renderers — inside the Phase 7 tier budget.

*Reverse it by* removing `Zone.City` from the roster in `WildlifeBuilder`.

---

## Phase 11 — UI kit, environment rebuild, traffic lights, gameplay (2026-09-01)

### D24. Asset-pack buildings are the default; the primitive box is a fallback that should never fire
`CityBuilder.BuildLot` now picks a modelled building for every lot and only falls back to a
primitive box if nothing in the catalogue fits. The build logs a warning if the box count is
non-zero. It is currently **zero across all 417 buildings**.

This reverses D11, and the reversal is arithmetic rather than taste. D11's reasoning was that the
only pack available shipped four buildings at 4.5-8.6k triangles, so putting one on every lot
meant half a million triangles for a skyline of four repeated shapes. SimplePoly City ships
**40 models at 132-1,568 triangles, one renderer and one material each** -- a modelled building
now costs about what a textured box costs, and there are enough of them that the skyline does not
visibly repeat. The premise D11 rested on is gone.

*Reverse it by* restoring the `rng.NextDouble() < 0.34` gate in `BuildLot`.

### D25. Everything positioned in the city is expressed as a block index
`CityBuilder` gained `BlockCentre`, `BlockEdge`, `JunctionCentre`, `GridOrigin`, `GridSpan` and
`CentreBlock`, and the shop doors, mission contacts, mission checkpoints, the police-station
fallback spawn and the offshore boat were all rewritten against them.

Those were absolute world coordinates, hand-picked off a 5x5 grid. Expanding to 9x9 moves every
block: three of the four shop doors would have ended up inside a wall, the courier run's four
drops would all have been in one corner of the map, and the "clear the crew out of the park" job
would have been set in the middle of an office block. **None of that would have thrown an error.**
Deriving the positions is what makes the grid size a number anyone can change.

*Reverse it by* re-hardcoding coordinates, which is how this phase started.

### D26. The map is 1600 m and the heightmap resolution did not change
`TerrainBuilder.MapSize` 1000 -> 1600 (2.56x area), `CityCenter` (560,500) -> (896,800) -- the
same normalised spot on the island -- and `CityExtents` 215 -> 360.

`HeightmapRes` stayed at 513, which coarsens the terrain from 1.95 to 3.12 m per texel. That is
the right trade for a phone: the city pad is flattened by `CityFlatness` regardless, so the extra
detail would only have shown on the mountains, and a 1025 heightmap would have roughly quadrupled
terrain patch cost. The island's shape is all in normalised (u,v), so the coast, the beach and the
mountain ring scaled with the map on their own.

*Reverse it by* changing the three constants. `CityExtents` must stay above half of
`CityBuilder.GridSpan` (342 m at 9 blocks) or the outer blocks are built on unlevelled ground.

### D27. Lane markings are one mesh per road, not one object per dash
`CityBuilder.BuildDashStrip` builds all of a road line's dashes into a single mesh.

At the Phase 1 grid a box per dash was about 650 renderers, which was tolerable. At the Phase 11
grid it would have been roughly **2,100 renderers spent on road paint** -- more than the rest of
the city put together, and the single largest line item in the scene. Batching per road gives 20
renderers for identical pixels. This is the main reason the map grew 3.24x while the renderer
count grew 49%.

*Reverse it by* restoring the per-dash `Box` loop, and expect about +2,000 renderers.

### D28. The player starts unarmed; the pistol is a thing in the world
`SceneAssembler` no longer sets `HasPistol = true`. Six `WeaponPickup` objects are placed on block
kerbs, the nearest 38 m from the spawn point, and Interact takes one.

**This changes existing default-armed behaviour, which is why it is logged.** Before this the
player began every first run with a gun already in hand and the armed HUD context on frame one.
Arming is now something that happens at a place, as a result of a press.

Deliberately a proximity prompt and an explicit press rather than a trigger volume: walking over a
gun and silently acquiring it gives the player no moment of choice and no way to decline. The
prompt reuses the same Interact affordance the doors, shops and mission givers already use, so it
needed no new HUD element and no new button. `PlayerCombat` was not modified -- `GivePistol` was
already public and `WeaponSocket` already follows `IsArmed`, so the pickup talks to neither the
socket nor the animator.

*Reverse it by* setting `combat.HasPistol = true` in `SceneAssembler.BuildPlayer`.

### D29. Weather is a state of the day cycle, not a system of its own
`DayNightCycle` owns the schedule (`RollWeather`, once per day, 30% chance) and the lighting
response (`Wetness` damps the sun to 45%, cools the ambient toward the fog colour, and pulls the
fog in by 380 m). `WeatherSystem` only draws the rain and decides nothing.

The split follows what each already owns: the day clock is what schedules weather, and the
lighting is what weather changes -- both were already in `DayNightCycle`. A separate always-on
weather manager would have had to reach into the sun, the ambient and the fog to do anything
convincing.

A per-day roll rather than a per-frame chance, because a per-frame chance makes rain start and
stop constantly; a day is either wet or it is not, which is what a player notices. The particle
system stops rather than idling at zero emission -- it is clear far more often than it is raining,
and an idle system still costs a simulation step and a draw call every frame.

*Reverse it by* setting `EnableRain = false`, which leaves `Wetness` at 0 and the emitter stopped.

### D30. Every font size in the UI is snapped to one of seven steps
`UiTheme.SnapSize` rounds any requested point size onto a 14/18/22/26/32/44/64 scale, and
`SceneAssembler.Label` plus every direct `fontSize` assignment goes through it.

The HUD and menus are 92 legacy `UI.Text` on a runtime-built dynamic font atlas, and PHASE8
measured **22 distinct sizes across two fonts**, with an atlas repack observed on device at
startup. Every distinct size is another atlas entry. It is now **7 sizes across 1 font**.

Chosen over migrating to TextMeshPro, which is the real fix and which the kit's bundled
`Carlito-Bold SDF` asset would support: swapping 92 components mid-phase is a large uncontrolled
change, and PHASE8 is explicit that the atlas was *not* the cause of that phase's bug. Bounding
the size set gets most of the benefit for a fraction of the risk. TMP remains the right eventual
answer (D5).

*Reverse it by* deleting the `SnapSize` calls; the sizes fan back out to 13 or more.

### D31. Captions are recoloured by a sweep over the built canvas, not by editing colour literals
`UiTheme.EnforceContrast` walks the finished canvas after every screen is built and darkens any
`Text` that is light-coloured *and* sits on a Soft Touch surface.

The old interface was near-black panels with white captions. The kit's panels and buttons are
cream, so every one of those captions inverted from readable to invisible. There were around forty
such literals across two files totalling about 2,500 lines. Fixing them by hand means finding all
forty and getting each one right; fixing them by what they are actually drawn on is one rule that
cannot miss one. Captions over the world -- the ammo counter, the speedometer, the busted banner --
have no kit sprite behind them and are correctly left alone.

*Reverse it by* deleting the `EnforceContrast` call in `MenuBuilder.Build`.

### D32. Traffic-light lamps are overlay quads on the pack model, measured by raycast
The Tarbo pack ships each signal as **one mesh with one material and no child objects**, so its
three lenses cannot be addressed individually -- which is exactly what `TrafficLightLamps` needs.
The model supplies the pole, mast arm and housing; three small emissive quads are laid over its
moulded lenses for the controller to drive.

The lens positions were measured by firing a grid of raycasts at the mesh and reading where the
surface protrudes, not estimated off a screenshot: head band y 6.50-7.30, lens apexes at y 6.90,
lenses at x -4.30 / -3.30 / -2.30, housing face at z -0.342, apexes at z -0.519.

`TrafficLightController` and `TrafficLightLamps` are untouched -- the swap is entirely inside
`RoadNetworkBuilder.BuildSignalHead`. `LitBorder` went 1 -> 2 so the expanded grid lights its
inner 6x6 (36 junctions, about 576 renderers) rather than 8x8 (64 junctions, about 1,020).

*Reverse it by* pointing `SignalPrefab` at a missing path -- the builder already falls back to the
Phase 2 primitive pole and logs a warning.

### D33. Wildlife counts scale with the size of the city
`WildlifeBuilder` counts are authored per 25 city blocks and multiplied by `(Blocks^2 / 25)`,
clamped to 3.5x.

Absolute counts on a 2.56x map thin the strays from roughly one per block to one per four, which
is the same "technically placed, never seen" failure the City zone was added in 9b-fix to fix.
Scaling with the grid keeps the encounter rate constant as the map changes.

*Reverse it by* returning `DensityScale` to 1.

### D34. Palmov's "wooden winter houses" are excluded from the building roster
Three snow-roofed log cabins. They are good models, and in a city with palm trees on the seafront
they read as a bug rather than as variety. The rest of the Palmov Houses set -- four buildings, a
temple and a railway station -- is used.

*Reverse it by* re-adding the three `PH("Wooden winter houses/...")` entries.

### D35. "Low Poly Houses Free Pack" is not in the project; Palmov's Houses folder stands in
The task named three environment packs. Two are present. There is no folder, prefab or material
anywhere under `Assets/` belonging to a pack by that name -- the only house-like models outside
SimplePoly City are in
`Palmov Island/Low Poly Atmospheric Locations Pack/Prefabs/Houses`.

Those nine (four buildings, a temple, a railway station, three winter cabins) are used in its
place, and they share one material so they batch well. Between them and SimplePoly City's 40 the
city has 50 distinct building models, so nothing was lost -- but the pack was **not** silently
assumed to be present under another name. If it was meant to be imported, it did not arrive.

---

## Phase 12 — 2026-09-01

### D36. PLAY is one button; New Game lives behind a confirmation on the profile screen
The Phase 7 title screen offered Continue and New Game side by side. The lobby offers one
large PLAY: with no save it starts a new game, with a save it continues, and the caption under
the button says which ("Start a new game" / "Continue — level 3 — $2,850 — 0 jobs done").

Two buttons of equal weight, one of which silently erases a save, is a trap on a phone where
the press target is a thumb. Starting over is now a rail tap, then NEW GAME, then a second tap
on the same button while it reads TAP AGAIN TO ERASE — it is the only destructive control in
the front end and the only one that asks twice.

`LobbyScreen.StartNewGame` is the Phase 7 `MainMenu.NewGame` body, moved and not rewritten.

*Reverse it by* putting a second button next to PLAY that calls `StartNewGame` directly.

### D37. The rail's fourth slot is EXIT, not Store — the store is already reachable
The task asked for a Store icon "if not already reachable". It is: `StorePanel` is opened by
the `StoreButton` on the HUD, which is present and active during play. A second door into the
same one-item storefront would be the only rail icon that duplicates something.

The slot went to EXIT instead, because the Phase 7 title screen had a Quit button that saves
and quits and the lobby would otherwise have dropped it — a real regression on the desktop
target, where there is no OS back gesture to leave with.

*Reverse it by* swapping the fourth `RailButtonAt` call for one that opens `StorePanel`.

### D38. All four rail icons are generated glyphs, including settings
The kit ships exactly one of the four icons the rail needs: `Decoration/setting.png`. It was
used first, and it does not work at this size — it is a painted three-quarter render of a gear
crossed with two spanners, authored as a large decorative piece, and at the 60-unit size a rail
icon draws at it resolves to a gold smudge. That is not a judgement call from the source file;
it was captured at 2400x1080 and 2048x1536 and looked at.

The other three (a bust, a standing figure, a power symbol) do not exist in the kit in any form.
They are drawn in `LobbyBuilder` as signed-distance fields — trivially antialiased, resolution
independent — and tinted with `UiTheme.Ink` so the rail reads as one set. A cog was added to
match rather than leaving one painted icon among three flat ones.

*Reverse it by* passing `UiTheme.SettingsIcon` to the settings slot again.

### D39. The lobby character is a display body on its own stage, not the player
The player is somewhere in the city — possibly indoors, at night, in the rain, or wherever a
save restored them. None of that frames a portrait, and pointing a camera at them would make
the lobby's appearance depend on where the last session ended.

Instead `LobbyStage` parks a second body in dead air at `(-420, 400, 300)` — 200 m above the
interiors, which use the same trick — with its own camera rendering into a 640x896
`RenderTexture` the lobby shows through a `RawImage`. The body comes from the same
`CharacterCatalog.AttachBody(root, CharacterCatalog.Player, ...)` call the player is built from
and carries the same `PlayerSkinSwapper`, so it cannot drift into being a different character
or wear an outfit differently.

Cost, measured: **4 renderers and 6,101 triangles** (3,082 → 3,086; 2,464,197 → 2,470,298).
While the lobby is open they are the *only* things drawn — see D41.

*Reverse it by* pointing `LobbyPreview.StageCamera` at the player and deleting the stage.

### D40. The profile screen shows only statistics that were already being tracked
A profile screen would normally show time played, kills, distance driven, arrests, missions
failed. **None of those are tracked anywhere in this project** and inventing them would mean
either fake numbers or a new counting system in a phase that is meant to be a screen.

What is shown is what `PlayerProgress`, `Garage` and `SaveSystem` already know: level, XP and
XP to the next level, money, missions completed, outfits owned, items bought, unlocks earned,
properties, vehicles in the garage, and the save file's own modification time. Eight rows, all
real.

*Reverse it by* adding counters to `PlayerProgress` and two more rows to `ProfilePanel.Refresh`.

### D41. The lobby empties the world camera rather than disabling it
While the lobby is open the city is behind an opaque panel and cannot be seen, so rendering it
costs the heaviest frame in the game for pixels nobody looks at.

`LobbyPreview` sets `Camera.main.cullingMask = 0` and puts it back on close. **Disabling the
camera instead would have been wrong**: `Camera.main` only resolves an *enabled* camera, and
`PlayerController`, `PlayerCombat`, `ObjectiveMarker`, `WeatherSystem` and — critically —
`PerformanceTuner.ApplyCamera` all resolve it lazily. `GameSettings` applies the quality tier
during `Start`, which is while the lobby is up; a null `Camera.main` at that moment would have
silently dropped the Phase 7 detail-layer cull distances for the whole session.

*Reverse it by* deleting the `_worldMaskBorrowed` block in `LobbyPreview`.

### D42. The lobby borrows the day/night cycle's key light while it is open
Only one directional light contributes on mobile — `Mobile_RPAsset` ships
`m_AdditionalLightsRenderingMode: 0` (Disabled) — so a lamp aimed at the stage would render as
nothing on a phone. And `DayNightCycle.Update` re-applies the sun and the ambient every frame
regardless of timescale, so overwriting them once does not hold.

So the lobby does what `InteriorManager` does when the player walks into a shop: it stands the
cycle down, points the sun at the stage, flattens the ambient, turns fog off, and hands all of
it back on close (`DayNightCycle.Refresh()`). Nothing else is being drawn at the time, so there
is nothing to disturb — and it means the lobby looks the same at 3 a.m. in the rain as it does
at noon.

*Reverse it by* deleting `BorrowLighting`/`ReleaseLighting` and accepting a dark portrait at night.

### D43. The profile name survives New Game
`PlayerProgress.ResetProgress` clears money, XP, level, unlocks, items, properties and the
active skin. It deliberately does **not** clear `ProfileName`: the name identifies the person
holding the phone, not the run they are on, and making them type it again after every restart
would be a chore with no upside.

It is written to the save file only over a save that already exists. Creating one on a rename
would turn a first-ever run into a "continue", because the lobby decides between New Game and
Continue on whether a file is present; a fresh name is written by the first autosave instead.

*Reverse it by* adding `ProfileName = DefaultProfileName;` to `ResetProgress`.

### D44. Save format v4 adds `ProfileName` only
One field. A v3 file loads with it absent, `PlayerProgress.CleanProfileName` turns the empty
string into "Rookie", and the file is rewritten as v4 on the next write — the same forward
path Phase 5 used for v1 → v2. Verified against the real v3 file on this machine: it came back
as v4 with money, XP, level, garage, owned items and active skin all intact.

*Reverse it by* dropping the field and returning `CurrentVersion` to 3; v4 files then load with
the name ignored.

## D-041 — One stick drives the car; the gas/brake pedals are gone
**Date:** 2026-09-02 · **Context:** combat/weapon brief, item 12

Driving used a movement stick for steering plus separate Gas and Brake buttons. On a
phone that is a two-thumb layout for a one-thumb job: the right thumb holds the pedal,
so the left thumb is the only one free, and it has to steer. Holding a pedal and
steering with the same hand is not possible.

**Decision:** the drive stick's Y axis *is* the throttle. Push forward to accelerate,
pull back to brake and then reverse, and how far you push is how much you get. The
X axis still steers, unchanged.

- **Deadzone is radial, not per-axis** (`DriveDeadzone`, 0.15). A square deadzone lets a
  diagonal push through at a magnitude a straight push would have swallowed, which makes
  the car twitch on diagonals. Measured: `(0.10, 0.10)` — magnitude 0.141 — reads exactly
  zero on both axes, where a square zone would have leaked.
- **Travel is rescaled past the deadzone**, so the first responsive degree is near-zero
  rather than jumping to 0.15. Measured: stick 0.16 → steer 0.012.
- **Two smoothing rates.** `ThrottleSmoothing` 0.12 s normally; `ThrottleReversalSmoothing`
  0.05 s when the stick crosses zero. Slamming forward-to-back is a braking input and a
  driver expects it to bite immediately.
- **Ticked on unscaled time**, including in the unfocused branch, so a car coasts to a
  stop during a phone call instead of freezing at whatever throttle was applied.

**Pedals kept working, just hidden.** `TargetThrottle()` still honours `TouchGasHeld` /
`TouchBrakeHeld`, so an older HUD layout or a partial rebuild keeps driving rather than
going dead. Visibility is behind `HudContext.StickThrottle` (default true).

**Handbrake stays visible.** It is a distinct manoeuvre — a deliberate slide — not a
throttle control, so folding it into the stick would remove a move rather than simplify
an input.

## D-042 — InputHub gets the project-standard lazy singleton fallback
**Date:** 2026-09-02

`InputHub.Instance` was a plain auto-property assigned in `Awake`, unlike the ~12 other
singletons here which all fall back to `FindAnyObjectByType`. Any domain reload that did
not re-run `Awake` left it null and *all input silently died* — no exception at the
source, just an unresponsive game. Now matches the rest of the codebase.

---

> **Numbering note.** Entries above use two schemes that have collided: `### D41`/`D42`
> (the lobby, Phase 12) and `## D-041`/`D-042` (the joystick pass). They are different
> decisions carrying the same numbers. Phase 13 continues the `### D44` sequence, which is
> the one the phase documents cite. Cite a decision by its title as well as its number.

### D45. The interface font is a slot, not a path

Phase 13 was asked to set every text element in the **Fatality FPS gaming font**. That font is
not in this project, is not anywhere on this machine, and fetching a licensed typeface is not
something this build may do on its own.

Hard-coding a path to a file that does not exist would produce either a null font or a silent
fall back to Unity's built-in face with nothing saying so. Instead `UiTheme.Font` scans
`Assets/Game/UI/Fonts` and takes, in order: a file whose name contains "fatality", any other
`.ttf`/`.otf` there, the kit's Righteous, Unity's LegacyRuntime.

**The consequence that matters is the logging.** `UiTheme.FontSource` names the face that won and
`MenuBuilder.Build` prints it, so a build cannot report the intended font as being in use when it
is not. Today it prints `Righteous (kit fallback -- Fatality not present)`.

Righteous ships inside the Space Exploration GUI Kit and is the closest face in the project to
the aggressive display look the brief describes. Dropping the real file into that folder and
re-running step 6b re-faces all 129 `Text` components with no code change.

**One face only.** D30 bounds the runtime dynamic-font atlas at one face and seven point sizes,
because PHASE8 measured a device-side repack caused by 22 distinct sizes. A second face left in
that folder is ignored on purpose.

### D46. Nine-slice borders are measured from the artwork, not typed into a table

The Space kit ships **every** sprite with a zero border, and this project draws its art at sizes
it was not authored for: a 356x96 button becomes a 520x96 PLAY, a 1128x512 container becomes a
640x800 card. Without borders the corners smear.

Phase 11 solved the same problem for the previous kit with a hand-typed table of 18 borders.
Phase 13 does not have one. `UiTheme.PrepareSprites` reads each PNG **off disk** -- not through
the importer, so the texture's Read/Write flag is irrelevant -- finds the opaque silhouette's
bounding box, measures the corner radius along its edges, and writes `padding + radius + 2`.

Symmetric on each axis, because every piece in this kit is a rounded rectangle: a border that
comes out lopsided is a measurement artefact and would render as a visibly off-centre frame.

**Why it is worth the code.** A typed table is correct exactly once. Swapping a sprite for a
different size tier -- which this phase did repeatedly while choosing among the kit's four --
silently invalidates a typed border and nothing complains. A measured one re-derives itself.

### D47. Two inks, and `UiTheme.Ink` was deleted rather than redefined

Soft Touch was cream panels with dark text; the Space kit is deep-indigo panels carrying *light*
painted buttons. A single screen now has both surfaces on it, and the correct ink is opposite on
each: a heading on a panel must be light, and the caption on the button below it must be dark.

The old single `UiTheme.Ink` served both and cannot express that. **It was deleted**, so all
~30 call sites became compile errors and each was answered individually with `TextOnPanel` or
`TextOnButton`. Redefining it would have compiled cleanly and produced invisible text across
roughly half the interface.

Two build-time sweeps back that up, both run at the end of `MenuBuilder.Build`:

- **`EnforceContrast`** (rewritten from D31). The Phase 11 version only ever *darkened* light
  text and would have left every panel caption black on indigo. It now compares luma in both
  directions; neutral text snaps to the surface's ink, and deliberately coloured text keeps its
  hue and has only its value moved, so the palette survives the sweep.
- **`AuditFrames`** (new). Measures each nine-sliced kit panel's border, converts it into canvas
  units, and reports every `Text` or control laid out underneath it, with the overlap in units.

The audit exists because this kit's frames are 87-166 px against Soft Touch's 26-64, so every
card in the project was mis-sized the moment the kit changed. It found **91** intrusions on the
first run -- including the settings title drawn across its own moulding and the shop's stock rows
running out past both sides of the sheet -- all of which passed every existing assertion. It
reports **0** now, and it runs on every build, so it cannot be forgotten.

### D48. The rail goes back to kit icons; D38 is superseded

D38 replaced the previous kit's settings icon with a hand-drawn signed-distance-field glyph,
because that icon was a **painted three-quarter render** that resolved to a gold smudge at the
rail's 60-unit size -- and then drew three more glyphs to match it.

The Space kit ships a **separate flat single-colour pictogram set** (`Picto_Icons`) authored for
small use, which is precisely the thing D38 looked for and could not find. All five generated
glyphs and their ~120-line SDF rasteriser are deleted, and the rail, the thumb controls, the HUD
corners and the menus now all draw from one set.

D38's reasoning was right for its kit and is not right for this one. The general rule it implies
survives: **check an icon at the size it will be drawn, not at the size it was authored.**

### D49. Button states come from the kit's four sprite sets, not from tints

The kit ships normal, highlighted, pressed and disabled art in five silhouettes and four colours.
`UiTheme.StyleButton` assigns all four and leaves every `Selectable` colour at white.

**Tinting would have hidden the states rather than shown them.** A tint multiplies painted art
instead of replacing it, so a pressed tint reads as the same button slightly darker, and a faded
disabled tint erases the button rather than making it look unavailable. Phase 11 recorded the
same trap for the quality-tier row, where a 14% alpha made the kit's painted button vanish
entirely.

`HudButton` -- this project's own thumb control, which is not a uGUI `Selectable` -- gained
`NormalSprite`/`PressedSprite` and swaps them in `ApplyTint`, so the eight thumb buttons get the
kit's pressed art too.

Three colour families assigned by role rather than by taste: **gold** for the single primary
action on a screen, **blue** for everything ordinary, **purple** for secondary and
over-the-world controls. Silhouette is chosen by pixel density -- the rail and the thumb cluster
take the kit's *large* 120x112 square rather than the 60x56 medium, because they are drawn at
104-132 units.

### D50. Re-skinning the interface is one step, not the whole pipeline

The HUD is built by `SceneAssembler.Assemble`, which also destroys and rebuilds the player, the
camera, the lighting and the ocean. Re-skinning therefore used to mean re-running all 17 pipeline
steps and re-checking everything Phases 9 through 12 established -- for a change that touches
only the canvas.

`Tools > Mini GTA > 6b. Rebuild Interface Only` throws away the canvas, builds a new one against
the existing world, and re-points the four references that live outside it:
`PlayerController.Hud`, `PlayerVehicleController.Hud`, `PlayerCombat.Hud`, `MissionHud.Hud`.

**Those four are the entire external surface of the HUD** -- everything else on the canvas is
found at runtime by type. A future screen that adds a fifth must add it to `RebuildInterface`
too. A dangling HUD reference is silent, in the same way the null police prefabs of Phase 9 were
silent, and this is the note that says so.

This is the third re-skin of this interface. It should not have cost a full pipeline run the
first two times either.

### D51. Backgrounds bleed past the safe area; controls stay inside it

`SafeAreaFitter` insets one container that every UI root hangs off, which is right for controls
and wrong for the two things that are supposed to reach the physical screen edge: a full-screen
backdrop and a modal dimmer.

Phase 13 shipped the lobby's starfield and all nine modal scrims inside that container. On a
phone reporting a 141 px cutout the backdrop stopped 110 canvas units short on each side and the
inset drew as a flat grey band down both edges and along the bottom — the defect that prompted
this pass. The same applied to every dimmer: a modal left an undimmed strip of the world at each
edge.

`SafeAreaBleed` is the inverse component. It sits on the one Image inside a screen that is meant
to bleed, and expands that Image back out to the full canvas by converting the canvas's corners
into its parent's local space. The root above it stays inset, so a card centred on it still
clears the notch.

**Ten of them in the scene**: the lobby backdrop and nine scrims. Anything full-screen added
later needs one, and the responsiveness sweep exempts them from its overflow check — measuring a
deliberate bleed against the safe area would report the fix as the bug.

**It also has to be pokeable synchronously.** The component polls in `Update`, and no frame
elapses inside an MCP editor command, so a capture taken straight after changing the simulated
safe area photographed the *previous* shape's bleed. `Apply()` is public and `PhaseCapture.UiShot`
calls it before rendering. That is the same class of trap as the dead `onClick` listeners in
PROJECT_STATE: the editor tooling does not run frames, and anything that depends on one has to be
driven by hand.

### D52. Responsiveness is measured against a device matrix, not eyeballed at three sizes

Phase 13 verified layout by capturing three aspect ratios and looking at the pictures. That found
real defects and missed a whole class, because **an editor capture applies no safe area at all** —
`SafeAreaFitter` only runs in play mode, so every capture this project had ever taken was of a
canvas with no notch inset.

`Tools > Mini GTA > 6c. Test UI Responsiveness` simulates both halves: the render size, through a
camera with a target texture, which is what `CanvasScaler` actually reads; and the safe-area
inset, applied by hand because `Screen.safeArea` cannot be set. Then it measures, over ten
landscape shapes from 1.33:1 to 2.37:1 including three that report insets:

- anything leaving the safe area, with the overshoot in units;
- text whose own `preferredWidth`/`preferredHeight` exceeds its box;
- the lobby's cross-corner collisions, which no layout system compares;
- touch targets, **in millimetres** — units x scale factor / dpi x 25.4 — against a 7 mm floor,
  which is roughly where Apple's 44 pt and Google's 48 dp both land.

First run: 10 overflows, 20 clipped text boxes, 300 undersized targets. It found a version label
that had hung 126 units off the right edge of every screen since Phase 12 and had never been
photographed because it renders empty until a build number is set.

### D53. The lobby's hero column is sized from the height available to it

The character plate was a fixed 600 units wide and PLAY a fixed 520, on a screen whose height
varies by 22% across the shapes the game can be handed. Nothing clipped — the failure is one of
proportion: on a short 21:9 phone the plate kept its width while its height shrank, so the
character sat in a band of dead plate, and on a 4:3 tablet the reverse.

Both now hang off one `Column` carrying an `AspectRatioFitter` in `HeightControlsWidth` at 0.66,
and take its full width. The plate, the button and the caption therefore stay in proportion to
each other and to the screen on every shape.

**0.66 is calibrated, not chosen.** At the 1920x1080 reference the content box is 908 units tall
and 0.66 x 908 = 599 — the 600 the screen was designed at. So the reference aspect renders
exactly as it did before and every other aspect now follows it instead of drifting.

The stats caption stays on the content box rather than the column: it is a sentence and wants the
screen's full width, where the column is deliberately narrow.

### D54. The canvas reference resolution stays 1920x1080, and it was measured

The brief for this pass suggested changing it to ~1503x755. Run through the sweep, that
introduces **185 elements leaving the safe area**:

| Reference | Match | Overflow | Clipped | Under 7 mm |
|---|---|---|---|---|
| **1920x1080** | **0.65** | **0** | **0** | 275 |
| 1920x1080 | 0.50 | 0 | 0 | 271 |
| 1503x755 | 0.65 | 185 | 0 | 203 |
| 1503x755 | 1.00 | 142 | 0 | 230 |

A smaller reference makes everything render physically larger, which is why its touch-target
count improves — but every layout in this project is authored in 1920x1080 units, and at
1503x755 the canvas collapses to 1614x726, where an 800-tall pause card does not fit. It buys 72
slightly larger buttons for 185 elements off the screen.

The concern behind the suggestion is real and is answered directly instead: the main menu's
targets were raised until they pass (PLAY 8.6 mm, rail 7.8 mm, name chip 7.2 mm), along with the
three corner-anchored HUD buttons where it costs no layout.

`match 0.65` is kept for the same reason it was chosen in Phase 8: it biases toward height, which
is what keeps the thumb clusters the same physical size across aspect ratios. At 0.50 the sweep
is within noise of it.

**The card screens are still 4.3-5.1 mm and are knowingly left that way.** Raising them means
re-fitting five card layouts against the kit's painted frames, which is a larger change than the
request; `6c` reports them on every run so they cannot be forgotten.

## D42 — "~100 NPCs with jobs" read as a share of the crowd, not 100 named roles

The Part 3 brief asked for "at least ~100 concurrent NPCs exhibiting job behaviour" and then
said in as many words that this is a distribution target, not 100 unique jobs. Built to that
reading: a fixed *percentage* of the crowd is assigned a job archetype on spawn, so at the
300-pedestrian population roughly a third are visibly doing a job loop and the rest keep the
existing wander. The alternative reading — 100 individually authored roles — would be content
work, not a behaviour system, and nothing in the project supports it.

## D43 — Weapon pickup placement is generated, not hand-listed

The old table was six hand-written entries, all pistols, covering 6 of 81 city blocks with the
whole downtown empty. Replaced with a rule that walks the block grid, so coverage cannot drift
back into a cluster when Blocks changes, and the weapon follows the district. See
WeaponSetup.BuildPickupTable.

## D44 — RUN is a latch, not a hold

Tap to lock sprint on, tap again to release. Holding a button for the length of a street is the
most tiring thing a touch layout can ask for and it competes with the thumb that steers.
Keyboard Shift stays a hold; the two are separate state so neither clears the other.
