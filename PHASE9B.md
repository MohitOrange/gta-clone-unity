# Mini GTA — Phase 9b: Animation Source Replacement

Builds on [PHASE9](PHASE9.md). The animation library is replaced; **nothing else changed.**
Character models, vehicles, weapons, environment, traffic, missions, wanted/heat, economy,
save format and monetization were not touched.

> **Status: Editor-verified, device pass pending.** Everything below was measured in Unity Play
> mode on a desktop GPU. Per CLAUDE.md §5 that is not verification. An APK build and a physical
> device run is the required next step, and is Phase 10's job.

---

## Clip mapping

| Animator state | Pack | Clip | Blend / speed |
|---|---|---|---|
| Locomotion — Idle | Kevin Iglesias | `HumanM@Idle01` | threshold 0.000 |
| Locomotion — Walk | Kevin Iglesias | `HumanM@Walk01_Forward` | threshold **0.455** |
| Locomotion — Run | Kevin Iglesias | `HumanM@Run01_Forward` | threshold **0.750** |
| Locomotion — Sprint | Kevin Iglesias | `HumanM@Sprint01_Forward` | threshold **1.000** |
| Jump | Kevin Iglesias | `HumanM@Jump01 - Begin` | 0.67 s |
| Fall | Kevin Iglesias | `HumanM@Fall01` | 1.00 s, looping |
| Land | Kevin Iglesias | `HumanM@Jump01 - Land` | 0.60 s |
| Swim | **Mixamo, retained** | `Swimming` → clip `Swim` | 4.53 s, looping |
| Sit (in a vehicle) | Kevin Iglesias | `HumanM@MilitaryIdle01` | **substitute**, see below |
| ~~UpperBody — ArmedIdle~~ | ~~Kevin Iglesias~~ | ~~`HumanM@Gun_Aim01`~~ | **REMOVED — see the addendum, Issue 1** |
| Hands — Grip | Kevin Iglesias | `Human@ObjectGripHands01` | fingers-only mask, weight driven by `Armed` |
| UpperBody — Punch | EEJANAI | `back fist` | 1.17 s at **1.5×** |
| UpperBody — Shoot | Kevin Iglesias | `HumanM@Gun_Aim01_Shoot01` | 0.80 s at **1.6×** |
| UpperBody — Hit | Kevin Iglesias | `HumanM@Damage01` | 0.63 s |

> **This table was amended by the Phase 9b-fix addendum below.** `ArmedIdle` held a two-handed
> aim pose permanently and was one half of the reported "zombie arms" bug; it is replaced by a
> fingers-only grip layer. The `UpperBody` layer now ships at weight 0 and is driven.

One controller (`PlayerLocomotion.controller`) drives the player, the police and the whole
crowd, so this table is the whole game's animation set.

### Why those blend thresholds

Not eyeballed. Each locomotion clip ships an `[RM]` root-motion twin, so its authored ground
speed can be *measured* by sampling that twin start-to-end:

| Clip | Length | Root travel | Authored speed |
|---|---|---|---|
| `Walk01_Forward [RM]` | 0.80 s | 1.600 m | **2.00 m/s** |
| `Run01_Forward [RM]` | 0.60 s | 2.400 m | **4.00 m/s** |
| `Sprint01_Forward [RM]` | 0.53 s | 3.200 m | **6.00 m/s** |

`PlayerController` moves at `WalkSpeed = 2.2` and `RunSpeed = 5.8` m/s and encodes `speed01`
as 0 → 0 m/s, 0.5 → 2.2 m/s, 1.0 → 5.8 m/s. Putting each clip at the `speed01` where the
character genuinely travels at that clip's authored speed is what stops the feet sliding:

```
Walk    2.0 m/s -> 2.0 / (2 x 2.2)               = 0.455
Run     4.0 m/s -> 0.5 + (4.0 - 2.2) / (2 x 3.6) = 0.750
Sprint  6.0 m/s -> 0.5 + (6.0 - 2.2) / (2 x 3.6) = 1.028, clamped to 1.0
```

Measured in play mode at full sprint: blended authored gait **6.00 m/s** against an actual
**5.80 m/s** — a **3.4 %** residual, which is the clamp above and is not visible. These
constants are tied to `WalkSpeed`/`RunSpeed`; change those and recompute.

### Why those playback speeds

