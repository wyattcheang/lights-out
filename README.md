# Lights Out

First-person F1-style racing on all 40 circuits from bacinger/f1-circuits, 24 current and 16 past venues. 32 of them have measured elevation. The game has two front ends:

- **web/**: a single-file browser game (Three.js r128). It supports public and private online rooms through the claude.ai Artifact `room` capability.
- **unity/**: a Unity 6 port of the same simulation. Online rooms use Unity Multiplayer Services (Relay) and Netcode for GameObjects. See `unity/LightsOutUnity/README.md`.

## Features

- **Car and energy:** 2026-style power unit (400 kW engine + 350 kW ERS, 4 MJ store). Deployment tapers at speed. It has overtake mode, harvesting under braking and lift-and-coast, and a recharge map.
- **Aero and tyres:** active aero has straight and corner modes. Soft, Medium and Hard tyres have wear, a performance cliff and warm-up. You pick your starting tyre before the race.
- **Pit stops:** pit lane with a speed limiter and garages. Using two compounds is mandatory.
- **Race control:**
  - Yellow and double-yellow flags by marshal sector.
  - VSC with a delta, and a Safety Car with the "SC in this lap" call and a restart.
  - Blue flags, and track limits (warnings, then black-and-white flag, then a 5 s penalty).
  - Penalties for overtaking under caution, and points.
- **Elevation:** the track surface follows measured height. Slopes change speed through gravity, and crests and compressions change grip through vertical load.

## Layout

```
web/src/       game modules (core, sim, render2d, render3d, hud, net, main) + head/body HTML
web/build.py   bundles src + data/tracks3.json -> web/dist/lights-out.html (single file)
web/tests/     headless Playwright checks: sim3.js (all-track race sim), mp.js (two-client online test, mockroom.js fakes the room API)
data/          tracks.json (original outlines), tracks3.json (re-ordered + elevation, used by the game), elev.json, f1.geojson
tools/elevation/  fetch_tel.py + align.py: pull F1 telemetry laps and align them to the outlines (rotation search + ICP)
tools/carmodel/   build_car.py: turns the source car model into web/src/carmodel.js (decimate, split parts, quantize)
unity/LightsOutUnity/  Unity project assets (Assets/LightsOut) and setup guide
```

## Build and test (web)

```
python web/build.py                      # -> web/dist/lights-out.html, web/tests/test*.html
cp node_modules/three/build/three.min.js web/tests/   # npm i three@0.128.0 playwright
node web/tests/sim3.js                   # race sim across every circuit
(cd web/tests && python -m http.server 8765) & node web/tests/mp.js   # two-client online test
```

## Credits

- Circuit outlines: [bacinger/f1-circuits](https://github.com/bacinger/f1-circuits) (MIT).
- Elevation: aligned from public F1 timing telemetry published by [TracingInsights](https://github.com/TracingInsights).
- Car model: ["F1 2026 concept (polygon model)"](https://sketchfab.com/3d-models/f1-2026-concept-polygon-model-ea3bde709b1e4dc9b0ec8557d106ed42) by [Qvist_designs](https://sketchfab.com/Qvist_Designs), licensed [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/). The web build uses a decimated copy split into paint, carbon and moving parts (`tools/carmodel/build_car.py`).

This is an unofficial fan project, not affiliated with Formula One Licensing B.V. or the FIA.
