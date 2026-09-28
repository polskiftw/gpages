# Parametric generalization: every response cap C >= 3

The concrete `C=4` witness in `THEOREM.md` is already sufficient to disprove a policy claimed to work for **all** finite caps. This note strengthens the result: the same stopping-horizon contradiction can be constructed for every finite cap `C >= 3`, including `C=40`.

## Construction

Fix any integer `C >= 3`.

Use a finite alphabet containing distinct atomic symbols

```text
a, p, c, d,
α_1 ... α_(C-1),
γ_1 ... γ_C,
δ_1 ... δ_C.
```

Construct two worlds.

Common immediate-yield tags:

```text
A_i = a α_i          for i = 1 ... C-1
```

Markers:

```text
P0 = p c
P1 = p d
```

World-specific exploit tags:

```text
G_i = c γ_i          for i = 1 ... C
H_i = d δ_i          for i = 1 ... C.
```

Then

```text
D0 = {A_1 ... A_(C-1), P0, G_1 ... G_C}
D1 = {A_1 ... A_(C-1), P1, H_1 ... H_C}.
```

Each world has exactly `2C` tags.

Choose deterministic priorities so all `G_i` outrank `P0` in `D0`, and all `H_i` outrank `P1` in `D1`. The relative order of the other tags is irrelevant.

The critical responses are

```text
q = a:
    D0 -> C-1 common A-tags
    D1 -> C-1 common A-tags

q = p:
    D0 -> [P0]
    D1 -> [P1]

q = c:
    D0 -> C G-tags       (P0 is capped out)
    D1 -> []

q = d:
    D0 -> []
    D1 -> C H-tags       (P1 is capped out).
```

Because every suffix symbol is unique to exactly one tag, no other nonempty substring creates a hidden common high-yield query.

## Small stopping target

Let

```text
K_small = C - 1.
```

Since `C >= 3`, `K_small >= 2`.

Query `a` discovers `C-1` tags in either world in one request. No other query can return `C-1` tags in both worlds. Therefore

```text
OPT(K_small) = 1
```

and exact optimality uniquely forces first query `a`.

## Larger stopping target

Let

```text
K_large = C + 1.
```

One query cannot reach `C+1` because the response cap is `C`, so at least two requests are necessary.

Query `p` first. It returns `P0` or `P1`, identifying the world. Then query `c` in `D0` or `d` in `D1`. The second query returns `C` new tags, for a total of `C+1`.

Thus

```text
OPT(K_large) = 2.
```

Now consider any first query other than `p`.

- If it is world-specific, one world returns nothing. Even after the second request that branch can contain at most `C` discoveries, fewer than `C+1`.
- If it has the same response in both worlds, it does not identify which exploit side is live. The best common immediate query is `a`, yielding `C-1`. A second `p` query reaches only `C`; guessing `c` or `d` fails in one world; every other query is weaker.
- By construction, no other query has nonempty distinct responses in both worlds.

Therefore exact two-request optimality for `K_large` uniquely forces first query `p`.

The same target-oblivious first action cannot be both `a` and `p`. Hence exact universal anytime optimality is impossible for every finite cap `C >= 3`.

## Arbitrary finite dataset sizes

For any `N >= 2C`, let `m = N - 2C` and pad the worlds asymmetrically with strings over two fresh symbols:

```text
D0(N) = D0 ∪ {e, ee, ..., e^m}
D1(N) = D1 ∪ {f, ff, ..., f^m}.
```

The worlds remain equal in size. Padding queries are one-sided and therefore cannot repair the `K_large` empty-branch bound; they also cannot beat `a` on `K_small` in both worlds.

So the contradiction persists for every finite `N >= 2C`.

For the live-site-sized cap `C=40`, the conflicting targets can be chosen as

```text
K_small = 39
K_large = 41,
```

and the base witness needs only 80 tags per world. It can then be padded to 330,000, 330,000,000, or any other finite size `N >= 80` without changing the contradiction.

## Consequence

The impossibility is not an artifact of choosing cap four. It is a structural exploration-versus-immediate-yield conflict present across the entire capped-retrieval family for every `C >= 3`.
