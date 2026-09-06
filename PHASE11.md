# Mini GTA — Phase 11: UI kit, environment rebuild, traffic lights, gameplay pass

Builds on [PHASE9](PHASE9.md) and [PHASE9B](PHASE9B.md). Four separate pieces of work landed
together: the interface was re-skinned onto the Soft Touch UI Kit, the city was rebuilt from
asset-pack buildings on a map 2.56× the size, the traffic signals were replaced with the Tarbo
pack, and four gameplay items were added or re-verified.

> **Status: Editor-verified, device pass pending.** Everything below was measured in Unity Play
> mode on a desktop GPU. Per CLAUDE.md §5 that is not verification. An APK build on a physical
> device remains the required next step, and this phase makes it considerably more urgent — the
> triangle count has doubled.

---

## Entry gate: the Phase 9b-fix defects

Re-checked before any Phase 11 work started, because the task said to stop if any were still
broken. All four held.

| 9b-fix issue | Re-checked | Result |
|---|---|---|
| Zombie-arms idle | `UpperBody` layer weight, 25 characters | **Fixed.** All three layers ship at weight 0; 0 of 25 characters stuck. Arms hang naturally in the rendered frame. |
| Road striping | Crosswalk bounds | **Fixed.** Zebra at `15.1600..15.1800` on layer `Detail`, road top `15.1200`. |
| Missing animals | Count, distance, height | **Fixed.** 59 animals, nearest 19.3 m, 17 within 150 m, `y == Home.y` on every one. |
| FIRE does nothing | Ammo + layer weights | **Fixed.** Ammo decrements, `Hands` layer at 1.00 while armed. |

---

## Part A — UI overhaul

### What changed

A new `Assets/Editor/UiTheme.cs` owns the entire look: palette, type scale, sprite table,
nine-slice borders, and the `StyleButton` / `StylePanel` / `StyleText` helpers.
`MenuBuilder` and `SceneAssembler` ask it for a button or a panel instead of setting colours
inline, so re-skinning again is a change to one file.

| Screen | Result |
|---|---|
| Main menu | Kit panel with the brown header, cream buttons, ink captions |
| Settings | Kit panel, kit slider track/fill/knob, quality tier row |
| Pause | Kit panel, five kit buttons |
| HUD action cluster | Jump / Run / Fire / Interact on the kit's button art |
| HUD round buttons | Pause, map, store, clear-wanted on the kit's quiet button |
| Store / shop rows | Kit sheet panel, rows on the kit's quiet button |
| Mission strip, banner | Kit row art |

### Measured

| | Before | After |
|---|---|---|
| Distinct font sizes on the canvas | **22** | **7** (14/18/22/26/32/44/64) |
| Distinct fonts | 2 (LegacyRuntime + Carlito) | **1** (Carlito) |
| Buttons on kit art | 0 | **29** |
| Buttons unthemed | 30 | **0** (+1 deliberately invisible map tap target) |
| Materials needing URP conversion | 104 | 0 |

**The font-size number is the one that matters.** PHASE8 recorded 92 legacy `UI.Text` across 22
sizes on one runtime-built dynamic atlas, with a repack observed on device at startup, and flagged
it as a live hazard. Every distinct size is another atlas entry, so bounding the set to seven
shrinks the atlas and makes a mid-session repack far less likely. See DECISIONS D30 — and note
that PHASE8's own conclusion was that the atlas was **not** the cause of the Phase 8 striping
(Vulkan pre-transform was); this is hardening a known risk, not fixing the old bug.

### Phase 8 fixes re-verified after the re-skin

| Phase 8 fix | State |
|---|---|
| Canvas Scaler | `ScaleWithScreenSize`, ref `1920×1080`, match `0.65` — unchanged |
| Safe area | One `SafeAreaFitter` on `SafeArea`, holding all 18 UI roots — unchanged |
| Buttons wired at runtime | Untouched; no button wiring moved into editor scripts |
| Vulkan pre-transform | Untouched (a player setting, not a canvas one) |

Captured at **2400×1080** (the test device's landscape resolution) and **1600×720** (a different
aspect). No overlap, no clipping, no cut-off text at either.

### Two real layout defects the re-skin exposed, both fixed

1. **The kit's panel art has a decorative header across its top fifth.** The main-menu title and
   the first settings row were positioned for a plain dark rectangle and landed on the leather.
   Fixed by moving content below the header line (+266 in card space) and growing both cards.
