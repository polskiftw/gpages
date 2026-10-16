cmake_minimum_required(VERSION 3.16)
project(maze_simulator_prototype LANGUAGES CXX)

add_library(maze_core src/maze.cpp)
target_include_directories(maze_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/src)
target_compile_features(maze_core PUBLIC cxx_std_20)

add_executable(maze-cli src/main.cpp)
target_link_libraries(maze-cli PRIVATE maze_core)

add_executable(maze-tests tests/test_maze.cpp)
target_link_libraries(maze-tests PRIVATE maze_core)

# Mathematical top-down SVG preview of EXACT Maze::passages output.
# No renderer, SDL, OpenGL, or alternate maze algorithm is involved.
add_library(maze_preview_core src/maze_preview.cpp)
target_link_libraries(maze_preview_core PUBLIC maze_core)
target_compile_features(maze_preview_core PUBLIC cxx_std_20)

add_executable(maze-preview src/maze_preview_main.cpp)
target_link_libraries(maze-preview PRIVATE maze_preview_core)

add_executable(maze-preview-tests tests/test_maze_preview.cpp)
target_link_libraries(maze-preview-tests PRIVATE maze_preview_core)

add_library(stairs_core src/stairs.cpp)
target_link_libraries(stairs_core PUBLIC maze_core)
target_compile_features(stairs_core PUBLIC cxx_std_20)

add_executable(maze-stairs src/stairs_main.cpp)
target_link_libraries(maze-stairs PRIVATE stairs_core)

add_executable(stair-tests tests/test_stairs.cpp)
target_link_libraries(stair-tests PRIVATE stairs_core)

# Self-contained analytic physical stair geometry and OBJ-room exporter.
add_library(stair_geometry_core src/stair_geometry.cpp)
target_compile_features(stair_geometry_core PUBLIC cxx_std_20)
target_include_directories(stair_geometry_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/src)

add_executable(maze-geometry src/geometry_main.cpp)
target_link_libraries(maze-geometry PRIVATE stair_geometry_core)

add_executable(stair-geometry-tests tests/test_stair_geometry.cpp)
target_link_libraries(stair-geometry-tests PRIVATE stair_geometry_core)

# The small handcrafted first-person stair lab is independent of the huge-world
# topology; its collision and geometry unit tests require NO SDL or OpenGL.
add_library(playable_room_core src/playable_room.cpp src/playable_mesh.cpp)
target_link_libraries(playable_room_core PUBLIC stair_geometry_core)
target_compile_features(playable_room_core PUBLIC cxx_std_20)

add_executable(playable-room-tests tests/test_playable_room.cpp)
target_link_libraries(playable-room-tests PRIVATE playable_room_core)

# On Gentoo, the installed SDL3 + OpenGL libraries are discovered from the
# system. The existing topology-only targets still build without them.
option(MAZE_BUILD_PLAYABLE "Build the SDL3/OpenGL first-person stair laboratory" ON)
option(MAZE_REQUIRE_PLAYABLE "Fail configuration when SDL3 or OpenGL is unavailable" OFF)
if(MAZE_BUILD_PLAYABLE)
    find_package(SDL3 CONFIG QUIET)
    find_package(OpenGL QUIET)
    if(TARGET SDL3::SDL3 AND TARGET OpenGL::GL)
        add_executable(maze-playable src/playable_main.cpp)
        target_link_libraries(maze-playable PRIVATE playable_room_core SDL3::SDL3 OpenGL::GL)
        target_compile_features(maze-playable PRIVATE cxx_std_20)
        message(STATUS "Building native SDL3/OpenGL 3.3 maze-playable")
    elseif(MAZE_REQUIRE_PLAYABLE)
        message(FATAL_ERROR "maze-playable requires SDL3::SDL3 and OpenGL::GL")
    else()
        message(STATUS "Skipping maze-playable: install SDL3 and OpenGL development libraries")
    endif()
endif()

enable_testing()
add_test(NAME maze-connectivity COMMAND maze-tests)
add_test(NAME maze-topdown-accuracy COMMAND maze-preview-tests)
add_test(NAME stairs-connectivity COMMAND stair-tests)
add_test(NAME stair-physical-geometry COMMAND stair-geometry-tests)
add_test(NAME playable-room-collision COMMAND playable-room-tests)
