# Mini GTA — Phase 7: Polish, Menus, Audio & Performance

Builds on [PHASE1](PHASE1.md)–[PHASE6](PHASE6.md).

> # ⚠ THE PERFORMANCE NUMBERS IN THIS DOCUMENT ARE VOID
>
> Every measurement in the tier table and the culling section below was taken while the
> settings it describes were **not being applied**. Two independent faults, both found in
> Phase 14:
>
> 1. **BUG-018** — no render pipeline was assigned, so `PerformanceTuner.ApplyPipeline`
>    resolved a null URP asset and returned. Shadow distance, cascades, render scale and MSAA
>    were never set on anything.
> 2. **BUG-019** — the `Detail` layer did not exist (layer 8 was still named `MainBall` from
>    an imported pack), so `ApplyCamera` returned before setting `layerCullDistances`. The
>    "368 fewer renderers, a 31 % reduction" headline below never happened.
>
> Both were silent early-returns on a missing prerequisite. **Do not use any number in this
> file for a decision.** The corrected, re-measured tier table is in
> [PHASE14.md](PHASE14.md#phase-7-re-baseline). The *settings* in the tier table below were
> right all along — only their application was broken — so the table is still useful as a
> statement of intent, just not as evidence.

**The Android APK is built, signed and verified**: `Builds/Android/MiniGTA-0.1.0.apk`, 126.9 MB,
ARM64, APK Signature Scheme v2. It has not yet been run on a physical device.

---

## What Phase 7 delivers

| Brief item | Status | Where |
|---|---|---|
| Minimap | Already shipped in Phase 4 | `Minimap` |
| Full map screen | Done | `FullMapScreen` — tap the minimap |
| Pause menu | Done | `PauseMenu` |
| Settings: volume | Done | master / music / effects buses |
| Settings: sensitivity | Done | 0.3×–2.5× multiplier on the authored value |
| Settings: control layout | Done | left/right-handed mirror, plus invert-Y |
| Settings: quality tiers | Done | Low / Medium / High, `PerformanceTuner` |
| Sound effects | Done | 7 effects, all synthesised at runtime |
| Music + ambient loop | Done | 12.8 s music bed, 8 s ambience |
| Main menu | Done | `MainMenu` — Continue / New Game / Settings / Quit |
| Game-over / mission-complete screens | Already shipped in Phases 3–4 | `PlayerStatusHud`, `MissionHud` banners |
| Object pooling | Done, for police | `PrefabPool`, `PoliceDispatcher` |
| Frustum culling | Unity does this; added distance culling on top | `PerformanceTuner`, `PerformanceSetup` |
| LOD | Done, as distance culling + terrain LOD | see *On "LOD"* below |
| Capped physics step | Done | `Time.maximumDeltaTime`, per-tier physics rate |
| Reduced shadow/draw distance on low tier | Done | 38 m shadows, 420 m draw, 85 m detail |
| Installable Android build | Built, signed, verified | `Builds/Android/MiniGTA-0.1.0.apk` |
| iOS build | Skipped, as agreed. Settings configured | `IosSetup` |

---

## Audio, with no audio files

The project contains zero sound assets, so `ProceduralAudio` synthesises all ten clips from
raw samples at load. This is not a placeholder gesture — it keeps the repo free of binary blobs
nobody can review, makes every sound a handful of readable numbers, and means the audio system
is testable without sourcing a library first. Swap any of them for a real clip later;
`AudioManager` only cares that it gets an `AudioClip`.

Measured at runtime — none silent, none clipping:

| Clip | Length | Peak |
|---|---|---|
| gunshot | 0.280 s | 0.953 |
| punch | 0.200 s | 0.595 |
| horn | 0.550 s | 0.360 |
| crash | 0.450 s | 0.726 |
| click | 0.060 s | 0.337 |
| chime | 0.700 s | 0.299 |
| footstep | 0.120 s | 0.273 |
| engine loop | 0.400 s | 0.189 |
| ambience | 8.000 s | 0.693 |
| music | 12.800 s | 0.235 |

Two details worth keeping:

- **The engine loop is exactly 22 cycles at 55 Hz.** A loop whose length is not a whole number
  of cycles clicks audibly at the seam every time it wraps, and at idle that is three times a
  second.
- **Ambience and music fade in and out at their own ends.** Same problem, solved the other way,
  because neither is periodic.

**One source per sound, not one per object.** One-shots come from a fixed pool of twelve
`AudioSource`s rather than `PlayClipAtPoint`, which allocates a GameObject per sound; when all
twelve are busy the oldest is stolen. The engine loop belongs to the player's own vehicle only —
twenty AI cars each running a looping source would cost twenty voices to produce a wash nobody
can pick apart.

Everything is wired through events that already existed: `PlayerCombat.Attacked`,
`VehicleHorn.Honked`, a new `VehicleImpact.Occurred`, `PlayerProgress.LeveledUp`,
`MissionManager.MissionEnded`. `AudioHooks` is the only file that has heard of both gameplay and
audio, exactly as `AdRewards` is the only file that knows about both missions and ads.

Footsteps are timed off distance travelled, not off the animation, so the step rate tracks real
speed for free without needing animation events baked into retargeted Mixamo clips.

---

## The performance pass

### What was actually costing frames

The scene had **1,322 mesh renderers**, and roughly **763 of them are road markings** — 32 cm
dashes that are illegible past about sixty metres. Over half the renderer budget was going on
detail nobody can see.

`PerformanceSetup` (step 11) moves those onto a `Detail` layer and `PerformanceTuner` gives that
layer a short far plane. Only collider-free objects qualify; culling the renderer of something
you can walk into would leave an invisible wall, so anything with a collider is skipped by rule,
not by hope.

**Measured from one fixed camera position:**

| Tier | Detail cull | Renderers drawn |
|---|---|---|
| High | 320 m | 1,194 |
| Low | 85 m | **826** |

**368 fewer renderers, a 31 % reduction**, with nothing visible changing at speed.

### Tier table

| | Low | Medium | High |
|---|---|---|---|
| Shadow distance | 38 m | 65 m | 120 m |
| Shadow cascades | 1 | 1 | 2 |
| Render scale | 0.75 | 0.90 | 1.00 |
| MSAA | off | off | 2× |
| Draw distance | 420 m | 700 m | 900 m |
| Detail cull | 85 m | 170 m | 320 m |
| LOD bias | 0.55 | 1.00 | 1.50 |
| Terrain pixel error | 24 | 10 | 5 |
| Physics rate | 30 Hz | 50 Hz | 50 Hz |
| Frame cap | 30 | 60 | 60 |
| Skin weights | 2 bones | 4 bones | 4 bones |

`Time.maximumDeltaTime` is capped at 0.1 s on every tier. This is the setting that actually
protects the frame rate: without it a 300 ms stall queues fifteen physics steps, each of which
makes the next frame later still, and the game never recovers.

The first-run tier is guessed from RAM and core count, deliberately conservative. A phone that
could have run High and got Medium loses a little draw distance; a phone that gets High and
cannot run it gets a slideshow and an uninstall.

### On "LOD"

The brief asked for LOD. Mesh-level LOD does not apply here — every building, kerb and road
marking is a single scaled primitive box, and there is no lower-detail version of a cube. What
*is* implemented under that heading is distance culling of decoration, terrain LOD via
`heightmapPixelError` and `basemapDistance`, and `QualitySettings.lodBias` for anything that
does carry an LODGroup later. Calling that "LOD done" without saying so would be misleading.

### Pooling

`PoliceDispatcher` used to instantiate cruisers on escalation and destroy them all on stand-down,
so a player who repeatedly gained and lost heat paid full instantiation every time. Now units go
back to a `PrefabPool`. Wrecked cruisers and dead officers are still destroyed — reusing them
would put a pre-crashed car on the road.

**Measured across three chases:**

| | Cruisers on street | Total ever instantiated |
|---|---|---|
| 4 stars | 4 | 4 |
| stand down | 0 | 4 (all 4 asleep in pool) |
| 5 stars | 6 | **6** — only the 2 extra were new |
| stand down | 0 | 6 |
| 5 stars again | 6 | **6** — nothing new at all |

The third full five-star chase instantiated **zero** objects.

Bullets are not pooled because there are none: the pistol is hitscan (`Physics.Raycast`), so
there was never an object to recycle.

---

## Menus

All four screens live on the existing HUD canvas over the running city. A separate menu scene
would mean loading the entire world twice — once for a backdrop, again when the player pressed
Play — and on a phone that is a long black screen for no gain.

`MenuState` is a **counter, not a flag**, because menus nest: the pause menu opens settings,
which closes the pause menu, which would otherwise unpause the game underneath the panel the
player is still reading. Time restarts only when the last one closes, and it restores the
timescale it found rather than assuming 1 — an ad overlay could own it when the player hits pause.

New Game does not reload the scene. Everything a run accumulates lives in `PlayerProgress`, the
mission manager and the garage, so resetting those three and moving the player home *is* a new
game — and it skips a reload the player would otherwise sit through.

---

## Verified in Play mode

- **Launch → Continue → pause → resume**: `timeScale` 0 → 1 → 0 → 1, controls hidden and
  restored at each step
- **Volume**: master 0.90 → 0.40 moved the music source from 0.450 to 0.200 exactly; music bus
  to 0 silenced it
- **Sensitivity**: ×2.0 → 44.00/24.00, ×0.5 → 11.00/6.00, back to ×1.0 → 22.00/12.00 — exact
  round trip, no drift
- **Handedness** on a 2778 px screen: stick 583 ↔ 2195, cluster 2396 ↔ 382, jump button
  2579 ↔ 199. Every pair sums to 2778, and the round trip is exact
- **All three tiers** applied every value in the table above
- **Culling**: 1,194 renderers at High → 826 at Low, same camera position
- **Pooling**: three five-star chases, 6 instantiations total
- **URP assets restored** on exiting Play mode: Mobile back to 60 m/1/0.85, PC to 50 m/4/1.00
- Zero console errors across the whole session

---

## Bugs worth remembering

- **The pause menu could be closed by Escape but never opened.** A hidden `UiPanel` deactivates
  its GameObject, so its `Update` stops running — and the key check lived there. Moved to
  `MenuInput` on the always-active canvas.
- **`HudContext.Apply()` put the joystick back on top of an open menu.** It owns those widgets
  for its own reasons (on-foot versus driving) and knows nothing about menus, so any context
  change behind a menu undid the hide. `HudLayout` now re-asserts visibility in `LateUpdate` —
  whoever asks last wins, so it asks last.
- **Play mode was silently editing the URP asset.** It is a project asset, not a scene object,
  so a tier applied at runtime persisted into the repo after exiting. `PerformanceTuner` now
  captures the originals and puts them back in `OnDestroy`, editor-only.
- **`Camera.layerCullSpherical` is not supported under URP** and logs a warning on every tier
  change. Removed; the planar cull is what URP honours.
- **A pooled rigidbody keeps its velocity.** A cruiser returned mid-crash and woken later would
  launch itself. `PrefabPool.Take` zeroes velocity, and writes position *after* activation
  because a disabled rigidbody ignores the write.

---

## Known rough edges

- **Nothing here has been profiled on a real device.** The renderer counts and tier numbers are
  measured, but measured in the editor on a desktop GPU. The 31 % culling win is real; whether
  Low tier holds 30 fps on a mid-range phone is still an assumption.
- The music is a four-chord loop, not a soundtrack. It exists so the city is not silent.
- No audio ducking beyond the indoor ambience fade — a gunshot during the music bed just adds.
- The full map does not pan or zoom. The city fits on one screen, so it does not need to yet,
  but a bigger world would.
- Settings has no "reset to defaults", and no per-effect volume beyond the three buses.
- iOS Build Support is not installed on this machine, so the iOS settings are stored but
  untested — they cannot be validated until the project opens on a Mac.
- `MenuState.ForceClear` exists but nothing calls it. It is there for a future state change that
  must not leave time stopped.

---

## The build

```
Tools > Mini GTA > 9. Switch Platform to Android     (long: reimports every texture)
Tools > Mini GTA > 13. Build Android APK             (long: IL2CPP compiles to native)
```

Output: `Builds/Android/MiniGTA-0.1.0.apk`, 126.9 MB. Install with:

```bash
adb install -r Builds/Android/MiniGTA-0.1.0.apk
```

Use step 14 for a Play Store `.aab` instead — that one cannot be sideloaded.

`AndroidBuild` refuses to run if the active platform is wrong or if `City.unity` is not the
first enabled scene, rather than producing a package that boots to an empty scene.

### Verified after the build, not assumed

| Check | Result |
|---|---|
| Archive integrity | 637 entries, central directory reads cleanly |
| Signature | `apksigner verify` → **Verifies**, v2 scheme, 1 signer (Unity debug key) |
| Package | `com.minigta.city`, versionName 0.1.0, versionCode 1 |
| Label | Mini GTA |
| targetSdk | 36 |
| ABI | `arm64-v8a` only |
| Permissions | `INTERNET` only — nothing else requested |
| Native libs | `libil2cpp.so` 56 MB, `libunity.so` 23 MB, `libswappywrapper.so` (frame pacing) |
| Payload | 20 MB scene bundle, 7 MB IL2CPP metadata, split sharedassets |

The absence of `META-INF/CERT.RSA` is correct, not a problem: that is the obsolete v1 JAR
signature scheme. Modern Unity signs with v2, which lives in a block before the zip central
directory and is not a zip entry at all.

### It failed three times first, and none of them were the code

1. **`Failed to write file: sharedassets0.assets`** — the C: drive was full. 36 MB free of 201 GB.
2. **`clang++: error: clang frontend command failed due to signal`** — still out of disk, this
   time crashing the IL2CPP compiler mid-file rather than failing a write cleanly.
3. **`linker command failed with exit code 1`** — compiled everything, then ran out at the link
   step with 359 MB left.

A fourth error, `Error building Player because scripts have compile errors in the editor`, was a
red herring: a URP package texture (`FilmGrain/Medium05.png`) failed to import during the
platform switch and left `EditorUtility.scriptCompilationFailed` stuck true. Reimporting that one
asset and requesting a clean recompile cleared it. The scripts were fine throughout.

Two changes made the build fit:

- **ARM64 only, dropping ARMv7.** IL2CPP was compiling every script to native code twice. Google
  Play requires 64-bit and treats 32-bit as optional; ARMv7 exists for phones old enough that
  this game would not run on them anyway. This halved the native compile and the package size.
- **`Library/Bee` junctioned to another drive.** The IL2CPP object cache is the single largest
  build artifact at ~2.5 GB. A directory junction puts it somewhere with room, and Unity follows
  it transparently:

  ```bash
  mklink /J "Library\Bee" "D:\unity_build_cache\gta_Bee"
  ```

  Delete the junction to undo it; Unity rebuilds the cache in place as before. Nothing in the
  project or in anyone's other projects had to be deleted.

`runInBackground` was also switched off. Headless editor testing had left it on, and on a phone
that means the game keeps simulating and playing audio after the player switches away — which is
precisely the behaviour `OnApplicationPause` exists to avoid.

**To restore ARMv7** (only worth it for pre-2015 devices), set `targetArchitectures` back to
`ARMv7 | ARM64` in `MobileSetup.ConfigureAndroid` and rebuild. With the Bee junction in place
there is now disk headroom for it.

### What still needs checking on the device

None of this can be answered from the editor: touch controls under real fingers, the mock ad
overlay at phone resolution, whether the save file survives an app kill, and the actual frame
rate at each quality tier.

---

## Editor testing note

Unchanged from Phase 6: the Unity player loop freezes when the Game view is not being drawn, so
anything time-based stalls during headless testing. `Application.runInBackground = true` works
around it. That is an editor artifact — leave it false for the mobile build.
