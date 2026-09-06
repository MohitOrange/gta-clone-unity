# Mini GTA — Phase 9: Asset Integration

Builds on [PHASE1](PHASE1.md)–[PHASE8](PHASE8.md). Seven Asset Store packs replace the
primitive-box art layer. **No gameplay system changed.** Traffic, missions, wanted/heat,
economy, save format and monetization were not touched.

> **Status: Editor-verified, device pass pending.** Everything below was tested in Unity Play
> mode on a desktop GPU. Per CLAUDE.md §5 that is *not* verification — an APK build and a run
> on the physical device is the required next step. See *What the editor cannot tell us*.

---

## What Phase 9 delivers

| Brief item | Status | Where |
|---|---|---|
| Player = Apocalyptic Survivor, Mixamo clips retained | Done | `CharacterCatalog.Player` |
| NPCs/police with model + colour variety | Done, **not from the pack asked for** | `CharacterCatalog.Crowd` — see D7 |
| Vehicles from the Mobile Optimized car pack | Done | `VehicleBuilder` |
| Generic hand-socket weapon system | Done | `WeaponSocket`, `WeaponLibrary`, `WeaponSetup` |
| Animals as procedurally-placed ambient wildlife | Done | `WildlifeBuilder`, `AmbientAnimal` |
| Cartoon City + Atmospheric Locations in the city | Done | `EnvironmentCatalog`, `CityBuilder` |
| Sky / day-night from the packs | **Deliberately not adopted** | see *Sun, moon and sky* |
| URP material conversion for the packs that needed it | Done | `AssetPackSetup` |

---

## Compatibility findings, per pack

These are measured, not read off a store page. Every number came from querying the imported
files in the Editor.

### Player — "Low Poly Apocalyptic Survivor/Assassin" (`Assets/Shady_3d`) — clean fit
**Humanoid**, avatar valid and `isHuman`, materials already URP/Lit, 6,101 triangles across
4 skinned renderers. The Phase 1 retargeting pipeline applies with no changes.

Two things the pack got wrong that had to be fixed on the way in:

- **The three "variant" prefabs it ships are identical.** All four skinned renderers are
  enabled in every one of them, so the helmet and the gas mask render inside each other. The
  README describes toggling them by hand; nobody did. `CharacterCatalog.ApplyHeadGear` applies
  the variant instead of trusting the prefab.
- **The armature carries a 100× scale with 1.0 on every bone.** Invisible on the character
  itself, because the skinned renderer bakes the bind pose — and a live grenade for anything
  parented to a bone. See *Bugs worth remembering* #1.

The model is 1.95 m tall as authored and is scaled to 0.92 to match the 1.80 m character
controller that doorways, seats and step offsets were tuned against in Phases 1–5.

> Incidentally this **fixes a bug that predates Phase 9**: the outgoing player model,
> `Remy.fbx`, is **4.15 m tall** inside that same 1.80 m controller, and always has been. It
> was never noticed because you only ever see it from behind. The new player is 35,196 → ~5,000
> triangles *and* the right size.

### NPCs — "Low Poly Characters Lite" (`Assets/PolygonalAssets`) — cannot be pedestrians
This pack **cannot** do the job the task asked of it, and no amount of import configuration
changes that:

- `animationType = Generic`, **no avatar**, **zero SkinnedMeshRenderers**.
- The FBX files contain **no Deformer, Cluster, LimbNode or Skin records at all**. For contrast,
  the Apocalyptic FBX has 401 Deformers, 196 Clusters, 98 LimbNodes and 4 Skins. These are
  static props shaped like people, not characters.
- They are modelled in a **T-pose** (~1.25 m wide for a ~1.65 m figure). Confirmed by rendering.
- Their materials were built-in **`Standard`** and rendered **magenta** under URP.

So they cannot walk, run, flinch or ragdoll, and retargeting is not merely inadvisable, it is
impossible — there is no skeleton to retarget onto. The integration path chosen instead is
DECISIONS.md **D7**: street NPCs stay on humanoid rigs, and the Lite models are used in the one
place where a motionless figure with its arms out is correct rather than broken — **shop-window
mannequins in the Threads clothing store**.

### Vehicles — "Mobile Optimized Free Low Poly Cars" (`Assets/Awbmecreations`) — clean fit
Materials already URP/Lit, and the whole pack shares **one 512 px colour-atlas material**, so
six different car models cost the same draw-call state as one. Structure is exactly what
`CarController` wants: a body with one MeshRenderer and four named tyre children.

