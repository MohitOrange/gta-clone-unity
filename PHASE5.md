# Mini GTA — Phase 5: Economy, Shops, Customisation & Interiors

Builds on [PHASE1](PHASE1.md)–[PHASE4](PHASE4.md).
Open `Assets/Scenes/City.unity` and press **Play**.

Four buildings around the city have lit doors. Walk up on foot and press **ENTER**.

---

## What Phase 5 delivers

| Brief item | Status | Where |
|---|---|---|
| Currency earned from missions | Done (Phase 4) | `PlayerProgress.Money` |
| Shops: weapons, ammo, health | Done | Ammu-Mart, `Shop` |
| Vehicle upgrades (speed / armour / colour) | Done | Chop Shop, `VehicleUpgrades` |
| Cosmetic player skins | Done | Threads, `PlayerSkinSwapper` |
| Garage to store and customise vehicles | Done | `Garage` |
| Building interiors with own collision, lighting, NPCs | Done | `Interior`, `InteriorBuilder` |
| Property/safehouse purchase | Done | `Property` |
| Safehouse as save point | Done | `Interior.SavesOnEntry` |
| Fast travel | Done | `Property.FastTravelTo` |

---

## The four buildings

| Door location | Business | Stock |
|---|---|---|
| (491, 400) | **Ammu-Mart** | Med Kit $120, Body Armour $350, Ammo Box $90, Pistol $600, Extended Pouches $900 |
| (612, 431) | **Chop Shop** | Engine Tune $700, Reinforced Panels $550, four resprays $300–450 |
| (491, 614) | **Threads** | Five outfits, $200–900 |
| (384, 425) | **Safehouse** | $4,000 to buy. Save point + fast travel desk. |

Upgrade prices escalate: an engine tune is $700 at stock, $1,260 for the second level,
$1,820 for the third (`basePrice * (1 + level * 0.8)`).

---

## Why interiors are rooms, not scenes

The brief allowed either a scene swap or a tucked-away room. **Rooms won**, and the reason is
worth recording:

A scene swap unloads the city. That means losing the traffic, killing any pursuit, and
discarding a mission in progress — plus a load screen every time someone opens a shop door.
Rooms parked in dead air off the west edge of the terrain (x ≈ -420, y = 200) cost nothing
when empty, because they are simply outside every camera's frustum. The whole game stays in
one scene, and **a wanted level survives the player ducking indoors** — which is a mechanic,
not just a saving.

Going inside is more than a teleport. `InteriorManager` also:

- Replaces the world ambient with the room's own colour and **disables the day/night cycle**,
  which would otherwise darken a lit shop at night.
- Turns fog off — a small room inside a fogged world looks hazy.
- **Pauses traffic and police spawning**, which would otherwise chase a player who is now a
  kilometre off the map.
- Pulls the camera in from ~5 m to 3.4 m so it does not sit outside the wall.

Room lighting is emissive ceiling strips plus the ambient override, not real-time point
lights. The mobile URP tier has additional lights disabled, so actual lights would render as
nothing on a phone while costing on desktop.

---

## Save format

Bumped to **version 2**. Version 1 saves load fine — new fields default and the file is
rewritten as v2 on the next save. Added: `OwnedItems`, `OwnedProperties`, `ActiveSkin`, and a
`Garage` array of per-vehicle upgrade data.

Buying anything writes a save immediately, as does walking into the safehouse.

---

## Three bugs worth remembering

1. **You entered a shop and were instantly thrown back out.** The entry point sat 1.3 m from
   the exit trigger, whose radius was 1.8 m — so the player tripped the exit on their first
   frame inside. `ArmDelay` was meant to prevent exactly this, but it was set in `OnEnable`,
   which fires once at scene load, so the grace period had expired hours before anyone used
   the door. The exit is now re-armed on every entry, and the entry point moved to a 3.3 m gap.
   **A grace period has to start when the thing happens, not when the component wakes up.**
2. **Buttons rendered as blank grey rectangles.** The `Label()` helper styles text but takes no
   caption, so `MakePanelButton` created correctly-sized, correctly-fonted, *empty* labels.
   Nothing errored; the button simply had no words on it.
3. **Upgrades must be applied from stock values, never compounded.** `VehicleUpgrades` captures
   the vehicle's factory numbers on Awake and re-derives from those every time. Multiplying
   whatever is currently set would stack on every respawn, reload and second visit to the
   garage, producing a 400 kph sedan after three upgrades.

---

## Verified in Play mode

- Walked into Ammu-Mart: indoor ambient applied, fog off, traffic and day/night paused, camera at 3.4 m
- **Med Kit refused with "Full"** rather than taking money for a wasted heal
- Body Armour bought: $1,400 → $1,050, armour 0 → 100
- Chop Shop: engine fitted, **price escalated $700 → $1,260**, gold respray correctly refused as "Too expensive" at $350
- Safehouse: locked while unowned, purchase refused while broke, bought for $4,000 → **door unlocked, fast travel registered**
- Fast travel **blocked while wanted** ("Not while the police are looking for you"), worked when clean — 400 m jump
- Save round-trip: money, completed missions, owned items, owned property, active skin and vehicle upgrades all restored

---

## Known rough edges

- **Renderer count ~1,422.** Five phases of accumulated budget, still never profiled on a device.
  This is now well overdue.
- Skins recolour the existing model rather than swapping it. Humanoid retargeting from Phase 1
  means a full model swap is possible; recolouring just avoids re-binding the Animator for a
  cosmetic.
- The garage can display and upgrade a vehicle but there is no "drive it out" button wired to
  the UI — `Garage.DeliverSelected()` exists and works, but nothing calls it yet.
- Only one property exists. `Property` is a list-driven system, so adding more is data, not code.
- Shop rows are rebuilt wholesale on every purchase. Fine at this stock size; a large
  inventory would want row reuse.
- Interiors have no windows or exterior geometry connection — the building you walk into is not
  the building you see from the street.
- Still on Windows Standalone; the Android switch remains pending from Phase 1.