Each attack clip is played at the rate that makes its useful length match the cooldown
`PlayerCombat` already gates it by, so the animation ends exactly as the player regains the
ability to attack:

```
melee : 1.17 s / 1.5 x 0.70 exit ~= 0.55 s = PlayerCombat.MeleeCooldown
shoot : 0.80 s / 1.6 x 0.60 exit  = 0.30 s = PlayerCombat.PistolCooldown
```

---

## Retargeting and root-motion findings

### Kevin Iglesias — needs nothing from us
`animationType = Human`, `avatarSetup = CopyFromOther` against `HumanM_ModelAvatar`, every
clip `humanMotion = true`. These go through Unity's humanoid avatar exactly as the Mixamo
clips did, so **the Phase 1 `MixamoImportSetup` pipeline does not apply and is not needed.**

**Root motion is already baked out as shipped**: every clip imports with
`lockRootPositionXZ = lockRootHeightY = lockRootRotation = true` and
`keepOriginalPosition/Orientation = true`. That is precisely the configuration PHASE1 had to
apply by hand to Mixamo. The pack ships the root-motion versions separately, in `RootMotion/`
subfolders and marked `[RM]` — we use the in-place ones and read the `[RM]` ones only to
measure gait speed.

### EEJANAI — half the pack is unusable, and it is not the half you would guess
The **FBX files are `Generic` with `NoAvatar`** and their clips are `humanMotion = false`.
Those cannot retarget onto anything. But the pack *also* ships standalone **`.anim` assets**
in `FreeFighterAnimations/Animations/`, and those **are** `humanMotion = true`. Those are the
usable ones, and they are what is wired in. Anyone re-doing this who reaches for the FBX
folder will get a rig that silently refuses to retarget.

All 18 `.anim` clips are flagged `loop = true`, including the single strikes. A looping punch
on a one-shot state stalls forever if the exit transition is ever interrupted, so
`AnimatorBuilder` clears the flag on the clip it uses.

### The Phase 9 armature-scale quirk does not bite here
PHASE9 found the Apocalyptic rig carries a **100× scale on its armature with `localScale` 1.0
on every bone** — the cause of the 19.5 m pistol. Before wiring anything, every candidate clip
was sampled onto both the Apocalyptic rig and a Mixamo NPC body and the bones measured:

| Clip group | Apocalyptic bone shift | Ch02 bone shift | NaN | Head height |
|---|---|---|---|---|
| Idle / Walk / Run / Sprint | 1.25–1.38 m | 0.50–0.59 m | none | 1.38–1.58 m |
| Jump / Fall / Land | 1.42–1.65 m | 0.45–1.21 m | none | 0.92–1.65 m |
| Gun aim / shoot / damage | 1.19–1.43 m | 0.55–0.63 m | none | 1.56–1.58 m |
| EEJANAI melee (8 clips) | 1.04–1.55 m | 0.55–1.35 m | none | 1.04–1.61 m |

Head height stays in the 1.38–1.65 m band against a 1.636 m bind pose, so **humanoid
retargeting absorbs the armature scale, as it should**. Now measured rather than assumed.

Root motion was then confirmed at runtime, not just at import: with zero input and the armed
idle playing, the player moved **0.00007 m over 15.39 s**. `applyRootMotion` is `false` on the
player, on all 16 pedestrians and on the police officer.

---

## What changed, and what was removed

### Changed
| File | Change |
|---|---|
| `Editor/AnimatorBuilder.cs` | Rewritten against the new packs; 4-point locomotion blend, new `ArmedIdle` state, per-state playback speeds |
| `Editor/WeaponSetup.cs` | The socket reference pose moved from Mixamo `Pistol Idle` to `HumanM@Gun_Aim01` |
| `Editor/MixamoImportSetup.cs` | Narrowed to the character models plus the one retained swim clip |
| `Game/Scripts/Weapons/WeaponSocket.cs` | Now drives the animator's `Armed` bool |

**The weapon socket had to move, and this was not optional.** Phase 9 *derives* the hand-socket
rotation by posing the rig with a reference aim clip — and that clip was `Pistol Idle`, which
this phase deletes. The new pack poses the hand differently, and the derived rotation changed
from `(282.2, 242.0, 217.8)` to `(290.7, 218.9, 319.9)`. Had the reference not been re-pointed,
every weapon in the game would have been silently mis-aimed.