2. **The quality-tier buttons vanished.** `SettingsPanel.UnselectedTint` was `alpha 0.14`, which
   is invisible when it multiplies a painted sprite instead of a flat rectangle. Both tints are
   now opaque and come from the theme.

---

## Part B — Environment rebuild

### Map size

| | Before | After |
|---|---|---|
| `TerrainBuilder.MapSize` | 1000 m | **1600 m** (2.56× area) |
| `CityCenter` | (560, 500) | **(896, 800)** — same normalised spot |
| `CityExtents` | 215 | **360** |
| `CityBuilder.Blocks` | 5 × 5 = 25 | **9 × 9 = 81** |
| Grid span | 380 m | **684 m** (3.24× city area) |
| Heightmap | 513 (1.95 m/texel) | 513 (**3.12 m/texel**) — deliberate, see D26 |

### Buildings

Primitive boxes are gone. **417 buildings, 100% from asset packs, 0 primitive fallbacks** — and
the builder now logs a warning if that fallback ever fires, so a regression cannot be silent.

Catalogue: 50 models — 40 from SimplePoly City, 6 from Palmov Island's Houses folder, 4 from
Cartoon City (kept because they are the only buildings in any pack shipping the emissive window
overlay `NightLights` drives after dark).

Placement is still fully procedural and now district-aware: blocks are classified Downtown /
Midtown / Suburb by ring distance from the centre, each district has its own mix of
Tower / Block / Shop / House / Landmark, and lots are quartered downtown but cut into ninths in
the suburbs so a 17 m house is not marooned in a 28 m plot. Landmarks are capped at 12 city-wide.

**Measured footprints drive the fit**, not guesses — every candidate is measured (cached) and
scaled to the lot within its own allowed range, trying both orientations.

### Audit of things that assumed the old bounds

All were found and fixed by deriving them from the grid (D25). None would have thrown an error:

| Was | Now |
|---|---|
| 4 shop doors at absolute coordinates | `CityBuilder.BlockEdge(block, edge)` |
| 4 mission contacts at absolute coordinates | `Kerb(block, edge, along)` |
| 8 mission checkpoints at absolute junction indices | `RoadRel(di, dj)`, centre-relative |
| "Clear the lot" park site at block (2,3) | `CityBuilder.MissionParkBlock` |
| Police-station fallback spawn `(377, ·, 500)` | `JunctionCentre(0, Blocks/2)` |
| Offshore boat `(178, ·, 500)` | `MapSize * 0.178` |
| Ocean plane at `(-200, ·, 500)` | Derived from `MapSize` |
| Minimap bounds | Already derived — no change needed |

### Renderer and triangle count

Measured the same way before and after (mesh + skinned renderers, index count / 3):

| | Phase 9 baseline | Phase 11 | Delta |
|---|---|---|---|
| Renderers | 1,984 | **3,082** | **+1,098 (+55.3%)** |
| Triangles | 1,144,580 | **2,464,197** | **+1,319,617 (+115.3%)** |
| Colliders | 460 | 957 | +497 |
| On `Detail` (cullable) | 1,017 | 850 | −167 |
| Animals | 43 | 189 | +146 |

**A 3.24× larger city cost 55% more renderers**, and the reason is D27: lane-marking dashes were
one GameObject each, which would have been about 2,100 renderers of road paint on the new grid.
Batching them per road line gives 20 renderers for the same pixels. Without that single change
the scene would be around 5,000 renderers.

Where the renderers are:

| Group | Count |
|---|---|
| City — props (furniture, clutter, bushes) | 971 |
| City — buildings | 427 |
| City — sidewalks / park / roads / markings | 186 |
| RoadNetwork — signals | 576 |
| RoadNetwork — crosswalks | 144 |
| Traffic | 252 |
| Wildlife | 385 |
| Interiors / missions / player / ocean | 129 |

Triangles doubled because a modelled building has real geometry where a box had twelve triangles.
It is still cheap per building — SimplePoly models are 132–1,568 triangles each.

---

## Part C — Traffic lights

Signal heads are now `TB_CITY_Prop_TrafficLight_VSet_Black` — a mast-arm signal reaching over the
carriageway. **`TrafficLightController` and `TrafficLightLamps` were not modified.**

The pack ships each signal as **one mesh, one material, no child objects**, so its three lenses
cannot be addressed individually — which is exactly what the lamp component needs. The model
supplies pole, arm and housing; three emissive quads sized to the moulded lenses are laid over it
for the controller to drive. Lens positions were **measured by raycasting a grid at the mesh**
(D32), not read off a screenshot.

