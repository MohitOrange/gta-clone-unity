# Mini GTA — Phase 8: The Main Menu Was Unusable On A Real Phone

Builds on [PHASE1](PHASE1.md)–[PHASE7](PHASE7.md).

Phase 7 shipped a signed APK that had never been run on a phone. It was installed on one for the
first time and the main menu was unusable: elements grossly the wrong size, cut off at the edges,
button text shredded into vertical stripes.

**Everything in this document was measured on the device.** Where a number appears it came from
`adb`, from `logcat`, or from measuring the pixels of a screenshot pulled off the phone. The
editor is not cited as evidence for anything, because the editor is what produced this bug.

Final build: `Builds/Android/MiniGTA-0.2.0.apk`, 125.8 MB.

---

## The device

| | |
|---|---|
| Model | vivo I2019, Android 14 (API 34) |
| Display | 1080 × 2400, 440 dpi (396 override) |
| Cutout | 82 px, centred punch-hole `Rect(509,0 – 571,82)` |
| Render area | **2318 × 1080** landscape (2400 − 82 for the cutout letterbox) |
| GPU / API | Adreno, Vulkan |
| adb | `C:\platformtools\platform-tools\adb.exe` — **not on PATH** |

---

## Root cause 1 — Vulkan pre-transform (the oversized, cut-off, striped menu)

`ProjectSettings.asset` had `vulkanEnablePreTransform: 1`.

With it on, Unity does not render into a landscape surface. It renders into the display's
**native portrait swapchain** and rotates during rendering. SurfaceFlinger confirmed this
exactly:

```
SurfaceView[com.minigta.city/...](BLAST)
    activeBuffer   = [1080x2318]      <- portrait
    geomBufferSize = [0 0 2318 1080]  <- landscape layer
    geomBufferTransform = 7           <- rotated 270 degrees into place
```

The 3D scene handles this — its shaders compile with `UNITY_PRETRANSFORM_TO_DISPLAY_ORIENTATION`.
On this Adreno driver the **ScreenSpaceOverlay canvas does not**, so the entire UI was drawn with
the swapchain's aspect instead of the screen's.

That predicts an exact, non-uniform distortion, and the prediction matched the phone:

| | predicted | measured on device |
|---|---|---|
| horizontal scale | 1080 / 2318 = **0.4659** | **0.4570** |
| vertical scale | 2318 / 1080 = **2.1463** | **2.1266** |

The measurement is the `NEW GAME` button. Unity's own numbers said it was **448.6 × 79.0 px**
(420 × 74 authored × 1.0682 canvas scale — i.e. **the layout was always correct**). It *rendered*
at **205 × 168 px**.

Everything the report described falls out of those two numbers:

- **"Oversized"** — vertically stretched 2.13×.
- **"Cut off at the edges"** — the canvas is 2318 units wide, squashed to 2318 × 0.466 = 1080 px,
  so the whole menu occupied the left 1080 px of a 2318 px surface. Measured navy panel extent:
  x = 82 … 1185.
- **"Positioned for a different resolution"** — 1080 tall stretched to 2318 px, of which only
  1080 is on screen, so **less than half the menu was visible** and `CONTINUE` was off the top.
- **"Vertical striping / tearing on the text"** — glyph quads pushed through a wildly
  non-uniform transform. It was not a font-atlas problem. It vanished the moment the transform
  was fixed, with the font untouched.

**Fix:** `PlayerSettings.vulkanEnablePreTransform = false`, pinned in
[MobileSetup.cs](Assets/Editor/MobileSetup.cs) so re-running step 7 cannot silently undo it.

**Why no editor could ever have caught this.** The editor renders D3D11 on a desktop GPU and
never takes the Vulkan pre-rotation path at all. There is no Game-view resolution, no aspect
setting and no device simulator that reproduces it.

### What was *not* the cause

Worth recording, because all three were plausible and all three were checked and cleared:

- **The CanvasScaler.** There is exactly **one** Canvas and **one** CanvasScaler in the entire
  project; the six "screens" are children of it. It was already
  `ScaleWithScreenSize, 1920×1080, MatchWidthOrHeight, match 0.65` — already what the task asked
  for. On device it resolved to `scaleFactor 1.0682`, canvas `2170 × 1011` units. Correct.
- **Orientation.** An early `dumpsys` read the window as portrait `1080 × 2318`, which would
  have explained every symptom. It was an artefact of measuring while the activity sat behind
  the lockscreen. With the app actually in the foreground: `Requested w=2318 h=1080`, `land`,
  `ROTATION_90`, `Screen.orientation = LandscapeLeft`. **Orientation was never broken**, and
  "fixing" it on the first reading would have been the same mistake that produced this phase.
- **The dynamic font atlas.** Real (92 legacy `UI.Text`, 22 distinct sizes, one runtime atlas
  that grows 256×256 → 256×512), but not the cause of the striping. Still a latent risk — see
  *Known rough edges*.

---

