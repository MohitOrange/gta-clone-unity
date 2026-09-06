# Mini GTA — Phase 14: full game systems pass

Work order: the MASTER BUILD PROMPT, sections 0–10. Builds on [PHASE13](PHASE13.md) (the
Space Exploration re-skin) and [PHASE9B](PHASE9B.md) (the animation pack and the humanoid rig).

> **Status: sections 0–9 done or partial, 10 and 3B not started.** Everything below is
> Editor/Play-mode verified. Per CLAUDE.md §5 that is *not* verification for anything touching
> UI, input or performance — nothing in this phase has been on the device yet.

---

## The bug log

Numbering started in Section 1. Before this file existed the numbers lived only in commit
messages and code comments, which is why there is no BUG-001..007 — those are Phase 1–7
issues recorded in their own phase docs under different names.

| # | Title | State | Fixed in |
|---|---|---|---|
| BUG-008 | Melee used the camera's forward, not the body's | FIXED | Section 1 |
| BUG-009 | Attack input destroyed during cooldown | OPEN | — |
| BUG-010 | Delivery mission failed at 12s of a 165s run | FIXED | Section 1 |
| BUG-011 | (no repro) | CLOSED | — |
| BUG-012 | Death animation never played | FIXED | `3dbd563` |
| BUG-013 | `Health.Heal` accepts negatives and can kill | OPEN | — |
| BUG-014 | Armed NPCs had no melee at any range | FIXED | `38c5286` |
| BUG-015 | (resolved during Section 1) | CLOSED | — |
| BUG-016 | 24 `Bld_Pack` buildings carry Animators with no controller | OPEN (benign) | — |
| BUG-017 | `WeatherSystem` threw a NullReferenceException every frame | FIXED | `553839f` |
| BUG-018 | No render pipeline assigned — the whole world drew magenta | FIXED | `80c4282` |
| BUG-019 | `Detail` layer never existed, so detail culling never ran | FIXED | see below |
| BUG-020 | Unused `Toon`/UTS shaders fail to compile against this URP | OPEN (benign) | — |
| BUG-021 | Pedestrian separation steering was O(n²) over the whole population | FIXED | Section 3 |
| BUG-022 | Crowd clones inherited the template's route and piled up 780 m away | FIXED | Section 3 |
| BUG-023 | Frozen pedestrians rendered in the T-pose | FIXED | Section 3 |
| BUG-024 | Clones inherited disabled renderers and could never be shown again | FIXED | Section 3 |
| BUG-025 | Build settings pointed at the HelicopterAttack demo, not the game | FIXED | `05b39fa` |
| BUG-026 | Release signing pointed at a nonexistent keystore in another project | **FIXED** -- new key, verified on the APK | `c49ea69` |
| BUG-027 | Asset-pack materials still on Built-in shaders draw magenta under URP | FIXED | Section 6 |
| BUG-028 | The application icon is the HelicopterAttack pack's store icon | OPEN (needs art) | Section 8 |
| BUG-029 | Rebuilding the animator controller nulls every Animator in the open scene | FIXED (procedure) | Section 2.6 |
| BUG-030 | A parked player-owned car was teleported away by the traffic recycler | FIXED | Section 5 |
| BUG-031 | Stripping components with `Destroy` left them live for the rest of the frame | FIXED | Section 5 |
| BUG-032 | `WeaponController`, `CheatConsole` and `HelicopterController` attached to nothing | FIXED (heli deferred) | audit fix-up |
| BUG-033 | Part 1 committed `City.unity` with 78 null animator refs (BUG-029, saved through) | FIXED | `bb7b042` |
| BUG-034 | Crowd cost weighting used `mesh.triangles`, empty on non-readable meshes in a build | FIXED | audit fix-up |
| BUG-035 | Weapon library's 7 definitions were identical placeholders | FIXED | audit fix-up |
| BUG-036 | No texture in the project had an Android platform override; ETC2 was never applied | FIXED | `86e9485` |

---

## BUG-012 — death animation never played

**Symptom.** A killed NPC kept playing `HumanM@Idle01`. The pack has shipped `Death01-03`
since Phase 9b.

**Why it resisted diagnosis.** Every direct check passed. The `Dead` parameter really was
`True` on the animator. The `Death` state really was in `PlayerLocomotion.controller`, with a
motion assigned, and the `AnyState → Death` transition really was present, unmuted, with
`hasExitTime = false` and the right condition. All of that was verified both in the asset
YAML on disk and on the live runtime animator.

The reason those checks all passed is that they were reading a machine that had been switched
off. `RagdollLite.Collapse` set `animator.enabled = false` on the first frame of the collapse
— the same frame `Health.Died` raised `Dead`. The state machine never got a frame in which to
evaluate the transition, so the rig froze on its last evaluated clip.

**A hypothesis worth recording as wrong.** The prior session's leading theory was that
pedestrians ran a *different* controller than the one being inspected. They do not: 79 scene
rigs all reference `PlayerLocomotion.controller`, confirmed by GUID search and again at
runtime (`controller = PlayerLocomotion`). Chasing this cost a session.

**Fix.** `Collapse` is two-stage: the rig plays its death clip, then the pose freezes and the
body goes physical. The hold is read from the death state's own length rather than hardcoded,
because `Death01/02/03` differ (`Death01` is 0.733 s) and a constant would freeze some deaths
mid-fall and let others finish and stand there before toppling. Culling is forced to
`AlwaysAnimate` first — pedestrian animators run `CullCompletely`, so a body dying off-screen
would otherwise pop into its death pose when the camera found it. Rigs whose controller has no
`Dead` parameter collapse immediately, exactly as before.

**Verified** on two pedestrians, one killed while paused and one under running time:
`IsName("Death") = True`, clip `HumanM@Death01`, then `animator.enabled = False` with
`Rigidbody` and `CapsuleCollider` present.