`Armed` was declared by the Phase 1 controller and then **never set by anything and never
consumed by any state** — which is exactly why PHASE9 logged "the player's hand does not close
around the grip" as a rough edge. It now has a producer (`WeaponSocket`, which is the thing
that put the weapon there) and a consumer (the `ArmedIdle` state). **That Phase 9 rough edge is
closed.** Measured: barrel-vs-forward alignment improved from 0.94 to **0.99**.

> **Amended.** The consumer is now the fingers-only `Hands` layer, not `ArmedIdle`. The producer
> is unchanged. See the addendum, Issue 1.

### Removed
13 Mixamo FBX files deleted from `Assets/animations`: `Idle`, `Standard Walk`, `Running`,
`Jumping`, `Falling Idle`, `Falling To Landing`, `Punch Combo`,
`Standing Melee Attack Kick Ver. 1`, `Hit Reaction`, `Gunplay`, `Pistol Idle`, `Sitting Idle`,
and the stray `Ely By K.Atienza` (a character model that PHASE1 noted was sitting in the
animations folder by mistake and that nothing referenced).

**54 MB → 688 KB.** Only `Swimming.fbx` remains.

Before deleting, every live asset — the scene, the controller and all 10 game prefabs — was
checked with `AssetDatabase.GetDependencies`. The only remaining dependency on
`Assets/animations` was `Swimming.fbx`. After deletion the controller was re-inspected:
**0 broken clip references across all 11 clip-bearing states.**

`MixamoImportSetup.cs` was **not** deleted, because it is not now-unused. It configures the
character *models* in `Assets/characters` — three bodies in the street crowd and the four
shopkeepers, per Phase 9 D8 — and extracts their embedded textures, without which they render
pure white. Its animation-clip tables were cut down to the single retained swim clip and its
documentation rewritten to say what it now is: a character-model importer, plus one clip.

`Gunplay.fbx` deserves a note: PHASE1 flagged it as a truncated 0.20 s download and PHASE9
escalated it because it had become the clip that plays when you fire. It is gone, replaced by
a properly authored 0.80 s firing animation.

---

## Gaps, and the substitutions chosen

1. **Swim — no clip in either pack.** Mixamo `Swimming.fbx` is retained. Explicitly authorised
   as the single exception. Verified working (see below).
2. **Seated / driving — no clip in either pack.** `HumanM@MilitaryIdle01` substituted: a braced
   standing idle with the hands forward at chest height. It is a standing pose used seated and
   will not survive scrutiny — but `CarController` sets `HideOccupant = true`, so it is only
   ever visible on the motorbike and the boat, where hands-forward reads as gripping. Logged as
   DECISIONS.md D14.
3. No state anywhere in the controller is left without a clip. The one state that reports no
   motion is `None` on the upper-body layer, which is *deliberately* empty — that is the
   mechanism by which the masked layer contributes nothing until an action fires.

---

## Editor test results

| # | Test | Result |
|---|---|---|
| 1 | Idle, walk, run, sprint, jump, land, swim — no popping, no T-pose | **PASS**, all seven observed live |
| 2 | Melee punch when unarmed / out of ammo | **PASS** |
| 3 | Armed attack plays, weapon stays aligned during it | **PASS** |
| 4 | NPC / pedestrian animations on the new pack | **PASS**, 16 of 16 |
| 5 | Police combat animations | **PASS with one caveat**, see below |
| 6 | No drift — root motion locked | **PASS**, 0.00007 m over 15.39 s |
| 7 | Old Mixamo clips and scripts removed | **PASS**, 13 files, 0 broken references |
| 8 | Phase 8 UI and Phase 9 models/environment not regressed | **PASS** |

Observed state by state, driven through `InputHub` — the same code path a finger takes:

