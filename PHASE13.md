# Mini GTA — Phase 13: the Space Exploration re-skin

Builds on [PHASE11](PHASE11.md) (the Soft Touch re-skin and `UiTheme`), [PHASE12](PHASE12.md)
(the lobby) and [PHASE8](PHASE8.md) (canvas scaler, safe area, the dynamic-font-atlas hazard).

The whole interface moves from the Soft Touch UI Kit to the **Space Exploration GUI Kit**, and
every piece of text moves onto a single display face. Panels, buttons, bars, icons, dividers
and the colour palette are now that kit's; the HUD, the lobby, the menus, the shop and the map
were rebuilt against it.

> **Status: Editor- and Play-mode-verified, device pass pending.** Per CLAUDE.md §5 that is not
> verification. Nothing here has been touched by a finger. See *What the Editor cannot answer*.

---

## The font, and the one thing this phase could not do

The brief asked for the **Fatality FPS gaming font** on every text element. **That font is not in
this project.** There is no copy in `Assets/`, none anywhere on this machine, and fetching a
licensed typeface is not something this build may do on its own.

So the font is a **slot, not a path**:

```
Assets/Game/UI/Fonts/     <- drop Fatality.ttf here
```

`UiTheme.Font` resolves in this order, and logs which one won:

| | Face | In use |
|---|---|---|
| 1 | a `.ttf`/`.otf` in `Assets/Game/UI/Fonts` whose name contains "fatality" | — |
| 2 | any other `.ttf`/`.otf` in that folder | — |
| 3 | **Righteous**, shipped inside the Space Exploration GUI Kit | **yes** |
| 4 | Unity's built-in `LegacyRuntime.ttf` | — |

Righteous is the stand-in: geometric, wide, heavy, no lower-case delicacy — the closest thing in
the project to the aggressive esports display face the brief describes. Drop the real file in
that folder, re-run `Tools > Mini GTA > 6b. Rebuild Interface Only`, and all 129 `Text`
components change face together. Nothing else has to change.

**The build log names the face it used**, so a report can never claim the intended font is in
place when it is not:

```
[Menus] interface face: Righteous (kit fallback -- Fatality not present)
```

`Assets/Game/UI/Fonts/README.txt` says the same thing next to the folder.

---

## What changed, against the ten instructions

| # | Asked for | Done |
|---|---|---|
| 1 | One font everywhere | **129 of 129** `Text` on one face. See the caveat above |
| 2 | Kit panels and frames for menus, dialogue, HUD containers | 8 container sprites in use across every screen |
| 3 | Kit button states applied consistently | **37 of 38** buttons carry all four kit sprites; the 38th is an invisible hit area over the minimap |
| 4 | Kit icons for inventory, health, ammo, shields, status | 20 pictograms and 4 painted emblems; the 5 hand-drawn glyphs from D38 are gone |
| 5 | Kit colour palette | 17 palette values, every one sampled out of the kit's own PNGs |
| 6 | Kit progress bars for health, stamina, XP, loading | 6 bar sprites: vitals, XP, vehicle condition, sliders, the ad countdown |
| 7 | Kit sci-fi lines and frames for borders and dividers | 3 rule sprites + the kit's selection glow used as an outer frame in 3 places |
| 8 | Clear hierarchy from font size and weight | 7-step scale, unchanged in count; hierarchy carried by size **and** weight **and** colour |
| 9 | Responsive and readable after the swap | 3 aspect ratios captured; a new build-time contrast sweep and frame audit |
| 10 | Hover / active / disabled visually distinct | Sprite swap, not tint — see below |

---

## The palette is measured, not chosen

Every colour was taken from a histogram of the kit's own artwork over its opaque pixels, so the
text and the panels cannot disagree. The hex in each row is the pixel it came from.