## Root cause 2 — four HUD buttons had no click handler in the build

Separate bug, same "worked in the editor" signature. Found while running the test matrix: the
pause button, the minimap, the store button and the clear-wanted button did nothing on the phone.

Instrumenting the raycaster proved the taps were landing correctly:

```
MGTA_DIAG TAP raw=(838.00, 996.00)  hits=1   [0] HUD/PauseButton
MGTA_DIAG TAP raw=(143.00, 937.00)  hits=1   [0] HUD/Minimap/MapButton
MGTA_DIAG TAP raw=(2216.00, 830.00) hits=2   [0] HUD/StoreButton [1] HUD/LookZone
```

The button was hit. Nothing happened. The reason:

```csharp
// MenuBuilder.cs, an EDITOR script that builds the scene
button.onClick.AddListener(menu.TogglePause);
```

`AddListener` registers a **non-persistent** listener. It lives in memory and is **never
serialised into the scene**. Verified directly — every one of these reported
`onClick.GetPersistentEventCount() == 0`. So the listener survived only until the next domain
reload, and did not exist at all in a player build. In the editor you rebuild the scene, press
Play, and the button works, every time.

Two of the three were lambdas, which could never have serialised even in principle.

**Fix:** wire them at runtime, exactly as every button that *did* work already does
(`MainMenu.Awake`, `PauseMenu.Awake`, `SettingsPanel.Awake`). Added an `OpenButton` field to
`PauseMenu`, `FullMapScreen` and `StorePanel`; the builders now assign the reference (which
*does* serialise) and each panel wires its own listener in `Awake`.

| Button | before | after |
|---|---|---|
| PauseButton | editor `AddListener` → dead | `PauseMenu.OpenButton` → `TogglePause` |
| Minimap MapButton | editor lambda → dead | `FullMapScreen.OpenButton` → `OpenFrom(null)` |
| StoreButton | editor lambda → dead | `StorePanel.OpenButton` → `Toggle` |

`ClearWantedButton` already self-wires in its own component and was never affected.

---

## Safe area

There was **no safe-area handling anywhere in the project** — `Screen.safeArea` appeared in zero
of the 110 scripts.

Added [SafeAreaFitter.cs](Assets/Game/Scripts/UI/SafeAreaFitter.cs) on a single `SafeArea`
container inserted between the canvas and all 18 UI roots, so every screen inherits it and the
next screen cannot forget it.

**Honest note on its effect here:** this device reports

```
[SafeArea] screen=2318x1080 safeArea=(0,0,2318x1080) -> anchors (0,0)..(1,1)
```

Because `renderOutsideSafeArea = false`, Android letterboxes the app away from the cutout before
Unity sees it, so the safe area *is* the whole render area and the fitter is a **no-op on this
configuration**. It becomes load-bearing if that setting is turned on, on a device that reports
insets anyway, or when a gesture bar overlaps. It is correct and verified not to regress
anything; it is not verified to *do* anything, because on this phone there is nothing to do.

### A bug I introduced and caught while inserting it

Reparenting all 18 roots reversed their sibling order — which is UI draw and hit-test order, so
`LookZone` (a full-height invisible pad) ended up on top of every button. Cause: reading
`GetSiblingIndex()` *inside* the reparent loop, by which point earlier children had already left
the canvas and every remaining one reported index 0. Fixed by appending in collected order, and
the order was restored explicitly and re-verified.

---

## Hardcoded pixel sizes — measured, then deliberately left alone

Every element is centre-anchored with a fixed `sizeDelta` (Title 1200 wide, Settings card
1180 × 820, and so on). The task asked for these to be made relative. They were swept against
the real scaler across the whole landscape aspect range instead:

| screen | canvas (ref units) | elements too wide | too tall |
|---|---|---|---|
| 1440 × 1080 (4:3) | 1593 × 1194 | 0 | 0 |
| 1920 × 1080 (16:9) | 1920 × 1080 | 0 | 0 |
| 2318 × 1080 (device) | 2170 × 1011 | 0 | 0 |
| 2560 × 1080 (21:9) | 2315 × 977 | 0 | 0 |

Nothing overflows anywhere in the supported range. The app is landscape-locked, so 4:3 is the
narrowest case that can occur. Rewriting 92 elements to relative anchoring would have been a
large blind refactor with real regression risk and **no observable benefit on any reachable
configuration**, so it was not done. Recorded as residual risk, not as done.

---

## Verified on the device

Every row below was performed on the vivo I2019 running the installed APK, and confirmed from a
screenshot pulled off the phone or from `logcat`. None of it is editor testing.

