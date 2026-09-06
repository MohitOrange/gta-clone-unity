# Mini GTA — Full Project Asset & Usage Analysis

**Read-only analysis pass. No project file was modified.**
Generated 2026-09-05 against commit `586b8eb`, Unity 6000.5.8f1, URP, Android-first.

Companion file: [`asset_inventory.csv`](asset_inventory.csv) — 5,653 rows, one per non-`.meta` file.

---

## 0. Headline numbers

| Measure | Value |
|---|---|
| Non-`.meta` files under `Assets/` | **5,653** |
| Total size | **2.34 GB** |
| Files the shipped game actually pulls in | **526** (9.3%) |
| `City.unity` build dependency closure | **643 assets** |
| Scenes in the project / in the build | **73 / 1** |
| C# scripts (game + editor + packs) | **191** (117 game, 37 editor, 37 pack) |
| Animator controllers | **29** — of which **1** drives the game |
| Animation clips | **275** — of which **16** are wired into the game |
| Materials | **578** — 228 on non-URP shaders, but only **1** such slot on a scene renderer |
| Audio files | **0** — by design, all sound is synthesised at runtime |

**The single most important number: `Assets/characters/` is 1.80 GB — 77% of the entire
project — and 183 of its 224 files never ship.**

### A correction to my previous session report

In my last summary I said `Assets/characters/` held *"13 Mixamo humanoid FBX nobody has used."*
**That was wrong.** Nine of the thirteen are live dependencies of `City.unity` and appear in the
scene as pedestrians, shopkeepers and mission NPCs. The correct statement is below in §3.2.

---

## 1. Folder & Project Structure

### 1.1 Top-level map

| Folder | Purpose | Verdict |
|---|---|---|
| `Assets/Game/` | **All first-party runtime code and data.** `Scripts/` (117 files, 21 subsystems), `Prefabs/` (10), `Materials/`, `Animator/`, `World/`, `UI/`, `Data/` | Clean, conventional |
| `Assets/Editor/` | **The build pipeline.** 34 builders/tools that *generate* the scene | Clean |
| `Assets/Scenes/` | `City.unity` — the entire game | ⚠️ also contains a vendor pack (below) |
| `Assets/Settings/` | URP render pipeline assets | Clean |
| `Assets/Resources/`, `TutorialInfo/` | Unity template leftovers | Removable |
| 14 vendor pack roots | Asset-store content | Mixed (see §3) |
| `Assets/characters/`, `Assets/animations/` | Mixamo imports | ⚠️ lowercase, non-standard |
| `Assets/_Recovery/` | A 12 MB orphaned `0.unity` | ⚠️ dead |

### 1.2 Structure conventions

The project has a **clear and well-observed convention for first-party content**: everything the
team wrote lives under `Assets/Game/` (runtime) or `Assets/Editor/` (build-time), with runtime
scripts grouped by subsystem. That is better discipline than most projects this size.

The convention breaks down entirely at the boundary with vendor packs, which were imported
wherever their `.unitypackage` happened to land.

### 1.3 Flagged structural problems

| # | Problem | Detail |
|---|---|---|
| S-1 | **A vendor pack is nested inside the scenes folder** | `Assets/Scenes/CatBorg Studio/3D Pirates Lowpoly Pack/` — the pirate pack used by Section 6 lives *inside* `Assets/Scenes/`. `Assets/Scenes/` holds 134 files where it should hold 1. |
| S-2 | **`Assets/_Recovery/0.unity`** | A 12 MB scene, nothing references it, dated 2026-09-02. A Unity crash-recovery artefact committed by accident. |
| S-3 | **Inconsistent casing** | `Assets/characters/`, `Assets/animations/`, `Assets/ithappy/` are lowercase; everything else is PascalCase or vendor-branded. |
| S-4 | **72 unused scenes** | 73 `.unity` files, one in the build. The other 72 are pack demo scenes. Each drags its own materials and prefabs into "referenced" status and obscures real usage analysis. |
| S-5 | **Duplicated UI kit** | `SoftTouch_UI/` (14.3 MB) was fully replaced by `Space_Exploration_GUI_Kit` in Phase 13. Zero files ship. |
| S-6 | **Two unused weapon packs coexist** | `Low Poly Weapons VOL.1` (16 prefabs) is entirely unused; `ithappy/Weapons_FREE` is the one actually wired in. |
| S-7 | **`.fbm` texture sidecar folders** | Mixamo FBX files each carry a `.fbm` folder of extracted textures — 1.2 GB across 13 characters. |