```
IDLE      base=[HumanM@Idle01 w1.00]                              upper=[]                        Armed=True
WALK      base=[HumanM@Idle01 w0.51, HumanM@Walk01_Forward w0.49] upper=[HumanM@Gun_Aim01 w1.00]  Speed=0.225
SPRINT    base=[HumanM@Sprint01_Forward w1.00]                    planar=5.80 m/s   slide error 3.4%
TAKEOFF   base=[HumanM@Jump01 - Begin w1.00]   Grounded=False  Vert=+3.76  y 15.04 -> 15.96
FALL      base=[HumanM@Fall01 w1.00]           Grounded=False  Vert=-7.58
LAND      base=[HumanM@Jump01 - Land w1.00]    Grounded=True   y=15.031
SWIM      base=[Swim w1.00]                    InWater=True    swam z 500 -> 541
SHOOT     base=[Walk w0.85, Run w0.15]         upper=[HumanM@Gun_Aim01_Shoot01 w1.00]   ammo 48->47
MELEE     base=[Walk w0.85, Run w0.15]         upper=[back fist w1.00]                  Armed=False, weapon holstered
```

**The masked layer still works.** Both attacks were fired *while walking*, and the base layer
kept blending `Walk 0.85 / Run 0.15` at 2.20 m/s throughout. Locomotion is never interrupted.

**Swim is properly tested this time.** PHASE9 flagged the in-water trigger as unverified because
the editor player loop stalled. Here the player was placed 0.9 m under a measured surface of
7.46 m: `InWater` went true, the base layer switched to `Swim w1.00`, and the character swam
from z=500 to z=541.

**Police caveat.** The dispatcher deploys cruisers first and only puts officers on foot under
conditions that did not arise in this session, so the officer prefab was instantiated directly
next to the player at 5 stars and left to its own AI. It sprinted at the player on
`HumanM@Sprint01_Forward w1.00` with `HumanM@Gun_Aim01 w1.00` on the upper layer, `Armed=True`,
`weapon='pistol'`, socket world scale exactly 1.000 and the pistol 0.160 m from the hand bone.
What was **not** directly observed is the officer's `ShootUpper` one-shot firing — that routes
through `PlayerAnimation.TriggerShoot` into the same state verified on the player, but it was
not seen on an officer. Recorded as observed-by-inference, not as tested.

**Weapon alignment during the attack animation**, measured mid-shot: pistol world size
`(0.062, 0.146, 0.227)` — 22.7 cm, correct — socket world scale `(1.000, 1.000, 1.000)`,
0.160 m from the hand bone, barrel-vs-forward dot **0.99**. No drift, no scale creep. The
Phase 9 socket fix survives the animation change.

**Regression census**, unchanged from the end of Phase 9 apart from the animator:

```
renderers 1,985   triangles 1,144,660   colliders 460   detail layer 1,017
animal pack demo scripts in scene: 0        (Phase 9 fix holds)
weapon library: 7 weapons, pistol scale 0.365
player body: Apocalyptic character.fbx @ 0.92
crowd: 16 NPCs across 3 models          wildlife: 43 animals
UI: SafeArea with 18 roots, ScaleWithScreenSize @ 1920x1080, all four buttons wired
console: 0 errors, 0 warnings
```

---

## Known rough edges

- **The seated pose is a standing pose.** Visible on the motorbike and the boat only. Neither
  pack ships a seated clip; a third pack or a hand-authored pose is the only real fix.
- **Swimming with a pistol still shows the armed hold on the upper body.** The swim clip is on
  the base layer and the armed pose on the masked layer, so they compose. This is pre-existing
  layer structure, but Phase 9b made it visible by giving `Armed` a pose at all. Holstering on
  water entry would fix it and is a one-line change to `WeaponSocket` — deliberately not made
  here, because it is a gameplay behaviour change rather than an animation swap.
- **3.4 % sprint foot-slide residual**, because the game tops out at 5.8 m/s and the clip is
  authored at 6.0. Closable by setting `PlayerController.RunSpeed = 6.0`, which is a handling
  change and out of scope.
- **Only forward locomotion is used.** Both packs ship 8-way strafe sets (Walk/Run/Sprint
  Backward, Left, Right, and the diagonals). The controller blends on speed alone, so a
  character strafing or reversing plays the forward cycle. A 2D directional blend tree would
  use them and is the obvious next animation improvement.
- **The EEJANAI FBX folder (~80 MB) is dead weight on disk.** Its Generic clips are unusable
  and only the `.anim` assets are wired. Unity does not ship unreferenced assets into a build,
  so this costs disk and import time, not APK size. Left in place rather than half-deleting a
  freshly imported pack.
- **Turn-in-place clips are unused.** The pack ships `Turn01_Left/Right`; the character rotates
  by yaw without a turn animation, so a stationary turn pivots the feet.