Real-world scale, pivots on the ground. Sport Car_39 measures 2.094 × 1.369 × 4.741 m with
track ±0.889 and wheel radius 0.392 — close enough to the old procedural car (1.86 × 4.30,
radius 0.36) that every handling constant carried over untouched.

The pack ships **no motorbike and no boat**; those stay procedural.

### Weapons — "Weapons FREE" (`Assets/ithappy/Weapons_FREE`) — needed a real socket system
URP/Lit already, 132–1,708 triangles. But the pack uses **two different modelling conventions**:
firearms run along **+Z**, melee weapons along **+Y**, everything at roughly 2.5× life size,
with pivots near but not on the grip. There is no single offset that fits every prefab, which
is precisely why the socket had to be built as data rather than a constant.

### Animals — "Animals FREE" (`Assets/ithappy/Animals_FREE`) — clean fit, one landmine
Seven prefabs, all rigged and animated, one skinned renderer and one collider each, 818–2,538
triangles, URP/Lit. A uniform animator contract across all seven: `Vert` (0 idle, 1 moving) and
`State` (0 walk, 1 run) into one nested blend tree.

The landmine: the prefabs ship with the pack's own playable-creature demo scripts attached, and
one of them polls `Input.GetAxis`. This project is **Input System only**, where that call
*throws*. See *Bugs worth remembering* #2.

### Environment — "Cartoon City FREE" and "Low Poly Atmospheric Locations"
Cartoon City is URP/Lit already; the Atmospheric pack was built-in `Standard` (one single
material for ~200 prefabs, atlas-driven) and rendered magenta until converted.

The Cartoon City **buildings are expensive and few**: 4,486–8,578 triangles each, and only four
distinct models for ~90 building lots. Using them everywhere would have cost roughly half a
million triangles to produce a skyline of four repeated shapes. They are mixed in at about a
third of lots instead — DECISIONS.md **D11**.

Cartoon City also ships MeshColliders on everything, including those buildings. A 5,746-triangle
static mesh collider per tower, ninety times over, answers exactly the same gameplay question
("can I walk through this?") as a box. `EnvironmentCatalog.NormaliseColliders` converts any mesh
collider over 400 triangles to a box. **Heavy mesh colliders remaining in the scene: 0.**

---

## Sun, moon and sky — deliberately unchanged

The task asked whether the atmospheric pack improves on `DayNightCycle`. It does not, and
adopting it would be a regression.

The scene sky is `Default-Skybox` (**`Skybox/Procedural`**), which follows the sun transform
automatically; `DayNightCycle` drives sun and moon rotation, colour, ambient and fog on top of
it. The only sky asset in any of the seven packs is `Animals_FREE/Skyboxes/Skybox_5.mat`, a
**static `Skybox/Cubemap`**. A baked cubemap cannot rotate with the sun — swapping it in would
freeze the sky at one time of day while the lighting continued to move underneath it.

`DayNightCycle` was left untouched. What *was* added is `NightLights`: the Cartoon City
buildings ship a separate 66–244 triangle shell of emissive windows per model, and one component
now switches all 22 of them on after dusk and off at dawn. Disabled renderers cost nothing to
draw, so this is free by day.

---

## What was rebuilt, and what was reused

Reused unchanged: `PlayerController`, `CarController` physics, `TrafficSpawner`, `Pedestrian`,
`HeatSystem`, `PoliceDispatcher`, `MissionManager`, `SaveSystem`, `Shop`, `AdService`,
`DayNightCycle`, `PerformanceTuner`, the whole HUD.

New shared code — the point of which is that a fix lands in one place, not four:

| File | What it owns |
|---|---|
| `Editor/AssetPackSetup.cs` | Built-in → URP material conversion, narrowly scoped to the pack folders |
| `Editor/CharacterCatalog.cs` | **One** `AttachBody` for player, police, crowd and shopkeepers |
| `Editor/EnvironmentCatalog.cs` | **One** `Place` that normalises pivots, scale and colliders across two packs |
| `Editor/WeaponSetup.cs` | Builds the weapon library from measured bounds; derives the hand socket |
| `Editor/WildlifeBuilder.cs` | Procedural animal and wild-vegetation placement |
| `Game/Scripts/Weapons/WeaponSocket.cs` | Generic bone attachment — knows about sockets, not pistols |
| `Game/Scripts/Weapons/WeaponLibrary.cs` | Per-weapon fit data, one shared asset |
| `Game/Scripts/World/AmbientAnimal.cs` | Roam / pause / flee, transform-driven, self-culling |
| `Game/Scripts/World/NightLights.cs` | One owner for every emissive window shell in the city |

