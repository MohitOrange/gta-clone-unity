# PROJECT: Mini GTA (Unity 6 / URP, mobile-first)

## STANDING RULES — READ BEFORE EVERY SESSION

### 1. Autonomy Rule
- Never pause to ask me clarifying questions mid-task.
- If a decision is ambiguous, choose the most sensible default, 
  implement it, and log the assumption in DECISIONS.md.
- Only stop and ask if a choice is irreversible AND high-cost 
  (e.g. "delete all save data").

### 2. Session Continuity
- Before doing anything else, read PROJECT_STATE.md, DECISIONS.md, 
  and PHASE1.md–PHASE7.md (and any later phase files) for full context.
- Before ending a session (or before context runs low), update 
  PROJECT_STATE.md with: what's done, what's in progress, exact 
  next step, any blockers.
- Work in small, self-contained units (one system/feature/bugfix per 
  session) so a cut-off session doesn't leave broken half-work.

### 3. Quality Bar (no prototypes)
- Every feature must be built in two passes: 
  (1) functional pass, (2) polish pass — edge cases, error 
  handling, visual/UX juice, performance.
- Before marking anything "done": run/compile it, test it against 
  the acceptance criteria given in the task, and report pass/fail 
  per criterion. Don't self-report "done" without this.
- No placeholder logic left in "final" code without an explicit 
  TODO flag and my sign-off.

### 4. Pre-Flight Checks
- At the start of any build/run task: verify folder structure 
  (Assets/Scripts, Assets/Scenes, Assets/Prefabs, Assets/Art, 
  Assets/Audio), confirm disk space, confirm required Unity 
  packages/plugins are installed. Fix or create what's missing 
  before proceeding, and log it.

### 5. Platform Priority
- PRIMARY target: Android (mobile APK). All UI/UX/input decisions 
  optimize for touch and mobile screens first.
- SECONDARY target: Desktop.
- Build and test an APK after every major feature — do not wait 
  until the end. Editor-only testing is NOT acceptable verification 
  for UI, input, or performance claims — this project has already 
  been burned by that once (see PHASE8 Canvas/UI scaling bug).
- Mobile-specific requirements: Canvas Scaler set to "Scale With 
  Screen Size," safe-area handling for notches on EVERY UI screen 
  (not just HUD), touch input via Unity's Input System, ETC2 texture 
  compression, IL2CPP backend, ARM64 only.

### 6. Asset Workflow
- Before writing gameplay code for a new feature, produce a list 
  of required assets (models, sprites, sounds, fonts) with 
  suggested free/low-cost Unity Asset Store sources or specs for 
  placeholders.
- Give me that list so I can download/import — do not generate 
  custom art/audio assets yourself unless explicitly told to.
- Build shared/common systems (one PlayerController, one 
  InteractableBase, etc.) — avoid duplicated logic per object.
- Maintain ARCHITECTURE.md with a system map before implementation 
  of any new major system.

### 7. Theme & UI Consistency
- Color palette: [FILL IN — e.g. #1A1A2E, #E94560, #0F3460]
- Font: [FILL IN font name(s)]
- Never use default Unity UI buttons/panels — always use themed 
  prefabs from Assets/UI/Prefabs.
- All new UI must reuse existing theme prefabs/styles, not invent 
  new ones, unless told otherwise.
- All UI elements must be anchored/sized relatively (not hardcoded 
  pixels) so they scale correctly across screen sizes and aspect ratios.

### 8. Testing Checklist (apply to every mechanic and every UI screen)
- Works correctly on touch input AND controller/keyboard.
- No double-triggers, no clipping/collision bugs, no null refs.
- Tested at minimum 2 different screen resolutions/aspect ratios.
- Tested in an actual Android build on a physical device, not just Editor.
- For UI: no overlapping/cut-off elements, no text rendering artifacts, 
  safe-area respected.

### 9. Notifications (optional, only if webhook configured)
- On completing a major milestone (feature done, build succeeded, 
  build failed), send a status message to: [WEBHOOK_URL]
- Do not notify on minor sub-steps — major milestones only.

### 10. Monetization
- Ads and IAP already exist (Phase 6). Do not modify monetization 
  code unless a task explicitly says so.

## KNOWN PROJECT FACTS (do not rediscover these — read the phase docs)
- Unity 6 (6000.5.8f1), URP, single-scene design (Assets/Scenes/City.unity).
- Input System package only — legacy Input.GetAxis will throw.
- Save file lives at Application.persistentDataPath, currently format v3.
- Renderer count and performance tiers are documented in PHASE7.md — 
  read before touching performance code.
- Full history of bugs already found and fixed is in PHASE1–7.md — 
  check there before "discovering" a bug that was already solved.