---

## 2. Asset Inventory by Category

Full per-file detail is in `asset_inventory.csv`. Summary:

| Category | Ships | Referenced elsewhere | Pack-demo only | Unused |
|---|---|---|---|---|
| Character (model) | 25 | 0 | 155 | 3 |
| Creature (model) | 7 | 0 | 0 | 0 |
| AnimationClip (`.anim`) | 1 | 0 | 0 | 23 |
| AnimatorController | 8 | 0 | 21 | 0 |
| AvatarMask | 3 | 0 | 4 | 4 |
| Weapon (model / prefab) | 11 / 11 | 0 | 20 / 22 | 2 / 5 |
| Vehicle (model / prefab) | 24 / 25 | 0 / 12 | 152 / 147 | 0 / 27 |
| Environment (model) | 72 | 1 | 433 | 16 |
| Material | 76 | 3 | 224 | 65 |
| Texture | 159 | 23 | 675 | 2,036 |
| Prefab (other) | 93 | 162 | 235 | 249 |
| ScriptableObject | 3 | 4 | 18 | 4 |
| Shader | 1 | 0 | 1 | 1 |
| Font | 1 | 1 | 1 | 3 |
| **Audio** | **0** | **0** | **0** | **0** |

### 2.1 Scripts — 117 game scripts across 21 subsystems

| System | Count | Files |
|---|---|---|
| UI | 26 | `HudContext`, `LobbyScreen`, `SafeAreaFitter`, `SafeAreaBleed`, `Minimap`, `ShopUI`, `PauseMenu`, `SettingsPanel`, `ProfilePanel`, … |
| Traffic / crowd | 10 | `CrowdDirector`, `CrowdGrid`, `Pedestrian`, `TrafficCar`, `TrafficSpawner`, `RoadNetwork`, `TrafficLightController`, `PoliceVehicle`, `RedLightMonitor`, `TrafficLightLamps` |
| Vehicles | 9 | `Vehicle`, `CarController`, `BikeController`, `BoatController`, `HelicopterController`, `VehicleDriver`, `VehicleDamage`, `VehicleUpgrades`, `Garage` |
| Missions | 8 | `MissionBase`, `MissionManager`, `DeliveryMission`, `EliminationMission`, `EscortMission`, `HeistMission`, `MissionGiver`, `EscortClient` |
| Combat | 6 | `Health`, `PlayerCombat`, `HostileNpc`, `DamageReaction`, `RagdollLite`, `UniformTint` |
| Audio | 6 | `AudioManager`, `ProceduralAudio`, `VehicleAudio`, `FootstepAudio`, `AudioHooks`, `UiClickAudio` |
| Ads / IAP | 6 | `AdService`, `MockAdService`, `GoogleMobileAdsService`, `IAdService`, `IapService`, `AdRewards` |
| World | 5 | `DayNightCycle`, `WeatherSystem`, `WaterVolume`, `NightLights`, `AmbientAnimal` |
| Weapons | 5 | `WeaponController`, `WeaponLibrary`, `WeaponSocket`, `WeaponPickup`, `WeaponVfx` |
| Interiors | 5 | `Interior`, `InteriorManager`, `InteriorNpc`, `InteriorExit`, `Doorway` |
| Economy | 5 | `Shop`, `ShopItem`, `Property`, `SkinLibrary`, `PlayerSkinSwapper` |
| Core | 5 | `HeatSystem`, `CrimeReporter`, `GameStateManager`, `GameSettings`, `DeviceDiagnostics` |
| Input | 4 | `InputHub`, `VirtualJoystick`, `HudButton`, `TouchLookZone` |
| Police | 3 | `PoliceDispatcher`, `PoliceOfficer`, `PolicePursuit` |
| Player | 3 | `PlayerController`, `PlayerAnimation`, `PlayerVehicleController` |
| Performance | 3 | `PerformanceTuner`, `CrowdBenchmark`, `PrefabPool` |
| Cheats | 3 | `CheatCodes`, `CheatRegistry`, `CheatConsole` |
| Progression | 2 | `SaveSystem`, `PlayerProgress` |
| NPC | 2 | `NpcManager`, `TownNpc` |
| Camera | 1 | `ThirdPersonCamera` |