The existing kerbside placement was kept and is not an accident: the pack's arm extends toward the
model's −X, which after the facing rotation is the far side of the carriageway, so the Phase 2
right-hand-kerb position puts the head directly over the approaching driver's lane.

`LitBorder` went 1 → 2, lighting the inner 6×6 (36 junctions, 144 heads, 576 renderers) rather
than 8×8 (64 junctions, ~1,020 renderers) — 2.25× Phase 9's signal bill for a city 3.24× the area.

Verified: red, amber and green each light in isolation with the other two dark, colour order
correct from the driver's side, overlay squares inside the octagonal lenses.

---

## Part D — Gameplay

### Weapon pickup

The player now starts with **fists only** (`HasPistol = false`, `Ammo = 0`). Six `WeaponPickup`
objects sit on block kerbs; the nearest is **38.4 m** from the spawn point, on the same avenue.

Verified end to end: walked to the pickup, pressed Interact through `InputHub.QueueInteract()` →
`HasPistol = True`, `Mode = Pistol`, `Ammo = 24`, `WeaponSocket.Equipped = "pistol"`, pickup
consumed (`Available = False`, visual hidden). The rendered frame shows the pistol in the hand,
correctly scaled and oriented.

`PlayerCombat` was not modified. The pickup calls `GivePistol`; `WeaponSocket` already follows
`IsArmed` and equips the model on its next frame.

### Attack and damage

Fired through the real `HudButton` pointer path at a stationary pedestrian:

```
ammo    23 -> 22
target  38.1 -> 4.1 hp   (exactly 34 = PlayerCombat.PistolDamage)
```

**Two false negatives along the way, both mine, both worth recording:** aiming the *player* at a
target proves nothing because `PlayerCombat` aims down the **camera**, not the body; and freezing
a target by disabling its `CharacterController` also removes its collider from the physics scene,
so the bullet passes straight through. Neither was a game bug.

### Animals

**189 animals**, 7 species across city, park, coast and mountain zones. Counts scale with the
grid (D33), because holding them absolute on a 2.56× map had thinned encounters to 3 within 150 m
of spawn — the same "placed but never seen" failure the City zone was added in 9b-fix to fix.

| | Before scaling | After |
|---|---|---|
| Total | 58 | **189** |
| Nearest to spawn | 51.0 m | **27.1 m** |
| Within 150 m | 3 | **12** |
| Within 260 m (sim range) | 10 | **72** |

**Worst rise above home: 0.00 m** — the 9b-fix levitation fix still holds on the larger map.
Animator culling is `CullCompletely` and `AmbientAnimal` stops simulating past 260 m, but 189
skinned characters is a real number and is the single item on this page most in need of a device
measurement.

### Rain

`DayNightCycle` gained the weather state; `WeatherSystem` draws it. Rain is an occasional state of
the day cycle — rolled once per day at 30% — not an always-on system (D29).

Measured at the same spot, clear vs. full downpour:

| | Clear | Raining |
|---|---|---|
| `Wetness` | 0.00 | 1.00 |
| Sun intensity | 1.70 | **0.77** (×0.45) |
| Fog | 260–950 m | **117–570 m** |
| Live particles | 0 (system stopped) | 1,350 at 900/s |

The emitter tracks the camera 14 m overhead. Verified in a rendered frame: the street is visibly
overcast, the far end fogged out, and the drops read clearly against both sky and asphalt.

**The first attempt was invisible.** The drop material was additive, which only ever brightens —
pale drops over an overcast sky and a light road added almost nothing, and the frame looked dry
despite 1,350 live particles. Alpha blending lets a drop darken as well as lighten, which is what
makes it read. The second attempt then over-corrected into white bars and the drop width was
halved. Both are in the file's comments so the next person does not repeat them.

### Sun and lighting

Verified in rendered frames at the same junction, with the new buildings in shot:

| Time | Sun | Ambient | Result |
|---|---|---|---|
| 10:05 clear | 1.70, warm | 0.57/0.60/0.68 | Correct shadows from the player and lampposts, saturated building colours, blue sky. **Not washed out.** |
| Raining | 0.77 | cooled toward fog | Flat overcast, colours desaturated, shadows soft |
| 20:38 night | 0.00, disabled | 0.24/0.26/0.34 | Sun off, moon 0.08, vehicle brake lights and signal lenses read against a dark street. **Not black.** |

