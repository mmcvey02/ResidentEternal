#!/usr/bin/env bash
# Type-checks the mod against the compile-only stubs and runs the tests, with Mono (mcs). This is what CI runs.
# The real build, against the game's DLLs, is RaccoonCitySkylines/RaccoonCitySkylines.csproj (see README).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$here/build"
mkdir -p "$out"
mcs -target:library -langversion:7 -nowarn:1591 -out:"$out/GameStubs.dll" "$here/stubs/GameStubs.cs"
# -langversion:4 because Cities: Skylines compiles source mods itself with an old Mono compiler, and the release ships
# the mod as source; no reference beyond the game's own assemblies (mscorlib, System, UnityEngine, ColossalManaged,
# ICities, Assembly-CSharp) for the same reason.
mcs -target:library -langversion:4 -warnaserror+ -r:"$out/GameStubs.dll" \
    -out:"$out/RaccoonCitySkylines.dll" "$here"/RaccoonCitySkylines/*.cs
# The tests compile the game-independent sources directly, so they run without any stub.
mcs -langversion:7 -warnaserror+ -out:"$out/Tests.exe" "$here/tests/Tests.cs" \
    "$here/RaccoonCitySkylines/Json.cs" "$here/RaccoonCitySkylines/LinkServer.cs" "$here/RaccoonCitySkylines/PoseMapper.cs" \
    "$here/RaccoonCitySkylines/SharedFrames.cs" "$here/RaccoonCitySkylines/OutbreakModel.cs"
if [[ "${1:-}" != "--no-test" ]]; then
    mono "$out/Tests.exe" unit "$here/../protocol/vectors.json"
fi