**Script usage was resolved by code reference, not by GUID.** A GUID-only rule reports every
static class, base class and `AddComponent` target as dead — it initially flagged
`SceneAssembler.cs`, `CrimeReporter.cs` and `Vehicle.cs` as unused, which is nonsense. Corrected
results: 135 `USED (code)`, 24 `USED (menu + code)`, 6 menu-only entry points, 21 orphan
MonoBehaviours (18 of them inside vendor packs), 2 orphan static classes.

### 2.2 ScriptableObjects / config

| Asset | Purpose | Status |
|---|---|---|
| `Assets/Game/Data/WeaponLibrary.asset` | 7 weapon definitions | ⚠️ see §5.3 — all 7 carry identical placeholder stats |
| `Assets/Game/World/IslandTerrain.asset` | Generated terrain | SHIPPED |
| `Assets/Game/World/OceanMesh.asset` | Generated ocean | SHIPPED |

### 2.3 Audio — zero files, and that is deliberate

There is **not one `.wav`, `.mp3` or `.ogg` in the project**, and no `AudioSource` in the scene.
`ProceduralAudio.cs` synthesises every sound as raw samples at runtime, with a stated rationale:
the repo stays free of opaque binary blobs and every sound is a few readable numbers.

This is a **documented design decision, not a gap** — but it is also the reason the game currently
has no music, no ambience, and engine/weapon sounds built from square waves and xorshift noise.
Listed under §5 as a polish item, not a defect.

---

## 3. Usage Cross-Reference

Method: `AssetDatabase.GetDependencies("Assets/Scenes/City.unity", recursive)` gives the
authoritative shipped set (643 assets). Everything else was resolved by GUID scan across 1,577
reference containers, plus string-path scanning of the 34 editor builders that *generate* the
scene, plus per-clip controller cross-reference.

Status meanings in the CSV:

- **SHIPPED** — in `City.unity`'s dependency closure. It is in the game.
- **REFERENCED OUTSIDE BUILD** — something outside its own pack points at it, but the scene does not.
- **EDITOR-WIRED (dir)** — a builder script names its exact folder and picks entries by name.
- **PACK-DEMO ONLY** — referenced *only* by files inside its own pack. Effectively unused.
- **UNUSED** — nothing anywhere references it.

### 3.1 Whole packs with zero shipped files

| Pack | Size | Files | Note |
|---|---|---|---|
| `ithappy/Military_Free` | 30.5 MB | 151 | **Imported, never placed.** See §5.2 |
| `Loading Games/Toon City Pack` | 26.6 MB | 475 | ⚠️ **The Section 10 "base environment" pack is not in the scene at all** |
| `SoftTouch_UI` | 14.3 MB | 74 | Superseded by the Space kit in Phase 13 |
| `_Recovery` | 11.2 MB | 1 | Crash artefact |
| `HelicopterAttack` | 7.2 MB | 82 | Only the pack's demo scenes reference it; no helicopter in the game |
| `Low Poly Weapons VOL.1` | 4.2 MB | 36 | Duplicate of the weapon pack actually used |
| `Low Poly Helicopters Pack Free` | 0.6 MB | 21 | Never placed |
| `EEJANAI_Team/Commons` | 1.5 MB | 6 | Demo materials on `Toon` shaders (BUG-020) |

### 3.2 Character variety — the real picture

**This is the correction to my earlier claim.** Nine of the thirteen Mixamo characters ship.

**Distinct human models placed in `City.unity`: 9**

| Model | Instances | Used as |
|---|---|---|
| `Shady_3d/Apocalyptic character` | 176 | **Player**, lobby stage body, and 168 pedestrians |
| `characters/Ch02_nonPBR` | 48 | Pedestrians |
| `characters/Ch22_nonPBR` | 18 | Shop interiors + mission NPCs |
| `characters/Ch31_nonPBR` | 14 | Mission NPCs + interiors |
| `characters/Ch07_nonPBR` | 14 | Shop interiors |
| `characters/Ch16_nonPBR` | 14 | Shop interiors |
| `characters/Lola B Styperek` | 11 | Pedestrians |
| `characters/Ch08_nonPBR` | 7 | Mission NPCs |
| `characters/Kachujin G Rosales` | 5 | Pedestrians |
| `characters/Arissa` | 4 | Mission NPC |

Plus 7 animal models (Chicken ×45, Dog ×39, Kitty ×36, Pinguin ×24, Deer ×23, Horse ×16, Tiger ×6).

