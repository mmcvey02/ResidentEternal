# Raccoon City Skylines

**Cities: Skylines x Resident Evil 2 (2019): a two-game passthrough mashup.** Both games run at the same time, and a
mod in each lets them talk:

- **Your city becomes RE2's skyline.** Cities: Skylines renders your city from RE2's camera, and a ReShade add-on
  draws it into RE2's sky. Your towers rise above the RPD as you look up from the streets of Raccoon City.
- **RE2 fights your city's T-virus outbreak.** The outbreak spreads through your districts. Police and healthcare
  slow it, blackouts and commuters spread it, and every zombie killed in RE2 pushes it back in the district
  where RE2 is anchored. Mr. X and G cause surges.
- **The city shapes RE2.** The district's infection raises RE2's own dynamic difficulty (the game rank), and a
  blackout in the city darkens RE2.
- **RE2 shows up in the city.** RE2's picture appears as a bodycam panel while you build, and the survivor is a
  marker on your map.

This repurposes the "passthrough" method behind the September 2026 game-mashup videos (Minecraft inside GTA V
and Elden Ring), from Rehan Sheikh's open-source
[`universal-modder`](https://github.com/rehan-remade/universal-modder). **[docs/HOW_IT_WORKS.md](docs/HOW_IT_WORKS.md)**
explains that technique and maps each of its tools to the ones used here.

```
Resident Evil 2 (host renderer)                          Cities: Skylines (guest + city simulation)
  RaccoonSkylines.dll  REFramework plugin  -- TCP 127.0.0.1:25600, JSON lines -->  LinkServer
                                           <-------------------------------------  (city state, waves, blackouts)
                       ReShade add-on      <-- shm Local\RaccoonSkylines.City ---  SkylineExporter
                                           --- shm Local\RaccoonSkylines.Bodycam ->  BodycamPanel
  raccoon_skylines.lua  game rank, kills, HUD  <-> reframework/data/raccoon_skylines/*.json <-> the plugin
```

The full wire format is in [protocol/PROTOCOL.md](protocol/PROTOCOL.md).

## What's in here

| path | what |
|---|---|
| `cities/RaccoonCitySkylines/` | The Cities: Skylines mod (C#, ICities + CitiesHarmony). `Bridge` runs everything; `LinkServer` is the link; `CameraSync` + `PoseMapper` drive the camera from RE2; `SkylineExporter` publishes the frame; `OutbreakModel` / `OutbreakController` / `CityProbe` run the outbreak; `BodycamPanel` and `LeonMarker` show RE2 in the city |
| `cities/stubs/`, `cities/tests/`, `cities/build.sh` | Compile-only stand-ins for the game's API (so CI type-checks the whole mod), unit tests, and a stand-in city built from the real `LinkServer` |
| `re2/src/` | The RE2 plugin: `plugin.cpp` (REFramework entry, camera, link, Lua file bridge), `compositor.cpp` (ReShade add-on: city into the sky, bodycam out), `re_camera.cpp`, `common/` (link, frames, JSON; portable and tested on Linux) |
| `re2/shaders/RaccoonSkylines.fx` | The ReShade effect: sky replacement, colour/brightness match, rain haze, outbreak vignette, blackout |
| `re2/lua/raccoon_skylines.lua` | REFramework script: infection to game rank, kill/Mr. X hooks, survivor position and health, HUD, test buttons |
| `tools/` | The Python reference (`rcsk/`), `fake_city.py` and `fake_re2.py` stand-ins, `make_vectors.py`, `check_shader.sh` |
| `tests/` | End-to-end tests: every pairing of C# (Mono), C++ and Python over the real socket and shared memory |

## Requirements

- Windows 10/11, with both games running at once (a mid-range GPU managed MC + GTA V fine).
- **Cities: Skylines** (the 2015 game), with the Harmony mod (Steam Workshop item 2040656402, the dependency
  `CitiesHarmony.API` uses).
- **Resident Evil 2** (2019) on Steam, running in **DX11** (Graphics > Rendering mode). Single-player only.
- **REFramework** for RE2 (`dinput8.dll` + `reframework/`).
- **ReShade 6 with add-on support** (the "with full add-on support" installer) for RE2, with depth enabled.
- To build: the .NET SDK (the mod) and Visual Studio 2022 C++ tools, or MinGW-w64 from WSL (the plugin).

## Build and install

**Cities: Skylines mod** (Windows):
```bat
dotnet build -c Release cities\RaccoonCitySkylines\RaccoonCitySkylines.csproj
```
It builds against your game's `Cities_Data\Managed` (pass `-p:CitiesManaged=...` if the game isn't in the default
Steam folder) and copies itself to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\RaccoonCitySkylines`.
Enable it in Content Manager > Mods. Options are under Options > Raccoon City Skylines.

**RE2 plugin**, from WSL (or `re2\build.bat` with Visual Studio):
```bash
re2/fetch_deps.sh      # REFramework + ReShade API headers into re2/third_party (not committed)
re2/build_mingw.sh     # -> re2/build/win64/RaccoonSkylines.dll
re2/install.sh         # copies the DLL, the Lua script and the shader into RE2's folder (--remove undoes it)
```
In ReShade's overlay: enable **RaccoonSkylines**, and under *Global preprocessor definitions* set
`RESHADE_DEPTH_INPUT_IS_REVERSED=1`. The plugin writes its settings to
`reframework/data/raccoon_skylines/config.json` on first run (port, camera rate, FOV convention, bodycam rate/size).

## Play

1. Start Cities: Skylines and load a city with a police station. **Run it windowed and small**, for example
   960x540. That window is the skyline feed (Unity 5.6 can only read the back buffer synchronously), so smaller
   is faster. Leave it visible, not minimised.
2. Start RE2. The plugin connects by itself, whichever game started first. Cities: Skylines hands its camera to
   RE2 and hides its UI. In RE2, go somewhere with open sky (the RPD's front gate, the streets, the rooftop).
3. Keys in Cities: Skylines: **F9** switches between the skyline feed and building (the bodycam shows while you
   build), **F10** re-centres RE2's current spot onto the anchor, **F11** shows or hides the bodycam.
4. In REFramework's menu, *Script generated UI > Raccoon City Skylines* shows the link and the outbreak, and has
   *Send test kill / Mr. X / G* buttons that exercise the whole loop.

If the towers drift as you turn, set *RE2 vertical FOV override* in the effect, or `fovIsHorizontal` in
`config.json`. If turning left in RE2 turns the city right, untick *Mirror Z* in the mod's options. *Heading* and
*City metres per RE2 metre* aim and stretch RE2 across your city.

## Testing without the games

Most of this was built and tested the way the Minecraft x GTA V project was: against stand-ins, because neither
game runs in CI.

```bash
cities/build.sh            # type-checks the mod against cities/stubs, runs 63 unit tests (Mono)
re2/build_mingw.sh         # cross-compiles the DLL against the real REFramework and ReShade headers
tools/check_shader.sh      # compiles the .fx with ReShade's own FX compiler, built from source
python -m pytest tests     # 12 end-to-end tests across C#, C++ and Python
```

`python tools/fake_city.py` stands in for Cities: Skylines (link, outbreak state, a synthetic skyline frame), and
`python tools/fake_re2.py --snapshot sky.png` stands in for RE2 (orbiting camera, kills, Mr. X). Run either against
a real game to bring up one side at a time.

## What still needs the real games

All the code compiles: the DLL against the real REFramework and ReShade headers, the shader with ReShade's
compiler, the mod against stand-ins of the game API. The link, the frames, the outbreak and the camera mapping
are tested across all three languages. Nothing has been run inside either game yet. Before you trust it, check:

- **RE2 names in `raccoon_skylines.lua`** marked `VERIFY`: the game-rank singleton and field, the player and
  hit-point accessors, and the methods hooked for kills and Mr. X. Look them up in REFramework's Object
  Explorer. A wrong name turns off that one feature with a log line, and the test buttons still work.
- **Cities: Skylines API calls** in `CityProbe.cs`, `CameraSync.cs`, `SkylineExporter.cs` and `LeonMarker.cs`
  (`ImmaterialResourceManager.CheckLocalResource`, `ElectricityManager.CheckElectricity`, `UIView.Show`,
  `ToolManager.EndOverlayImpl`). They follow published CS1 mods, but `cities/stubs/GameStubs.cs` only mirrors
  them, so the real `dotnet build` is the check.
- **Camera conventions:** whether RE Engine's `get_FOV` is vertical, and the Z mirror. Both are settings, see *Play*.
- **ReShade depth on RE2:** the sky mask needs ReShade's generic depth to pick RE2's main depth buffer. Use the
  effect's *Sky mask* debug view.

## Rules this follows

From `universal-modder`'s safety rules: single-player only. RE2 has no online mode to touch. Nothing from either
game ships here: no game files, no decompiled code. The REFramework and ReShade headers are fetched at build
time. The Cities: Skylines mod writes nothing into save games (its outbreak state is a sidecar file per city).
The link listens on 127.0.0.1 only.

## Credits

- The passthrough method and the Minecraft x GTA V reference design: Rehan Sheikh's
  [universal-modder](https://github.com/rehan-remade/universal-modder) (MIT), inspired by chasm's
  Minecraft-in-Skyrim and TobynJacobs' Minecraft-in-Elden-Ring.
- [REFramework](https://github.com/praydog/REFramework) by praydog, [ReShade](https://reshade.me) by crosire,
  [CitiesHarmony](https://github.com/boformer/CitiesHarmony) by boformer, [Harmony](https://github.com/pardeike/Harmony)
  by Andreas Pardeike.
- Cities: Skylines belongs to Colossal Order and Paradox Interactive; Resident Evil 2 to Capcom. This is a fan
  project.