| Role | Value | Sampled from |
|---|---|---|
| Panel body | `#463577` @ α194 | every `Containers/Large` interior |
| Solid plate | `#271C47` | `shop-item-container`, `mission-bar-empty` |
| Deepest (focused field) | `#161438` | `username-password-container-selected` |
| Text on a panel | `#DFF1F7` | the kit's own white, off `gem-1` and `gun` |
| Muted text | `#A3AAC1` | the metallic grey in `gun-128` |
| **Text on a button** | `#271C47` | the outline the kit draws every painted button with |
| Accent (neon cyan) | `#9FDFF7` | `inventory-highlight` glow |
| Positive | `#ABDCF4` | `mission-bar-full` |
| Gold | `#F7E2A6` | `coin-128` |
| Danger | `#FF6FC8` | `heart-128` (`#FF99E0`), pushed hotter |
| Lilac | `#BB8FE2` | `circle-button-blank` |
| Shield / steel | `#9ABBF4` | the blue button face |
| Scrim | `#130E2B` @ α209 | `background-overlay-large` |

### The polarity inverted, and that is the dangerous part

Soft Touch was **cream panels with dark text**. This kit is **deep-indigo panels carrying light
painted buttons**. So a single screen now has both kinds of surface on it, and the correct ink is
*opposite* on each: a heading on a panel must be light, and the caption on the button directly
below it must be dark.

The old `UiTheme.Ink` served both and cannot express that. **It was deleted rather than
redefined**, so every one of its 30-odd call sites became a compile error and had to be answered
one at a time with either `TextOnPanel` or `TextOnButton`. The failure mode otherwise is
invisible text, which no assertion catches — PHASE12 found five layout defects by looking at
pictures and none of them by asserting.

Two safety nets back that up, both run at the end of every build:

- **`UiTheme.EnforceContrast`** — rewritten. The Phase 11 version only ever *darkened* light text
  and would have left every panel caption black on indigo. It now compares luma in both
  directions, snaps neutral text to the surface's ink, and for deliberately coloured text (money
  gold, a damage warning) keeps the hue and moves only the value.
- **`UiTheme.AuditFrames`** — new. See below.

---

## The frame audit, and why it exists

**This kit's painted frames are two to three times thicker than the ones every card in this
project was sized against.** Soft Touch's panels had 26–64 px borders; these have 87–166. Every
card was laid out against the old numbers, so on the first pass:

- the word SETTINGS was drawn across the top moulding of its own panel;
- the shop's stock rows ran out past **both** sides of the sheet and onto the screen behind it;
- the full map's streets were painted over the panel frame and out into the world;
- the dialogue box's hint sat 147 units outside the card.

All four passed every assertion. All four were found by looking at pictures.

A picture will find the next one too — but only if somebody takes it, of the right screen, at the
right size. So `UiTheme.AuditFrames` measures each nine-sliced kit panel's border, converts it
into canvas units, and checks every `Text` and control under it against the rectangle that
leaves. It reports the overlap in units, so the fix is a number instead of a hunt.

It found **91 intrusions** on the first run. Calibrating out the one legitimate case — a control's
own caption is deliberately stretched over its whole face so centred text stays centred at any
size — left the real ones, which are now all fixed:

```
FRAME AUDIT: 0 overhangs
```

It runs inside `MenuBuilder.Build`, so it cannot be forgotten.

### Sizes that had to change because of it

| Screen | Was | Now | Why |
|---|---|---|---|
| Settings card | 1180×900 on `setting-container` | 1180×900 on `pause-container` | the kit's own settings frame is 165 px; it left 850×570 of a 900-tall card |
| Shop sheet | 920×660 on `shop-container` | 1000×760 on `pause-container` | 165 px frame vs 828-wide stock rows |
| Pause card | 620×720 | 640×800 | title and status line both under the 88 px frame |
| Character / Profile | 1240×860 | 1300×920 | title, both buttons and eight statistic rows under the frame |
| Store card | 820×460 | 1000×620 | 87 px frame left 646×286 |
| Ad offer | 760×320 | 900×460 | 87 px frame left 586×146 for two buttons and two lines |
| Dialogue | 1280×380 on `victory-defeat` | 1280×420 on `shop-item` + outer glow | see below |
| Full map frame | `shop-container` | `shop-item` + outer glow | see below |
| Profile XP track | 18 units tall | 32 | the level-bar sprite's own lip is 11 px top, 10 bottom — nothing fit between them |
| HUD XP track | `level-bar` at 14 units | `mission-bar` at 16 | same problem; the thin bar is the right sprite at that size |