**So why does the crowd look identical in every screenshot?** Two separate reasons, and only the
second is a bug:

1. **Skew.** Of 232 skinned bodies under `Traffic`, 168 (72%) are the same Apocalyptic character —
   and so is the player. The player is visually indistinguishable from the most common pedestrian.
2. **`CrowdDirector` clones exactly one template at runtime.** `ResolveTemplate()` returns *the
   first living pedestrian in the registry* and every one of the 234+ pedestrians it spawns to
   reach the 300 target is a clone of that single body. The ~66 authored pedestrians have
   variety; the runtime-grown crowd has none. Worse, *which* model gets cloned is whatever
   happens to be first in the registry — so it is not even deterministic between runs.

**Mixamo characters that do NOT ship: 4** — `Ch15_nonPBR`, `Ch21_nonPBR`, `Ch29_nonPBR`, `Remy`.
Together with their `.fbm` texture folders these are **742.9 MB across 96 files**, the single
largest cleanup opportunity in the project.

**Excluded on purpose:** the 5 `PolygonalAssets/Low polyCharactsre lite` models contain no
skeleton at all (DECISIONS D7) — they are static props shaped like people, used as shop
mannequins. 11 of 25 files ship in that role.

### 3.3 Animation clips — 16 of 275 are wired into the game

| | Count |
|---|---|
| Clips in the project | 275 |
| Referenced by *any* controller (incl. 28 pack demo controllers) | 77 |
| **Referenced by the game's controller (`PlayerLocomotion`)** | **16** |
| Referenced by nothing at all | 198 |

**The complete animation set of the shipped game:**

`HumanM@Idle01` · `HumanM@Walk01_Forward` · `HumanM@Run01_Forward` · `HumanM@Sprint01_Forward` ·
`HumanM@Jump01 - Begin` · `HumanM@Jump01 - Land` · `HumanM@Fall01` · `Swim` ·
`HumanM@MilitaryIdle01` (as "Sit") · `HumanM@Death01` · `HumanM@Damage01` ·
`HumanM@Gun_Aim01` · `HumanM@Gun_Aim01_Shoot01` · `HumanM@Gun_Reload01` ·
`Human@ObjectGripHands01` · `back fist`

**Unused clips by pack:** Kevin Iglesias 141, ithappy Animals 25, EEJANAI Fighter 18.

The Kevin Iglesias pack is the significant one, because it contains clips the game visibly needs:

| Unused clip group | What its absence costs |
|---|---|
| `Walk01_/Run01_/Sprint01_` **Backward, Left, Right, ForwardLeft, ForwardRight, BackwardLeft, BackwardRight** (36 clips) | **The character has no strafing or backpedalling animation.** Move sideways or backwards and it plays the forward walk cycle while sliding. |
| `HumanM@Turn01_Left` / `Turn01_Right` | No turn-in-place; the character pivots rigidly. |
| `HumanM@Gun_Aim02`, `Gun_Aim02_Shoot01`, `Gun_Reload02` | A whole second weapon stance sitting unused — free variety for a second weapon type. |
| `HumanM@Talk01` | `TownNpc` dialogue plays with no talking animation. |
| `HumanM@Idle01-Idle02`, `Idle02`, `MilitaryIdle01-Idle01` | No idle variation — 300 pedestrians breathe in lockstep. |
| **All 26 `HumanF@` female clips** | The rig set is male-only; female characters (Lola, Arissa, Ch02) run male animation. |

The `[RM]` root-motion twins (18 clips) are correctly unused — PHASE9B used them to *measure*
authored clip speed for the blend thresholds, not to play.

### 3.4 Weapons

`ithappy/Weapons_FREE` is **fully wired**: all 12 prefabs ship (pistol, rifle, shotgun, sniper,
knife, axe, bat, hockey stick, plus attachments).

`Low Poly Weapons VOL.1` — **16 prefabs, 0 used** (AK74, M1911, M4, Uzi, M249, M107, RPG7,
Bennelli M4, RGD-5 grenade, scopes).

**Animation coverage is the gap, not models:**

