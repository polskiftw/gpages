# Algorithm notes: deterministic hierarchical 3D chunk tree

## Model

A finite world has `W x H x F` cells. Every cell has up to six face-adjacent
neighbors: N, E, S, W, Up, Down. Floors are an integral Z coordinate; no diagonal
or teleport edges exist in the base graph. Chunks are `32 x 32 x 1` cells,
including smaller partial chunks along the world boundary. Their abstract chunk
graph is a rectangular `ceil(W/32) x ceil(H/32) x F` 3D lattice.

**Mandatory macro tree:** Given box B of chunk coordinates:

- If B has one chunk, it is trivially connected.
- Otherwise, select one nontrivial axis with the greatest span (seeded tie break).
- Choose a split in roughly the middle third of its length, leaving nonempty
  boxes L and R. Use a hash of `(world seed, B bounds, decision tag)`.
- Select one pair of face-adjacent chunks crossing that split plane, using hashes
  of the same box bounds and separate decision tags.
- Repeat on L and R until each contains a single chunk.

The mandatory edge for a queried *adjacent chunk pair* is determined by following
only their common recursion path, until the box first separates them. The edge
exists iff it is the one selected portal at that split. No global graph is stored.
This takes at most logarithmic recursion depth for the balanced splits.

**Local chunk tree:** Each chunk generates a standard iterative depth-first maze
on its tile grid. The root starts visited, random unvisited neighbors are carved,
and the iterative stack backtracks. Exactly `number_of_tiles - 1` local edges
are carved. RNG is seeded only from the world seed and the chunk coordinates.

**Doorways:** Every mandatory adjacent macro pair opens one deterministic cell
edge on their common horizontal boundary. If the macro pair differs in Z, it
opens one `Up/Down` connection at the same local X/Y location on both floors.
Additional horizontal macro edges (one doorway each) and local loops are
controlled by `loop_bps`. Additional direct floor-to-floor shafts at individual
cells are controlled by `stair_bps`. They are independent keyed decisions.

## Why it is always connected

The base case (one macro chunk) is a connected graph with no macro edges.
Inductively, if the left and right child boxes each contain a spanning tree,
adding exactly one face-adjacent edge between them yields a spanning tree of
the parent box. Thus the macro graph is a tree of *all* chunks on *all* floors.
Each chunk's cell-level maze is also a spanning tree. Joining those local trees
along the macro tree with one physical passage per macro edge constructs a
spanning tree over *every cell* in the whole world.

With `loop_bps=stair_bps=0` the total number of edges is therefore exactly
`W*H*F - 1`. Enabling either option adds edges but never deletes the base edges,
so the resulting graph stays connected. This is why simply generating unrelated
random mazes in different chunks and randomly connecting seams is not safe;
it can leave disconnected regions unless connectivity is explicitly maintained.

## Determinism and boundaries

Use specified 64-bit SplitMix-style avalanche hashing, not language-runtime
`std::hash<string>`, nondeterministic seeds, global mutable RNG, or dependency on
chunk loading order. The parent box hash determines all its decisions. The
canonical ordered *pair of adjacent chunk coordinates* hashes the location of
its horizontal seam doorway or vertical portal. The local chunk seed determines
its iterative DFS. Each vertical extra shaft uses the global XY tile coordinate
and *lower* Z floor, which both layers independently agree upon.

Consequently, requesting chunks in different orders or evicting them does not
change the world. Partial chunks share their boundary lengths exactly. World
outer boundaries never have openings. The test suite verifies these invariants.

**Changing W, H, or F changes the root box**; that is intentionally not a
world-resizing API. Full reproducibility requires keeping the exact algorithm
version and all world settings. The algorithm is deterministic for a given build
and fixed parameters, but long-term compatibility requires versioned saves.

## Performance characteristics

Let `S = 32`, `M = ceil(W/S)*ceil(H/S)*F` (conceptual chunks), and let `K` be
the configured number of cached chunks. For one chunk:

- Local DFS and extra openings: O(S^2) time and O(S^2) temporary memory.
- Inspect six potential macro neighbors: O(log M) decisions in a balanced tree,
  with a constant number of 64-bit hashes per level.
- Retained passage map: at most 1024 bytes per full chunk, plus container and
  LRU bookkeeping overhead. Overall live memory is O(K*S^2 + S^2).
- No O(W*H*F) allocation or initialization takes place in normal generation.

The `check` command is **deliberately different**: it explicitly runs whole-world
BFS for a small finite maze to validate the algorithm and uses O(W*H*F) memory.
Do not run global exact shortest-path or coverage algorithms on massive worlds
without a separately designed streaming or hierarchical navigation approach.

## Known structural biases and future improvements

Recursive bisection produces macroscopic chokepoints, often only one between
large subregions. This is a feature for very difficult mazes but can look
unnatural if every region is separated by a prominent straight wall. Loops make
alternatives; later styles might add irregular region boundaries, more than one
portal per chosen divide, corridor-length controls, landmarks, secret doors,
rooms, or varying topology per floor. These must preserve the base spanning tree
or revalidate connectivity if edges are removed.

The 2D chunk DFS favors long winding paths and is not a uniform spanning tree.
Wilson's algorithm would be suitable as an *alternative local chunk generator*,
but is not required for streaming correctness. A purely global Wilson run would
not allow independent random-access chunk generation at astronomical sizes.

## The vertical geometry contract

Each up/down bit means `same (x,y), adjacent z`, not that a staircase mesh is
already created. The renderer should treat these positions as reserved stair
shafts and generate adequate portals. Physical stairs that require horizontal
run length or a 2x2 footprint need a later deterministic placement rule that
preserves topology and collision accessibility. The first prototype can use a
functional ladder/elevator or centered stair tile; the visuals can remain
faithful to the Windows 95 screensaver style.