### The trap that invalidated the first two attempts

`Time.timeScale` was **0** for both. The lobby was open, and `MenuState` zeroes the timescale
while any menu is stacked. Nothing animates, and a coroutine's `WaitForSeconds` never returns,
so the fix looked broken when it was merely unobservable.

This is the second time this has produced a false negative — it also invalidated a melee test
in Section 1.5. **Before measuring anything in play mode, assert `Time.timeScale > 0`**, and
call `MenuState.ForceClear()` if it is not. `Time.time == 0` after many frames is the tell.

---

## BUG-014 — armed NPCs had no melee at any range

`HostileNpc` shot at every distance including zero. Besides reading as a bug, the raycast is
unreliable at contact range: the muzzle origin can already be inside the target's collider, so
the ray starts *past* it and hits whatever is behind.

Inside `MeleeRange` the enemy now swings. The swing deliberately mirrors `PlayerCombat.Melee`
rather than inventing a second set of rules, per item 2.9 — same hit sphere half a range ahead
of the chest, same forward vector, same 0.2 dot facing gate. Those two were wrong *together*
in BUG-008 and are now right together in both classes.

The swing damages any `Health` in the arc rather than only the current `Target`, which is what
makes NPC-vs-NPC damage work: a swing aimed at the player catches whoever else is standing in
it, exactly as the player's does.

**Verified** with a hostile built the way `EliminationMission` builds one (an NPC body plus
`AddComponent<HostileNpc>`) targeting a pedestrian: 21 damage dealt, being one swing (12) at
contact and one shot (9) after the victim fled to 6.6 m — both paths and the range switch.

**`PoliceOfficer` was deliberately not changed.** It already has a designed close-range
behaviour in `State.Arresting`. A baton takedown would conflict with the arrest flow; that is
a design change, not a bug fix.

---

## BUG-017 — a NullReferenceException on every frame

Found incidentally by reading the console after the runs above, which is the only reason it
was found at all — it is silent in gameplay.

`WeatherSystem` cached a `ParticleSystem.EmissionModule` in a field in `Awake` and wrote
through it in `LateUpdate`. **The module structs are handles holding a pointer back to the
owning system, not values.** A cached one does not survive a domain reload, so after any
script recompile the field was still there but its owner was gone, and every write threw
*"Do not create your own module instances, get them from a ParticleSystem instance"*.

That is a per-frame managed exception in `LateUpdate`, on every frame the game runs, whether
or not it is raining. The console logged 25 in one second.

They are cheap to fetch, so it now fetches one at the point of use. Console error count for
`WeatherSystem`: 25/second → 0.

---

## BUG-018 — no render pipeline assigned

**The single most serious thing found in this phase, and it was found by accident** — while
capturing a screenshot to prove the muzzle tracer worked.

`ProjectSettings/GraphicsSettings.asset` had `m_CustomRenderPipeline: {fileID: 0}`, and all six
quality levels had `customRenderPipeline: {fileID: 0}` as well. The project was running the
**Built-in** pipeline while every one of its materials uses `Universal Render Pipeline/Lit`,
which has no valid pass under Built-in. The entire world drew magenta.

Sampling 401 `MeshRenderer`s found **0 broken shaders** — every material was fine and every
shader compiled. The materials were never the problem; there was simply no URP asset for them
to render under.

**Not a regression from this phase.** The identical `fileID: 0` is in the baseline commit
`5fb49fc`, and nothing in `Assets/Game/Scripts` ever assigns the pipeline — `PerformanceTuner`
only reads it. `Mobile_RPAsset` and `PC_RPAsset` existed the whole time, orphaned.

**The second consequence is why this was worth chasing rather than just repainting.**
`PerformanceTuner` resolves its asset as
`QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline`
(`PerformanceTuner.cs:160` and `:184`). Both were null, so `urp` was null, so **the entire
PHASE7 tier system has been silently doing nothing** — shadow distance, render scale, MSAA and
cascades were all no-ops. Every performance number in PHASE7 needs re-measuring now that the
tiers actually apply.

`Mobile_RPAsset` is the default, because Android is the PRIMARY target per CLAUDE.md §5.
Quality levels are deliberately left on *use default* so exactly one URP asset is ever live:
`PerformanceTuner` mutates the active asset at runtime and restores it in `OnDestroy`, and
PHASE7 records that this already leaked tier edits into the repo once.

**Verified** by capture through the real game camera — terrain, ocean, sky and cast shadows all
correct where the previous capture was solid magenta.

> **This is why CLAUDE.md §5 says Editor-only testing is not verification.** Every parameter
> read in this session passed while the game was unshippable. It took a rendered frame.

---

## Phase 7 re-baseline

The PHASE7 tier table was measured against settings that were never applied. It is now void;
this is the replacement, measured with URP actually assigned and detail culling actually
running.

### BUG-019 — the `Detail` layer never existed

`PerformanceTuner.ApplyCamera` does:

```csharp
if (_detailLayer < 0) _detailLayer = LayerMask.NameToLayer(DetailLayer);   // "Detail"
if (_detailLayer < 0) return;                                              // <-- always taken
```

`NameToLayer("Detail")` returned **-1**. The 850 decorative renderers were sitting on layer
index **8**, which this project's TagManager still called **`MainBall`** — a leftover layer
name from an imported asset pack, next to an empty `SmallBall`. Every one of the 850 objects
is named `Detail_*` (shrubs, bushes, trash cans, stones, grass), so the intent was never in
doubt; the objects were on the right *index* under the wrong *name*.