| Weapon | Model | Aim | Fire | Reload | Equip / Holster |
|---|---|---|---|---|---|
| pistol | ✅ | ✅ `Gun_Aim01` | ✅ | ✅ `Gun_Reload01` | ❌ **no clip exists** |
| rifle / shotgun / sniper | ✅ | ⚠️ reuses the pistol clip | ⚠️ pistol clip | ⚠️ pistol clip | ❌ |
| knife / bat / axe | ✅ | ❌ | ⚠️ `back fist` (melee) | n/a — but the data says otherwise (§5.3) | ❌ |

The pack ships `WeaponHold_AssaultRifle01`, `WeaponHold_Rifle01` and `WeaponHold_Bazooka01`
masked poses — **exactly the right asset for per-weapon stances — and none is wired.**

### 3.5 Vehicles

All 8 vehicle prefabs carry `Vehicle` + a controller + `VehicleDamage`, and **all 8 have both a
`DriverSeat` and an `ExitPoint`** — seat logic is complete, not decorative.

| Prefab | Kind | Occupancy |
|---|---|---|
| `Car_0` … `Car_5`, `Car_Police` | Car | full, `HideOccupant = true` |
| `Bike` | Bike | full, occupant visible |
| `Boat` | Boat | full, occupant visible |

Runtime: 12 traffic cars (10 civilian + 2 police), each civilian one carrying `VehicleDriver`
(added this session). **`HelicopterController` exists but appears 0 times in the scene** — there
is no helicopter in the world to enter.

**Unused vehicle art:** 24 `Vehicle_*_separate.prefab` from Cartoon City (Taxi, Ambulance, Bus,
SUV, Pick-up, Container truck, and a **Police Car**), plus the Military `Hummer_003` and
`Tank_006`, plus the whole HelicopterAttack demo fleet.

---

## 4. Animator Controller Deep Dive

29 controllers exist. **28 are vendor demo controllers.** One drives the entire game.

### 4.1 `Assets/Game/Animator/PlayerLocomotion.controller`

Drives the **player, the police, and all 300–1000 pedestrians** — 79 rigs in the saved scene.
12 parameters, 4 layers, 14 states.

**Layer 0 — `Base Layer`** (Override, no mask, default `Locomotion`)

| State | Motion | WD | Transitions |
|---|---|---|---|
| `Locomotion` | 1D blend tree: Idle 0 / Walk 0.455 / Run 0.750 / Sprint 1.000 | **True** | → `Jump` [Jump], → `Fall` [!Grounded] |
| `Jump` | `HumanM@Jump01 - Begin` | False | → `Fall` [VerticalSpeed < 0], → `Fall` [exit 0.90] |
| `Fall` | `HumanM@Fall01` | False | → `Land` [Grounded] |
| `Land` | `HumanM@Jump01 - Land` | False | → `Locomotion` [exit 0.55], → `Locomotion` [Speed > 0.35] |
| `Swim` | `Swim` | False | → `Locomotion` [!InWater] |
| `Sit` | ⚠️ **`HumanM@MilitaryIdle01`** | False | → `Locomotion` [!InVehicle] |
| `Death` | `HumanM@Death01` | False | → `Locomotion` [!Dead] |

AnyState → `Swim` [InWater && !InVehicle] · → `Sit` [InVehicle] · → `Death` [Dead], all
`canTransitionToSelf = false`.

**Layer 1 — `AimPose`** (Override, `UpperBody` mask, weight 0, code-driven) — one state
`AimIdle` = `HumanM@Gun_Aim01`, looping, no transitions. Added this session for Section 2.6.

**Layer 2 — `UpperBody`** (Override, `UpperBody` mask, weight 0, code-driven)

`None` (**no motion**, `wd=False`, tag `Rest`) · `PunchUpper` (`back fist`, speed 1.5) ·
`ShootUpper` (`Gun_Aim01_Shoot01`, speed 1.6) · `HitUpper` (`Damage01`) · `ReloadUpper`
(`Gun_Reload01`). Four AnyState triggers in, all exit back to `None` on exit time.

**Layer 3 — `Hands`** (Override, `Hands` mask, weight 0, code-driven) — one state `Grip`.

### 4.2 Flagged states and transitions

