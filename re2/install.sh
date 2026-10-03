#!/usr/bin/env bash
# Installs the RE2 half into a Resident Evil 2 (2019) folder. Run from WSL or Git Bash.
#   re2/install.sh [--remove]          RE2_DIR overrides the Steam library search
# Needs, already installed by you: REFramework for RE2 (dinput8.dll + reframework/) and ReShade 6 *with add-on
# support* (dxgi.dll), with "Generic Depth" enabled. This script adds only its own files and lists them.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"

find_re2() {
  local roots=("/mnt/c/Program Files (x86)/Steam/steamapps/common" "/c/Program Files (x86)/Steam/steamapps/common")
  for r in "${roots[@]}" /mnt/*/SteamLibrary/steamapps/common /mnt/*/Steam/steamapps/common; do
    [[ -f "$r/RESIDENT EVIL 2  BIOHAZARD RE2/re2.exe" ]] && { echo "$r/RESIDENT EVIL 2  BIOHAZARD RE2"; return; }
  done
}
RE2_DIR="${RE2_DIR:-$(find_re2)}"
[[ -n "$RE2_DIR" && -f "$RE2_DIR/re2.exe" ]] || { echo "set RE2_DIR to the folder with re2.exe"; exit 1; }

dll="$here/build/win64/RaccoonSkylines.dll"
[[ -f "$dll" ]] || dll="$here/build/msvc/Release/RaccoonSkylines.dll"
files=(
  "reframework/plugins/RaccoonSkylines.dll"
  "reframework/autorun/raccoon_skylines.lua"
  "reshade-shaders/Shaders/RaccoonSkylines.fx"
)

if [[ "${1:-}" == "--remove" ]]; then
  for f in "${files[@]}"; do rm -fv "$RE2_DIR/$f"; done
  echo "left reframework/data/raccoon_skylines/ (your config) in place"
  exit 0
fi

[[ -f "$RE2_DIR/dinput8.dll" && -d "$RE2_DIR/reframework" ]] || { echo "install REFramework for RE2 first (dinput8.dll + reframework/)"; exit 1; }
[[ -f "$RE2_DIR/dxgi.dll" || -f "$RE2_DIR/ReShade64.dll" ]] || echo "warning: no ReShade found; the skyline needs ReShade 6 with add-on support"
[[ -f "$dll" ]] || { echo "build the DLL first (build_mingw.sh or build.bat)"; exit 1; }

mkdir -p "$RE2_DIR/reframework/plugins" "$RE2_DIR/reframework/autorun" "$RE2_DIR/reshade-shaders/Shaders"
cp -v "$dll" "$RE2_DIR/reframework/plugins/RaccoonSkylines.dll"
cp -v "$here/lua/raccoon_skylines.lua" "$RE2_DIR/reframework/autorun/raccoon_skylines.lua"
cp -v "$here/shaders/RaccoonSkylines.fx" "$RE2_DIR/reshade-shaders/Shaders/RaccoonSkylines.fx"
echo "installed. In ReShade: enable RaccoonSkylines, and set RESHADE_DEPTH_INPUT_IS_REVERSED=1 under Global preprocessor definitions."
