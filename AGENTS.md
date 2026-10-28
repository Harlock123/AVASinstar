# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its zones, the
controls and the Linux sound and gamepad requirements; this file covers how the code is put together and what to
watch for.

## What this is

A recreation of Williams' 1983 arcade game Sinistar, in C# on .NET 10 and Avalonia 12. One self-contained
executable per platform (Windows, macOS and Linux, x64 and ARM). The sounds are synthesised by the scripts in
`tools/`. It is laid out more simply than the newer AVA games: one project at the repository root, no test project,
no `global.json` or `Directory.Build.props`, and sound through NetCoreAudio rather than OpenAL.

## Commands

```bash
dotnet build AVASinstar.slnx                      # needs the .NET 10 SDK (AVASinstar.csproj targets net10.0)
dotnet run                                        # play it, from the repository root; there are no command-line options
python3 tools/make_sounds.py                      # rebuild the effects, roar and scream in Sounds/ (standard-library Python only)
tools/make_voice.sh                               # rebuild the spoken lines (Sounds/Voice*.wav); needs Piper, see below
```

There are no tests yet.

`make_voice.sh` speaks the lines with the Piper text-to-speech engine and the `en_GB-vctk-medium` voice, then runs
them through `tools/sinistar_voice.py`, which pitches them down and emulates the cabinet's CVSD speech chip. Set
`PIPER` and `VOICE` to point it at the Piper binary and the voice model.

## Layout

- `Engine/`:
  - `GameEngine.cs` runs the states, the ship, shooting and scoring, and holds `ShipControls` (a stick vector plus
    fire and bomb).
  - `GameEngine.Enemies.cs` lays out each zone and runs the workers and warriors; `GameEngine.Sinistar.cs` builds,
    wakes and destroys the Sinistar.
  - `AttractPilot.cs` flies the ship in the silent demonstration.
  - `InputHandler.cs` turns keys, mouse and gamepad into the stick; `ControllerManager.cs` reads SDL2 gamepads.
  - `SoundManager.cs` plays the effects, the heartbeat loop and the Sinistar's voice through NetCoreAudio.
  - `HighScoreTable.cs` and `InitialsEntry.cs` do what their names say.
- `Models/`: the ship, planetoids, crystals, workers, warriors, Sinibombs and the Sinistar (`Entities.cs`),
  explosions, and `World.cs`, the sector that wraps at its edges.
- `Views/`: `GameCanvas`, split over partial files for space, the screens and drawing helpers, draws the 1024x768
  frame (scaled to fit the window) and runs the game loop. Ctrl+S saves a screenshot to the Pictures folder.
- `Sounds/`: the generated WAV files, copied next to the executable. `Images/`: the README's screenshots.

## Things to know before changing it

- **Variable time step.** A `DispatcherTimer` ticks about every 16 ms; `GameCanvas` measures the real time since
  the last tick, caps it at 0.1 s and passes it to `GameEngine.Update(deltaTime)`. Keep new movement scaled by
  `deltaTime`.
- **Randomness is unseeded.** `GameEngine` and `Explosion` use a shared `new Random()`, so games are not
  reproducible. Only the starfield uses a fixed seed (`Random(1983)` in `GameCanvas.Space.cs`).
- **Stick direction.** `InputHandler.Stick` is a vector with negative Y meaning up the screen, from keys, d-pad,
  analog stick or the mouse pointer. Keys and gamepad take over from the mouse.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. Keep it that way
  if you touch it: unlocked SDL calls from two threads corrupt its heap.
- **Sound fails silently.** On Linux NetCoreAudio runs `aplay` for each sound, and `SoundManager` swallows playback
  errors, so a missing `aplay` means no sound and no error.
- **High scores** live in the app-data folder (`AVASinstar`).
- **Line endings.** A few files (`App.axaml`, `App.axaml.cs`, `MainWindow.axaml.cs`, `Program.cs` and
  `Engine/ControllerManager.cs`) use CRLF; the rest use LF. Keep each file's existing line endings so diffs show
  only real changes.
- **Warnings are not errors** here, and nullable reference types are on.

## Making a change

- Build, then play it to check the change: there are no tests to catch regressions.
- Update `README.md` (features, controls, screenshots) to match.
- New sounds go into `tools/make_sounds.py` (or `make_voice.sh` for speech), not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds the project on every push and pull request (there is no test step). It then
publishes self-contained single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run
keeps the builds as workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a
`v*` tag makes a proper release. After a release it asks the portfolio site to rebuild, if the
`SITE_REFRESH_TOKEN` secret is set.