**Where a thick frame was genuinely wanted, it moved outside.** The map and the dialogue box use
the kit's thin plate for the panel and its *selection glow* stretched 22–24 units beyond it as an
outer frame. That satisfies instruction 7 without a border that eats the interior.

---

## Buttons: sprite swap, not tint

The kit ships four separate sprite sets — normal, highlighted, pressed, disabled — in five
silhouettes and four colours. Instructions 3 and 10 want all four used and visibly distinct, and
this kit gives that directly.

**Tinting would have hidden it.** A tint *multiplies* painted art rather than replacing it, so a
"pressed" tint reads as the same button slightly darker, and a faded "disabled" tint makes the
button vanish instead of looking unavailable. `UiTheme.StyleButton` sets all four sprites and
leaves every colour at white.

Three colour families, chosen by role rather than by taste:

| Family | Used for |
|---|---|
| **Gold** (`Strong`) | the single primary action on a screen: PLAY, RESUME, WATCH, REMOVE ADS, NEW GAME |
| **Blue** (`Normal`) | everything ordinary |
| **Purple** (`Quiet`) | secondary and over-the-world: BACK, CLOSE, MAIN MENU, the HUD thumb cluster |

Three silhouettes, picked by pixel density rather than by shape alone: `Wide` (356×96) for menu
rows, `Square` from the kit's **large** tier (120×112, not the 60×56 medium) for the rail and the
thumb controls, `Slim` (304×52) for stock rows and chips.

`HudButton` gained `NormalSprite`/`PressedSprite` and swaps them in `ApplyTint`, so the eight
thumb controls get the kit's pressed art too. That is the one runtime script this phase changed.

The lobby's name chip is the exception: it is a button, but it sits between two dark stat chips
and styled as a painted face it came out a pale lilac slab next to them. `StyleChipButton` keeps
the chip art and takes its states from the kit's focused/unfocused field pair.

---

## Nine-slice borders are derived, not typed

The kit ships **every** sprite with a zero border, and its art is used here at sizes it was not
authored for. Phase 11 solved this with a hand-typed table of 18 borders. This phase does not
have one.

`UiTheme.PrepareSprites` reads each PNG off disk — not through the importer, so the texture's
Read/Write flag is irrelevant — finds the opaque silhouette's bounding box, measures the corner
radius along its edges, and writes `padding + radius + 2` as the border. Symmetric on each axis,
because every piece in this kit is a rounded rectangle and a lopsided border is a measurement
artefact that renders as a visibly off-centre frame.

Measured, for the record:

```
pause-container-large     1128x512   90, 88, 90, 88
setting-container-large   1440x952  165,165,165,165
shop-container-large      1592x952  165,166,165,166
victory-defeat-large       896x432   87, 87, 87, 86
shop-item-container-large  400x440   17, 14, 17, 14
large-blue-medium          356x96    14, 13, 14, 13
square-blue-large          120x112   19, 20, 19, 20
extra-long-blue-medium     304x52     9,  9,  9,  9
mission-bar-empty-medium   336x24     6,  5,  6,  5
level-bar-empty-medium     208x38     7, 11,  7, 10
```

A sprite swapped for a different size re-measures itself. The table cannot go stale.

---

## Icons: the kit's, including the four D38 drew by hand

PHASE12 D38 replaced the previous kit's settings icon with a hand-drawn signed-distance-field
glyph, because that kit's icon was a **painted three-quarter render** that resolved to a gold
smudge at the rail's 60-unit size — and then drew three more to match it.

This kit ships a **separate flat single-colour pictogram set** authored for exactly that use,
which is the thing D38 could not find. So all five generated glyphs and their 120-line SDF
rasteriser are deleted and the rail is back on kit art. The interface has one icon vocabulary
again instead of two.

