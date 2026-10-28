# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its features and
controls; this file covers how the code is put together and the rules that keep it working.

## What this is

A recreation of Atari's 1980 arcade game Missile Command, in C# on .NET 10 and Avalonia 12. Defend six cities from
three missile bases with a crosshair driven by mouse, keys or gamepad. One self-contained executable per platform
(Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code, and the sounds are synthesised by
`tools/make_sounds.py`.

This is an older, simpler layout than the author's later AVA games: a single project at the repository root, no
`global.json` or `Directory.Build.props`, and no test project.

## Commands

```bash
dotnet build AVAMissile.slnx                      # needs the .NET 10 SDK
dotnet run --project AVAMissile.csproj            # play it (opens a window)
python3 tools/make_sounds.py                      # rebuild Sounds/ (standard-library Python only)
```

There are no tests yet. The project enables nullable reference types but does not treat warnings as errors.

## Layout

- `AVAMissile.csproj`, `AVAMissile.slnx`, `Program.cs`, `App.axaml`, `MainWindow.axaml`: the app shell.
- `Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` has the states (start screen, attract, play, pause, game over, initials, high scores), the bases, cities, crosshair
    and counter-missiles, and scoring; `GameEngine.Wave.cs` has the waves: warheads, splitting MIRVs, bombers,
    satellites, smart bombs, fireballs, the tally and THE END.
  - `AttractPilot.cs` is the computer player for attract mode.
  - `InputHandler.cs` reads keys, mouse buttons and the gamepad; `ControllerManager.cs` wraps SDL2.
  - `SoundManager.cs` plays the WAV files in `Sounds/` through the `NetCoreAudio` package, including looping
    sounds and the mute toggle (M).
  - `HighScoreTable.cs` and `InitialsEntry.cs` do what their names say.
- `Models/Entities.cs`: the playfield measures (`Field`, a fixed 1024 x 768 screen) and the bases, cities,
  missiles, warheads and fireballs.
- `Views/`: `GameCanvas` (split over partial files: battle, screens and drawing primitives) draws the frame,
  scales it to the window, and runs the game from a `DispatcherTimer` of about 16 ms. Ctrl+S saves a screenshot
  to the Pictures folder.
- `Sounds/`: the generated WAV files, copied to the output folder.
- `Images/`: the README's screenshots.

## Rules that keep it working

- **Variable time step.** The engine is advanced by the real time since the last tick (`GameEngine.Update`),
  capped at 0.1 s. There is no fixed-step loop, and random numbers come from an unseeded `static Random`, so runs
  are not reproducible.
- **Engine first, drawing second.** Gameplay belongs in `Engine/` and `Models/`; `Views/` reads the engine and
  draws it.
- **Input goes through `InputHandler`.** For the crosshair, down is positive Y: Up subtracts and Down adds.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`.
- **High scores** live in the app-data folder (`AVAMissile`).
- **Line endings.** Some files are CRLF (`App.axaml`, `App.axaml.cs`, `MainWindow.axaml.cs`, `Program.cs`,
  `Engine/ControllerManager.cs`); the rest are LF. Keep each file's existing endings so diffs show only real
  changes.

## Making a change

- `dotnet build` must pass. With no tests, try the change in the game itself.
- Update `README.md` (features, controls) and, if the screen changes, the pictures in `Images/`.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds every push and pull request (there are no tests to run). It then publishes
self-contained single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps
the builds as workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*`
tag makes a proper release; either then asks the portfolio site to rebuild.
