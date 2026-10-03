# How the "merge two games" technique works, and how it maps to Cities: Skylines x Resident Evil 2

## The technique in the video

The September 2026 wave of mashup videos ("Minecraft inside Elden Ring", "Minecraft inside GTA V", "skateboarding
in MW2") came from AI coding agents driving an open-source toolkit, Rehan Sheikh's
[`universal-modder`](https://github.com/rehan-remade/universal-modder). Its `mashup-mods` skill names four ways
to put one game inside another, from lightest to heaviest:

1. **Port the content.** Recreate the guest's enemy, weapon or block as new content in the host's own mod API.
2. **Passthrough.** Both games run at the same time. A mod in each one exchanges state over `127.0.0.1`, and the
   guest's rendered picture goes through named shared memory into the host's renderer, at the right place and
   depth. The host feeds things like collision back to the guest.
3. **Embed a decomp as a library** (libsm64-style).
4. **Reimplement, then fuse** (the Rust MW2 x Skate 3 runtime).

The headline demo, real Minecraft inside GTA V story mode
(`examples/minecraft-gta5-passthrough`), is pattern 2:

```
GTA V story mode                                     Minecraft + Fabric mod
  ScriptHookV ASI  -- WebSocket 127.0.0.1:25599 ---->  camera, ground probes, keys
                   <---------------------------------  explosions, arrow hits, mob events
  ReShade add-on   <-- shm "Local\MCPassthroughFrame" -- Minecraft colour + depth, hand/HUD overlay
  MCPassthrough.fx: depth-test Minecraft against GTA's depth buffer, re-project for latency
```

- GTA's camera drives Minecraft's camera, so both render the same view.
- Ground probes from GTA become invisible barrier blocks, so Steve walks on Los Santos.
- Minecraft's frame is composited where it is nearer than GTA's depth.
- Minecraft events (TNT, arrows) are acted out with GTA natives (explosions, bullets).
- It was built mostly without the games, against stand-ins (`fakehost.py`, a fake D3D11 "GTA").

## Applying it to Cities: Skylines and Resident Evil 2

Cities: Skylines is a top-down city builder (Unity 5.6, Mono, C# mods through ICities + Harmony). Resident Evil 2
(2019) is a third-person survival horror game on Capcom's RE Engine (REFramework for code, ReShade for post
effects). They have no shared mechanic, so a literal "Steve in Los Santos" copy (one game's character walking
in the other's world) doesn't fit. RE2's maps are narrow corridors and streets, and a C:S city is kilometres
across. What does fit is each game supplying what the other lacks:

- **RE2 gets a real city.** Raccoon City in RE2 is a painted backdrop. In the mod, Cities: Skylines renders your
  city from RE2's camera, and the compositor puts it into RE2's sky. Your skyscrapers rise above the RPD's walls
  and the alley rooftops, they move with the camera, and your city's clock tints them.
- **Cities: Skylines gets an outbreak with someone fighting it.** The mod runs a T-virus outbreak through your
  districts. Police and healthcare coverage slow it, blackouts speed it up, and commuters spread it. Every
  zombie killed in RE2 lowers infection in the district where RE2 is anchored. Mr. X and G cause surges.
- **The coupling goes both ways.** The district's infection drives RE2's own dynamic difficulty (the game
  rank), and a blackout in that district darkens RE2. RE2's picture goes back to Cities: Skylines as a bodycam
  panel, and the survivor shows up as a marker on your city map.

### Tool-for-tool mapping

| Minecraft x GTA V | Cities: Skylines x Resident Evil 2 | Why it changed |
|---|---|---|
| ScriptHookV + ASI loader (host code) | **REFramework plugin** `RaccoonSkylines.dll` + **REFramework Lua** `raccoon_skylines.lua` | RE Engine's community framework, like ScriptHookV for RAGE |
| ReShade add-on + `MCPassthrough.fx` | **ReShade add-on in the same DLL** + `RaccoonSkylines.fx` | Kept as is; ReShade works on RE2 (DX11) |
| Fabric mod (guest) | **ICities mod** + **CitiesHarmony** (`cities/RaccoonCitySkylines`) | Cities: Skylines' official mod API; Harmony for the overlay hook |
| WebSocket on 127.0.0.1:25599 | **TCP, newline-delimited JSON** on 127.0.0.1:25600 | Cities: Skylines runs Mono's .NET 3.5, which has no WebSocket client or server |
| shm `Local\MCPassthroughFrame` (colour + depth) | shm `Local\RaccoonSkylines.City` (colour) **and** `Local\RaccoonSkylines.Bodycam` (the other way) | Sky replacement needs RE2's depth, not the city's; the bodycam adds the reverse direction |
| GTA camera -> Minecraft camera (1 m = 1 block) | RE2 camera -> C:S camera, **re-anchored** at your police station (the RPD) with heading and scale | The two worlds don't share coordinates, so an anchor maps one onto the other |
| Ground probes -> barrier blocks | (none) | Only RE2 has collision that matters; the coupling is simulation, not physics |
| MC explosions -> GTA explosions | RE2 kills / Mr. X / G -> C:S outbreak; C:S infection / blackout -> RE2 game rank and lighting | Each game drives the other's systems |
| `fakehost.py`, `fakegta.cpp` | `tools/fake_city.py`, `tools/fake_re2.py`, `Tests.exe city`, `test_common client` | Same lesson: build and test the whole pipeline without either game |

### Why sky replacement and not full depth compositing

The Minecraft x GTA V compositor depth-tests every Minecraft pixel against GTA's depth buffer, because Minecraft
blocks sit inside Los Santos. Here the city is always beyond RE2's level geometry: RE2's playable space is a
few streets, and your city is the horizon. So the compositor only needs to know where RE2 shows sky, which
RE2's depth buffer says on its own (far-plane pixels). This also avoids the hard part on the Cities: Skylines
side. Unity 5.6 has no asynchronous GPU readback, and exporting linear depth would need a custom shader built
into an AssetBundle with exactly Unity 5.6. The protocol keeps a depth flag (`flags & 1`) for that upgrade.

### Coordinates

RE2 sends its camera untouched (position in metres, rotation quaternion, FOV). The C:S mod mirrors Z (RE Engine
and Unity disagree on handedness), re-centres on the first pose it sees (or F10), scales, rotates by the
anchor's heading, and adds the anchor. `protocol/vectors.json` holds 24 test vectors that the Python reference
and the C# mod must both reproduce exactly. Two properties are also tested: walking forward and turning left in
RE2 do the same in the city.