| Where | Icon |
|---|---|
| Rail | `person`, `info`, `settings`, `power` |
| Thumb controls | `up-arrow`, `bolt`, `door`, `gun`, `fast-forward`, `stop`, `redo`, `megaphone` |
| HUD corners | `pause`, `shop`, `skull` |
| Vitals | `heart`, `shield` — beside the bars rather than captioned |
| Ammo | `gun`, on a dark chip |
| Menus | `play`, `map`, `settings`, `check`, `home`, `back`, `next`, `cross` |
| Money / level / wanted | the painted `coin` and `star` emblems |

Seventeen `Image`s still use the four generated shapes — the joystick ring and knob, map blips,
the objective arrow, the legend dots, the stage's contact shadow and the wardrobe's colour
swatch. Those are geometry, not iconography; the kit has no equivalent and should not.

---

## Testing results

Editor and Play mode. Every figure below is measured, and every layout claim was checked against
a rendered frame as well as an assertion — the PHASE9B lesson.

| # | Test | Result | Evidence |
|---|---|---|---|
| 1 | One font across the whole interface | **PASS** | 129 `Text`, **1** font (`Righteous-Regular`), and the build logs which face won |
| 2 | Type scale still bounded (D30) | **PASS** | exactly **7** sizes: 14 / 18 / 22 / 26 / 32 / 44 / 64 — unchanged in count from Phase 12 |
| 3 | Kit art across every surface | **PASS** | 206 `Image`: **136** kit sprites, 17 generated shapes, 53 untextured (scrims, road bars, colour-only fills) |
| 4 | All four button states from the kit | **PASS** | **37 of 38**; the 38th is `MapButton`, an invisible tap target over the corner minimap with no sprite by design |
| 5 | Nothing laid out under a panel frame | **PASS** | `AuditFrames` 91 → **0**, and **0 at zero tolerance** as well, so nothing is passing on the 8-unit allowance |
| 6 | No Canvas / safe-area regression | **PASS** | scaler still `ScaleWithScreenSize`, ref `1920×1080`, match `0.65`; exactly **1** `SafeAreaFitter` holding **21** roots |
| 7 | Three aspect ratios, no overlap or cutoff | **PASS** | 2400×1080 (20:9), 2048×1536 (4:3), 1920×1080 (16:9) |
| 8 | Lobby renders at runtime with the real save | **PASS** | play mode: `HumanM@Idle01` playing on the stage body, name `Mohit`, `LEVEL 10`, `$29,960`, caption *"Continue - level 10 - $29,960 - 0 jobs done"* |
| 9 | HUD over the actual city | **PASS** | see the composite note below — health, shield, ammo `48`, 2 of 5 wanted stars lit, money, XP, and the thumb cluster all legible over bright sand |
| 10 | PLAY still enters the city | **PASS** | `timeScale` 0 → 1, `lobbyOpen` true → false, `cullingMask` 0 → −1 |
| 11 | Console clean, scene saved | **PASS** | **0 errors, 0 warnings**; `City.unity` saved; `PlayerSettings.runInBackground` still `false` |

### A composite capture, finally

PHASE12 recorded that `PhaseCapture.UiShot` draws the interface with no world behind it and
`PhaseCapture.Shot` draws the world with no interface over it, so no capture had ever shown what
a player actually sees. For a UI phase that is not good enough.

The trick: put the overlay canvas onto the main camera for the duration of one render.

```csharp
var mode = canvas.renderMode;
try {
    canvas.renderMode = RenderMode.ScreenSpaceCamera;
    canvas.worldCamera = Camera.main;
    canvas.planeDistance = 0.5f;
    Canvas.ForceUpdateCanvases();
    PhaseCapture.Shot("city_hud", 2400, 1080);
} finally { canvas.renderMode = mode; }
```

Two defects came straight out of the first such frame and would not have come out of anything
else:

