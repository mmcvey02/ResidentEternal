#!/usr/bin/env bash
# Fetches the two header-only APIs the plugin compiles against into re2/third_party/:
#   REFramework (praydog) include/reframework/API.h(pp)  -- the plugin API
#   ReShade (crosire) include/*.hpp                      -- the add-on API
# Nothing from either project is committed to this repository. Pin versions with REF_REF / RESHADE_REF.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
tp="$here/third_party"
REF_REF="${REF_REF:-v1.5.7}"  # plugin API 1.10: loads in REFramework builds from July 2024 on (newer ones accept older plugins)
RESHADE_REF="${RESHADE_REF:-v6.3.0}"  # build against an older add-on API: the add-on then loads in ReShade 6.3.0 and every newer version
mkdir -p "$tp"

fetch() { # name url ref subdir
  local name="$1" url="$2" ref="$3" sub="$4"
  rm -rf "$tp/$name.tmp"
  git clone --quiet --depth 1 --branch "$ref" --filter=blob:none --sparse "$url" "$tp/$name.tmp"
  git -C "$tp/$name.tmp" sparse-checkout set "$sub" >/dev/null
  rm -rf "$tp/$name"
  mkdir -p "$tp/$name"
  cp -r "$tp/$name.tmp/$sub" "$tp/$name/"
  cp "$tp/$name.tmp/LICENSE"* "$tp/$name/" 2>/dev/null || true
  git -C "$tp/$name.tmp" rev-parse HEAD > "$tp/$name/COMMIT"
  rm -rf "$tp/$name.tmp"
  echo "$name $(cat "$tp/$name/COMMIT")"
}

fetch reframework https://github.com/praydog/REFramework.git "$REF_REF" include
fetch reshade https://github.com/crosire/reshade.git "$RESHADE_REF" include
