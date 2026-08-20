#define main tag_gremlin_native_main
#include "native_sim.cpp"
#undef main

int main(int argc, char **argv) {
    if (argc < 2) return 2;
    World w = loadWorld(argv[1]);
    (void)w;
    return 0;
}