Fixed by renaming layer 8 to `Detail` in `ProjectSettings/TagManager.asset`. Scenes store layer
*indices*, not names, and nothing in the project referenced `MainBall` in code or in any asset,
so the rename moves no objects and touches no scene. `SmallBall` (layer 9, zero renderers) is
left alone as unrelated junk.

**This is the same failure shape as BUG-018**: a silent `return` when a prerequisite is
missing. Two of them, in the same file, both hiding a whole feature.

### The corrected tier table

Settings are read back **off the live URP asset and camera after applying each tier**, not
copied from the source. They match what PHASE7 intended — the intent was right all along, only
the application was broken.

| | Low | Medium | High |
|---|---|---|---|
| Shadow distance | 38 m | 65 m | 120 m |
| Shadow cascades | 1 | 1 | 2 |
| Render scale | 0.75 | 0.90 | 1.00 |
| MSAA | 1× (off) | 1× (off) | 2× |
| Draw distance | 420 m | 700 m | 900 m |
| Detail cull | 85 m | 170 m | 320 m |
| LOD bias | 0.55 | 1.00 | 1.50 |
| Physics rate | 30 Hz | 50 Hz | 50 Hz |
| Frame cap | 30 | 60 | 60 |
| Skin weights | 2 bones | 4 bones | 4 bones |
| **Renderers drawn — detail cull broken** | 273 | 1123 | 1692 |
| **Renderers drawn — detail cull working** | **183** | **803** | **1246** |
| **Reduction** | **−33 %** | **−28 %** | **−26 %** |

3,165 renderers are enabled in the scene; the "drawn" rows are what survives frustum and
distance culling from one fixed camera position. PHASE7 claimed a 31 % reduction from detail
culling and it is genuinely in that range — it just had never actually run.

### Frame times

| | Low | Medium | High |
|---|---|---|---|
| Editor fps (capped) | 29.2 / 30 | 59.1 / 60 | 56.3 / 60 |
| Frame time | 34.25 ms | 16.93 ms | 17.77 ms |

Low and Medium are sitting on their frame caps, so those two numbers measure the cap, not the
load. **High is the only load-limited reading.**

### The number that matters for Section 3

Uncapped, High tier, detail culling live, **66 pedestrians**:

> **69.5 fps — 14.38 ms/frame — 2.29 ms of headroom against a 60 fps budget.**

That is on a desktop, in the Editor, with the Editor's own overhead, at 66 NPCs. Section 3
asks for **300–1000**, which is 4.5×–15× the NPC count, on a mid-range Android phone.

**2.29 ms does not absorb that.** Section 3 cannot be "spawn more and measure" — the budget is
gone before it starts. It has to be built as a budgeted system from the first line: a hard cap
on simulated NPCs, distance-banded LOD with most of the population in a cheap non-animated
state, and pooling rather than instantiation.

> Every one of these numbers is an **Editor, desktop** number. Per CLAUDE.md §5 none of it is
> verification. The device pass is still owed, and is now more important than before, because
> the headroom is thin.

---

## Section 2 — gun mechanics

Delivered:

- `WeaponController` — explicit state machine (`Idle/Equipping/Firing/Reloading/Switching/
  Locked`), two slots, pickup rules, shared by player and NPC per 2.9.
- Ballistics moved onto `WeaponDefinition`, so a weapon's numbers are a property of the
  weapon rather than of whoever holds it.
- `Reload` (Trigger) and `Dead` (Bool) animator parameters; `ReloadUpper` on the UpperBody
  layer; full-body `Death` with `AnyState → Death`.
- Death wiring through `DamageReaction`, and the `RagdollLite` handoff above.

**Asset gap, unresolved.** The pack ships no equip/draw/holster/switch clip. Items 2.2 and 2.7
ask for the equip animation to be played from the pack, and there is nothing to play. Applied
default: timed `EquipSeconds` states with a Hands-layer grip ramp. This needs either a new
asset or sign-off on the timed substitute.

### 2.4 — fire VFX (done)

`WeaponVfx` is one shared service: muzzle flash, tracer, impact spark, wired into all three
fire sites (`PlayerCombat.FirePistol`, `HostileNpc.Fire`, `PoliceOfficer.Fire`) so the player
and every armed NPC produce the same effect, per 2.9.

**Everything is built procedurally in `Awake`** — no prefab, no texture, no material, no
particle asset — because §6 says not to generate art assets, and a gunshot with no visible
effect at all is worse than a plain one. Everything is pooled: a firefight is the one moment
this project allocates hardest, and a `LineRenderer` per bullet is how one turns into a GC
pause.

The tracer is drawn from the **muzzle**, not the aim origin. The aim origin is the camera, so a
tracer starting there is a streak out of the player's own face — the same class of mistake as
BUG-008.

The flash pool is deliberately small (4). Each live flash is an extra realtime light, and URP
on mobile has a hard per-object additional-light limit past which lights are *silently
dropped* — an unbounded pool would make flashes vanish at random rather than fail loudly.

**Verified** in play mode: 12/4/8 pool objects built, all idle at rest, one `Shot` lights
exactly one tracer and one flash, and 41 shots leave the pool still at 12 objects (recycles,
does not grow). Confirmed visually in the capture above.

**Assets that would upgrade this in a polish pass** (call sites would not change): a muzzle
flash sprite sheet, a soft additive tracer texture, an impact spark texture and a bullet-hole
decal. Suggested sources: Unity Asset Store *Realistic Muzzle Flashes* or the free
*Cartoon FX Remaster Free* pack.

**Still open in Section 2:** 2.6 (aim-movement blend) and 2.10 (end-to-end verification pass).

---

## Section 3 — NPC scaling to 300-1000

**Result: 300 pedestrians now cost less than 66 did before this work.**

