#!/usr/bin/env bash
# Builds the downloadable release: dist/RaccoonCitySkylines-v<version>.zip
#   - Resident_Evil_2/: the prebuilt plugin DLL, the Lua script and the shader, in RE2's folder layout
#   - Cities_Skylines/: the mod as source (Cities: Skylines compiles source mods itself), plus a dotnet fallback
#   - INSTALL.bat, SETUP_GUIDE.md, extras/ (the stand-in test tools)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
version="${1:-0.1}"
name="RaccoonCitySkylines"
stage="$root/dist/stage/$name"
zip="$root/dist/$name-v$version.zip"

"$root/cities/build.sh"          # type-check + tests before shipping
"$root/re2/build_mingw.sh"
"$root/tools/check_shader.sh"

rm -rf "$root/dist/stage" "$zip"
mkdir -p "$stage"

# --- RE2 -----------------------------------------------------------------------------------------------------------
re="$stage/Resident_Evil_2"
mkdir -p "$re/reframework/plugins" "$re/reframework/autorun" "$re/reshade-shaders/Shaders"
cp "$root/re2/build/win64/RaccoonSkylines.dll" "$re/reframework/plugins/"
cp "$root/re2/lua/raccoon_skylines.lua" "$re/reframework/autorun/"
cp "$root/re2/shaders/RaccoonSkylines.fx" "$re/reshade-shaders/Shaders/"

