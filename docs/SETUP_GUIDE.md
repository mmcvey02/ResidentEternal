# Raccoon City Skylines: setup guide

Cities: Skylines x Resident Evil 2 (2019). Both games run at once: your city shows up in RE2's sky, and RE2 fights
your city's T-virus outbreak.

> **Status: experimental, version 0.1.** Everything compiles and the link between the games is tested, but it has
> not been run inside the real games yet. Expect to calibrate a few settings, and see *Troubleshooting* if
> something doesn't show up.

---

## 1. What you need

| | where to get it |
|---|---|
| **Cities: Skylines** (the original, 2015) on PC | Steam |
| **Resident Evil 2** (2019 remake) on PC | Steam |
| **REFramework** for RE2 | [github.com/praydog/REFramework-nightly/releases](https://github.com/praydog/REFramework-nightly/releases), file `RE2.zip` (see step 3 for which zip) |
| **ReShade, "with full add-on support"** | [reshade.me](https://reshade.me), the second download button |
| **This mod** | `RaccoonCitySkylines-v0.1.zip` |

Both games run at the same time, so a PC that runs each one comfortably on its own is a good start. Cities:
Skylines runs in a small window, which keeps it light.

## 2. Unzip the mod

Unzip `RaccoonCitySkylines-v0.1.zip` anywhere, for example your Desktop. Inside you'll find:

```
RaccoonCitySkylines/
  INSTALL.bat                     copies everything into place (steps 5 and 6)
  SETUP_GUIDE.md                  this guide
  Cities_Skylines/RaccoonCitySkylines/Source/   the Cities: Skylines mod (the game compiles it itself)
  Cities_Skylines/BUILD_WITH_DOTNET.bat         plan B if the game doesn't compile it (see Troubleshooting)
  Resident_Evil_2/                files that go into RE2's folder
  extras/                         test tools (optional)
```

## 3. Install REFramework into RE2

1. Find RE2's folder: in Steam, right-click **Resident Evil 2** > **Manage** > **Browse local files**. It's the
   folder with `re2.exe`.
2. Download REFramework for RE2 and extract **`dinput8.dll`** into that folder, next to `re2.exe`.
   - On the current Steam version of RE2, use `RE2.zip`.
   - If you switched RE2 to the old DirectX 11 version (Steam > Properties > Betas > `dx11_non-rt`), use the RE2
     zip that REFramework's release notes list for that version.
3. Start RE2 once. An REFramework menu should appear (press **Insert** to show or hide it). This creates the
   `reframework` folder. Quit the game.

## 4. Install ReShade into RE2

1. Run the ReShade installer ("with full add-on support").
2. Pick `re2.exe`, then the rendering API RE2 uses (**DirectX 10/11/12**).
3. When it asks which effects to install, tick **Standard effects** (the mod's shader needs `ReShade.fxh`, which
   comes with them). You can untick everything else.
4. Finish.

## 5. Install the RE2 files

Run **`INSTALL.bat`**. It looks for RE2 in the default Steam folder and asks for the path if RE2 is somewhere else.
Paste the folder from step 3.1.

To do it by hand instead, copy these from `Resident_Evil_2/` into RE2's folder, keeping the folders:

| file | goes to |
|---|---|
| `reframework/plugins/RaccoonSkylines.dll` | `<RE2>/reframework/plugins/` |
| `reframework/autorun/raccoon_skylines.lua` | `<RE2>/reframework/autorun/` |
| `reshade-shaders/Shaders/RaccoonSkylines.fx` | `<RE2>/reshade-shaders/Shaders/` |

## 6. Install the Cities: Skylines mod

`INSTALL.bat` does this too. By hand: copy the folder `Cities_Skylines/RaccoonCitySkylines` into

```
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\
```

(paste that path into File Explorer's address bar), so you end up with
`...\Addons\Mods\RaccoonCitySkylines\Source\*.cs`. Cities: Skylines compiles the mod by itself the next time it
starts.

## 7. Set up Cities: Skylines (once)

1. Start Cities: Skylines. **Content Manager > Mods**: tick **Raccoon City Skylines**.
2. **Options > Graphics**: set **Windowed**, resolution **960x540** (or 1280x720). This window is what gets drawn
   into RE2's sky; small is faster and plenty sharp for a skyline.
3. **Options > Raccoon City Skylines** has the mod's settings. The defaults are fine to start.
4. Load a city that has **at least one police station**. The police station is where RE2's camera stands in your
   city (it plays the RPD).

## 8. Set up ReShade in RE2 (once)

1. Start RE2 and load a save. Press **Home** to open ReShade.
2. **Home** tab: tick **RaccoonSkylines**.
3. Bottom of the Home tab: **Edit global preprocessor definitions**. Set `RESHADE_DEPTH_INPUT_IS_REVERSED` to `1`.
4. **Add-ons** tab: check that **Generic Depth** is on, and that **RaccoonSkylines** is listed there too. (It
   registers itself once the plugin loads.)
5. Optional check: enable ReShade's **DisplayDepth** effect for a moment. Near things should be dark and the sky
   white. If it looks inverted or flat, go back to step 3.

## 9. Play

1. **Start Cities: Skylines** and load your city. Leave its window open; don't minimise it.
2. **Start RE2.** The two connect by themselves, in either order. Cities: Skylines hides its UI and its camera
   starts following RE2's.
3. In RE2, go somewhere with open sky: the RPD's front gate and courtyard, the streets at the start, the gas
   station. Your city's towers fill the sky.

**Keys in Cities: Skylines:**

| key | does |
|---|---|
| **F9** | Switch between *skyline feed* (camera follows RE2) and *building* (normal play, with RE2's bodycam in the corner) |
| **F10** | Re-centre: where RE2 stands now becomes the police station |
| **F11** | Show or hide the bodycam |

**In RE2:** press **Insert** for REFramework's menu, then **Script generated UI > Raccoon City Skylines**. It
shows the district, infection, police, power and the game rank it set. Its **Send test kill / Mr. X / G**
buttons push events to the city, so you can check the whole loop.

**What happens:**
- The infection starts at your police station's district and spreads. Build police and healthcare to slow it.
  Keep the power on: blackouts speed it up.
- Kills in RE2 lower the infection. Mr. X and G cause surges, and RE2 flashes red when a wave hits.
- Higher infection makes RE2 harder (its built-in difficulty rank) and gives the picture a sick, green-tinged
  vignette. A blackout in the city darkens RE2.
- Infected residents get sick in Cities: Skylines, so ambulances get busy.

## 10. Calibrate

| problem | fix |
|---|---|
| The city's towers drift sideways as you turn | ReShade > RaccoonSkylines > **RE2 vertical FOV override**: raise or lower it until they stay put. Or set `"fovIsHorizontal": true` in `<RE2>/reframework/data/raccoon_skylines/config.json` |
| Turning left in RE2 turns the city right | Cities: Skylines > Options > Raccoon City Skylines > untick **Mirror Z** |
| The city faces the wrong way | **Heading** in the same options |
| The city looks too small or close, or you're below the ground | **Eye height** and **City metres per RE2 metre** |
| The city is too bright or the wrong colour for a rainy night | ReShade > RaccoonSkylines > **Match RE2's colour and brightness**, **Rain haze** |
| The city covers distant walls or doesn't show at all | ReShade > RaccoonSkylines > **Sky depth**, and the **Sky mask** debug view (white = where the city goes) |

## 11. Troubleshooting

| symptom | what to do |
|---|---|
| *Raccoon City Skylines* isn't in Content Manager | The game couldn't compile the source. Look for errors in `Cities_Data\output_log.txt` (in the game folder). Then use plan B: install the [.NET SDK](https://dotnet.microsoft.com/download) and run `Cities_Skylines\BUILD_WITH_DOTNET.bat`, which builds the mod against your game and installs it in place of the source |
| REFramework menu says *Waiting for Cities: Skylines* | Is Cities: Skylines running with a city loaded (not the main menu)? Is the mod enabled? Both use port 25600 on your own PC only; change it in both places (mod options and `config.json`) if something else uses it |
| RE2 doesn't start or crashes after installing | Move `reframework/plugins/RaccoonSkylines.dll` out and try again. If RE2 runs without it, open `<RE2>/re2_framework_log.txt` and report the last lines. Also check your REFramework zip matches your RE2 version (step 3) |
| RaccoonSkylines isn't in ReShade's effect list | Check `reshade-shaders/Shaders/RaccoonSkylines.fx` is in place and that you installed **Standard effects** (step 4) |
| The effect shows but no city | Cities: Skylines must be in *skyline feed* mode (F9) with its window not minimised. In ReShade, set **Debug view** to *City frame*: magenta means no frame is arriving |
| The game rank or kill events don't work | The script's RE2 names may not match your build. The REFramework console shows which feature turned itself off. The test buttons still work, and the rest of the mod is unaffected |
| Low frame rate | Make the Cities: Skylines window smaller, or lower *Skyline frames per second* in its mod options. In `config.json`, raise `bodycamEvery` or set it to `0` to turn the bodycam off |

**Testing one game at a time** (optional, needs [Python 3](https://www.python.org/downloads/)): in `extras/`,
`python fake_re2.py` pretends to be RE2, so you can watch Cities: Skylines react on its own. `python fake_city.py`
pretends to be Cities: Skylines, so RE2 gets a test skyline.

## 12. Uninstall

- **RE2:** run `INSTALL.bat /remove`, or delete the three files from step 5. Uninstall ReShade and REFramework the
  usual way if you want.
- **Cities: Skylines:** delete `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\RaccoonCitySkylines`.
  The mod never writes into your save games. It keeps its settings and the outbreak in
  `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\RaccoonCitySkylines*.json`, which you can delete too.

Single-player only. The link between the games listens on your own PC (127.0.0.1) and nowhere else.
