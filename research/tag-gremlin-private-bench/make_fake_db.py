#!/usr/bin/env python3
import argparse
import random

ALPHABET = "abcdefghijklmnopqrstuvwxyz0123456789."
WORDS = [
    "red", "blue", "green", "hair", "eyes", "cat", "dog", "artist", "sky", "night",
    "long", "short", "soft", "hard", "city", "forest", "magic", "robot", "retro", "space",
    "alpha", "beta", "gamma", "delta", "zero", "one", "two", "three", "moon", "star",
]

def make_name(rng, i):
    mode = rng.randrange(7)
    if mode == 0:
        return rng.choice(WORDS)
    if mode == 1:
        return rng.choice(WORDS) + "." + rng.choice(WORDS)
    if mode == 2:
        return "." + rng.choice(WORDS) + str(i % 97)
    if mode == 3:
        return rng.choice(WORDS) + "."
    if mode == 4:
        return rng.choice(WORDS) + ".." + rng.choice(WORDS)
    if mode == 5:
        return ".." + rng.choice(WORDS) + "." + str(i % 31)
    n = rng.randint(2, 12)
    return "".join(rng.choice(ALPHABET) for _ in range(n))

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--count", type=int, default=1200)
    ap.add_argument("--seed", type=int, default=90210)
    args = ap.parse_args()
    rng = random.Random(args.seed)
    names = {".hazz", "long.hair", "black.hair", "foo..bar", "trailing.", "..."}
    i = 0
    while len(names) < args.count:
        names.add(make_name(rng, i))
        i += 1
    rows = []
    for i, name in enumerate(sorted(names)):
        popularity = 1 + rng.randrange(max(5, args.count // 8))
        rows.append((popularity, name))
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        for pop, name in rows:
            f.write(f"{pop}\t{name}\n")

if __name__ == "__main__":
    main()
