# Lights Out — Unity version

A Unity port of the browser game: all 40 circuits, with measured elevation on 32 of them. It has 2026-style energy (ERS store, boost, overtake mode, harvesting, recharge map), active aero, Soft/Medium/Hard tyres with wear, pit stops, and race control (yellow/double-yellow, VSC, Safety Car, blue flags, track limits, penalties). You can race solo or in online rooms, which can be public or private.

Everything is built procedurally at runtime: terrain, track, kerbs, barriers, grandstands, gantry, marshal posts, cars and HUD. There are no prefabs or art assets to import.

## Setup (about 5 minutes)

1. **Create a project.** In Unity Hub, create a **Unity 6 (6.3 LTS or newer)** project with the **Universal 3D (URP)** template. The built-in 3D template also works, but URP looks better.
2. **Copy the code.** Copy the `Assets/LightsOut` folder from this zip into your project's `Assets` folder.
3. **Install packages.** Open *Window → Package Manager → + → Install package by name* and add:
   - `com.unity.inputsystem`. When Unity asks to enable the new input backends, click **Yes**. It restarts the editor.
   - `com.unity.netcode.gameobjects`
   - `com.unity.services.multiplayer`
4. **Link Unity Cloud (needed for online only).** Open *Edit → Project Settings → Services* and link the project to a Unity Cloud project. Single-player works without this step.
5. **Build the scene.** Run the menu item **Lights Out → Set Up Scene**. It creates `Assets/LightsOut/LightsOut.unity` with the game controller, NetworkManager + UnityTransport, sun and camera. It also adds the scene to Build Settings.
6. Press **Play**.

> Building a player: add `Universal Render Pipeline/Particles/Unlit` and `Universal Render Pipeline/Unlit` to *Project Settings → Graphics → Always Included Shaders*. These shaders are created from code, so the build would otherwise strip them.

## Online rooms

- **Host public room** creates a room that shows in everyone's room list. Use **Refresh** to update the list.
- **Host private room** creates a room that stays hidden. Share the room code, and friends type it in and click **Join code**.
- In the lobby, each driver picks a **starting tyre** and marks themselves Ready. The host picks the circuit, laps, AI count and incidents, then clicks **Start race**.
- The host runs race control: AI, flags, VSC/SC and penalties. Each player simulates their own car and streams it at 30 Hz. Remote cars are shown about 110 ms behind for smooth interpolation.
- The host must keep the game running. The setup step turns on *Run In Background* for this reason. Up to 10 players per room, connected over Unity Relay (no port forwarding).

## Controls

| Action | Keyboard | Gamepad |
|---|---|---|
| Drive | Arrows / WASD | Left stick, RT / LT |
| Boost (deploy) | B | X / Square |
| Overtake mode | O | |
| Energy map (balanced / recharge) | E | |
| Box this lap | P | |
| Next tyre | 1 / 2 / 3 (Soft / Medium / Hard) | |
| Camera (cockpit / chase / top-down) | C | |
| Reset car to track | R | |
| Pause (single player) | Esc | |

## Code layout

- `Scripts/Core`: track data, the circuit builder (centreline, elevation, grade, crest/compression, racing line and AI speed profile), and constants.
- `Scripts/Sim`: the race simulation. This is a direct port of the web version and is engine-independent.
- `Scripts/View`: procedural meshes and textures, the track scenery builder, the car model and the camera rig.
- `Scripts/Net`: Multiplayer Services sessions (public/private rooms) and Netcode named messages for race sync.
- `Scripts/Game`: the game flow, the fixed 120 Hz sim loop and the IMGUI menus/HUD.
- `Editor/LightsOutSetup.cs`: the one-click scene setup.

## Status

- **Tested outside Unity:**
  - The simulation (Core + Sim) was compiled and run headless on all 40 circuits. Lap times and strategy match the web version.
  - The whole project type-checks against API stubs.
- **Not tested inside the Unity editor:** rendering, networking and UI. Expect a few small fixes on the first open. If anything fails to compile, the console error will point at the line.

## Credits

- Circuit outlines: [bacinger/f1-circuits](https://github.com/bacinger/f1-circuits) (MIT).
- Elevation: aligned from public F1 timing telemetry ([TracingInsights](https://github.com/TracingInsights)).

This is an unofficial fan project, not affiliated with Formula One Licensing B.V. or the FIA.
