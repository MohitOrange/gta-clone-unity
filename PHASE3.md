# Mini GTA — Phase 3: NPCs, Combat & the Wanted System

Builds on [PHASE1.md](PHASE1.md) and [PHASE2.md](PHASE2.md).
Open `Assets/Scenes/City.unity` and press **Play**.

You start armed with a pistol and 48 rounds. Shoot someone in front of witnesses and the
stars start climbing.

---

## What Phase 3 delivers

| Brief item | Status | Where |
|---|---|---|
| Pedestrians with wander / avoid AI | Done | `Pedestrian` (`Wander` route + separation steering) |
| Melee attack | Done | `PlayerCombat.Melee` |
| Pistol with hit detection | Done | `PlayerCombat.FirePistol` |
| NPC reactions: flee, fall, ragdoll-lite | Done | `Pedestrian.Panic`, `DamageReaction`, `RagdollLite` |
| Wanted level 1–5 from crimes | Done | `HeatSystem`, `CrimeReporter` |
| Police spawn + chase by wanted level | Done | `PoliceDispatcher`, `PolicePursuit`, `PoliceOfficer` |
| Arrest / game-over | Done | `PoliceOfficer` arrest → `GameStateManager.Bust()` |
| Health + armour with HUD | Done | `Health`, `PlayerStatusHud` |

---

## Combat controls

| | Touch | Desktop |
|---|---|---|
| Attack | `FIRE` button | `F` |

One button does both: it fires the pistol when armed with ammo, and throws a punch when
unarmed **or out of ammo**. Running dry silently falls back to fists rather than making the
button stop responding.

Aim follows the camera, so the right-thumb look pad doubles as the gun sight — there is no
separate aim mode competing with the movement stick.

Attacks play on a **masked upper-body animation layer**, so you can punch or shoot while still
running. Freezing locomotion to play an attack would read as the controls having died.

---

## The wanted system

Crimes only cost you something **if somebody saw them**. That is the whole design:

| Crime | Heat | Witness rule |
|---|---|---|
| Ran a red light | 14 | police within 55 m |
| Fired a weapon | 12 | civilian within 26 m, or police |
| Assault | 18 | as above |
| Manslaughter | 55 | as above |
| Hit a pedestrian with a car | 45 | as above |
| Grand theft auto | 22 | as above |
| Assaulting an officer | 40 | always known |
| Killing an officer | 90 | always known |

Stars at 20 / 55 / 110 / 190 / 300 heat. Crimes against police are always known — the victim
radios it in. Once you are already wanted, everything counts: the police are actively looking.

Heat decays at 3.5/second after 8 seconds of good behaviour, which is how you escape.

**Police response** is a table, not a formula, because the escalation curve wants tuning by
hand: `0, 1, 2, 3, 4, 6` cruisers for 0–5 stars. Officers only start **shooting** at 3 stars
(`PoliceOfficer.MinStarsToShoot`) — below that they chase and try to cuff you. That is what
makes the star count mean something beyond "more cars".

---

## Failing

| | Trigger | Wake up at | Cost |
|---|---|---|---|
| **BUSTED** | An officer holds contact for 1.5 s while you are stopped | Police station | Weapon confiscated |
| **WASTED** | Health reaches zero | Hospital | Nothing yet |

Both clear your heat and stand down every unit. Neither is a reload — a mobile open world
should never make failure feel like losing progress.

---

## Design notes worth keeping

**One `Health` component for everything.** Player, civilians and police all take damage through
the same path, so a fist, a bullet and a car bumper converge on one place — which is also the
single point `CrimeReporter` hooks into. No weapon needs to know the wanted system exists.

**Ragdoll-lite is one capsule, not a skeleton.** A real ragdoll needs a dozen bodies and joints
per character; with a dozen NPCs on a phone that is not affordable. Freezing the skinned pose
and toppling a single rigid capsule reads convincingly at gameplay distance. The animator is
un-culled *before* being disabled — a culled animator would never have written the pose, and
the corpse would freeze in bind pose.

**Officers close on a subdued suspect.** Originally they held at their 6 m firing range, which
meant at high stars they could only ever shoot you and an arrest was unreachable. A suspect who
has stopped running now gets walked up to and cuffed whatever the star count; only someone
actively fleeing or fighting gets shot at.

**Uniform tint via property block.** Officers are recoloured with a `MaterialPropertyBlock`
rather than tinted material copies, so they keep sharing the Mixamo materials and still batch —
exactly when the most of them are on screen.

---

## Verified in Play mode

- Pistol dealt exactly 34 damage (its configured value) and decremented ammo
- Wanted escalated 0→1→2→3→4→5 across the real `CrimeReporter` witness path
- 5 stars dispatched **6 cruisers**, closing at 72–76 kph, one deploying an officer on foot
- Arrest → **BUSTED** → respawn at the police station, stars cleared, **weapon confiscated**
- Death → **WASTED** → respawn at the hospital, health restored
- Killing a civilian collapsed them via `RagdollLite` (Rigidbody added at runtime)

---

## Known rough edges

- **Renderer count ~1320.** Still unmeasured on a real device. This remains the number to watch.
- Officers shoot through a single raycast with a spread cone; there is no cover-seeking or
  suppression, so a firefight is a straight damage race.
- Cruisers occasionally wedge against building corners. The stuck-timer reverses them out after
  5 s, but the pursuit driver has no real pathfinding — it steers straight at you.
- No blood, muzzle flash or impact effects. `PlayerCombat.Attacked` is the hook for them.
- Civilians flee but do not scatter *intelligently* — they run directly away from the threat,
  which can take them into traffic. Arguably correct, occasionally comic.
- Corpses despawn after 25–30 s with no fade.
- Still on Windows Standalone; the Android switch remains pending from Phase 1.