# --- Cities: Skylines ----------------------------------------------------------------------------------------------
cs="$stage/Cities_Skylines"
mkdir -p "$cs/RaccoonCitySkylines/Source" "$cs/build"
cp "$root"/cities/RaccoonCitySkylines/*.cs "$cs/RaccoonCitySkylines/Source/"
# The fallback project compiles the same sources from ../RaccoonCitySkylines/Source.
sed -e 's|<Deterministic>true</Deterministic>|<Deterministic>true</Deterministic>\n    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>|' \
    "$root/cities/RaccoonCitySkylines/RaccoonCitySkylines.csproj" > "$cs/build/RaccoonCitySkylines.csproj"
python3 - "$cs/build/RaccoonCitySkylines.csproj" <<'PY'
import sys
p = sys.argv[1]
s = open(p).read()
s = s.replace("  <ItemGroup>\n    <Reference Include=\"Assembly-CSharp\">",
              "  <ItemGroup>\n    <Compile Include=\"..\\RaccoonCitySkylines\\Source\\*.cs\" />\n  </ItemGroup>\n\n  <ItemGroup>\n    <Reference Include=\"Assembly-CSharp\">", 1)
open(p, "w").write(s)
PY

crlf() { sed 's/$/\r/' > "$1"; }

crlf "$cs/BUILD_WITH_DOTNET.bat" <<'EOF'
@echo off
rem Plan B: builds the Cities: Skylines mod as a DLL against your game, for when the game can't compile the source.
rem Needs the .NET SDK: https://dotnet.microsoft.com/download
rem If Cities: Skylines isn't in the default Steam folder, run:  BUILD_WITH_DOTNET.bat "D:\...\Cities_Skylines"
setlocal
set "GAME=%~1"
if "%GAME%"=="" set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines"
if not exist "%GAME%\Cities_Data\Managed\Assembly-CSharp.dll" (
  echo Cities: Skylines not found at "%GAME%".
  echo Run: BUILD_WITH_DOTNET.bat "path\to\Cities_Skylines"
  pause & exit /b 1
)
where dotnet >nul 2>nul || (echo The .NET SDK is not installed: https://dotnet.microsoft.com/download & pause & exit /b 1)
dotnet build -c Release "%~dp0build\RaccoonCitySkylines.csproj" -p:CitiesManaged="%GAME%\Cities_Data\Managed" || (pause & exit /b 1)
rem The DLL replaces the source copy, so the game doesn't load the mod twice.
set "MOD=%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\RaccoonCitySkylines"
if exist "%MOD%\Source" rmdir /s /q "%MOD%\Source"
echo.
echo Installed the built mod to "%MOD%". Enable it in Content Manager ^> Mods.
pause
EOF

crlf "$stage/INSTALL.bat" <<'EOF'
@echo off
rem Raccoon City Skylines installer. Copies the Cities: Skylines mod and the RE2 files into place.
rem   INSTALL.bat            install
rem   INSTALL.bat /remove    remove everything this installed
setlocal EnableExtensions
set "HERE=%~dp0"
set "MOD=%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\RaccoonCitySkylines"
set "RE2=C:\Program Files (x86)\Steam\steamapps\common\RESIDENT EVIL 2  BIOHAZARD RE2"
if not exist "%RE2%\re2.exe" (
  echo Resident Evil 2 was not found in the default Steam folder.
  echo In Steam: right-click Resident Evil 2 ^> Manage ^> Browse local files, and copy that folder's path.
  set /p "RE2=Paste RE2's folder (the one with re2.exe): "
)
set "RE2=%RE2:"=%"
if not exist "%RE2%\re2.exe" (echo No re2.exe in "%RE2%". & pause & exit /b 1)

if /i "%~1"=="/remove" goto remove

echo.
echo === Cities: Skylines mod
if not exist "%MOD%" mkdir "%MOD%"
xcopy /e /i /y /q "%HERE%Cities_Skylines\RaccoonCitySkylines" "%MOD%" >nul || goto fail
echo   -^> %MOD%

echo === Resident Evil 2 files
if not exist "%RE2%\dinput8.dll" echo   WARNING: REFramework (dinput8.dll) is not installed in RE2 yet - see SETUP_GUIDE.md step 3.
if not exist "%RE2%\reshade-shaders\Shaders\ReShade.fxh" echo   WARNING: ReShade with Standard effects is not installed in RE2 yet - see SETUP_GUIDE.md step 4.
xcopy /e /i /y /q "%HERE%Resident_Evil_2" "%RE2%" >nul || goto fail
echo   -^> %RE2%\reframework\plugins\RaccoonSkylines.dll
echo   -^> %RE2%\reframework\autorun\raccoon_skylines.lua
echo   -^> %RE2%\reshade-shaders\Shaders\RaccoonSkylines.fx
echo.
echo Done. Next: SETUP_GUIDE.md steps 7 and 8 (enable the mod in Cities: Skylines, enable the effect in ReShade).
pause
exit /b 0

:remove
del /q "%RE2%\reframework\plugins\RaccoonSkylines.dll" "%RE2%\reframework\autorun\raccoon_skylines.lua" "%RE2%\reshade-shaders\Shaders\RaccoonSkylines.fx" 2>nul
if exist "%MOD%" rmdir /s /q "%MOD%"
echo Removed. (Settings in "%LOCALAPPDATA%\Colossal Order\Cities_Skylines\RaccoonCitySkylines*.json" and
echo "%RE2%\reframework\data\raccoon_skylines" were left; delete them if you like.)
pause
exit /b 0

:fail
echo Copy failed. Is the game running? Close it and try again.
pause
exit /b 1
EOF

# --- docs and extras -----------------------------------------------------------------------------------------------
cp "$root/docs/SETUP_GUIDE.md" "$stage/SETUP_GUIDE.md"
mkdir -p "$stage/extras/rcsk"
cp "$root/tools/fake_city.py" "$root/tools/fake_re2.py" "$stage/extras/"
cp "$root"/tools/rcsk/*.py "$stage/extras/rcsk/"
crlf "$stage/README.txt" <<EOF
Raccoon City Skylines v$version - Cities: Skylines x Resident Evil 2 (2019)

Start with SETUP_GUIDE.md. Short version:
  1. Install REFramework and ReShade (with full add-on support, Standard effects) into RE2.
  2. Run INSTALL.bat.
  3. Cities: Skylines: enable the mod, run windowed at 960x540, load a city with a police station.
  4. RE2: in ReShade (Home), enable RaccoonSkylines and set RESHADE_DEPTH_INPUT_IS_REVERSED=1.
  5. Start both games. F9 in Cities: Skylines switches between the skyline feed and building.

Experimental: compiled and tested without the games, not yet run inside them.
Source: https://github.com/mmcvey02/ResidentEternal
EOF

(cd "$root/dist/stage" && python3 -c "
import os, sys, zipfile
with zipfile.ZipFile(sys.argv[1], 'w', zipfile.ZIP_DEFLATED) as z:
    for d, _, files in os.walk('$name'):
        for f in sorted(files):
            z.write(os.path.join(d, f))
" "$zip")
rm -rf "$root/dist/stage"
echo "built $zip ($(du -h "$zip" | cut -f1))"
