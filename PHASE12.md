# Mini GTA — Phase 12: The lobby

Builds on [PHASE7](PHASE7.md) (menus, the New Game / Continue logic), [PHASE8](PHASE8.md)
(canvas scaler and safe area), [PHASE5](PHASE5.md) (outfits, save versioning) and
[PHASE11](PHASE11.md) (the Soft Touch UI kit).

The Phase 7 title screen — CONTINUE / NEW GAME / SETTINGS / QUIT stacked on a card — is gone.
In its place is a lobby: a left icon rail, the player's character standing lit on a plate in
the centre, one large PLAY button, and a top bar with the profile name, level and money.
Two new screens hang off the rail, and the settings panel was re-routed to it.

> **Status: Editor-verified, device pass pending.** Everything below was measured in Unity Play
> mode on a desktop GPU. Per CLAUDE.md §5 that is not verification. In particular **nothing here
> has been touched by a finger**, and the on-screen keyboard the rename field depends on does
> not exist in the Editor at all. See *What the Editor cannot answer* at the end.

---

## What it looks like

```
┌────────────────────────────────────────────────────────────────┐
│ [👤│ Rookie          ]                      [★ 1] [🪙 250]     │  top bar
│                                                                │
│ ┌──┐              ┌───────────────────┐                        │
│ │🧍│ CHARACTER    │                   │                        │
│ └──┘              │    the character  │  ← stage plate, a      │
│ ┌──┐              │    you are about  │    RenderTexture of a  │
│ │👤│ PROFILE      │    to play as     │    real 3D body        │
│ └──┘              │                   │                        │
│ ┌──┐              └───────────────────┘                        │
│ │⚙ │ SETTINGS                                                  │
│ └──┘              ┌───────────────────┐                        │
│ ┌──┐              │       PLAY        │  ← primary action      │
│ │⏻ │ EXIT         └───────────────────┘                        │
│ └──┘         Continue - level 3 - $2,850 - 0 jobs done         │
└────────────────────────────────────────────────────────────────┘
```

---

## Layout: why this one is anchored differently from every other screen

Every other screen in this project is a fixed-size card centred on the canvas. That is the right
shape for a modal and the wrong shape for a lobby, which has to fill the display.

The lobby is edge-anchored instead:

| Element | Anchoring |
|---|---|
| Top bar | Stretched to both screen edges, pinned to the top, fixed 104-unit height |
| Rail | Pinned to the left edge, centred vertically, fixed 112-unit buttons |
| Hero column | Stretched between the top bar and the bottom, **symmetric** left/right margins |
| Stage plate | 28.5%–100% of the hero column's height; fixed 600-unit width |
| PLAY | 10%–25% of the hero column's height; fixed 520-unit width |
| Caption | 0.5%–8.5% of the hero column's height |

Two things there are deliberate and worth keeping:

- **The hero column's margins are symmetric even though the rail is only on the left.** Making
  the column "the space left over beside the rail" would put PLAY 4.5% off-centre on a 4:3
  screen. The right-hand margin is empty on purpose so the primary action is in the middle of
  the *display*.
- **Touch targets keep a fixed size; everything else scales.** The rail buttons and the top bar
  are 112 and 104 reference units whatever the aspect ratio. A thumb does not get bigger on a
  tablet.

No new pixel constants were introduced outside the canvas reference space, and the Phase 8
`SafeArea` container still holds every UI root — the two new screens included, because the
lobby is built before `EnsureSafeArea` runs.

### Draw order was a real bug, twice

Sibling order is draw order, and it bit in both directions:

