# Mini GTA — Phase 2: Vehicles, Traffic & Traffic Rules

Builds on [PHASE1.md](PHASE1.md). Open `Assets/Scenes/City.unity` and press **Play**.

There is a car and a motorbike parked within a few seconds' walk of spawn, and a boat off the
beach to the west. Walk up to one and press **ENTER**.

---

## What Phase 2 delivers

| Brief item | Status | Where |
|---|---|---|
| Car, motorbike and boat, all drivable | Done | `CarController` / `BikeController` / `BoatController` |
| Enter/exit with a context-aware Interact button | Done | `PlayerVehicleController` |
| Acceleration, braking, steering, drifting | Done | `CarController` (handbrake cuts rear grip only) |
| Collision damage with dents + smoke | Done | `VehicleDamage` |
| Terrain-aware handling (slower uphill) | Done | `CarController.ApplyDrive` slope factor |
| Joystick steers, gas/brake/handbrake/horn buttons | Done | `HudContext`, `HudButton`, `InputHub` |
| Chase camera while driving | Done | `ThirdPersonCamera.SetTarget` |
| AI traffic on lane paths | Done | `TrafficCar`, `TrafficSpawner`, `RoadNetwork` |
| Traffic lights with a real state machine | Done | `TrafficLightController` |
| AI stops on red, goes on green | Done | `TrafficCar.MustStopForSignal` |
| Pedestrians crossing on the same light cycle | Done | `Pedestrian`, `TrafficLightController.PedestrianMayCross` |
| Running a red near police adds wanted heat | Done | `RedLightMonitor`, `HeatSystem`, `PoliceVehicle` |
| Fuel system | **Deliberately omitted** | per the brief |

---

## Driving controls

| | Touch | Desktop |
|---|---|---|
| Steer | Left joystick, X axis | `A` / `D` |
| Throttle / reverse | `GAS` / `BRAKE` buttons | `W` / `S` |
| Handbrake (drift) | `DRIFT` button | `Space` |
| Horn | `HORN` button | `H` |
| Get out | `ENTER` button | `E` |

Gas shares its screen position with Jump, and Brake with Run. The contexts are mutually
exclusive, so the thumb always finds the primary action in the same place. You cannot step out
above 25 kph.

---

## How the traffic system fits together

**One lane graph, generated from the same constants as the asphalt.** `RoadNetworkBuilder`
reads `CityBuilder.RoadWidth/BlockSize/Pitch/Blocks`, so the lanes cannot drift out of
alignment with the roads. 288 nodes over 36 intersections: per intersection and compass
direction, an approach node (the stop line) and an exit node. Approaches link to exits for
straight/left/right — never a U-turn — and only to turns that actually lead somewhere, so no
car can strand itself at the map edge.

**One clock per intersection.** `TrafficLightController` is a position within a repeating
cycle, not a transition machine. That makes `PhaseOffset` work the obvious way: an offset
junction starts further along the same cycle. Neighbours are checkerboarded half a cycle apart,
so you meet a mix of reds and greens rather than a synchronised wall.

**Pedestrians read the vehicle signal.** Walking east-west means crossing the north-south
carriageway, so `PedestrianMayCross` returns true exactly when north-south *vehicles* are red.
There is no separate walk clock to fall out of sync with.

**Violations need a witness.** `RedLightMonitor` only records an offence if a `PoliceVehicle`
is within 55 m. Blowing a red on an empty street is free; doing it in front of a cruiser is
not. Junction entry is latched so one crossing counts once.

Signal *heads* are only built on the inner 4×4 of junctions (64 heads). Controllers exist at
all 36 so AI behaviour is uniform — building 4 physical heads everywhere would cost ~570
renderers for signage you never see at the map edge.

---

## Tuning

- Traffic population: `TrafficSpawner.TrafficCount` (default 12) and `PoliceCount` (2).
  **This is the mobile frame budget** — raise it deliberately, not casually.
- Signal timing: `GreenDuration` / `YellowDuration` / `AllRedDuration` on any junction.
- Handling: `MotorTorque`, `MaxSteerAngle`, `HandbrakeGrip` on `CarController`.
- Offence values and decay: `HeatSystem`.

Rebuild after changing any generated geometry: **`Tools > Mini GTA > BUILD EVERYTHING`**, or
`Rebuild World Only` for a faster loop.

---

## Four bugs worth remembering

These cost real time and will bite again in later phases:

1. **A MonoBehaviour's file name must match its class name.** `PoliceVehicle` was declared
   inside `RedLightMonitor.cs`. `AddComponent<PoliceVehicle>()` appeared to work but never
   serialised into the prefab — so there were no police, and red lights could never be
   enforced. It now lives in `PoliceVehicle.cs`.
2. **A failed respawn must always have a fallback.** `TrafficSpawner` only fell back to "any
   node" on the initial fill. A car that fell out of the world was then too far away to pass
   the distance window, so the only thing that could rescue it was the placement that just gave
   up — it fell forever. There is now an unconditional fallback plus a kill-floor check.
3. **Buoyancy must exceed weight.** The boat's upward force was ~22% of its own weight, so it
   sank and sat 2 m under the surface while still reporting `IsAfloat`. `BuoyancyStrength` is
   now expressed as a multiple of weight (2.2), which is self-documenting.
4. **`WheelCollider.GetWorldPose` overwrites your mesh rotation.** Unity's cylinder stands on
   Y; a wheel spins about X. The 90° correction has to be re-applied every frame, or the tyres
   render as upright barrels. Separately, track width must exceed half the body width or the
   wheels are simply buried inside the bodywork.

---

## Known rough edges

- **Renderer count is now ~1280** (up from 917), mostly signal heads and crosswalk paint.
  Static batching absorbs most of it, but this is the number to watch on device.
- AI cars use a simple forward sensor; they queue politely but do not negotiate right-of-way
  with each other on an unsignalled turn, so an occasional nose-to-nose stall happens. The
  stuck-timer recycles them after 9 s.
- Pedestrians walk a fixed two-point crossing beat rather than a full sidewalk graph. Enough to
  demonstrate the shared light cycle; not yet a crowd.
- The bike's balance is a rider-stand-in torque, not real counter-steer. It stays up and leans
  into corners, but it will not do anything a physics purist would recognise.
- Vehicle audio (engine, horn, collisions) is not wired — `VehicleHorn.Honked` is the hook.
- Still on Windows Standalone; the Android switch from Phase 1 is unchanged and still pending.