Before Phase 9, four builders each had their own near-identical copy of "instantiate the FBX,
name it Body, add an Animator, assign the controller, turn root motion off, add
PlayerAnimation" — and those four copies had **already drifted apart on culling mode**. There is
now one.

### The weapon socket is derived, not guessed

Hardcoding a hand rotation does not survive a rig change: the Mixamo characters and the
Apocalyptic character disagree on every hand axis (this rig's fingers run down local **+Y** and
its bind pose faces **−Z**). What every humanoid rig *does* agree on is what "aiming a pistol"
looks like, because that is exactly what the humanoid avatar normalises.

So `WeaponSetup.ComputeHandSocket` poses the rig with the **`Pistol Idle`** clip and solves for
the rotation that makes the weapon point where the character points:

```
socketLocal = inverse(handWorldRotation) × lookRotation(characterForward, characterUp)
```

The result for this rig is `(282.2°, 242.0°, 217.8°)` — a number nobody would have typed in.
Weapon scale is derived the same way: each entry declares the real-world length it should end
up at and the scale falls out of the prefab's own measured bounds.

| Weapon | Modelled | Scale | In hand |
|---|---|---|---|
| pistol | 0.575 m | ×0.365 | 0.21 m |
| rifle | 1.757 m | ×0.512 | 0.90 m |
| shotgun | 1.731 m | ×0.549 | 0.95 m |
| sniper | 1.813 m | ×0.634 | 1.15 m |
| knife | 0.553 m | ×0.506 | 0.28 m |
| bat | 1.115 m | ×0.762 | 0.85 m |
| axe | 1.149 m | ×0.696 | 0.80 m |

Only the pistol is equipped by gameplay; the rest are wired and attachable, which is what
"generic" was supposed to mean.

---

## Editor test results

Driven through `InputHub` wherever possible — the same code path a real finger takes.

| # | Test | Result |
|---|---|---|
| 1 | Player renders, correct scale, no collision issues | **PASS** — Apocalyptic model, body scale 0.92, 1.79 m in a 1.80 m controller, grounded, shadow correct |
| 1 | Animation states play | **PASS** for idle / walk / run / jump / land / attack, measured live. **Swim: clip verified retargeting, in-water transition not exercised** — see below |
| 2 | NPCs and police show variety, animate, no clipping/floating | **PASS** — 16 NPCs over 3 distinct models + 3 headgear variants + 6 tints; all `Walk w1.00` at `Speed 0.50`, positions advancing |
| 3 | Vehicles drive; wheels align; no barrel bug; damage triggers; correct scale | **PASS** — 127.2 kph, 4/4 wheels grounded, mesh-to-collider offset ≤ 0.02 m, **axle-vs-up 0.022 on every wheel** |
| 4 | Weapon attaches to hand, visible, correct scale/orientation | **PASS** — 0.212 × 0.158 × 0.148 m pistol, socket world scale exactly 1.000, 0.16 m from the hand bone, barrel·forward = 0.94 |
| 5 | Animals present, animated, roaming | **PASS** — 43 placed; moving between samples; blend tree live (`Vert 0.21`, idle/walk/run blend); distant ones correctly not animating |
| 6 | City renders, no floating geometry, colliders work | **PASS** — see below |
| 7 | Renderer / triangle count vs baseline | **Measured, and it grew** — see below |
| 8 | Phase 8 UI fixes not regressed | **PASS** — `SafeArea` container with all 18 roots present; canvas `ScaleWithScreenSize` @ 1920×1080; all four previously-dead buttons still carry their `OpenButton` reference |

### On the animation states

All **14** Mixamo clips were additionally sampled onto the new rig directly and every one
retargets with real bone motion and no NaNs — including `Swimming` (4.53 s, 1.843 m max bone
shift). That is a stronger check of the *character swap* than watching states go by.

What was **not** verified is the in-water state *transition*: the editor player loop stalled
(the documented Phase 6 artifact — Unity stops ticking when the Game view is not drawn, and
play mode restarted several times as dynamic test assemblies compiled). The swim trigger is
Phase 1 code that Phase 9 did not touch, but it is honestly untested this pass and is on the
device-pass list.

### Geometry and colliders

Every placed prop was checked against the live terrain:

