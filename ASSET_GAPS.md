# Asset gaps — exact scope

Two animation clips do not exist in either imported pack, and neither has been faked. This
records **precisely what is affected**, so the cost of sourcing a pack can be judged before
buying one, and so the wiring work is a known quantity afterwards.

Audited 2026-09-05 against the live scene and `WeaponLibrary.asset`. Nothing here is a
workaround or an approximation — that was deliberate.

---

## Gap 1 — no seated pose

**What exists:** the animator's `Sit` state plays `HumanM@MilitaryIdle01`, a *standing* military
idle. Neither the Kevin Iglesias pack nor the EEJANAI pack ships a seated clip. `Sit` has been a
substitution since Phase 1.

**Trigger:** the `InVehicle` bool, `AnyState → Sit`, on the base layer.

### Where a standing figure is currently visible

| # | Context | Count | Visible? | Notes |
|---|---|---|---|---|
| 1 | **Player driving a Bike** | 1 prefab | **YES** | `HideOccupant = false`. Player stands upright on a moving motorbike. The worst of the four. |
| 2 | **Player driving a Boat** | 1 prefab | **YES** | `HideOccupant = false`. Standing at the helm reads better than the bike, but is still not sitting. |
| 3 | **Carjack driver** (`VehicleDriver`) | 10 at runtime | **YES** | One per civilian traffic car within 90 m. A standing body occupying a car seat, anchored by the hips. Reads acceptably through the tinted glass most cars have, poorly through clear glass. |
| 4 | Player driving a Car | 7 prefabs | **no** | `HideOccupant = true` — the player mesh is hidden entirely while driving a car. **No gap here.** |

**Total visible instances:** 2 player vehicle types + up to 10 concurrent NPC drivers.

### Not currently a gap, but the obvious next use

- **216 seat-like props** in the scene (benches, mostly street furniture from the environment
  packs). **No NPC ever sits on any of them** — `Pedestrian`'s states are Walking,
  WaitingToCross, Crossing, Fleeing and Dead. There is no sitting behaviour to animate.
- If a seated clip is sourced, "pedestrians occasionally sit on benches" becomes cheap to add
  and would do a lot for how lived-in the streets read. That is behaviour work on top of the
  clip, not just a wiring job.

### What a fix needs

1. One seated idle clip, humanoid, retargetable to the shared rig (the pack's clips already
   retarget directly — see PHASE9B).
2. Ideally a second: seated-in-a-car differs from seated-on-a-bench in leg position.
3. Wiring is then a one-line change — point the `Sit` state's motion at the new clip in
   `AnimatorBuilder.cs`. The state, the parameter and the transitions all already exist.

---

## Gap 2 — no equip / draw / holster / switch clip

**What exists:** nothing. `WeaponController` runs the state machine on **timers only**. The
weapon mesh appears in or disappears from the hand instantly; the timer exists purely to lock
input for a plausible duration.

**What does have an animation, for contrast:** `Firing` (`TriggerShoot`) and `Reloading`
(`TriggerReload`) both play real clips. The gap is specific to getting the weapon in and out of
the hand.

### Every state and path with no clip

| State / path | Entry points | Duration | What the player sees |
|---|---|---|---|
| `Equipping` | 3 — after a switch completes (`FinishState`), pickup into a free slot, pickup that displaced a held weapon | `EquipSeconds` per weapon | Weapon appears in hand instantly, then input is locked for the duration |
| `Switching` | 1 — `TrySwitch` / `TryCycle` | `EquipSeconds` of the outgoing weapon | Old weapon vanishes, new one appears, no draw motion |
| Holster | `ApplyToSocket` → `Socket.Holster()` when no weapon is active | none | Weapon simply disappears from the hand |
| Stance change | `ApplyToSocket` → `SetStance` | instant, undamped | Carry pose snaps between stances (deliberate — damping it would slide the arms while the mesh has already changed) |

### The dead time this currently covers

`EquipSeconds` per weapon, from `WeaponLibrary.asset`:

| Weapon | EquipSeconds |
|---|---|
| pistol | 0.40 s |
| rifle | 0.65 s |
| shotgun | 0.70 s |
| sniper | 0.90 s |
| knife | 0.30 s |
| bat | 0.45 s |
| axe | 0.50 s |

Those are the windows a draw animation would fill. They were authored to feel proportionate to
weapon size, so a sourced clip set can be time-scaled to them rather than the reverse.

### What a fix needs

1. A draw/equip clip and a holster clip, upper-body-maskable (they need to play over
   locomotion, like the existing one-shots on the `UpperBody` layer).
2. Ideally per weapon class — a pistol draw and a rifle draw differ visibly. The `Stance`
   parameter (0 pistol, 1 alt pistol, 2 rifle, 3 assault rifle, 4 bazooka) already exists to
   select between them, so a stance-indexed blend would drop straight in.
3. Wiring: add `Equip`/`Holster` trigger parameters, two states on the `UpperBody` layer
   following the existing `AddOneShot` pattern, and two calls in `WeaponController`. The state
   machine, the timers and the input locking are already correct and would not change.

---

## What is deliberately NOT done

No approximation was blended from existing clips for either gap. A standing idle stretched into
a "sit", or a reload clip reused as a "draw", would look wrong in a way that is harder to notice
in review than an obvious absence — and it would make the gap invisible to the next audit. Both
are recorded here instead.