The atmospheric pack's own sky was **not** adopted — the Phase 9 investigation already concluded
the procedural `DayNightCycle` sky was the better fit, and nothing here changed that.

---

## Testing results

Every item from the task, with what was actually measured. All Editor-only.

| # | Test | Result | Evidence |
|---|---|---|---|
| 1 | UI re-skinned, correctly scaled, no Phase 8 regressions | **PASS** | 29/29 visible buttons on kit art, 1 font, 7 sizes; scaler and safe area unchanged; captured at 2400×1080 and 1600×720 |
| 2 | Pack buildings across the map, map visibly larger, no primitive default | **PASS** | 417 pack buildings, **0** primitive; grid 5×5 → 9×9, map 1000 → 1600 m; aerial and street frames |
| 3 | Traffic lights swapped, signal logic unchanged | **PASS** | Tarbo mast-arm heads at 144 approaches; `TrafficLightController` / `TrafficLightLamps` untouched; red / amber / green each verified lighting alone |
| 4 | Weapon must be picked up before the player is armed | **PASS** | Starts `HasPistol=false, Ammo=0`; after Interact `Pistol / 24 / equipped`, pickup consumed; pistol visible in hand |
| 5 | FIRE triggers attack animation and deals damage | **PASS** | Ammo 23 → 22; pedestrian 38.1 → 4.1 hp = exactly 34 (`PistolDamage`), through the real `HudButton` pointer path |
| 6 | Animals visible and animating across ≥3 zones | **PASS** | 189 animals, 7 species over city / park / coast / mountain; nearest 27 m, 12 within 150 m; 0.00 m drift |
| 7 | Rain triggers and renders without tanking performance | **PASS** | `Wetness` 0 → 1, sun ×0.45, fog 950 → 570 m, 1,350 particles; system fully stopped when clear. **Frame cost not measured on device.** |
| 8 | Sun / lighting correct on the new assets | **PASS** | Day, rain and night frames: correct shadows, saturated colours, not washed out, not black |
| 9 | Renderer / triangle count re-measured against Phase 9 | **PASS (reported)** | 1,984 → **3,082** renderers (+55%), 1,144,580 → **2,464,197** triangles (+115%) |

Console at the end of the pass: **0 errors, 0 warnings.** Scene saved.
`PlayerSettings.runInBackground` restored to `false` (the Phase 7 setting) after testing.

---

## Known rough edges

- **A save made before this phase strands the player outside the city.** `SaveSystem` auto-loads
  on start and restores the player's position; a save from the old map restores `(415, 14.7,
  477.5)`, which is now west of the city on the beach. The save *format* is unchanged (v3) and
  was deliberately not touched, so this is reported rather than fixed. The obvious remedy is to
  clamp a loaded position into the city bounds on load, or to bump the save version and ignore
  stale positions. **A new game is unaffected.**
- **The pause button draws over the title screen.** Pre-existing, but the cream card makes it
  obvious where the old black panel hid it.
- **Most buildings have no night-lit windows.** Only the four Cartoon City models ship the
  emissive overlay `NightLights` drives, so after dark the skyline is darker than it was when a
  third of it was Cartoon City towers.
- **Signal heads only on the inner 6×6 junctions.** By design (D32), but the outer ring of the
  city has controllers without visible signals.
- **At distance a signal reads as all three lenses lit.** The lit-state overlay is 0.46 m against
  a 0.60 m moulded lens, so from far away the pack's own painted red/amber/green shows around it.
  Correct from any range a driver actually reads the signal at (verified close up); cosmetic
  beyond that. Widening the overlay puts its square corners outside the octagon, so the real fix
  is an octagonal quad rather than a bigger square.
- **189 animals is the least-tested number on this page.** Animators are `CullCompletely` and
  simulation stops past 260 m, but this is 3.2× the Phase 9b-fix population and it has never run
  on a phone.
- **Terrain is coarser** at 3.12 m/texel. Deliberate (D26); visible only on the mountains.
- **"Low Poly Houses Free Pack" is not in the project.** Palmov's Houses folder stands in. See
  D35 — this was not silently assumed.

---

## Device verification is the pending next step

Nothing here ran on hardware. The triangle count doubled and the renderer count rose 49%; the open
question is whether a mid-range phone holds 30 fps at the Low tier with this scene. That is
Phase 12's job.
