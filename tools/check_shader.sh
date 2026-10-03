#!/usr/bin/env bash
# Builds ReShade's own FX compiler (HLSL/GLSL backends) from source and compiles re2/shaders/RaccoonSkylines.fx with it,
# so shader mistakes fail CI instead of failing silently inside RE2. Everything is fetched into a temp folder.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
work="${TMPDIR:-/tmp}/rcsk-fxc"
mkdir -p "$work"
[[ -d "$work/reshade" ]] || git clone --quiet --depth 1 https://github.com/crosire/reshade.git "$work/reshade"
[[ -d "$work/reshade-shaders" ]] || git clone --quiet --depth 1 --branch slim https://github.com/crosire/reshade-shaders.git "$work/reshade-shaders"
if [[ ! -x "$work/fxc" ]]; then
  cat > "$work/version.h" <<'H'
#pragma once
#define VERSION_MAJOR 6
#define VERSION_MINOR 0
#define VERSION_REVISION 0
#define VERSION_BUILD 0
#define VERSION_STRING_PRODUCT "6.0.0"
#define VERSION_STRING_FILE "6.0.0"
H
  cat > "$work/stubs.cpp" <<'C'
#include "effect_codegen.hpp"
namespace reshadefx {
codegen *create_codegen_dxbc(unsigned int, bool, bool, int) { return nullptr; }
codegen *create_codegen_dxil(unsigned int, bool, bool, int) { return nullptr; }
codegen *create_codegen_spirv(bool, bool, bool, bool, bool) { return nullptr; }
}
C
  s="$work/reshade/source"
  g++ -std=c++20 -O1 -w -I"$work" -I"$s" -o "$work/fxc" "$work/reshade/tools/fxc.cpp" "$work/stubs.cpp" \
    "$s/effect_expression.cpp" "$s/effect_lexer.cpp" "$s/effect_parser_exp.cpp" "$s/effect_parser_stmt.cpp" \
    "$s/effect_preprocessor.cpp" "$s/effect_symbol_table.cpp" "$s/effect_codegen_hlsl.cpp" "$s/effect_codegen_glsl.cpp"
fi
for backend in --hlsl --glsl; do
  "$work/fxc" $backend --width 1920 --height 1080 -D RESHADE_DEPTH_INPUT_IS_REVERSED=1 \
    -I "$work/reshade-shaders/Shaders" -Fo "$work/out$backend" "$here/re2/shaders/RaccoonSkylines.fx"
done
echo "RaccoonSkylines.fx compiles (HLSL, GLSL)"
