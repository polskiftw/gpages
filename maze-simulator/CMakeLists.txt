cmake_minimum_required(VERSION 3.16)
project(maze_simulator_prototype LANGUAGES CXX)

add_library(maze_core src/maze.cpp)
target_include_directories(maze_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/src)
target_compile_features(maze_core PUBLIC cxx_std_20)

add_executable(maze-cli src/main.cpp)
target_link_libraries(maze-cli PRIVATE maze_core)

add_executable(maze-tests tests/test_maze.cpp)
target_link_libraries(maze-tests PRIVATE maze_core)

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

enable_testing()
add_test(NAME maze-connectivity COMMAND maze-tests)
add_test(NAME stairs-connectivity COMMAND stair-tests)
add_test(NAME stair-physical-geometry COMMAND stair-geometry-tests)
