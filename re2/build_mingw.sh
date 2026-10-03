#!/usr/bin/env bash
# Cross-compiles RaccoonSkylines.dll for Windows x64 from Linux/WSL with MinGW-w64 (CI does this).
# On Windows with Visual Studio, use build.bat instead.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
[[ -d "$here/third_party/reframework" && -d "$here/third_party/reshade" ]] || "$here/fetch_deps.sh"
out="$here/build/win64"
mkdir -p "$out"
CXX="${CXX:-x86_64-w64-mingw32-g++-posix}"
command -v "$CXX" >/dev/null || CXX=x86_64-w64-mingw32-g++
"$CXX" -std=c++20 -O2 -shared -Wall -Wextra -Wno-unused-parameter -Wno-missing-field-initializers -Wno-cast-function-type \
  -DWIN32_LEAN_AND_MEAN -DNOMINMAX -DUNICODE -D_UNICODE \
  -I"$here/src" -isystem "$here/src/mingw_shim" -isystem "$here/third_party/reframework/include" -isystem "$here/third_party/reshade/include" \
  "$here/src/plugin.cpp" "$here/src/compositor.cpp" "$here/src/re_camera.cpp" "$here/src/config.cpp" \
  "$here/src/common/json_lite.cpp" "$here/src/common/link.cpp" "$here/src/common/frames.cpp" \
  -o "$out/RaccoonSkylines.dll" -lws2_32 -static -static-libgcc -static-libstdc++
echo "built $out/RaccoonSkylines.dll"
