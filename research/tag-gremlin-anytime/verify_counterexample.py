#!/usr/bin/env python3
"""Mechanical verifier for the Tag Gremlin anytime-optimality counterexample.

This is not the proof itself; THEOREM.md is. The script exhaustively enumerates every
productive nonempty substring query in the concrete worlds and checks the two claims
used by the proof:

* K=3: only first query 'a' can guarantee completion in one request.
* K=5: only first query 'p' admits an adaptive second query that guarantees completion
  in two requests.

It also checks padded instances D0(m), D1(m) for representative m values.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, Iterable, List, Mapping, Sequence, Set, Tuple

CAP = 4
WorldName = str
Tag = str
Query = str
Response = Tuple[Tag, ...]


@dataclass(frozen=True)
class World:
    tags: Tuple[Tag, ...]
    priority: Tuple[Tag, ...]  # highest first

    def response(self, query: Query) -> Response:
        matches = [tag for tag in self.priority if query in tag]
        return tuple(matches[:CAP])


def build_worlds(padding: int = 0) -> Dict[WorldName, World]:
    if padding < 0:
        raise ValueError("padding must be non-negative")

    base0 = ("a0", "a1", "a2", "pc", "c4", "c5", "c6", "c7")
    base1 = ("a0", "a1", "a2", "pd", "d8", "d9", "dx", "dy")

    pad0 = tuple("e" * i for i in range(1, padding + 1))
    pad1 = tuple("f" * i for i in range(1, padding + 1))

    # Padding is placed below the base witness in the global priority order. It does
    # not contain any of a/p/c/d and therefore cannot alter the critical responses.
    prio0 = ("c4", "c5", "c6", "c7", "a0", "a1", "a2", "pc") + pad0
    prio1 = ("d8", "d9", "dx", "dy", "a0", "a1", "a2", "pd") + pad1

    return {
        "D0": World(tags=base0 + pad0, priority=prio0),
        "D1": World(tags=base1 + pad1, priority=prio1),
    }


def nonempty_substrings(text: str) -> Set[str]:
    return {
        text[i:j]
        for i in range(len(text))
        for j in range(i + 1, len(text) + 1)
    }


def productive_queries(worlds: Mapping[WorldName, World]) -> List[Query]:
    # Any query outside this set returns [] in every world, so it cannot outperform
    # a productive query for either target considered here.
    out: Set[str] = set()
    for world in worlds.values():
        for tag in world.tags:
            out.update(nonempty_substrings(tag))
    return sorted(out)


def one_step_guarantees(
    worlds: Mapping[WorldName, World], queries: Sequence[Query], target: int
) -> List[Query]:
    good: List[Query] = []
    for query in queries:
        if all(len(set(world.response(query))) >= target for world in worlds.values()):
            good.append(query)
    return good


def first_queries_with_two_step_adaptive_guarantee(
    worlds: Mapping[WorldName, World], queries: Sequence[Query], target: int
) -> Dict[Query, Dict[Response, List[Query]]]:
    """Return first queries for which every response branch has a valid second query.

    The second query may depend on the observed first response, exactly as an adaptive
    policy is allowed to do.
    """

    winners: Dict[Query, Dict[Response, List[Query]]] = {}

    for q1 in queries:
        branches: Dict[Response, List[WorldName]] = {}
        for name, world in worlds.items():
            branches.setdefault(world.response(q1), []).append(name)

        branch_choices: Dict[Response, List[Query]] = {}
        possible = True

        for first_response, candidate_names in branches.items():
            discovered = set(first_response)
            valid_seconds: List[Query] = []

            for q2 in queries:
                if q2 == q1:
                    continue
                if all(
                    len(discovered | set(worlds[name].response(q2))) >= target
                    for name in candidate_names
                ):
                    valid_seconds.append(q2)

            if not valid_seconds:
                possible = False
                break
            branch_choices[first_response] = valid_seconds

        if possible:
            winners[q1] = branch_choices

    return winners


def verify(padding: int) -> None:
    worlds = build_worlds(padding)
    queries = productive_queries(worlds)

    first_for_3 = one_step_guarantees(worlds, queries, target=3)
    first_for_5 = first_queries_with_two_step_adaptive_guarantee(
        worlds, queries, target=5
    )

    assert first_for_3 == ["a"], (
        f"padding={padding}: expected unique K=3 first query 'a', got {first_for_3}"
    )
    assert set(first_for_5) == {"p"}, (
        f"padding={padding}: expected unique K=5 first query 'p', "
        f"got {sorted(first_for_5)}"
    )

    p_branches = first_for_5["p"]
    assert p_branches == {("pc",): ["c"], ("pd",): ["d"]}, (
        f"padding={padding}: unexpected adaptive p branches: {p_branches}"
    )

    print(
        f"PASS padding={padding:>2}  size={8 + padding:>2}  "
        f"productive_queries={len(queries):>3}  K3={{a}}  K5={{p}}"
    )


def main() -> None:
    for padding in (0, 1, 2, 5, 10, 25, 50):
        verify(padding)

    print("\nCounterexample verified.")
    print("Formal all-N padding argument: see THEOREM.md section 6.")


if __name__ == "__main__":
    main()