1. The lobby has to come **after** the HUD's round pause button, or the pause button draws on
   top of it. (That is the Phase 11 known rough edge "the pause button draws over the title
   screen" — fixed here as a side effect.)
2. The lobby's own overlays have to come **after the lobby**. Built before it, the character
   select opened, built its cells, reported `IsOpen == true` — and was completely invisible.
   A capture caught it; no assertion would have.

The settings panel is shared with the pause menu and is built early, so `LobbyScreen.Raise()`
brings whatever the rail opens to the front as it opens. The pause menu never needed this
because it hides itself first; the lobby deliberately does not.

---

## The character on the plate

The task said to reuse the existing character pipeline rather than build a preview system. It
does:

- The body is `CharacterCatalog.AttachBody(subject, CharacterCatalog.Player, ...)` — the same
  call, with the same `Look`, that builds the player. It cannot become a different character.
- It carries a `PlayerSkinSwapper`, the same component that recolours the player, so an outfit
  looks identical here and in the city.
- It plays `PlayerLocomotion.controller`, the game's only animator controller, with
  `updateMode = UnscaledTime` so it breathes while the lobby holds `Time.timeScale` at zero.

It is a *second* body on a stage in dead air, not the player themselves — see DECISIONS D39 for
why. Cost: **4 renderers, 6,101 triangles**, and while the lobby is open they are the only
things being drawn at all.

### Framing was measured, then corrected against the picture

The camera solves its own distance from the model's height:
`distance = (height × 1.04) / 2 tan(fov/2)`, with the shot's bottom edge 15 cm below the floor
so there is ground under the boots for the plate's contact shadow.

**The 1.04 is calibrated, not derived, and that is the interesting part.** `Renderer.bounds` on
a skinned mesh is the bind pose's, padded for bone spread: this model measures **2.02 m** that
way against a body that draws **1.79 m**. Framing off the raw bounds put the character at 65%
of the plate with a band of dead air over its head. The number was corrected by looking at the
render, and the comment in the file says so, so the next person does not "fix" it back.

### Lighting

`Mobile_RPAsset` has additional lights **disabled**, so a lamp aimed at the stage renders as
nothing on a phone. And `DayNightCycle.Apply()` runs every frame regardless of timescale, so
overwriting the sun once does not hold.

So the lobby borrows the key light the way `InteriorManager` borrows the ambient when the player
walks into a shop: stand the cycle down, aim the sun at the stage, flatten the ambient, fog off,
and hand all of it back on close. Verified: after PLAY the cycle is enabled again, the sun is
back on, ambient is `(0.542, 0.578, 0.658)` and fog is on at 260–950 m.

---

## Sub-screens

### Character select

Six tiles in a grid: the model as authored, plus the five outfits Threads sells.

**The grid is built from the shop's own stock.** A new `SkinLibrary` reads every
`ShopCategory.Skin` item off the `Shop` components in the scene, so an outfit's name, price,
level gate and colour are authored exactly once — in `InteriorBuilder`, where they always were.
Ownership is the shop's existing rule (`PlayerProgress.Owns`), so the wardrobe can never become
a way around the till.

Verified with one outfit granted and the player at level 3:

| Tile | State shown |
|---|---|
| Standard Issue | OWNED |
| Street Grey | locked, `$200` |
| Crimson Jacket | **WEARING**, green marker bar |
| Navy Suit | locked, `$400` |
| Emerald Coat | `$650` — the `LEVEL 3` gate lifted when the level was reached |
| Ivory Set | locked, `LEVEL 4` |

Tapping a locked tile does not silently do nothing — it says why: *"Emerald Coat — $650 at
Threads"*, and `ActiveSkin` was confirmed unchanged. Tapping an owned one sets the skin, and
**both** swappers repaint: `player swapper current='skin.crimson'`,
`preview swapper current='skin.crimson'`.

### Profile

Name field, level, an XP bar, eight statistics, and NEW GAME / BACK.

Every figure comes from something already tracked. Statistics a profile screen would usually
show and this game does not count — time played, kills, distance driven, arrests — are absent
rather than invented (DECISIONS D40).

NEW GAME arms on the first tap (`TAP AGAIN TO ERASE`, money confirmed unchanged) and disarms
itself after four seconds.

### Settings

Unchanged, re-routed. `SettingsPanel.OpenFrom(this)` already re-shows its caller on close; the
lobby is never hidden, so that call lands on an open panel and does nothing — which is exactly
the wanted behaviour. Captured with all sliders, the layout/invert pair, the quality tier row
and the detail line intact.

---

## Nesting, and what it does to the clock

The rail opens sub-screens **over** the lobby, not instead of it, so `MenuState`'s depth goes
1 → 2 → 1 and `Time.timeScale` is only handed back when the lobby itself closes. Measured at
every step of the journey: `timeScale = 0` throughout, `lobbyOpen = true` throughout.

Android Back / Escape is routed `PauseMenu.OnBackPressed` → `LobbyScreen.OnBackPressed`, which
closes the deepest overlay and does nothing on the bare lobby. Verified: Back over the profile
closed the profile and left the lobby open with the pause menu still shut; Back again on the
bare lobby changed nothing. The pause menu can no longer open on top of the lobby.

---

## Save format: v3 → v4

One field added, `ProfileName`. The real v3 file on this machine was loaded and rewritten:

```
"Version": 3  →  "Version": 4
              +  "ProfileName": "Rookie"
```

Money, XP, level, unlocks, owned items, active skin, garage and position all survived. A v3 file
has no name at all, and `PlayerProgress.CleanProfileName("")` returns the default — so an old
save gets a name rather than a blank chip.

Name cleaning verified: `"Vex"` → `Vex`; `"   "` → `Rookie`; a 31-character name → 16 characters.

---

## A latent Phase 5 bug fixed on the way

`PlayerProgress.ActiveSkin` has been saved and restored since Phase 5, and
`PlayerSkinSwapper.OnSkinChanged` recorded the id **and changed nothing** — because the colour
lived on the shop item and the id alone said nothing about what to paint. An outfit bought in
one session came back as the stock model in the next.

It was never visible before because nothing displayed the character outside gameplay. It is very
visible in a lobby. `SkinLibrary` resolves the id against the same shop stock, so the fix adds a
lookup rather than a second table. Verified in a rendered frame: a session started against a
save with `"ActiveSkin": "skin.crimson"` shows the character in the crimson outfit.

---

## Testing results

All Editor-only. Every row was driven through the real `IPointerClickHandler` path with
`ExecuteEvents`, not by calling the handler methods.

| # | Test | Result | Evidence |
|---|---|---|---|
| 1 | Lobby loads on launch, rail icons present, correctly positioned and scaled | **PASS** | `lobbyOpen=True timeScale=0` at frame 1; 4 rail slots at derived positions 251 / 93 / −65 / −223 with captions clear of the button art; captured at 2400×1080, 2048×1536 and 1920×1080 |
| 2 | PLAY centred, triggers New Game / Continue, loads into City.unity | **PASS** | Both branches driven. **Continue:** money 2,850 → 2,850, level 3, skin kept. **New Game:** money 10,249 → 250, level 1, skin cleared, pistol removed, player warped to spawn (561, 15.36, 800). `timeScale` 0 → 1, `cullingMask` 0 → −1, cycle re-enabled, stage camera off. World render confirmed in a frame |
| 3 | Character select: owned/locked states, selection updates preview and carries into gameplay | **PASS** | 6 tiles, states in the table above; locked tap left `ActiveSkin` unchanged; owned tap set it and **both** swappers repainted; the crimson body is visible in the lobby frame after a reload |
| 4 | Profile opens, name editable, persists across a save/reload cycle | **PASS** | Renamed to `Vex`, wrote v4, restarted play mode, came back as `Vex`. Cleaning rules verified. NEW GAME confirmed to need two taps |
| 5 | Settings opens from the new rail icon, all existing settings still work | **PASS** | Opens at depth 2 over the lobby; sliders, layout/invert, quality tier (LOW lit green) and the detail line all present and unchanged |
| 6 | No Canvas / safe-area regressions vs Phase 8 / 11 | **PASS** | Scaler still `ScaleWithScreenSize`, ref `1920×1080`, match `0.65`. Exactly **1** `SafeAreaFitter`, holding **20** UI roots (was 18; +2 for the new screens). **1** font, **7** sizes (14/18/22/26/32/44/64) across 125 `Text` — the Phase 11 atlas bound holds |
| 7 | Two different aspect ratios, no overlap or cutoff | **PASS** | 2400×1080 (20:9) and 2048×1536 (4:3) — see the note below on why these two |
| 8 | Console clean, scene saved | **PASS** | 0 errors, 0 warnings at the end of the pass; `City.unity` saved; `PlayerSettings.runInBackground` confirmed still `false` |

### On "two aspect ratios"

Phase 11 captured at 2400×1080 and 1600×720. **Those are the same aspect ratio** (2.222) and the
same canvas reference size (2220×999) — it was two resolutions of one shape, and it could not
have caught an aspect-ratio bug. This phase used 2400×1080 (2220×999 reference) and 2048×1536
(1593×1194 reference), which are genuinely different: the second is 28% narrower and 20% taller
in canvas units. 1920×1080 was captured as well.

### Scene census, measured the same way as Phase 11 (edit mode)

| | Phase 11 | Phase 12 | Delta |
|---|---|---|---|
| Renderers | 3,082 | **3,086** | +4 |
| Triangles | 2,464,197 | **2,470,298** | +6,101 |
| Colliders | 957 | 957 | — |
| On `Detail` | 850 | 850 | — |
| Buttons on the canvas | 30 | 35 | +5 |
| `Text` components | — | 125 | — |

The +4 renderers and +6,101 triangles are the display body, exactly. They are drawn only while
the lobby is open, and while it is open the world camera's culling mask is zero — so the lobby
renders four skinned meshes and a canvas, and nothing else.

---

## Five defects found by looking at the captures, and fixed

Listed because each one passed every assertion first. This is the Phase 9b lesson applied.

1. **The rail captions were unreadable.** Placed inside the button, they landed on the art's
   bottom bevel and read as a smudge at both resolutions. Moved outside, below the button.
2. **The chip's "PROFILE" caption was cut in half** by the top edge of the screen. Removed — a
   chip with an avatar and a name does not need labelling.
3. **The kit's settings icon resolved to a gold smudge** at rail size. Replaced with a drawn cog
   matching the other three glyphs (DECISIONS D38).
4. **The PLAY caption sat 450 units right of centre.** A stretch-anchored rect given
   `offsetMax.x = 900` on an axis whose anchors are equal spans *from* the anchor, not around
   it. Symmetric offsets.
5. **The profile screen printed "$2,850" through "UNLOCKS EARNED".** Both columns were laid out
   around a column *centre*; the left column's value and the right column's caption occupied the
   same pixels. Rewritten as two columns with captions on their own left edge and values on
   their own right edge.

Plus one caught by assertion rather than by eye: **the money chip read $0 on a first run.**
`PlayerProgress` sets its starting money in its own `Awake`, and Awake order is undefined — a
lobby that woke first read zero. With a save, `SaveSystem.Loaded` corrected it; without one,
nothing did. `LobbyScreen.Start` now covers that gap.

---

## Two things learned about the tooling (not game bugs)

Both cost real time. Recording them so the next phase does not.

- **Runtime `onClick` listeners die on every MCP command.** The bridge compiles a throwaway
  assembly per call, and the domain reload that follows wipes non-persistent UnityEvent
  listeners while leaving the GameObjects intact — so every button wired in `Awake` goes dead
  after the first command, without `Awake` ever running again. This looks exactly like the
  Phase 8 dead-button bug and is not it: the Phase 7 pause menu and settings panel fail the
  same way under the same conditions, and a probe listener added in the same command fires
  normally. **Drive a whole click journey inside one `RunCommand`**, and force
  `CanvasGroup.alpha = 1` before capturing, because no frames elapse inside a single command
  and the fade never runs.
- **`PhaseCapture.UiShot` renders the interface but not the world underneath it, under URP.**
  Its `uiCam` clears depth-only to preserve the colour the main camera wrote, and the URP render
  graph clears colour anyway — so the backdrop comes back as the camera's flat background
  colour. It never mattered before because every screen captured with it was opaque (the lobby
  included). It matters if you want to photograph the HUD over the city: use `PhaseCapture.Shot`
  for that, which is correct — the same vantage through `Shot` renders the street, the parked
  cars and the sea perfectly.

---

## Known rough edges

- **Nothing has been touched by a finger.** Device verification is pending for this phase along
  with 9, 9b, 9b-fix and 11.
- **The rename field's on-screen keyboard is completely untested.** uGUI's `InputField` opens
  `TouchScreenKeyboard` on Android; the Editor uses the hardware keyboard and exercises none of
  that path — not the keyboard appearing, not the layout while it covers half the screen, not
  the Done button, not losing focus to the Back gesture. This is the single most device-dependent
  thing in the phase.
- **A legacy `InputField`, not TextMeshPro.** Consistent with the rest of the interface and with
  DECISIONS D5, and it keeps the phase from adding a second font atlas — but TMP remains the
  right eventual answer for all of this.
- **The wardrobe grid does not scroll.** Six outfits fit in 3×2. Tiles shrink to fit if more are
  added, and past about nine they would get small; a scrolling grid is the fix when that happens.
- **The XP bar is a heavy dark slab at exactly 0%,** which is what a player sees the moment they
  level up. It is correct — the fill renders gold as soon as there is progress — but the empty
  track is visually loud at 1,120 units wide.
- **The lobby's idle sway is a single sine on the body's heading.** It reads as breathing at 11
  seconds a cycle; it is not an idle animation, and the character does not react to anything.
- **`Library/Bee` is a real directory on C: again, not a junction to D:.** It is only 0.21 GB
  today because no APK has been built since it was lost, and C: has 5.8 GB free. It must be
  re-junctioned **before** the next device build — PHASE7 records three build failures caused by
  exactly this. See the command in PROJECT_STATE.
- Carried over untouched: `DeviceDiagnostics.cs` still ships and should be deleted before
  release (its one reference to `MainMenu` was repointed at `LobbyScreen`, nothing more); the
  Android Back button still does not open the pause menu from gameplay; and a pre-Phase-11 save
  still restores a position outside the new city.

---

## What the Editor cannot answer

Per CLAUDE.md §5, none of the following is settled by anything above:

- Whether the rail's 112-unit buttons are comfortable under a real thumb, and whether the top
  bar's name chip is reachable one-handed on a 6.5" phone in landscape.
- Whether the on-screen keyboard works at all for the rename field.
- What the stage costs on a phone: a second camera, a 640×896 `RenderTexture` and a skinned mesh
  every frame the lobby is open. It should be far cheaper than the city it replaces — the world
  camera draws nothing while the lobby is up — but "should be" is the word Phase 9b was written
  about.
- Whether the safe area behaves on the test device's 82 px cutout now that content is anchored
  hard to the left edge and the top edge, which the old centred card never was.

## Rebuilding it

```
Tools > Mini GTA > 10. Build Menus and Audio
```

Additive and idempotent. It removes `Lobby`, `CharacterSelect`, `ProfilePanel` and `LobbyStage`
first, so re-running does not stack them. A full `BUILD EVERYTHING` reaches it too —
`SceneAssembler.Assemble` calls `MenuBuilder.Build` at the end, and that calls `LobbyBuilder`.
