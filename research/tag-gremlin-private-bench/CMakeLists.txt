cmake_minimum_required(VERSION 3.20)
project(taggremlin_tagbench LANGUAGES CXX)

add_executable(tagbench tagbench.cpp)
target_compile_features(tagbench PRIVATE cxx_std_20)
if(MSVC)
  target_compile_options(tagbench PRIVATE /W4 /permissive-)
else()
  target_compile_options(tagbench PRIVATE -Wall -Wextra -Wpedantic)
endif()

enable_testing()
add_test(NAME tagbench_selftest COMMAND tagbench --selftest)