| Population | Frame time | Notes |
|---|---|---|
| 66 (before Section 3) | 14.38 ms | the shipping scene, O(n²) steering |
| 66 (grid only) | 13.27 ms | |
| **300** | **13.61 ms** | 110 drawn, 0 mismatch |
| 600 | 20.22 ms | measured mid-pass, before the visibility fix |
| **1000** | **29.23 ms** | 416 drawn |

Uncapped, High tier, desktop Editor. **300 is comfortably inside a 60 fps budget; 1000 is not,
and 1000 was never going to be on a phone.** The recommended shipping default is 300 with the
caps below; 1000 works and is stable, at ~34 fps in the Editor.

### What actually blocked scaling

Not rendering. `Pedestrian.Separation()` walked the entire registry, once per pedestrian per
frame -- **O(n²)**:

| Population | Distance checks per frame |
|---|---|
| 66 | 4,356 |
| 300 | 90,000 |
| 1000 | **1,000,000** |

That is pure CPU, so no animation or renderer LOD could have touched it. `CrowdGrid` is a
uniform spatial hash, rebuilt lazily once per frame on first use, and `Separation`,
`CountNear` and `AlarmNear` all go through it. Cost now follows local crowd density rather
than total population.

### The system

- **`CrowdGrid`** — spatial hash. Lazy rebuild, no scene object to forget to add.
- **`CrowdDirector`** — grows the population, bands it, recycles stragglers toward the player.
- **Three bands, all hard-capped**: Full (every frame, separation steering, cap 40), Simple
  (one frame in four, no separation, cap 120), Frozen (not ticked). Caps rather than pure
  distance, so walking into a dense square cannot blow the budget. Verified holding exactly at
  the caps at every population.
- **Visibility is a separate axis from simulation.** Beyond `VisibleBand` (150 m) the meshes
  are switched off; a Frozen pedestrian inside it still draws and still animates.
- **Placement on pavements** via `RoadNetwork`, not a uniform disc.
- **Recycling** brings distant pedestrians back near the player, capped per frame.

### Four bugs found while building it, all by measurement

- **BUG-021** the O(n²) steering above.
- **BUG-022** clones inherited the template's `PointA`, and `Pedestrian.Start` does
  `transform.position = PointA` -- so all 534 clones teleported onto the template's route on
  their first frame, centroid 780 m from the camera, none of them close enough to band above
  Frozen.
- **BUG-023** Frozen pedestrians rendered in the **T-pose**. Disabling an Animator freezes the
  rig where it is, and a pedestrian that spawned Frozen never evaluated a frame, so it had no
  pose to hold. Caught in a screenshot, not in a counter. PHASE9B shipped this exact bug once.
  Fixed by tying the animator to *visibility* rather than to the simulation band: drawn implies
  animated.
- **BUG-024** clones inherited **disabled renderers**. `Instantiate` copies component state, so
  cloning a template that happened to be outside the visible band produced clones whose meshes
  were off -- while the clone's `_renderersOn = true` initialiser claimed otherwise, so the
  early-out blocked every attempt to switch them back on. 414 pedestrians inside the visible
  band, 33 drawn, one of the missing 381 standing 9.3 m from the camera at Full LOD. A cached
  flag describing another object's state is only ever a guess.

> Three of those four were **silent**: the counters looked healthy and the frame time looked
> plausible. BUG-023 and BUG-024 were only found by rendering a frame and looking at it.

### Known limitations

- **All clones share one character model.** The crowd is visibly 400 copies of one person. The
  variety pass is not done.
- **Recycling is slow to converge** at the shipping rate (3/frame). It is correct but a player
  sprinting across the map will outrun it for a while.
- Editor, desktop, one camera position. Per CLAUDE.md §5, not verification.

---

## BUG-026 — the APK could not be signed

The first device build failed after five seconds:

```
UnityException: Unable to sign the Android application
No keystore passwords were found.
```

`ProjectSettings.asset` had:

```
AndroidKeystoreName: D:/JoySmashProjects/keystore/bundle.keystore
androidUseCustomKeystore: 1
```

That is a keystore belonging to **a different project entirely** (`JoySmashProjects`), on the
drive with under 2 GB free — and **the file does not exist**. Same family as BUG-025: settings
inherited from somewhere else and never noticed, because an interactive Editor session that
had the passwords typed into it would have built fine. Only a clean batch process exposed it.

The passwords are correctly *not* in the repository, so they cannot be recovered from it, and
they are not something to be handed around.

**A benchmark build does not need the release key.** Development builds are now signed with
the Android debug key, which installs on a device perfectly well and cannot be shipped — the
right property for a measurement build. The signing choice is restored in a `finally`, so a
development build can never leave custom signing switched off behind it; that is how a release
later goes out debug-signed without anyone noticing.

Release builds keep the custom keystore and now **fail loudly** if it is missing or has no
password, rather than falling back. A release that silently debug-signs is worse than one that
does not build.

> **Still open for release:** a real keystore for `com.minigta.city` has to be created or
> located, and its path fixed in Publishing Settings. Until then only development builds are
> possible. Not a blocker for performance measurement; it is a blocker for shipping.

---

## Section 9 — cheat codes

**67 codes registered.** Item 9 asks for 50 or more.

| Category | Codes |
|---|---|
| Player | 12 |
| Weapons | 12 |
| World | 18 |
| Money | 6 |
| Police | 6 |
| Debug | 6 |
| Crowd | 4 |
| Vehicles | 3 |

### Two gates, meaning different things

`CheatRegistry.Available` is a property of the **build**: a shipping player build cannot reach
any of this whatever it types, per the standing resolution on 9.4. `CheatRegistry.Enabled` is a
property of the **session**: even in a development build the codes are inert until the hidden
phrase is typed, so a tester mashing keys cannot trip one.