| # | Severity | Finding |
|---|---|---|
| A-1 | **Placeholder clip** | **`Sit` plays `HumanM@MilitaryIdle01` — a standing military idle.** Neither animation pack ships a seated pose, so *nothing in this project can sit down*. Affects the player driving, and the carjack driver added this session, who is a standing figure occupying a car seat. |
| A-2 | **Dead parameter** | **`Armed` (Bool) is declared and set by `WeaponSocket`, but no transition or state in the controller reads it.** The grip is driven entirely from code via layer weight. Harmless today; a trap for anyone who assumes setting `Armed` changes the pose. |
| A-3 | **Latent zombie-arms** | `UpperBody/None` has **no motion**. It is safe *only* because `writeDefaultValues = False`. Flip that flag — or add a state with WD on — and the humanoid zero pose is written over torso, head and both arms again. This exact defect has shipped **twice** (Phase 9b-fix, BUG-023). Verified this session: pinning the layer to weight 1.0 on that state renders a normal standing character, and the sweep counts 0 defect frames. |
| A-4 | **Mixed write-defaults** | `Locomotion` has `wd=True`; every other state on the same layer has `wd=False`. Unity's guidance is to be consistent per controller. Mixed WD is a classic source of properties that fail to reset when a state is left. Not currently causing a visible bug. |
| A-5 | Cosmetic | `Base Layer` has `defaultWeight = 0`. Unity forces layer 0 to weight 1, so this is inert — but it reads as a mistake. |
| A-6 | **No blend-tree coverage for lateral motion** | `Locomotion` is a 1-D blend on `Speed` only. There is no `DirectionX`/`DirectionY` parameter, so strafe/backpedal cannot be represented even though 36 directional clips are sitting in the project (§3.3). |

### 4.3 The other 28 controllers

- **7 animal controllers** (`Chicken`, `Deer`, `Dog`, `Horse`, `Kitty`, `Pinguin`, `Tiger`) —
  1 layer, 1 state, 2 params each. **In use** by `WildlifeBuilder`.
- **19 EEJANAI single-clip demo controllers** — 1 state each. Pack demos, unused.
- `Human Basic Motions` demo — 2 layers, unused.
- **`HumanM@SoldierAnimations.controller`** — 18 layers, 174 states, **126 of them with no
  motion**, 49 parameters. A vendor demo controller. Not used, and should not be: 126 empty
  states across 18 Override layers is the A-3 hazard multiplied by a hundred.

---

## 5. Gaps & Recommendations

### 5.1 Missing asset categories (nothing in the project fills these)

| Priority | Gap | Impact |
|---|---|---|
| **Blocks gameplay polish** | **Seated pose clip** | Every driver and passenger stands inside the vehicle. Affects player driving + the new carjack driver. |
| **Blocks gameplay polish** | **Equip / draw / holster / switch clips** | Items 2.2 and 2.7 are served by timed `EquipSeconds` states with no animation. |
| Visible | **Directional locomotion wiring** | 36 clips exist (§3.3); the blend tree has no lateral axis to play them on. Strafing slides. |
| Visible | **Female animation set** | 26 `HumanF@` clips exist unused; female characters run male animation. |
| Visible | **Idle variation** | 300 pedestrians share one 2.70 s idle loop, in phase. |
| Visible | **Talk animation** | `HumanM@Talk01` exists and is unused; NPC dialogue is silent and still. |
| Visible | **Police vehicle matching the theme** | See §5.2. |
| Nice-to-have | **Real audio** | All sound is synthesised square waves and noise. Deliberate, but no music or ambience exists. |
| Nice-to-have | **App icon** | 0 of 6 Android icon slots assigned. `Assets/Game/UI/Branding/app_icon.png` does not exist. |
| Nice-to-have | **Fatality font** | Absent; Righteous stands in. |
| Nice-to-have | **VFX textures** | Muzzle flash / tracer / impact are built procedurally with no texture. |

### 5.2 "Referenced 0 times" — imported but not wired

| Asset / pack | Size | The obvious use |
|---|---|---|
| **`ithappy/Military_Free`** — 151 files | 30.5 MB | Section 3B.6 checkpoint zone: barricades, sandbags, crates. `Hummer_003` as a five-star military response unit. **No police car and no police character in this pack** — a tank at 2 stars would look wrong, which is why 4.2 was flagged rather than forced. |
| **`Loading Games/Toon City Pack`** — 475 files | 26.6 MB | **Section 10's stated "base environment across the entire map" — and not one file is in the scene.** The city is currently built from SimplePoly + Cartoon City + Palmov instead. This is the single largest unwired *intended* asset. |
| `Cartoon_City_Free` vehicles — 24 prefabs | — | Taxi, Ambulance, Bus, SUV, Pick-up, Container truck — **and a Police Car** that would fit the city aesthetic far better than the Military pack. |
| `Low Poly Weapons VOL.1` — 16 prefabs | 4.2 MB | AK74, M1911, Uzi, M249, RPG7 — a full modern arsenal, unused, while `WeaponLibrary` defines 7 weapons off the other pack. |
| `Low Poly Helicopters Pack Free` — 21 files | 0.6 MB | Section 7.4's NPC/police air units. |
| `HelicopterAttack` — 82 files | 7.2 MB | Section 7's combat helicopter. `HelicopterController` exists but nothing is placed. |
| `WeaponHold_*` masked poses — 3 clips | — | Per-weapon carry stances; the correct asset for §3.4's coverage gap. |
| `Gun_Aim02` / `Gun_Reload02` — 3 clips | — | A second weapon stance, free. |