| # | Requirement | Result | Evidence |
|---|---|---|---|
| 1 | Main Menu renders, no glitching, buttons tappable | **PASS** | Title/subtitle/4 buttons correct size and position, text crisp. CONTINUE, SETTINGS, QUIT all respond |
| 2 | Settings renders; sliders + toggles work | **PASS** | MUSIC 50→**93%** by drag; INVERT Y OFF→**ON**; LOW/MEDIUM/HIGH switch and the tier line updates |
| 3 | In-game HUD sized, placed, touch-responsive | **PASS** | Joystick knob tracks the finger and the player moves; look-drag rotates the camera; FIRE 48→45 for 3 taps and 48→38 for 10 — **exactly one shot per tap, no double-triggers** |
| 4 | Vehicle HUD (steering, gas/brake/handbrake/horn) | **NOT TESTED** | Could not reach a vehicle — see below |
| 5 | Pause menu and Full Map | **PASS** (after fixing root cause 2) | PAUSED with all 5 items; MAP opens with grid, player arrow, job marker, legend, coordinates, CLOSE |
| 6 | Store / shop panels | **PARTIAL** | IAP StorePanel **PASS** (title, REMOVE ADS $2.99, RESTORE, CLOSE). The four in-world shops **NOT TESTED** — see below |
| 7 | Force-kill, relaunch, Continue, save loads, UI stable | **PASS** | `[Save] Loaded ... (level 1, $250, 0 missions done)`; HUD correct after relaunch; settings persisted across the kill |
| 8 | Ad overlay renders at device resolution | **PARTIAL** | Ad **banner** renders correctly ("Advertisement - your banner here"). Full-screen rewarded overlay **NOT TRIGGERED** — see below |
| 9 | No UI regression at either quality tier | **PASS** | Low (`shadow=38m draw=420m scale=0.75 physics=30Hz fps 30`) and High (`120m/900m/scale=1/50Hz/fps 60`) both render the menus correctly |

**Not done, and why — no workaround was found in this session:**

- **Vehicle HUD (4).** Needs the player next to a car. Traffic spawns 12 AI cars, but none came
  within interaction range across roughly ten minutes of walking the streets and holding position
  at an intersection; the INTERACT button never appeared (confirmed by pixel-diffing the action
  cluster across four spaced captures — identical every time). The vehicle HUD is built by the
  same `HudContext` on the same canvas as the on-foot HUD that passed, so it is *likely* fine —
  but likely is not tested, and it is recorded as untested.
- **In-world shops (6).** Ammu-Mart / Chop Shop / Threads / Safehouse require walking into
  specific interiors, which was not reached.
- **Full-screen ad overlay (8).** Gated behind a wanted level. Firing 10 rounds with no witnesses
  raised no heat, so the "lose the heat" offer never appeared.

---

## Build history for this phase

| version | contents |
|---|---|
| 0.1.0 | the broken Phase 7 build |
| 0.1.1 | + geometry/font diagnostics — proved the layout was already correct |
| 0.1.2 | + `vulkanEnablePreTransform = false` — **menu fixed, striping gone** |
| 0.1.3 | + tap-raycast instrumentation — proved the dead buttons were being hit |
| 0.1.4 | + runtime wiring for the four dead HUD buttons |
| **0.2.0** | + SafeArea container, sibling order restored. **Final** |

Disk: Phase 7's `Library\Bee` → `D:` junction had been lost, leaving 6.1 GB free on C:. Restored
before building; C: went to 15.7 GB and all five builds succeeded with no disk failures.

---

## Known rough edges

- **The dynamic font atlas is still a real risk.** 92 legacy `UI.Text` on one runtime-rebuilt
  atlas across 22 sizes and 2 styles. One rebuild (256×256 → 256×512) was observed on device at
  startup. It did not cause the Phase 8 striping, but a runtime atlas repack is a genuine hazard
  and the fix is TextMeshPro with a pre-baked SDF atlas. TMP Essentials are not imported and 92
  components would have to be converted — not attempted here.
- **`DeviceDiagnostics` is still in the build.** It logs geometry, tap raycasts and font rebuilds
  every run. Delete `Assets/Game/Scripts/Core/DeviceDiagnostics.cs` and its component on the HUD
  before shipping.
- **The Android Back button still does not open the pause menu.** `MenuInput` reads
  `Keyboard.current.escapeKey`, and the diagnostic confirmed `Keyboard.current` **is present** on
  this device — so the earlier theory that it is null was wrong. The most likely remaining cause
  is Android 13+ predictive back (`enableOnBackInvokedCallback` is in the manifest) consuming the
  gesture before it becomes a key event. Not confirmed, not fixed. The on-screen pause button
  works, so pause is reachable.
- **Benign logcat noise**, present before and after the fixes: `ClassNotFoundException:
  AssetPackManager` (Unity probing for Play Asset Delivery, unused) and Adreno
  `GraphicBufferAllocator` failures for pixel formats 56/59 (driver capability probing at 4×4).
- The player can spawn wedged against building geometry; the first movement test went nowhere
  until the camera was rotated. Gameplay issue, not UI, not investigated.

---

## Reinstall

```bash
adb install -r Builds/Android/MiniGTA-0.2.0.apk
```

`adb` is not on PATH on this machine — use `C:\platformtools\platform-tools\adb.exe`.
