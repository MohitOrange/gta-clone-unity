# Mini GTA — Phase 1

Mobile-first open-world foundation in **Unity 6 (6000.5.8f1) + URP**.

Open `Assets/Scenes/City.unity` and press **Play**.

---

## What Phase 1 delivers

| Brief item | Status | Where |
|---|---|---|
| Heightmap terrain, city in a coastal valley, mountains at the edges | Done | `Assets/Editor/TerrainBuilder.cs` |
| Ocean with animated water shader | Done | `Assets/Game/Shaders/OceanWater.shader` |
| Low-poly city: roads, sidewalks, buildings, park | Done | `Assets/Editor/CityBuilder.cs` |
| Third-person camera, right-thumb swipe + mouse look | Done | `ThirdPersonCamera.cs` |
| Joystick movement, sprint, jump, swim, collision | Done | `PlayerController.cs` |
| Animation state machine (idle→walk→run→jump→land→swim) | Done | `AnimatorBuilder.cs`, `PlayerAnimation.cs` |
| Day/night cycle | Done | `DayNightCycle.cs` |
| Touch HUD + keyboard/mouse fallback | Done | `InputHub.cs` + `Assets/Game/Scripts/Input/` |
| Mixamo drop-in character pipeline | Done | `MixamoImportSetup.cs` |

**Not done:** iOS (build support isn't installed — Windows machine), and the platform is
still on Windows Standalone. See *Getting it on your phone* below.

---

## Controls

| | Touch | Desktop |
|---|---|---|
| Move | Left-thumb joystick (floating — appears where you touch) | `WASD` / arrows |
| Look | Drag anywhere on the right ~55% of screen | Hold **right mouse** + move |
| Sprint | `RUN` button (hold) | `Shift` |
| Jump | `JUMP` button | `Space` |
| Interact | `ENTER` button (only shows when something is in range) | `E` |
| Attack | `FIRE` button (only shows when armed) | `F` |

Both schemes are live simultaneously, so the mobile layout can be exercised on desktop.
Buttons auto-hide by context — swimming hides `JUMP`, driving will swap in pedals and horn.

---

## Regenerating the world

Everything is procedural. Nothing in the scene is hand-placed.

- **`Tools > Mini GTA > BUILD EVERYTHING`** — full rebuild from raw FBX to playable scene.
- **`Tools > Mini GTA > Rebuild World Only`** — terrain + city + scene; skips asset reimport.

Individual steps 1–9 are on the same menu if you need one in isolation.

To reshape the map, edit the constants at the top of `TerrainBuilder.cs`
(`MapSize`, `SeaLevel`, `PlateauHeight`, `MountainHeight`, `CityCenter`, `CityExtents`)
and `CityBuilder.cs` (`RoadWidth`, `BlockSize`, `Blocks`), then rebuild.

---

## Swapping the character

Retargeting goes through Unity's Humanoid avatar, so **any** Mixamo model plays **any**
Mixamo clip with no code change.

1. Drop the `.fbx` into `Assets/characters/` (model) or `Assets/animations/` (clip).
2. Run `Tools > Mini GTA > 1. Configure Mixamo Assets`, then `1b. Extract Character Textures`.
3. To change the player model, edit `PlayerModel` in `SceneAssembler.cs` and re-assemble.

Three things the import step fixes that Unity gets wrong by default, and that will silently
break the game if skipped:

- Unity imports Mixamo FBX as **Generic** rig with **no avatar** — clips then refuse to
  retarget across models.
- Mixamo textures are **embedded inside the FBX**. Until they are extracted, every character
  renders pure white.
- Root motion is baked out of every clip (`lockRootPositionXZ/Y`, `lockRootRotation`) because
  the `CharacterController` owns movement; leaving it in makes the character drift.

Loop flags are set per clip from the `LoopingClips` table in `MixamoImportSetup.cs`.

---

## Architecture notes

**Input** — `InputHub` is the single source of truth. HUD widgets push into it; keyboard and
mouse are polled into it. Gameplay reads only the merged result, so it never branches on
device. Widgets use uGUI pointer events, which means a mouse drag on desktop and a finger on
device follow the *identical* code path. Input zeroes out when the app loses focus.

This project is set to **Input System package only** (`activeInputHandler: 1`) — legacy
`Input.GetAxis` throws here. Use `UnityEngine.InputSystem` APIs.

**Camera** — `ThirdPersonCamera` runs at execution order 100 (LateUpdate) so it always sees the
player's final position. Spherecast collision pulls in instantly when blocked and eases back
out. `ConsumeLookDelta()` is single-consumer by design; calling it twice a frame loses input.

**Water** — the wave function is duplicated deliberately: HLSL in `OceanWater.shader` for
display, C# in `WaterVolume.SurfaceHeightAt` for gameplay. **Change one, change the other**, or
swimming will disagree with the visible surface.

**Swimming hysteresis** — the exit probe sits *lower* on the body than the entry probe, so
leaving the water is harder than entering it. Inverting that makes the state oscillate every
frame in wave chop.

---

## Mobile budget

Applied by `MobileSetup.ApplyMobileRenderBudgets()` to the `Mobile_RPAsset` tier:
0.85 render scale, 1 shadow cascade, 60 m shadow distance, no MSAA, no HDR, additional lights
off. Target 30 fps.

City geometry is primitive boxes on ~10 shared materials, all marked batching-static, so the
district collapses to a handful of draw calls. Flat decals (roads, lane markings, parapets)
carry no colliders. Terrain uses instanced draw with a 12 px LOD error and shadow casting off.

Scene currently holds ~910 renderers and ~110 colliders.

> **Not yet measured on a real device.** The budgets above are sensible defaults, not
> validated numbers — profile on your actual target phone before trusting them.

---

## Getting it on your phone

Android build support, SDK, NDK and JDK are all installed and configured
(IL2CPP, ARM64 + ARMv7, Vulkan → GLES3, landscape only, `com.minigta.city`, min API 26).

The project is **still on Windows Standalone**. To build:

1. `Tools > Mini GTA > 9. Switch Platform to Android`
   (or *File > Build Profiles > Android > Switch Platform*).
   First switch reimports every texture — expect several minutes.
2. Connect the phone with USB debugging on.
3. *File > Build And Run*.

---

## Known rough edges

- **`Gunplay.fbx` is only 0.20 s long** — almost certainly a truncated Mixamo download.
  Harmless now (no combat until a later phase); re-download before wiring weapons.
- **`Ely By K.Atienza.fbx` sits in `Assets/animations/`** but is a character model, not a clip.
  It imports fine as a humanoid; move it to `Assets/characters/` when convenient.
- The camera can push inside the player when jammed into a corner. Standard fix is fading the
  player's renderers below ~1.2 m camera distance — not yet implemented.
- Ocean tessellation is ~10 m per cell, so wave crests alias at the far horizon.