---

## What the editor cannot tell us

Per CLAUDE.md §5, none of the above is device verification. Still open, and Phase 10's job:

- Animation cost on a mobile CPU. The crowd is 16 humanoid animators plus the player, the
  police and 43 animals; humanoid retargeting is more expensive than generic, and the new
  locomotion blend evaluates **four** clips where the old one evaluated three.
- Whether the mobile skin-weight limit (2 bones on Low, per PHASE7's tier table) degrades these
  clips more or less than it degraded the Mixamo ones.
- Everything from PHASE9's open list: whether a mid-range phone holds 30 fps at Low with ~729
  renderers drawn and 1.14 M triangles.

**Required next step: build the APK, install it, run the Phase 8 device matrix, and watch
animation specifically.** Until then this phase is *Editor-verified*, not done.

---

## Rebuilding

```
Tools > Mini GTA > 5. Build Player Animator
```

then, because the weapon socket is derived from an animation clip and must be re-solved
whenever the animation source changes:

```
Tools > Mini GTA > 6. Assemble Scene
Tools > Mini GTA > 13. Build Character Prefabs
```

`BUILD EVERYTHING` does all three in the right order.

---

# ADDENDUM — Phase 9b-fix (2026-09-01)

Four defects found in Editor playtesting after Phase 9b was reported complete. All four are
fixed and re-verified. **One was mine from Phase 9b, one was a Phase 9 bug I wrote and did not
catch, one predates Phase 9 entirely, and one turned out to be a symptom rather than a defect.**

## What I got wrong in the Phase 9b report

Phase 9b reported the animation checklist as PASS. That report was based on reading
`GetCurrentAnimatorClipInfo(layer)` and confirming the expected clip name came back. For the
upper-body layer that check returned an empty array, and I recorded it as "the layer contributes
nothing". **That was the wrong conclusion and it is what let the zombie pose ship.**

An Override layer at weight 1 whose active state has no motion does not contribute nothing. It
writes the humanoid *zero-muscle* pose over every bone its mask covers, and that mask covered
the torso, head and both arms. The clip-info array is empty in exactly that case, so the check I
used could not have detected it. Only looking at the character could — and the Phase 9
screenshot shows the same arms-out stance, which I read at the time as "the Idle clip has the
arms out".

The corrective is in the checklist below: layer weights are now asserted explicitly, and every
state is confirmed as a rendered frame rather than as a string.

---

## Issue 1 — Player and NPCs stuck in an arms-forward "zombie" pose

**Reproduced** at the start of this session: player upper layer `weight = 1.00`, current state
`None`, `GetCurrentAnimatorClipInfo(1)` empty. The rendered frame shows both arms extended
straight forward with the legs animating normally underneath.

**Root cause: the reported hypothesis (c), plus a second contributing cause.**

1. The `UpperBody` layer is `Override`, `defaultWeight = 1`, masked over Body + Head + both arms
   + both fingers, and its default state `None` has **no motion**. An override layer at full
   weight with an empty state writes the humanoid zero pose across the whole mask. That is the
   arms-forward stance, and it applies to **every** character — the sixteen pedestrians as well
   as the player. **This structure predates Phase 9 and Phase 9b; it has been in the controller
   since Phase 1/3.**
2. On top of that, Phase 9b's `ArmedIdle` state held a full two-handed gun *aim* pose for as
   long as `Armed` was true — and the player is armed from the first frame, so the player was
   permanently aiming while walking around. **That half was mine (D17).**

**Fix.**

- `UpperBody` now ships at `defaultWeight = 0`. `PlayerAnimation.LateUpdate` raises it only
  while a combat one-shot is genuinely playing and lowers it again afterwards. "Genuinely
  playing" is asked of the animator rather than tracked with timers: the resting state carries a
  `Rest` tag, so *not resting, or mid-transition* is exactly *an action is on screen*. That stays
  correct if a clip length or an exit time changes later.
- `ArmedIdle` is deleted. Holding a weapon is now a **fingers-only** layer (`Hands`, masked to
  LeftFingers + RightFingers) playing the pack's `Human@ObjectGripHands01` grip pose, its weight
  driven by `Armed`. The intent behind `ArmedIdle` was right — PHASE9 correctly flagged the
  pistol sitting in an open hand — but closing a hand around a grip is a job for the fingers,
  not the shoulders. The arms are left entirely to locomotion.

**Verified.** Player and a pedestrian side by side on open road: arms hang naturally, the player
holds the pistol in a closed grip at his side, the NPC walks with a normal arm swing. Scene-wide
assertion: **0 of 26 characters** have the upper layer raised while resting.

## Issue 2 — White horizontal striping across road geometry

**Reproduced** with a daylight capture at a junction: the crosswalk paint tears into wide white
bands across the full carriageway while the lane dashes beside it render crisply.

**Root cause: coplanar geometry. Measurable, not a judgement call.**

```
road slab top face       = 15.1200
crosswalk paint top face = 15.1200   <-- exactly coplanar
lane marking             = 15.1200 .. 15.1400   (2 cm proud, never affected)
```

`RoadNetworkBuilder.BuildCrosswalks` centred a 0.02-thick box at `centre.y + 0.05`, putting its
top face at `centre.y + 0.06` — precisely the road surface. The depth buffer cannot separate two
coplanar surfaces, so the white `Sig_Zebra` material and the dark `Road` material fight for every
pixel, across all 64 junctions.

It is **not** the Cartoon City pack: that pack supplies no road geometry, and the roads are still
Phase 1 primitive boxes. It is **not** the Phase 8 dynamic-atlas bug, which was UI text. This is
a Phase 2 geometry error that nothing had made obvious before.

A second, compounding factor: Phase 7 moved the lane markings onto the cullable `Detail` layer
but **missed the crosswalk paint**, so the zebras kept drawing out to the tier's full draw
distance (420 m at the lowest) — exactly where depth precision is worst.

**Fix, both halves.**

- The paint is centred at `centre.y + 0.11`, spanning `+0.10 .. +0.12`: 4 cm clear of the road
  and 2 cm clear of the lane markings, which matters because the centre line runs straight
  through every crossing. No face of the box shares a plane with anything.
- `PerformanceSetup` gained a prefix list (`Detail_`, `Zebra_`) so the paint distance-culls like
  the lane markings. The rule that anything with a collider is never culled is unchanged; this
  paint is collider-free.

**Verified.** Same camera as the "before" capture: crisp, solid, correctly-bounded white
rectangles, no tearing. Measured now: `zebra 15.1600 .. 15.1800`, `layer = Detail`.

## Issue 3 — No animals visible during play

**Reproduced, and the real cause was not what the symptom suggested.** The 43 animals were
present and rendering. Two separate problems:

1. **They were levitating.** Sampling their positions mid-session found animals at
   **y = 78 to 255 metres** — hundreds of metres above a city that sits at y ≈ 15.
   `AmbientAnimal.SampleGround` took the first thing a downward ray hit, and the ray hits
   anything solid **including other animals**. Two animals standing close together each read the
   other's back as ground, each stepped up onto it, and the pair climbed without limit. Over a
   few minutes of play the whole population drifted into the sky. **This is a Phase 9 bug I
   wrote and did not catch**, because Phase 9 measured animal heights only in the Editor on
   freshly-placed objects, never after the simulation had run.
2. **They were all far from where anyone plays.** Every animal was in the park, on the coast or
   up the mountains. Nearest to the player's spawn point: **88 m**, then 122 m, then 160 m.
   Right zones, wrong for a player who spawns on a city street. And `SimulationRange` was 140 m
   against a draw distance of 420 m, so an animal could be plainly on screen and frozen.

**Fix.**

- The ground probe now uses `RaycastNonAlloc` and **skips any hit belonging to an
  `AmbientAnimal` or a `CharacterController`**, then clamps the result to within `MaxGroundRise`
  (3 m) of the patch the animal was placed on. No feedback loop survives either guard.
- A new `Zone.City` places 20 strays — dogs, cats, chickens — on the pavement band around city
  blocks, dealt from a **shuffled deck of blocks** so twenty strays land on twenty different
  blocks. Drawing at random with replacement left whole quarters empty and put the nearest
  animal 190 m from spawn, which is the same "technically placed, never seen" failure this zone
  exists to fix.
- `SimulationRange` raised 140 → 260 m, and the animals' animators set to `CullCompletely`, so
  the off-screen case is the renderer's job (free) rather than a distance guess.

**Verified.** 59 animals. Nearest to spawn **64 m** (was 190); **8 within 150 m** (was 0). After
18 s of simulation the worst rise above home across all 59 is **0.31 m** (was hundreds of
metres). Rendered frame: a stray dog on a city pavement beside a lamppost and a bin.

## Issue 4 — FIRE button appears to trigger no attack animation

**The button was never broken.** Pressing it through its real `IPointerDownHandler` path
decremented ammo 48 → 47 on the first attempt. It is a `HudButton` driven by pointer events, so
it was never exposed to the Phase 8 dead-listener bug.

**Root cause: a symptom of Issue 1.** The attack one-shot plays on the `UpperBody` layer — the
same layer already pinned at weight 1 holding an arms-forward pose. A 0.3-second firing
animation blended over a body already frozen in an aim stance is close to invisible. Fixing the
layer weight fixed the visibility; no change to `PlayerCombat` was needed or made.

**Verified.** Firing raises the upper layer 0.00 → 0.78 → 1.00 and returns it to 0.00 when the
shot completes; `HumanM@Gun_Aim01_Shoot01 w1.00` plays; ammo decrements. Melee likewise raises
the layer and plays `back fist w1.00`. Both were fired **while walking**, and the base layer kept
blending `Walk 0.85 / Run 0.15` at 2.20 m/s throughout — the masked layer still does its job.

---

## Re-verified Phase 9b checklist

Re-run in full, because the original run was not trustworthy.

| Test | Result | Evidence |
|---|---|---|
| Idle | **PASS** | `HumanM@Idle01 w1.00`, upper layer 0.00, arms down in the rendered frame |
| Walk | **PASS** | `Idle w0.51 / Walk w0.49` at Speed 0.23 |
| Run / Sprint | **PASS** | `Sprint01_Forward w1.00` at 5.80 m/s |
| Jump | **PASS** | `Jump01 - Begin w1.00`, Grounded false, y 15.2 → 16.4 |
| Fall / Land | **PASS** | `Fall01 w1.00` descending; `Jump01 - Land` on touchdown |
| Swim | **PASS** | `InWater=True`, `Swim w1.00`, swam z 500 → 531 |
| Melee (unarmed) | **PASS** | `back fist w1.00`, weapon holstered, locomotion continues |
| Armed attack | **PASS** | `Gun_Aim01_Shoot01 w1.00`, ammo decrements, locomotion continues |
| NPC animation | **PASS** | 16 pedestrians on the new clips, upper layer 0.00 |
| Police animation | **PASS** | officer sprinting, `handsW=1.00`, `weapon='pistol'`, `Armed=True` |
| No drift, root motion locked | **PASS** | **0.00000 m** over 12.4 s idle; **0** animators scene-wide with `applyRootMotion` |
| No T-pose / frozen / popping | **PASS** | 0 of 26 characters with the upper layer stuck raised |

Two earlier drift readings in this session (168 m and 131 m) were **my test scaffolding, not the
game**: police officers I had spawned arrested the player and teleported them to the station
spawn, and one placement put the player inside a building. Recorded because a number that large
looks alarming in a log and the explanation should not have to be rediscovered.

## Regression check

| | Value |
|---|---|
| Renderers / triangles / colliders | 2,003 / 1,163,656 / 477 |
| Detail (cullable) layer | 1,081 |
| Animals | 59 |
| Phase 8 UI | `SafeArea` with 18 roots, 30 buttons, canvas intact |
| Console | 0 errors, 0 warnings |

## Notes worth keeping

- **`Base Layer` serialises `defaultWeight = 0`.** A Unity quirk, not a bug: layer 0's weight
  field is unused and reads `1.00` at runtime. Verified. Do not "fix" it.
- **An empty state on an Override layer is never harmless.** If a layer must sometimes
  contribute nothing, drive its weight to 0. `GetCurrentAnimatorClipInfo` returning an empty
  array is the *signature* of this bug, not evidence of safety.
- **Two flat surfaces at the same height will always tear.** When adding ground decals, log the
  bounds and compare them against whatever is underneath. The audit that found the zebra was one
  query.
- **A raycast used as a ground probe must exclude anything that moves.** Whatever can stand on
  the result can become the result.

**Device verification remains the required next step and is still Phase 10's job.** Nothing in
this addendum was tested on hardware.
