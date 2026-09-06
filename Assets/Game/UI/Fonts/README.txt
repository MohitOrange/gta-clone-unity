DROP THE INTERFACE FONT IN HERE
===============================

Phase 13 re-skinned the whole interface and was asked to set every piece of text in the
"Fatality" FPS gaming font. That font is not in this project and is not something the build
can generate or fetch, so UiTheme treats this folder as a slot instead of hard-coding a path.

To install it:

  1. Drop Fatality.ttf (or .otf) into this folder. Any filename works as long as it
     contains the word "fatality" -- e.g. "Fatality.ttf", "fatality-regular.otf".
  2. Let Unity import it.
  3. Run  Tools > Mini GTA > 10. Build Menus and Audio   (or BUILD EVERYTHING).

Every Text component in the game is rebuilt against UiTheme.Font, so that one drop re-faces
the HUD, the lobby, the menus, the shop and the pause screen together. Nothing else changes.

RESOLUTION ORDER (UiTheme.Font)
  1. a .ttf/.otf in this folder whose name contains "fatality"
  2. any other .ttf/.otf in this folder
  3. Righteous-Regular, shipped inside the Space Exploration GUI Kit   <-- in use today
  4. Unity's built-in LegacyRuntime.ttf

The build log names whichever face won, so a pass can never report the intended font as
being in use when it is not. Look for the line beginning "[UI] Interface face:".

ONE FONT ONLY
  DECISIONS D30 bounds the runtime dynamic-font atlas at one face and seven point sizes,
  because PHASE8 measured a device-side atlas repack caused by 22 distinct sizes. Do not add
  a second face here expecting both to be used -- only one is picked, on purpose.
