cmake_minimum_required(VERSION 3.20)
project(RaccoonSkylines CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Headers fetched by fetch_deps.sh: REFramework's plugin API and ReShade's add-on API.
set(THIRD_PARTY ${CMAKE_CURRENT_SOURCE_DIR}/third_party)

set(COMMON src/common/json_lite.cpp src/common/link.cpp src/common/frames.cpp)

if(WIN32)
  add_library(RaccoonSkylines SHARED src/plugin.cpp src/compositor.cpp src/re_camera.cpp src/config.cpp ${COMMON})
  target_include_directories(RaccoonSkylines PRIVATE src ${THIRD_PARTY}/reframework/include ${THIRD_PARTY}/reshade/include)
  target_compile_definitions(RaccoonSkylines PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX UNICODE _UNICODE)
  target_link_libraries(RaccoonSkylines PRIVATE ws2_32)
  set_target_properties(RaccoonSkylines PROPERTIES PREFIX "")
  if(MINGW)
    target_link_options(RaccoonSkylines PRIVATE -static -static-libgcc -static-libstdc++)
  endif()
  if(MSVC)
    set_property(TARGET RaccoonSkylines PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded")
  endif()
endif()

# The portable half, tested on any OS.
add_executable(test_common tests/test_common.cpp ${COMMON})
find_package(Threads REQUIRED)
target_link_libraries(test_common PRIVATE Threads::Threads)
if(WIN32)
  target_link_libraries(test_common PRIVATE ws2_32)
endif()