- **The wallet's XP bar and the STORE button overlapped by 4 units.** Both are anchored to the
  top-right corner from different parents; nothing compares them.
- **"LOSE HEAT" printed through "STORE."** Two 136-unit captions under buttons 112 units apart.

Both fixed; the caption gap is now 12 units of box and 65 units of actual glyphs.

A third came from the same frame: at `HudIdle` α0.70 the thumb cluster washed into the beach
sand, which is the brightest ground in the game. Raised to 0.80.

---

## Known rough edges

- **The font is Righteous, not Fatality.** Everything else about instruction 1 is done; the file
  is the only thing missing, and the folder is waiting for it.
- **Nothing has been touched by a finger.** Device verification is still pending for this phase
  and for 9, 9b, 9b-fix, 11 and 12.
- **The 4:3 shop, store and ad screens were checked by audit, not by eye.** The frame audit is
  aspect-independent (it measures against each panel's own rect), and those three are centred
  fixed-size cards, so an aspect change moves them but cannot reflow them. The lobby, which *is*
  aspect-sensitive, was captured at all three.
- **`Assets/Game/UI/ui_star.png` is now unreferenced**, replaced by the kit's painted star. It
  will not ship — Unity only builds referenced assets — but it is dead in the project.
- **The overlays sit at `CanvasGroup.alpha = 1` in edit mode** and are hidden by each panel's
  `Awake` at runtime. Unchanged from Phase 12, and the reason a capture has to open exactly one
  screen at a time; two of mine caught three panels stacked before I noticed.
- **Legacy `Text`, not TextMeshPro.** DECISIONS D5 still stands, and this phase keeps the atlas
  bounded at one face and seven sizes rather than adopting TMP mid-re-skin. TMP remains the right
  eventual answer, and it is what would give real letter-spacing — which is the one esports-font
  affordance legacy `Text` cannot do at all.
- **No APK was built this phase, and that is a disk problem rather than a choice.** `C:` has
  **4.61 GB** free and `Library/Bee` is a **5.45 GB** real directory sitting on it rather than a
  junction to `D:` — PROJECT_STATE's step 0 said Bee was "only 0.21 GB", which was 26x out of
  date. PHASE7 records three build failures from this shortage at a point when there was *more*
  room, and PHASE9 records it corrupting the AssetDatabase. Moving Bee to `D:` (17.89 GB free)
  frees 5.45 GB and takes `C:` to about 10 GB, but it needs the Editor closed, so it is not
  something to do under a running one. See PROJECT_STATE step 0 for the commands.

---

## What the Editor cannot answer

Per CLAUDE.md §5:

- Whether Righteous — or Fatality, when it arrives — is legible at the 14-unit `Micro` step on a
  6.5" phone. It is used for the rail captions, the HUD control captions and the version label.
  A display face at 14 pt is the readability risk in this whole phase and a desktop monitor is
  the wrong instrument for it.
- Whether the atlas still repacks. The bound is unchanged at one face and seven sizes, but
  Righteous has different metrics from Carlito and PHASE8 observed a repack on device.
- What the kit's textures cost. 136 sprites are now referenced, several of them 1128×512 and
  1592×952. Nothing measured the ETC2 footprint or the atlas count on a phone.
- Whether the thumb cluster reads over the *city* rather than over sand and sky. The composite
  above is the beach; the city has dark asphalt and bright building faces in the same frame.

---

## Rebuilding it

```
Tools > Mini GTA > 1e. Prepare UI Kit Sprites     (measures and writes the borders)
Tools > Mini GTA > 6b. Rebuild Interface Only     (new in this phase)
```

**`6b` is new and it is the reason this phase was cheap.** The HUD is built by
`SceneAssembler.Assemble`, which also destroys and rebuilds the player, the camera, the lighting
and the ocean — so re-skinning used to mean re-running the whole 17-step pipeline and re-checking
everything Phases 9 through 12 established. `6b` throws away the canvas, builds a new one, and
re-points the four references that live outside it (`PlayerController.Hud`,
`PlayerVehicleController.Hud`, `PlayerCombat.Hud`, `MissionHud.Hud`).

Those four are the entire external surface of the HUD; everything else on the canvas is found at
runtime by type. **If a future screen adds a fifth, it has to be added to `RebuildInterface` too**
— a dangling HUD reference is silent, exactly like the null police prefabs in Phase 9 were.

---

# Phase 13b addendum — responsiveness

Reported after the re-skin: the main menu "does not fit correctly on mobile screens — elements
are overflowing, cropped, or misaligned on different device aspect ratios."

It was, and the reason is worth recording, because **the whole of Phase 13's layout verification
was blind to it**.

## Why Phase 13 could not see this

`SafeAreaFitter` runs in `Awake` and `Update`. Neither exists in edit mode. So every capture
Phase 13 took — three aspect ratios, carefully compared — was of a canvas with **no notch inset
at all**. On a device that reports insets the usable canvas is materially smaller than anything
that was ever photographed, and the code had never been asked to survive it.

The Device Simulator reports insets. That is what the report was looking at.

## The headline defect

**The lobby backdrop and every modal scrim stopped at the safe area.** They are children of the
`SafeArea` container along with everything else, so on a phone that reports a 141 px cutout the
starfield was drawn 110 canvas units short of the screen on each side and the inset showed as a
flat grey band down both edges and along the bottom. Same for the pause, settings, shop, store,
dialogue, character and profile dimmers.

Backgrounds want the *opposite* of a safe area from controls: controls must stay inside the inset
or they clip, a backdrop must reach past it or the screen shows through. `SafeAreaBleed` (D51)
splits those two jobs.

## Everything the sweep found

`Tools > Mini GTA > 6c. Test UI Responsiveness` (new — see D52) measures the interface against
ten screen shapes, simulating both the render size, which drives `CanvasScaler`, and the
safe-area inset, which drives the container. Before / after:

| | Before | After |
|---|---|---|
| Elements outside the safe area | 10 | **0** |
| Text clipped by its own box | 20 | **0** |
| Lobby element collisions | 0 | **0** |
| Touch targets under 7 mm | 300 | 275 (see below) |

The individual defects:

- **The version label hung 126 units off the right edge of the screen on every single shape.**
  `Caption()` point-anchors with a centred pivot, so a 300-wide label placed 24 units from the
  right corner put half of itself past it. It had been there since Phase 12 and no capture ever
  caught it because it renders empty until a build number is set.
- **The ammo counter clipped its own digits** — 32 units of box for a 39-unit line at 34 pt.
- **The speedometer clipped its own digits** — 74 units of box for a 79-unit line at 62 pt.
  Both are the display face being taller per point than Carlito was; the boxes were sized for
  the old font in Phase 11 and never re-measured after the face changed.
- **The character plate and PLAY were fixed at 600 and 520 units.** Not a clipping bug — a
  proportion one, and exactly what instruction 4 describes. On a short 21:9 phone the plate kept
  its width while its height shrank, so the character sat in a band of dead plate; on a 4:3
  tablet the reverse. Both now hang off a hero column whose width is derived from the height
  available to it (D53).

## Instruction 1: the reference resolution stays at 1920x1080

The brief suggested changing it to ~1503x755. That was measured rather than argued, by running
the sweep against each candidate:

| Reference | Match | Overflow | Clipped | Collisions | Under 7 mm |
|---|---|---|---|---|---|
| **1920x1080** | **0.65 (kept)** | **0** | **0** | **0** | 275 |
| 1920x1080 | 0.50 | 0 | 0 | 0 | 271 |
| 1920x1080 | 1.00 | 0 | 0 | 0 | 295 |
| 1503x755 | 0.65 | **185** | 0 | 0 | 203 |
| 1503x755 | 1.00 | **142** | 0 | 1 | 230 |

A smaller reference makes everything render physically larger — which is why its touch-target
count improves — but every layout in this project is authored in 1920x1080 units, and at
1503x755 the canvas shrinks to 1614x726 where an 800-tall pause card simply does not fit. It
trades 185 elements running off the screen for 72 slightly larger buttons. The underlying
concern is real and is answered directly by raising the touch targets instead.

`match 0.65` is kept as well: it is within noise of 0.50 on every count and it biases toward
height, which is what keeps the thumb clusters the same size across aspect ratios (the Phase 8
rationale).

## Instruction 7: there is no portrait to test

`allowedAutorotateToPortrait` and `...UpsideDown` are both **false**; both landscape orientations
are true. The OS never hands this UI a portrait window, so a portrait layout would be dead code —
and the gameplay HUD's thumb clusters assume landscape throughout. The matrix is ten landscape
shapes from 1.33:1 to 2.37:1 instead, which is the range the game can actually be handed.

## Instruction 9: touch targets

Measured in millimetres, not pixels: canvas units x scale factor / dpi x 25.4. On a 20:9 phone
at 395 dpi, 7 mm is 101 canvas units.

The main menu now passes:

| Control | Size | |
|---|---|---|
| PLAY | 124 units | **8.6 mm** |
| Rail: Character / Profile / Settings / Exit | 112 units | **7.8 mm** |
| Profile name chip | 104 units (was 84) | **7.2 mm** (was 5.8) |
| Wardrobe tile | 240 units | **16.7 mm** |
| Name field inside the chip | 88 units (was 52) | 6.1 mm — nested inside the 7.2 mm chip |

Three gameplay HUD buttons were raised at the same time, because they are corner-anchored with
nothing packed against them and the change costs no layout: **pause 78 -> 104 units** (5.4 ->
7.2 mm), **store and lose-heat 96 -> 104** (6.7 -> 7.2 mm).

**Not fixed, and deliberately so:** the buttons on the pause, settings, shop, store and dialogue
cards are 62-74 units, which is 4.3-5.1 mm. They are below the guideline. Raising them means
re-fitting five card layouts against the kit's painted frames, which is the work Phase 13 just
finished doing and is a bigger change than this request. The numbers are in the sweep output and
`6c` will keep reporting them.

## Verification

Ten shapes, each simulating render size *and* safe-area inset:

```
vivo I2019 (project device)   2318x1080  2.15:1   canvas 2170x1011  safe 2170x1011
20:9 phone                    2400x1080  2.22:1   canvas 2220x999   safe 2220x999
19.5:9 phone                  2340x1080  2.17:1   canvas 2183x1008  safe 2183x1008
16:9 phone                    1920x1080  1.78:1   canvas 1920x1080  safe 1920x1080
16:9 low-end                  1280x720   1.78:1   canvas 1920x1080  safe 1920x1080
4:3 tablet                    2048x1536  1.33:1   canvas 1593x1194  safe 1593x1194
21:9 ultrawide                2560x1080  2.37:1   canvas 2315x977   safe 2315x977
iPhone-class notch            2778x1284  2.16:1   canvas 2181x1008  safe 1960x959
punch-hole, one side          2400x1080  2.22:1   canvas 2220x999   safe 2118x999
gesture bar + cutout          2340x1080  2.17:1   canvas 2183x1008  safe 2016x963
```

`overflow 0, clipped text 0, collisions 0` on all ten. Frame audit still 0 at zero tolerance.
Console 0 errors, 0 warnings. Captures taken at the notch, cutout, 20:9, 4:3 and 21:9 shapes,
plus the pause menu over a notch to confirm the scrim reaches the screen edge.

## Still not answered by any of this

- **None of it has been on a phone.** The simulator's safe area is a model of a device, not a
  device. `renderOutsideSafeArea` is `false` in this project, which means the real Android build
  is letterboxed by the OS and reports a full-screen safe area — the easy case. The inset path
  now works, but it is exercised on hardware only by turning that setting on, or by shipping to
  a device that reports insets anyway.
- **The physical size of 14-unit text** is still the open readability question from Phase 13, and
  the sweep does not measure text legibility, only whether a box clips.
