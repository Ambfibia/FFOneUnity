# Character UI and default display sizes

The character selection and creation screens keep the source artwork and logical
681-pixel reference height. Their uniform scale now also fits the viewport width,
so the full composition stays visible at 16:9, 16:10 and in narrower windows.
The deletion dialog uses the same scale and stays centered after resizing.
Roster and HUD portraits frame the head; the main preview retains its full-body
camera. An empty roster hides Enter Game.

New preferences and Graphics → Default use **1920×1080** on 16:9 displays and
**1920×1200 (WUXGA)** on 16:10 displays. Both choices appear in the resolution
list, which scrolls when necessary. Existing saved resolutions remain intact.
Explicit player arguments `-screen-width`, `-screen-height` and
`-screen-fullscreen` take precedence over the initial saved/default resolution.
The fullscreen button also saves its chosen mode.

Creation fixes include checkbox proportions, the text beside each checkbox and
sentence-case name rules. Tutorial cinematic captions separate the speaker from
the dialogue. The location caption uses the lower-left anchor; the skip prompt
is centered at the top. Space and Escape skip a cinematic.

## Verification

The 2026-10-17 run passed **62/62 focused Editor tests** and both final player
viewports, with **18 PNG captures**. The final player also checks that gender
captions fit their actual content width. Its runtime SHA-256 is
`22e28718f0ab2935acdc94b4f2574c8bc611362912cb747b81822646f570239a`.
Both player viewports were repeated successfully on this final shared combat/buff
build. The final gameplay, presentation, weapon and buff Editor regression suite
also passed **328/328**, with no skipped tests, in
`Logs/CombatBuffEditorTests-20261017.xml`.

`Logs/FrontendFullHdTests.xml` records the focused Unity Editor checks for layout,
character creation, preferences, settings views, native views and tutorial
contracts.

After building the Windows player, run:

```powershell
python Tools/run_frontend_ui_validation.py
```

This opt-in development-player check uses an offline preview roster. It does not
log in, save a character or submit a deletion. It exercises the runtime UI panel,
checks button hit targets and captures these views at 1920×1080 and 1920×1200:

- Login and empty/occupied character selection.
- Delete confirmation with incorrect and correct first names, followed by Cancel.
- Generated and custom names.
- Male and female appearance panels.
- Quit menu and Cancel.

Captures and player logs are saved under `Logs/FrontendFullHd/<width>x<height>/`.
The runner verifies every PNG's dimensions and writes `validation.json` only
after both viewports pass. Cinematic caption changes are covered by compilation
and the tutorial contract tests; this offline frontend run does not play a
tutorial cinematic.