| Group | Objects checked | Highest float | Deepest sink | Floating > 1 m |
|---|---|---|---|---|
| Wild flora | 195 | 0.07 m | −0.05 m | **0** |
| Animals | 43 | 0.02 m | −0.31 m | **0** |
| City props | 292 | 0.00 m | 0.00 m | **0** |

(The −0.31 m is a penguin whose pack pivot sits below its own bounds.)

Collision, tested by driving at a wall at 20 m/s:

```
carX = 689.39   wall face = 691.80   -> BLOCKED, did not pass through
health 45.4 -> 35.7, damage smoke started
```

The nose came to rest 0.04 m short of the face. Vehicle health also fell 100 → 66.1 over an
unattended drive, so impact damage triggers from ordinary collisions, not just staged ones.

### Renderer and triangle counts

| | Phase 8 baseline | Phase 9 | Δ |
|---|---|---|---|
| Renderers in the scene hierarchy | 1,426 | **1,984** | +558 (+39 %) |
| Triangles in the scene hierarchy | 1,010,043 | **1,144,580** | +134,537 (+13 %) |
| Colliders | 173 | 460 | +287 |
| Renderers on the `Detail` (cullable) layer | ~763 | **1,017** | +254 |

**The renderer count grew by 39 %, and that should be flagged rather than buried.** Where it
went, and where it came back:

| | Before | After |
|---|---|---|
| 16 pedestrians | 49 renderers, **583,907 tris** | 61 renderers, **250,865 tris** |
| Player | 7 renderers, **35,196 tris** | 4 renderers, **~5,000 tris** |
| Park trees | 27 renderers (cylinder + sphere pairs) | 14 renderers, real trees |
| Parked vehicles | 34 renderers | 24 renderers |
| City props / clutter | 0 | 292 renderers, 79,784 tris |
| Wildlife + wild flora | 0 | 238 renderers, 139,539 tris |
| Cartoon City buildings | 0 | ~31 of 139 building renderers, 174,452 tris |

So the characters got dramatically cheaper and the world got dramatically busier, and the
world won on renderer count. Most of the new renderers are small props that are collider-free
by construction and therefore legal to distance-cull — which is why the `Detail` layer grew
by 254.

**Drawn from a fixed camera** (the Phase 7 method). Phase 7 did not record *which* camera
position it used, so its 1,194/826 figures are not directly comparable; both vantage points
below are recorded here so Phase 10 can compare like-for-like:

| Vantage | High | Medium | Low |
|---|---|---|---|
| A — city centre, `(560, 24, 470)` rot `(8, 20, 0)` | 718 | 526 | **367** |
| B — player spawn, `(377, 17, 500)` rot `(6, 90, 0)` | 1,253 | 880 | **729** |

Distance culling is doing more work than before: at vantage B it removes 42 % of the drawn
renderers between High and Low, against Phase 7's 31 %.

`PerformanceSetup` was extended to cover the new geometry. Rather than list every prefab name
and have that list rot, `EnvironmentCatalog.Place` renames anything it builds collider-free
with a `Detail_` prefix, and `PerformanceSetup` culls on that prefix. **The existing rule that
anything with a collider is never culled is unchanged** — a lamppost, bench, bus stop, tree,
rock or building keeps its collider and stays drawn; bins, bushes, grass and small stones do
not have one and can be culled.

---

## Bugs worth remembering

These cost real time and will bite again.

1. **A skinned mesh hides its armature's scale; a bone parent does not.** The Apocalyptic rig
   carries a **100× scale on the armature with `localScale` 1.0 on every bone**. The character
   renders correctly because the SkinnedMeshRenderer bakes the bind pose. The first build of
   the weapon socket put a **19.5-metre pistol** in the player's hand. `WeaponSocket.ApplyOffsets`
   now divides out `parent.lossyScale` so the socket's world scale is exactly 1 — which is what
   makes "Scale in the library means metres" true on *any* rig, whatever the exporter did.

2. **A free pack's demo scripts are live code in your scene.** All seven animal prefabs ship
   with the pack's playable-creature controllers attached, one of which polls `Input.GetAxis`.
   This project is Input System only, so that call throws — **43 animals throwing an
   `InvalidOperationException` every frame**. `WildlifeBuilder.StripPackDemoScripts` removes
   anything in the `ithappy` namespace. It has to do so in **dependency order**: the two scripts
   declare `RequireComponent` on each other and Unity refuses to delete the required one first.