`CheatConsole` matches typed characters through the Input System's `onTextInput` — this project
is Input System only, and the legacy `Input` class throws. There is deliberately **no UI**: the
whole point of a hidden toggle is that nothing advertises it. `ToggleFromDevMenu()` exists for a
button to call instead, because a phone has no keyboard and that is exactly where a hidden
toggle is most needed.

### Verified, in batch mode

```
registered = 67          duplicate codes = 0
--- gate, cheats disabled ---
HEAL       -> Disabled
NOT_A_CODE -> Disabled
--- gate opened ---
NOT_A_CODE -> Unknown    (the table is being consulted, not short-circuited)
CHEAT_LIST -> Applied    (needs nothing from the scene)
HEAL       -> Failed     (correct: edit mode has no player to heal)
```

Those last three are the interesting ones. A gate that returns the same answer for a valid and
an invalid code is not proving anything, so the test checks that once opened, a bad code says
*Unknown* while a good one says *Applied* — and that a code with nothing to act on says
*Failed* rather than lying about success.

**Every code does something real.** None is a stub; §3 forbids placeholder logic in finished
code, and a cheat that silently does nothing looks like a broken system rather than a missing
feature. Where there is nothing to act on the code returns false and the caller reports
`Failed`, which is a different and honest answer.

`NPC_MAX` is bound to `CrowdDirector.TrySetPopulation`, which itself refuses anything above the
shipping cap of 300 outside a development build — so the stress-test population is behind
*both* gates.

`SLOWMO` and friends deliberately refuse to set `Time.timeScale` to zero. `MenuState` owns
that, and a cheat that froze time with no menu open would leave no way back — the exact trap
that produced two false negatives during BUG-012.

**Not yet verified:** the codes have only been exercised in edit mode, where most correctly
report `Failed` for want of a player. Confirming they *do* the right thing needs a Play-mode
pass, which is blocked (see below).

---

## The Play-mode automation problem

Worth recording, because it cost a lot of time and will recur.

With the device unavailable, an Editor measurement needs Play mode to run unattended. **Both
available routes are blocked:**

1. **The MCP bridge** requires a human to approve the connection —
   `ConnectionValidator.ValidateAndApproveAsync` shows a prompt in the Editor. A tool call
   sat for 30 minutes waiting on a click that never came. This is a security gate and is not
   something to work around.
2. **`-batchmode -executeMethod` does not pump the Play loop.** `EditorApplication.EnterPlaymode()`
   is accepted, the backup scene is loaded, and then nothing further happens — the method
   returns and batch mode idles rather than running frames. Confirmed by a run that logged
   "entering Play mode" and then produced no output for twenty minutes.

A third route exists — PlayMode tests via `-runTests` — but this project has **no asmdefs**, so
all code lives in the predefined `Assembly-CSharp`, and an asmdef cannot reference that. Wiring
it up is a larger change than the measurement it would serve.

**What does work in batch mode is edit-mode `-executeMethod`**, with two caveats found the hard
way: a launch that recompiles scripts silently *skips* the method (run it twice), and batch mode
opens an **empty scene**, not the game — the first harbour run correctly reported "no
WaterVolume in the scene" because it was looking at nothing.

---

## Section 6 — harbour: ocean, ship, bridge

The ocean already existed: `WaterVolume` with wave maths mirrored on CPU and GPU, already
driving swimming and the boat's buoyancy. What Section 6 adds is the set-piece coast.

`HarborBuilder` places five pieces — an explorable ship, a wreck, two piers and a bridge —
finding the shoreline by probing sixteen directions outward from the map centre until the
ground drops below sea level, rather than having coordinates typed into it. **No art was
generated**: everything is a prefab already in the project.

| Piece | World size | Renderers | Colliders |
|---|---|---|---|
| Ship_Explorable | 34.4 × 31.7 × 21.6 m | 19 | 19 |
| Ship_Wreck | 25.8 × 26.9 × 33.1 m | 1 | 1 |
| Pier_Main / Pier_Side | 16.0 × 10.5 × 15.9 m | 1 | 1 |
| Bridge_Shore | 16.5 × 8.3 × 24.3 m | 1 | 1 |

The source prefabs are scenery — renderers with no collision — so the builder adds non-convex
`MeshCollider`s. Non-convex matters: a convex hull would fill in the decks and railings and turn
the ship into a solid block instead of something to walk around inside, which is the whole of
6.2's "static explorable set-piece".

### Three defects the logs called healthy

Each of these passed every counter and was caught only by rendering the frame and looking.

1. **The ship was seven metres long.** The audit reported 19 renderers, 19 colliders and a
   correct-looking size, and every number was true. `99_Ship_L1` is 7.6 m out of the box, and a
   player capsule is 1.8 m — the "explorable ship" was a rowboat. Scales are now applied from
   the measured native size, and the audit prints final world dimensions so this stays checkable.
2. **BUG-027: the ship rendered magenta.** Its material `M_Imphenzia_LowPollyStyle` is on
   Built-in `Standard`, which has no valid pass under URP. This is BUG-018's long tail — the
   401-renderer sample that found zero broken materials could only see what was *already in the
   scene*; the packs were full of Built-in materials waiting to be placed. `UrpMaterialFixer`
   converts them, carrying `_MainTex`→`_BaseMap` and `_Color`→`_BaseColor` across the shader
   swap, and is idempotent (a second run reports 2 URP, 0 converted).
3. **The ship floated on the water like a bath toy.** These prefabs pivot at the keel, so
   placing one at sea level puts the whole hull above the surface. Draft is now applied from
   measured bounds rather than a fixed offset, because the two ships differ in size and scale.

**Verified** by rendered frame: hull, skull sails, deck and cannons all correct, ship sitting at
a believable waterline, piers and bridge in place. Captured in **edit mode** — Play mode cannot
be driven unattended here, but a camera renders to a RenderTexture perfectly well outside it, so
the frame is real.

