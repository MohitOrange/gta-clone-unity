# Mini GTA — Phase 4: Missions & Progression

Builds on [PHASE1.md](PHASE1.md), [PHASE2.md](PHASE2.md) and [PHASE3.md](PHASE3.md).
Open `Assets/Scenes/City.unity` and press **Play**.

Four contacts stand around the city under spinning gold beacons. Walk up to one and press
**ENTER**. Only jobs your level allows will light up.

---

## What Phase 4 delivers

| Brief item | Status | Where |
|---|---|---|
| Mission-giver NPCs with map markers | Done | `MissionGiver` + beacons, minimap blips |
| Delivery / race (drive A→B on a clock) | Done | `DeliveryMission` |
| Elimination (defeat X enemies) | Done | `EliminationMission` |
| Escort / protect | Done | `EscortMission` + `EscortClient` |
| Heist-style fetch (steal and return) | Done | `HeistMission` |
| Mission log UI + objective markers | Done | `MissionHud`, `ObjectiveMarker` |
| Minimap | Done | `Minimap` |
| XP/level system with unlocks | Done | `PlayerProgress` |
| Save/load money, level, unlocks, mission state | Done | `SaveSystem` |

---

## The four jobs

| Contact | Job | Level | Reward | Type |
|---|---|---|---|---|
| Courier | **Courier Run** | 1 | $300 / 180 XP | 4 checkpoints, 165 s, must stay in a vehicle |
| Enforcer | **Clear the Lot** | 2 | $450 / 240 XP | Kill 4 hostiles in the park |
| Fixer | **Safe Passage** | 2 | $520 / 300 XP | Escort a client, 2 scripted ambushes |
| Boss | **Grand Theft** | 3 | $900 / 420 XP | Steal a guarded car, alarm raises heat, drive it to the drop |

The first job alone pays exactly enough XP to reach level 2, which lights up the next two
contacts. That is deliberate — one run demonstrates the whole progression loop.

**Unlocks by level:** 2 → motorbike, 3 → speedboat, 4 → extended ammo, 5 → body armour,
6 → north district. Levels come from `XpCurveBase*(n-1) + XpCurveQuadratic*(n-1)²`.

---

## HUD

- **Minimap** top-left: road grid drawn from the same constants as the streets, with blips for
  you (arrow, rotates with heading), available jobs (gold), the objective (green) and police (blue).
- **Objective panel** on the left: job title, current objective, countdown, distance.
- **Money / level / XP bar** top-right under the wanted stars.
- **Objective marker**: a green beam in the world, plus a screen arrow that sticks to the edge
  when the objective is off-camera. The arrow matters more than it looks — a phone's field of
  view is narrow and the objective is off-screen most of the time.

---

## Save/load

Written to `Application.persistentDataPath` — IndexedDB on WebGL, app-private storage on
Android, AppData on Windows. That is the same role localStorage plays for a web build without
tying the game to one platform's API.

Saved: money, XP, level, unlocks, completed missions, active mission id, player position.
Autosaves on mission completion, every 60 s, on pause and on quit.

**Not saved:** traffic, pedestrians, wanted level, mid-mission progress. That is disposable
world state which should be rebuilt fresh, so the file stays small and can never encode a
broken world. A mission that was active when you saved is offered again from the start rather
than resumed into a half-finished state that was never serialised.

Writes go to a temp file and are then swapped in, so an interrupted save cannot leave
unparseable JSON.

---

## Three bugs worth remembering

1. **Autosave can fire before load, wiping the save.** `OnApplicationPause` fires when the app
   loses focus — which in the editor happens constantly. If it fired before `Start()` had read
   the file, `Save()` wrote freshly-constructed default progress over real progress. Money and
   level looked fine because they were re-saved from live values; the completed-mission list
   silently emptied. `SaveSystem` now refuses every write until a load has been attempted.
   **This is the single most destructive class of bug a save system can have** and it is worth
   re-checking any time an autosave trigger is added.
2. **Restoring an exact position can drop the player somewhere lethal.** The saved spot was a
   live traffic intersection; loading there got the player run over and respawned at the
   hospital before they could move. Loading now snaps to ground and grants 4 s of
   invulnerability.
3. **A downward ground-snap raycast hits the player's own capsule.** It "snapped" the player to
   the top of their own head, 1.8 m above where they should be. The cast now skips anything
   belonging to the player, and treats vehicles as not-ground.

---

## Verified in Play mode

- Gating: at level 1 only the courier job is offerable; the rest report "Requires level 2/3"
- Full loop: accepted → 4 checkpoints → complete → **$250→$550, XP 0→180, level 1→2,
  unlocked the motorbike**, two new contacts opened up, autosaved
- Failure: abandoning the escort client failed with "You abandoned the client", paid **nothing**,
  did not mark the job complete, and left **zero** spawned hostiles or clients behind
- Save file round-trips money, XP, level, unlocks and completed missions across a play-mode cycle

---

## Known rough edges

- **Renderer count ~1348.** Four phases of accumulated budget, still never profiled on a device.
- Mid-mission progress is not saved. Quit during a job and you restart that job, not lose it.
- The minimap is a fixed whole-city view rather than a scrolling one. Fine for this map size;
  it will not scale if the world grows.
- Mission enemies reuse the police officer prefab with the police component stripped at spawn.
  It works and shares one rig, but they look like officers.
- No mission log *screen* listing all jobs — the brief's "mission log" is served by the active
  objective panel plus map blips. A full journal would be the next addition.
- Escort clients follow the player rather than pathing independently, so they will walk into
  traffic if you lead them there.
- Still on Windows Standalone; the Android switch remains pending from Phase 1.