3. **`GetWorldPose` needs a per-wheel mesh correction, not a project-wide one.** PHASE2's 90°
   fix exists because a Unity cylinder primitive stands on Y. The pack's tyres are modelled as
   wheels with the axle already on X, and applying that same 90° would lay them flat — the
   identical bug in mirror image. `CarController.Wheel.MeshRotationEuler` is now per wheel:
   identity for a modelled wheel, 90° for the cylinder the bike still uses.

4. **You cannot reparent a child out of a prefab instance.** The four tyres have to move out
   from under the body so `VehicleDamage` can crumple the body without dragging the wheels with
   it. Unity refuses until the instance is unpacked. (Removing a *component* from an instance is
   fine — that is a recorded override. Only reparenting and deleting children are blocked.)

5. **An interpolated rigidbody overwrites `transform.position`.** Teleporting a car for a test
   silently did nothing until the write went through `rb.position` instead. This is the same
   trap PHASE7's `PrefabPool` hit from the other direction.

6. **Two of seven packs shipped built-in-pipeline materials.** Under URP those are not subtly
   wrong, they are magenta. `AssetPackSetup` rewrites them onto URP/Lit, reading the old
   property values *before* the shader swap — assigning a new shader drops any property it does
   not declare, and `_Color`/`_MainTex`/`_Glossiness` are exactly the ones URP renamed.

---

## Known rough edges

- **The player's hand does not close around the grip.** The locomotion clips are unarmed Mixamo
  clips, so at idle and while walking the pistol sits correctly positioned in an open hand. The
  animator already declares an `Armed` bool that **nothing sets and no state consumes** — a
  pre-existing gap from Phase 1/3. Adding an armed idle/walk state is the natural fix and is a
  gameplay-animation change, not an asset swap.
- **`Gunplay.fbx` is still 0.20 s long.** PHASE1 flagged this as a truncated Mixamo download and
  it is still truncated. It is now on the critical path: it is the clip that plays when you fire.
- **Only four distinct pack buildings exist**, so at a third of lots you will notice repeats.
  More variety needs more assets, not more code.
- **Tinting a pack car tints its windows too.** The pack's cars share one colour-atlas texture,
  so `_BaseColor` multiplies everything. Model variety is used instead of colour variety, which
  is why there are six car models rather than six paint jobs.
- **The bike and the boat are still primitives.** Neither pack ships one.
- **NPC crowd triangles are still dominated by three Mixamo bodies** (~35 k each against ~5 k
  for the Apocalyptic model). They are kept for silhouette variety; dropping them would cut
  another ~190 k triangles at the cost of a more uniform crowd.
- **Swim state transition untested this pass** (see above).
- Renderer count is up 39 %. It is defensible — the additions are mostly cullable decoration —
  but it is the number to watch on the device.

---

## What the editor cannot tell us

Per CLAUDE.md §5 and the lesson Phase 8 paid for, **none of the above is device verification**.
The editor runs D3D11 on a desktop GPU at desktop resolution with a mouse. It cannot answer:

- Whether a mid-range phone holds 30 fps at Low with 729 renderers drawn and 1.14 M triangles
  in the scene — the single most important open question in this phase.
- Whether the extra ~287 colliders cost measurable broadphase time on ARM.
- How the new URP materials behave on a mobile GPU (the pack materials use metallic/smoothness
  maps the primitive boxes did not).
- Anything about touch input, the HUD at phone resolution, or the safe area.
- Whether the larger asset payload changes APK size or load time meaningfully.

**Required next step: `Tools > Mini GTA > 13. Build Android APK`, install, and run the Phase 8
device matrix again.** Until that happens this phase is *Editor-verified*, not done.

Note also that **`C:` had 1 GB free** when this session started — the `Library\Bee` junction to
`D:` had been lost again and Bee had grown back to 5.5 GB on `C:`. It is restored (C: now
6.3 GB free), but that is thin for an IL2CPP build; PHASE7 records that the build failed three
times on disk space alone.

---

## Rebuilding

The pipeline gained three steps and runs in dependency order:

```
Tools > Mini GTA > BUILD EVERYTHING
```

New individual steps, if you need one in isolation:

```
1c. Convert Asset Packs to URP        (run before anything places a pack prefab)
1d. Report Asset Pack Shaders         (cheap sanity check before blaming a builder for magenta)
16. Build Weapon Library
17. Build Wildlife and Wild Vegetation
```

`Rebuild World Only` now also runs wildlife placement and the culling sort, so a world tweak
does not silently leave the `Detail` layer stale.