---

## Section 7 — helicopters

`HelicopterController : Vehicle`, so it inherits enter/exit, occupancy, damage and the horn
rather than living beside the vehicle system.

**Control scheme as agreed: one stick, two altitude buttons, automatic yaw.** A faithful
helicopter needs collective, cyclic, anti-torque pedals and throttle — four axes, two coupled,
on a device with one thumb free. That is unflyable on a phone.

The physics stays honest even though the input does not. The rotor produces lift along the
aircraft's own up axis, and the machine moves sideways *because* it tilts, exactly as a real one
does; the stick controls the tilt. So it banks into turns, sags when levelling off and drifts on
after the stick is released, while remaining three inputs.

Details that matter:

- **Hover trim.** At neutral collective, lift exactly cancels gravity, so letting go holds
  altitude. Without it the player taps climb constantly just to stay level, which reads as a
  broken aircraft rather than a demanding one.
- **Auto-yaw only above walking pace.** A hovering helicopter has no direction of travel to
  face and would otherwise pirouette on the spot.
- **Rotor spool-up** (3.5 s) gates flight, so a stolen helicopter cannot leap off the pad.
  Spin-down is slower than spin-up — a rotor has inertia, and an engine cut mid-air should give
  the player a moment rather than dropping them.
- **Centre of mass below the hub**, because a helicopter hangs from its rotor. Without it the
  airframe handles like a brick on a pole and flips at the first input.
- **Ground effect** near the surface, as real downwash produces.

Input plumbing: `VehicleKind.Helicopter` added; `Vehicle.SetFlightInput(cyclic, collective)` is
a virtual no-op so no ground vehicle has to know it exists; `InputHub.Climb` reuses the existing
on-screen gas and brake buttons, since in the air the stick already carries movement and those
two buttons are free — no new touch controls, and the thumb does not move.

**Not verified.** This is physics that needs to be flown, and Play mode cannot be driven
unattended. Code compiles and is wired end to end; whether it *feels* right is unknown.

---

## Section 8 — icons

**BUG-028.** The configured application icon is
`helicopter-attack-3d-game-template-icon.png` — the HelicopterAttack asset pack's own store
icon — and all eighteen Android icon slots were empty. **The APK built earlier in this phase
carries that pack's artwork on the launcher.**

Same import-clobber family as BUG-025 (scene list, package name) and BUG-026 (keystore). Three
separate settings, all silently overwritten by one asset pack import, none of them warned about.

In-game HUD icons are fine — Phase 13 delivered those (`ui_icon_character`, `ui_icon_exit`,
`ui_icon_lock`, `ui_icon_profile`, `ui_icon_settings`, plus `ui_arrow`, `ui_ring`, `ui_star`).
What does not exist is a **launcher icon for Mini GTA**.

`IconBuilder` audits the configuration, **clears any borrowed icon** — leaving the Unity default
is not good, but it is honest, whereas shipping another product's icon misrepresents the app on
the launcher and in any store listing — and applies a supplied icon to every slot in one call.

**No icon was generated.** §6 reserves art for the project owner, and an application icon is
branding, which is exactly what a tool should not invent. The specification is printed by
`Mini GTA ▸ Icons ▸ Print required asset spec`:

> `Assets/Game/UI/Branding/app_icon.png` — PNG, 1024×1024 source, artwork inside the centre 66 %
> (Android's adaptive mask crops to a circle on many launchers), Alpha Is Transparency on,
> mipmaps off. Must not reuse artwork from an imported asset pack.

Once that file exists, `Mini GTA ▸ Icons ▸ Apply game icon` fills all slots.

---

## Known issues — carry these into the final consolidated report

These are **open limitations, not bugs to be rediscovered.** They belong in the final report,
not buried in a section.

1. **The crowd is one character model.** Every pedestrian `CrowdDirector` spawns is cloned from
   a single template, so a street of 400 people is 400 copies of the same person in the same
   outfit. Visible in every crowd screenshot. Needs a variety pass: a pool of body/outfit
   prefabs, or at minimum randomised material tinting through the existing `UniformTint`.
2. **Recycling converges slowly at the shipping rate.** `RecyclesPerFrame` is 3, so a player
   sprinting across the map outruns the crowd for a while and passes through visibly empty
   streets before it catches up. Correct, just slow. Raising it trades a smoother population
   for a per-frame teleport cost that has not been measured on device.
3. **Nothing in Phase 14 has run on the target device.** Every number is Editor, desktop.
4. **BUG-009, BUG-013, BUG-016, BUG-020** remain open (see the table).
5. **No equip/draw/holster clip exists in the animation pack**, so items 2.2 and 2.7 are served
   by timed states rather than a real animation. Needs an asset or sign-off.

---

## BUG-025 — the build was not the game

Found while preparing the first device build.

`EditorBuildSettings` contained only `Assets/HelicopterAttack/Scenes/Scene_MainMenu.unity` and
`Scene_1.unity` — the asset pack's own demo scenes. **`Assets/Scenes/City.unity`, which is the
entire game, was not in the build at all.** The Android application identifier had likewise
been overwritten to `com.BlackRoseDevelopers.HelicopterAttack`, and the version reset to 0.1 /
versionCode 1.

An APK built in that state would have succeeded, installed cleanly under the asset pack's
package name — so alongside the real game rather than over it — and run the helicopter demo.
Nothing would have warned.

Not caused by this phase: the identical settings are in the baseline commit `5fb49fc`.
`Builds/Android/MiniGTA-0.2.0.apk` is dated 31 Aug and still carries the correct package name,
so the pack was imported after that build, clobbered the settings, and the snapshot captured
the already-broken state.

The real package name was **recovered by reading the `AndroidManifest.xml` out of that shipped
APK**, not guessed: `com.minigta.city`.

`Assets/Editor/AndroidBuild.cs` now owns the build. It passes the scene list **explicitly**
rather than reading `EditorBuildSettings`, and re-asserts the package name, product name,
IL2CPP and ARM64 on every build, warning if it had to correct any of them. An asset pack can
no longer quietly change what gets built.

---

## Verification notes for whoever picks this up

- `Time.timeScale` — see the trap above. This has burned two sessions.
- No frames elapse inside a single MCP command. Anything that needs time to pass needs to be
  split across two commands.
- Play-mode `GameObject` renames and component edits revert on exit; `git status` after
  stopping is the check that the scene on disk was not touched.
- `Health.Apply(DamageInfo)`, not `Health.Damage(...)`.
- Missions build hostiles with `go.AddComponent<HostileNpc>()` on an NPC body — there is no
  hostile prefab, and no `HostileNpc` in the scene at rest.

---

## Section 2.6 — the weapon stance over locomotion

Delivered: a dedicated `AimPose` layer between the base layer and the one-shots, carrying one
looping state (`HumanM@Gun_Aim01`) masked to the upper body. Held for `AimStanceSeconds` (3 s)
after firing or reloading; `SetAiming()` holds it open for a future aim-down-sights control.

**Why not a state on the existing `UpperBody` layer.** That is what Phase 9b did, and it is what
produced the zombie-arms report. The one-shot layer's resting state is deliberately empty, so any
scheme that raises that layer for a stance must cross the empty state going in and coming out. A
layer holding nothing but the aim pose has no empty state to cross, so its weight can ramp 0→1→0
with nothing to blend through. That is the whole of "no popping or T-pose frames between states":
there is no frame in which this layer has nothing to say.

**Not held whenever armed.** Walking around permanently aiming is the other half of what the
9b-fix removed. The stance is a combat state with a timeout, not a property of carrying a gun.

### `Mini GTA ▸ Verify ▸ Animation stance sweep`

A play-mode sampler for this bug class, which has now shipped here twice. It samples every rig on
the shared controller each editor tick, counts frames where a masked layer carried weight while
writing the humanoid zero pose, and bounds the per-frame weight change so that "no popping" has a
number rather than an opinion.

**It was wrong twice before it was right, and both mistakes are worth keeping.**

1. It capped at 60 rigs and filled them with pedestrians, so it returned a confident **PASS having
   never sampled the player** — sixty unarmed civilians whose aim layer is legitimately flat at
   zero. It now takes the player first and reports INCONCLUSIVE, not PASS, when it cannot show it
   saw the subject. *A sweep that cannot name what it looked at is not evidence.*
2. It tested "layer carries weight while no clip is playing", which is the signature the 9b-fix
   wrote down, and flagged **55 defect frames on a rig that was fine**. Pinning the one-shot layer
   at full weight on its empty resting state and photographing the result settled it: with
   `writeDefaultValues` **off**, an empty state contributes nothing, and that is exactly how this
   project builds its resting state. The flag is the cause; the empty clip list is only a symptom.
   It now reads `writeDefaultValues` off the controller asset.

### Result — PASS

| Check | Result |
|---|---|
| Frames sampled | 5,326 over 60 rigs |
| Player sampled | yes, all 5,326 frames, peak AimPose weight 1.00 |
| Empty state at weight (the defect) | **0** |
| Empty but harmless (WD off) | 53 frames |
| States that could write the zero pose | **0** in the whole controller |
| Max per-frame ramp | 0.833 against a legitimate ceiling of 0.853 — no snapping |
| Stance up | 3,514 frames, **every one** with the legs moving |
| Fastest locomotion under the stance | `speed01` 1.00 |
| Base clips blended under it | Idle01, Walk01_Forward, Run01_Forward, Jump01-Land, Fall01 |

Confirmed as a rendered frame as well as a parameter read: weapon up across the chest, legs free,
no T-pose.

### BUG-029 — rebuilding the animator nulls every rig in the open scene

`Tools ▸ Mini GTA ▸ 5. Build Player Animator` deletes and recreates the controller asset. The GUID
survives (the `.meta` is preserved) and `City.unity`'s 79 references are untouched on disk — but
every `Animator` in the **loaded** scene is left pointing at a destroyed object.

The symptom is total and alarming: 526 animators in the scene, **0** on `PlayerLocomotion`, the
player with no controller at all. It looks exactly like the animation system having died.

It has not. Reopen the scene and all 79 come back. Any session that rebuilds the controller must
reopen `City.unity` before trusting a play-mode animation reading, or it is measuring wreckage of
its own making.

### Three more play-mode traps found this session

- **`MenuState.ForceClear()` does not restore the world camera's culling mask.** The lobby zeroes
  it (D41) and only the PLAY path puts it back, so every capture taken after a `ForceClear`
  renders sky and an empty ground plane. Cost a detour chasing an animation bug that was a camera
  setting. Set `Camera.main.cullingMask = ~0` before any play-mode capture.
- **The MCP bridge wraps command code in its own namespace**, so `CompilationPipeline` resolves to
  `Unity.CompilationPipeline` rather than `UnityEditor.Compilation.CompilationPipeline`. Fully
  qualify it. The bridge also **blocks `System.Reflection` entirely**, both as a `using` and fully
  qualified — verify against built output, not against assembly introspection.
- **Roughly 10 s of game time passes between two MCP commands.** Anything with a timeout shorter
  than that cannot be observed alive from here; widen it for the test and restore it afterwards.

---

## Section 5 — stealing a car out from under its driver

**Traffic cars had no driver at all.** `TrafficCar` steers the car directly and never occupies it,
so before this every car on the road was an empty shell and 5.2's "the driver exits and flees" had
nobody to eject. The cheap version — conjure a fleeing pedestrian at the moment of the carjack —
was rejected: the player would watch an empty car produce a person on being opened, which is worse
than no driver at all because it draws attention to the trick.

`VehicleDriver` therefore puts a real body in the seat, visible before the player commits to
taking the car. Visual occupant only — no AI, no `Pedestrian`, no collider, because the car is
already driving itself and a second brain in the seat is two things steering one vehicle. It
exists only within 90 m of the player, so the steady cost is the few cars on screen, not all ten.

The ejected driver is a crowd pedestrian, so the person who scrambles out inherits the flee
behaviour, the witness registration and the panic propagation — including, usefully, that a
fleeing pedestrian is itself a witness, so a carjack in a busy street is reported by its own
victim.

### Two defects, both found by looking rather than by counting

- **The driver rode on the roof**, torso through the windscreen. `DriverSeat` was authored for the
  player, whose pivot is the centre of a `CharacterController` capsule, so the marker sits 0.94 m
  up; a pedestrian body's pivot is at its feet. Placement is now *solved* from the hips bone
  against the seat marker — measured error **0.000 m** — rather than typed, which also means it
  survives Section 3B making the character vary.
- **BUG-031: `Destroy` is deferred to the end of the frame.** Stripping the `Pedestrian` off the
  body left it fully awake for every `Update` in between, writing its own locomotion parameters
  over the animator. Disabled first, destroyed second.

### 5.3 ownership — applied default

**A car is the player's permanently for the session**, not released after a distance. The
alternative reads better on paper and worse in play: the distance that releases a car is exactly
the distance at which the player cannot see it being taken, so the car simply stops being where it
was left with no explanation on screen. Permanent is the rule a player can predict.

**BUG-030**: `TrafficSpawner` teleported any car more than 260 m from the player to a fresh lane
node — including one the player had parked. Claimed cars are now not its business.

### Result — PASS

| Item | Result |
|---|---|
| 5.1 enter | `IsOccupied` True, `IsPlayerDriven` True |
| 5.2 driver out | `hasDriver` True→False, `TrafficCar.enabled` True→False |
| 5.2 flees | pedestrians 300→301, ejected driver 1.7 m from the car, `IsFleeing` True |
| 5.2 street reacts | 14 pedestrians panicking from the alarm |
| 5.3 claimed | `ClaimedByPlayer` True |
| 5.3 stays put | parked, walked 420 m away — drift **0.01 m** across many recycler sweeps |
| 5.4 exit | car remains where it was left |
| Crime | heat 0.0 → 22.0, exactly `VehicleTheftHeat`; wanted level 1 |

Confirmed visually: the driver standing at the door of the van she has just been pulled out of.

### ASSET GAP — nothing in this project can sit down

The `Sit` state's clip is `HumanM@MilitaryIdle01`, a **standing** idle. Neither animation pack
ships a seated pose, so `Sit` has been a substitution since Phase 1 and the driver is a standing
figure occupying a car seat. Reads acceptably through the tinted glass most of these cars have,
poorly through the clear-glass ones. **Needs a seated clip or sign-off** — same category as the
missing equip/holster clip behind 2.2 and 2.7.

---

## Section 4 — police and crime

4.3's design was already built in Phase 3 and is good: `HeatSystem` accumulates heat and derives
stars from a threshold table; `CrimeReporter` turns acts into heat **only when somebody saw them**,
which is what makes the wanted level feel fair — the same act is free down an empty alley and
expensive in a crowd. Crimes against police are always known, because the victim radios it in, and
once wanted, everything counts.

### 4.1 — the response scales with the city

The `CruisersPerStar` table was tuned when the crowd was 66. Section 3 took it to 300 and allows
1000, and a fixed six-car maximum in a city of a thousand reads as a police force that has given
up. The multiplier holds the *density* of the response constant.

Capped at 2.5x rather than linear: at 1000 the uncapped multiplier is 3.3x, which makes a
five-star response twenty pursuing cruisers — more expensive than the entire crowd they are
driving through. Zero stars stays zero at any population, and a scene with no crowd system scales
by 1.0.

Pools raised 8/10 → 16/20, because a pool smaller than the peak dispatch turns every stand-down
into a destroy and every escalation into an instantiate.

### Result — PARTIAL (4.1, 4.3, 4.4 pass; 4.2 deliberately not done)

| Check | Result |
|---|---|
| Scale at 1000 population | 2.50, at the cap |
| Cruisers per star | 0/1/2/3/4/6 → **0/2/5/8/10/15** |
| 4.4 crime | `FiredWeapon` +12.0 heat, witnessed |
| 4.4 escalation | 4 assaults → heat 78.0, 2 stars, **5 cruisers dispatched**, nearest 83 m and closing |
| 4.4 de-escalation | heat 78.0 → 62.8 → 29.3 → 0.0; stars 2 → 1 → 0 |
| 4.4 stand-down | 5 cruisers **returned to the pool** (idle=5/created=5), not destroyed |

### 4.2 — flagged, not forced

**Military FREE ships no police vehicle and no police character.** Its vehicles are a Hummer, a
tank and a helicopter. Forcing a tank into a two-star city response would look wrong, and the
brief says to flag that rather than force it.

Proposed instead, for sign-off: the **Hummer as a military escalation unit at five stars**, and
the pack's barriers, sandbags and crates for the checkpoint zone in 3B.6. The pack is currently
referenced **0 times** in `City.unity` — imported and entirely unused, which is exactly what 3B.5
exists to fix.

### Noted, pre-existing, not changed

The dispatcher never reduces an active pursuit when the star count drops, only when it reaches
zero — cars already sent keep coming until the player loses them entirely. Defensible, but it is a
behaviour rather than an oversight and should be a deliberate choice.
