# Raccoon City Skylines: wire protocol (v1)

Two channels, both local to one Windows PC. This is the same split as the Minecraft x GTA V passthrough
(`universal-modder/examples/minecraft-gta5-passthrough`): small state and events over a socket, and
pictures over named shared memory.

```
Resident Evil 2 (host renderer)                         Cities: Skylines (guest + city simulation)
  RaccoonSkylines.dll (REFramework plugin)  -- TCP 127.0.0.1:25600, JSON lines -->  LinkServer (C# mod)
                                            <-------------------------------------
  RaccoonSkylines.dll (ReShade add-on)      <-- shm "Local\RaccoonSkylines.City" --   SkylineExporter
                                            --- shm "Local\RaccoonSkylines.Bodycam" -> BodycamPanel
  raccoon_skylines.lua (REFramework Lua)   <-> files in reframework/data/raccoon_skylines/ <-> the plugin
```

## 1. The link: TCP, newline-delimited JSON

- The **Cities: Skylines mod is the server** on `127.0.0.1:25600` (loopback only, one client at a time).
- The **RE2 plugin is the client**. It reconnects every second, so the games can start in either order.
- One UTF-8 JSON object per line (`\n`). Every message has a type `t`. Unknown types and unknown fields
  are ignored, so either side can be newer.
- Lines longer than 64 KiB are dropped.

### RE2 -> City

| `t` | rate | fields |
|---|---|---|
| `hello` | on connect | `side: "re2"`, `v: 1` |
| `cam` | every rendered frame, capped at 60 Hz | `f` frame number, `p` [x, y, z] metres, `q` [x, y, z, w] camera rotation, `fov` vertical degrees, `w`/`h` back buffer size |
| `player` | 4 Hz | `p` [x, y, z], `hp` 0..1 (or -1 unknown), `area` string (RE2 map/area id, or "") |
| `ev` | when it happens | `id` (increasing per side), `k` kind, `d` object (see below) |

RE2 event kinds: `kill` (an enemy died, `d.enemy` type name), `hurt` (`d.hp`), `tyrant` (Mr. X
appeared), `birkin` (a G boss fight started), `area` (`d.area`), `save` (typewriter used).

### City -> RE2

| `t` | rate | fields |
|---|---|---|
| `hello` | on connect | `side: "city"`, `v: 1`, `city` city name |
| `city` | 4 Hz | `district` name, `inf` infection 0..1 there, `cityInf` citywide 0..1, `power` bool, `police` 0..1, `health` 0..1, `pop` int, `daylight` 0..1, `feed` bool (skyline feed running) |
| `ev` | when it happens | `id`, `k`, `d` |

City event kinds: `wave` (an outbreak wave hit the anchor district, `d.strength` 0..1), `blackout`
(`d.on` bool), `chirp` (`d.text`, flavour only).

## 2. Frames: named shared memory

Both directions use the same layout (little-endian). Names: `Local\RaccoonSkylines.City` (written by
Cities: Skylines, read by the ReShade add-on) and `Local\RaccoonSkylines.Bodycam` (written by the add-on,
read by Cities: Skylines). Off Windows (tests), the same bytes live in a file `/dev/shm/RaccoonSkylines.<Name>`.

```
header (4096 bytes)
   0 u32 magic 0x4B534352 ("RCSK")   4 u32 version (1)   8 u32 header bytes (4096)   12 u32 slot count
  16 i64 slot stride                 24 u32 max width    28 u32 max height
  32 i64 publish counter (bumped after each completed slot)    40 i32 latest slot (-1: none yet)   44 u32 writer pid
  256 + 128 * i: slot descriptor i
    +0  i64 seq (odd while the slot is being written)
    +8  i64 writer frame       +16 i64 host frame it answers (the `cam.f` it was rendered for; 0 if none)
    +24 u32 width              +28 u32 height
    +32 f32 near               +36 f32 far              +40 f32 vertical fov (degrees)
    +44 u32 flags: 1 = a depth layer follows the colour, 2 = rows are bottom-up
    +48 f32 daylight 0..1      +52 f32 infection 0..1 (city frames only)
slot i data at 4096 + i * stride:
  colour RGBA8, width * height * 4 bytes (alpha 0 = nothing there)
  [depth f32 linear metres, width * height * 4 bytes, if flags & 1]
```

A reader takes `latest slot`, reads the slot's `seq`, rejects odd values, copies the pixels, and reads
`seq` again: if it changed, the writer lapped it and the copy is thrown away (seqlock).

## 3. Lua <-> plugin: files

REFramework's Lua has no sockets, so the plugin and `raccoon_skylines.lua` meet in
`reframework/data/raccoon_skylines/` (the only folder REFramework's `json.*_file` functions can reach):

- `city.json`, written by the plugin at 4 Hz: `{ "link": bool, "city": <last city message>, "events": [<last 32 city events>] }`.
- `re2.json`, written by Lua at 4 Hz: `{ "player": <player message>, "events": [<last 32 RE2 events>] }`.
  The plugin forwards each event id once.

## 4. Coordinates

RE2 sends its camera untouched; Cities: Skylines maps it (`PoseMapper` in the mod, `tools/rcsk/mapping.py`
as the reference, `protocol/vectors.json` as the shared test vectors):

1. RE Engine -> Unity axes: mirror Z when `flipZ` is on (the default): `v = (x, y, -z)` and
   `q = (-qx, -qy, qz, qw)`.
2. Re-centre: `d = scale * (v - origin)`, where `origin` is the (mirrored) RE2 position captured on the
   first `cam` message or on the re-centre key.
3. Place in the city: `position = anchor + Ry(heading) * d`, `rotation = Ry(heading) * q`.

`anchor` is a point in the city (by default the first police station, raised by `eyeHeight`) and `heading`
is degrees clockwise about +Y.
