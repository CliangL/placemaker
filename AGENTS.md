# Placemaker: start here

Placemaker is a Cities: Skylines II (1.6.2f1) code mod. The player selects an area, picks a type and a preset (style),
previews it and builds it: roads, parking lots, parks, shops. It merges ParkingLotTool (KruemelGames), ParkManager
(tomkpunkt) and Grid Road Generator (Ti4goc), so it is GPL-3.0 and credits all three.

- `Core/`: the pure planner (.NET + Unity.Mathematics only), tested by `Tests/Placemaker.Tests` (xUnit, net9).
- `Game/`: the ECS game layer (tool, preview, build pipeline in `Game/Build/`).
- `UI/`: the React panel; `UI/src/contract.ts` is authoritative.
- `Lang/`: one strings file per area.
- `Recipes/`: the shipped types.

## Direction: tall and perfect, not wide

The focus, in order, is **Roads, Parking, Parks, Shops & squares**. Make those excellent before anything else.
Industry and Transport are hidden from the picker, and Gardens/Water fold into Parks while Squares/Events fold into
Shops. The code stays (`MERGED_GROUPS` / `SHELVED_GROUPS` in `UI/src/stages/PresetBrowser.tsx`). Hide things; don't
delete code.

## Read first

- `docs/ARCHITECTURE.md`, `docs/DESIGN.md`: how it is built and how it should feel.
- `docs/RESEARCH-NOTES.md`, `docs/RESEARCH-NOTES-ADDENDUM.md`: game engine rules (decompiled). A mandatory checklist.
- `suite/README.md`: the game runner (below).
- `docs/teardown/`: what went wrong and why. Start with `00-user-feedback-1215.md`, then `07` (shop defects), `08`
  (street zoning), `09` (switching freezes).
- `.forensics/suite/scrutiny/STATUS.md` (git-ignored, local): the state of the game runner and its known gaps.

## The game runner: close the loop yourself

Use it on every change before telling the user anything works. Its main purpose is to run the real game so **you
read the logs**. Pictures come second; the PASS/FAIL checks are helpers, and UNKNOWN means "not measured".

```
powershell -File tools/suite/Play.ps1                     # the user's playground: flat grid, camera on it, they play
powershell -File tools/suite/Run-Suite.ps1 -Set look-one  # one type and preset, one 45° picture, quit
powershell -File tools/suite/Run-Suite.ps1 -Set look      # every preset of a type, one 45° picture each
powershell -File tools/suite/Run-Suite.ps1 -Set queue -Queue   # keep the game open, then:
powershell -File tools/suite/Send-Suite.ps1 -File x.json        # feed it builds without relaunching
```

- Results go to `.forensics/suite/runs/<id>/`: `logs/Placemaker.log`, `logs/Player.log`, `suite.log`,
  `summary.md`, `contact.html` and `shots/`.
- One game at a time. Never close a game the user started.
- A code change needs a rebuild and a relaunch (about 60 s to a ready test world).
- After a cut-off run, `Run-Suite.ps1 -RestoreOnly` puts the user's game settings back.

## Build and test

- `dotnet build Placemaker.csproj -c Release`: builds the UI and installs into
  `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\Placemaker`. The game must be closed.
- `-p:Deploy=false -p:SkipUI=true`: a compile check only.
- `dotnet test Tests/Placemaker.Tests --filter ...`: the full run takes 20+ minutes, so run what you touch.
- When the user plays, the logs are in `...\Cities Skylines II\Logs\Placemaker.log` and the build audits in
  `...\ModsData\Placemaker\audit\`.
- Decompiled game source: `C:\Users\jack\AppData\Local\Temp\pm-decomp\game`. It is a temp folder; `ilspycmd` is
  installed to regenerate it from `Cities2_Data\Managed\Game.dll`.
- There is no `python` on this machine; use node for scripting.

## Known mod bugs (found by the game runner, not fixed yet)

- After a build, trees, bushes and lamps exist but are never drawn (about 300 of 650 pieces in a parking lot).
- Vanilla "Agriculture Lot" fields get placed inside parking and shop builds, and their fences block entrances.
- Strip-mall stalls can't be reached from the city. Pits 6–9 m deep in strip malls.
- Grown shops face the city street instead of our lane. The fix is the game's per-side "no zoning" road flag
  (teardown report 08).
- Build freezes of 1.4–2.7 s. Switching freezes come from decal copies forcing asset catalog rescans (report 09).
- The roads presets strict-grid and superblocks build nothing.

## How the user works

- They are not a programmer. Use plain language and short answers.
- Never say something is fixed unless you saw it work in the game **and** it is installed. Compare the time on
  `Mods\Placemaker\Placemaker.dll` with your commit.
- Fix things everywhere they occur: every type and code path that shares the mechanism, not just the one in the
  screenshot.
- Helper agents use the strongest model. Commit when a piece of work is done; the user tests in game.