### 5.3 Data-quality gaps

| # | Finding |
|---|---|
| D-1 | **`WeaponController` is attached to nothing.** The Section 2 state machine (Idle/Equipping/Firing/Reloading/Switching/Locked, two slots, pickup rules) exists, compiles, and is on **zero GameObjects**. The player still runs the Phase-7 `PlayerCombat`. **Five cheat codes call `OnPlayer<WeaponController>()` and will silently do nothing.** |
| D-2 | **`WeaponLibrary`'s 7 definitions carry identical placeholder stats** — every one is magazine 12, reserve 96, damage 34, reload 1.6 s, equip 0.45 s, `ClipFamily = "Gun"`. **The knife, the bat and the axe each have a 12-round magazine and a reload time.** The per-weapon data structure is right; the data was never authored. |
| D-3 | **`CheatConsole` is attached to nothing.** 67 cheat codes are registered and verified, and **the UI that enters them is not in the scene.** |
| D-4 | **`HelicopterController` is attached to nothing.** No helicopter exists in the world. |
| D-5 | **228 of 578 materials are on non-URP shaders** — but only **1** such slot is on a scene renderer, and that is the legitimate custom `MiniGTA/OceanWater`. BUG-027 is genuinely closed *for what is placed*; the 228 remain a landmine for anything placed from those packs in future (exactly how BUG-027 was created). |

### 5.4 Cleanup opportunities, by size

| Action | Recovers | Risk |
|---|---|---|
| Delete `Ch15`, `Ch21`, `Ch29`, `Remy` + their `.fbm` folders | **742.9 MB** | None — zero references |
| Delete `_Recovery/0.unity` | 11.2 MB | None |
| Delete `SoftTouch_UI` | 14.3 MB | None — superseded in Phase 13 |
| Delete `Low Poly Weapons VOL.1` | 4.2 MB | None — unless §5.2 wires it instead |
| Delete 72 unused pack demo scenes | ~15 MB | Low — also un-clutters usage analysis |
| Re-import shipped Mixamo textures at mobile resolution | up to ~500 MB | Medium — `characters/Textures` is 590 MB of source art for a phone game |

---

## 6. Known-Issues Cross-Check

Reconciled against `PHASE14.md`'s bug table, `PROJECT_STATE.md`, and code comments.

**Only one `TODO` exists in 191 scripts** — `CrowdDirector.cs:62`, "wire the NPC_MAX cheat code
to this". Genuinely low technical debt in comment form.

| Tracked item | State on disk | Verdict |
|---|---|---|
| BUG-009 attack input destroyed during cooldown | OPEN | Not re-examined this pass |
| BUG-013 `Health.Heal` accepts negatives | OPEN | Not re-examined this pass |
| BUG-016 24 `Bld_Pack` animators with no controller | **Confirmed: exactly 24** animators with a null controller | Still open, still benign |
| BUG-020 unused `Toon` shaders | **Confirmed**: `EEJANAI_Team/Commons` materials on `Toon` | Still open, still benign; would vanish with the §5.4 deletion |
| BUG-026 keystore | **Confirmed still broken**: `AndroidKeystoreName = D:/JoySmashProjects/keystore/bundle.keystore`, `useCustomKeystore = True`, file does not exist | **Release blocker, unchanged** |
| BUG-028 app icon | **Confirmed**: 0 of 6 Android slots assigned; `app_icon.png` absent | Open, needs art |
| BUG-025 build scene list | **Fixed and verified**: `EditorBuildSettings` = `City.unity` only; id `com.minigta.city`; IL2CPP; ARM64; v0.3.0 code 7 | Closed |
| BUG-027 non-URP materials | **Fixed for placed content** — 1 non-URP slot on a scene renderer, and it is intentional | Closed, with the §5.3 D-5 caveat |
| `DeviceDiagnostics` "delete before shipping" | **Not in the scene** (0 instances); the .cs file remains | Effectively done; delete the file |
| "The crowd is one character model" | **Refined** — see §3.2. The scene has 9 models; the *runtime* crowd clones 1 | Confirmed, root cause identified |
| "Nothing in Phase 14 has been on the device" | No APK newer than `MiniGTA-0.3.0-dev.apk`; `adb devices` empty | Unchanged |

