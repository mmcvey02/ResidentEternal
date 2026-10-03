@echo off
rem Builds RaccoonSkylines.dll with Visual Studio 2022+ (C++ desktop tools, x64). Run fetch_deps.sh first (Git Bash
rem or WSL), or copy REFramework's include\ to third_party\reframework\include and ReShade's include\ to
rem third_party\reshade\include.
setlocal
if not exist "%~dp0third_party\reframework\include\reframework\API.hpp" (echo missing third_party\reframework - run fetch_deps.sh & exit /b 1)
if not exist "%~dp0third_party\reshade\include\reshade.hpp" (echo missing third_party\reshade - run fetch_deps.sh & exit /b 1)
cmake -S "%~dp0." -B "%~dp0build\msvc" -A x64 || exit /b 1
cmake --build "%~dp0build\msvc" --config Release --target RaccoonSkylines || exit /b 1
echo built %~dp0build\msvc\Release\RaccoonSkylines.dll