---

## 7. Prioritized Action List

### ✅ Ready to use — no new asset needed
1. **24 Cartoon City vehicle prefabs**, including a **Police Car** that matches the city art style far better than the Military pack.
2. **36 directional locomotion clips** — strafe and backpedal, once the blend tree gains a lateral axis.
3. **`Gun_Aim02` / `Gun_Aim02_Shoot01` / `Gun_Reload02`** — a complete second weapon stance.
4. **3 `WeaponHold_*` masked poses** — per-weapon carry stances for rifle/assault rifle/bazooka.
5. **`HumanM@Talk01`**, **`Idle02`** and the two idle-transition clips — NPC talk + idle variation.
6. **`ithappy/Military_Free` props** — barricades, sandbags, crates for the 3B.6 checkpoint zone.
7. **`Toon City Pack`** — 475 files, the pack Section 10 names as the base environment.

### 🔌 Needs wiring — the asset and the code both exist, nothing connects them
1. **`WeaponController` onto the player** (D-1). The whole Section 2 state machine is inert, and 5 cheat codes silently fail because of it. *Highest-value fix in this report.*
2. **`CheatConsole` into the scene** (D-3). 67 verified cheats with no way to enter them.
3. **`HelicopterController` onto a placed helicopter** (D-4). Section 7 cannot be flown because there is nothing to fly.
4. **Author `WeaponLibrary`'s 7 definitions** (D-2). Give the knife, bat and axe melee stats instead of a 12-round magazine.
5. **`CrowdDirector` model pool** (§3.2). Replace the single `ResolveTemplate()` body with a weighted pool over the 9 models already in the scene — this is Section 3B, and the raw material is already shipping.
6. **De-skew the player from the crowd.** The player and 168 pedestrians are the same character.

### 🎨 Needs a new asset (nothing in the project can fill it)
1. **Seated pose clip** — nothing in this project can sit down (A-1).
2. **Equip / draw / holster / switch clips** — 2.2 and 2.7 have no animation to play.
3. **`app_icon.png`** — 1024×1024, artwork inside the centre 66%, not from an asset pack.
4. **`Fatality.ttf`** — drop into `Assets/Game/UI/Fonts`.
5. **Female animation set**, or accept male clips on female characters.
6. Optional polish: real audio, muzzle-flash/tracer/impact textures.

### 🤔 Needs your decision
1. **BUG-026 keystore** — points at another project's nonexistent file. **Hard release blocker.** Check backups before assuming it is lost; if the app was ever published, the identity cannot be updated without it.
2. **Section 4.2** — Military FREE has no police vehicle or character. Use the **Cartoon City Police Car** instead (recommended), or the Hummer as a five-star military unit, or reskin.
3. **Section 10** — Toon City Pack is entirely unplaced. Is it still the intended base environment, or has SimplePoly + Cartoon City superseded it? 26.6 MB and 475 files hang on the answer.
4. **Delete 742.9 MB of unused Mixamo characters?** `Ch15`, `Ch21`, `Ch29`, `Remy` — zero references. Keep only if 3B intends to wire them.
5. **`characters/Textures` is 590 MB of source art for a phone game.** Re-import at mobile resolution?
6. **Audio** — keep synthesised sound as the shipping answer, or source a library?
7. **Mixed write-defaults in `PlayerLocomotion`** (A-4) — make consistent, or leave since nothing is currently broken?

---

*Analysis performed read-only. `PROJECT_ANALYSIS.md` and `asset_inventory.csv` are the only files
written. Usage resolved via `AssetDatabase.GetDependencies`, a 6,214-entry GUID map across 1,577
reference containers, string-path scanning of 34 editor builders, per-clip controller
cross-reference, and live scene inspection.*
