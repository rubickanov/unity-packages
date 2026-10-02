# Known issues

Found in a review on 2026-10-02; not fixed, since no game uses the package yet. Fix before adopting it.

- **Pruned candidates are missing from the result.** `EQSQuery.cs:242-252` marks dominated items dead and
  `BuildResult` (`:303`) skips them, so `TopN` (`EQSQueryResult.cs:47`) and the debugger's gradient lose every
  place after the first. `EQSQueryTests.cs:187-208` lock this behaviour in. Keep pruned items out of the winner
  check only, not out of the result.
- **Pruning is unsound in three cases.** The cutoff (`EQSQuery.cs:231-250`) assumes each later test adds at most
  its weight:
  - a test scoring above 1 only logs a warning (`:216`) and is not clamped;
  - a negative weight is summed with its sign (`:239`), and `EQSTest._weight` (`EQSTest.cs:14`) is not clamped;
  - a later test with Normalize on takes min and max over the survivors only (`:197-206`), so pruning can change
    the winner. Example: after test 1, A 1.0, B 0.6, C 0.1; test 2 has weight 0.5 and raw A .5, B .6, C 0.
    Without pruning A wins (1.42 vs 1.1); with C pruned B wins (1.1 vs 1.0).
  Use absolute weights for the bound and prune only when no later test normalizes.
- **`LineOfSightTest` blocks on the target itself** (`:34`, `:96`): any hit counts, including the target's own
  collider, so items from `SphereOverlapGenerator` (aimed at `bounds.center`) always fail. Skip `item.Object`.
- **`SphereOverlapGenerator`** returns one item per collider (an object with several colliders appears several
  times), excludes only the querier's root and not its child colliders (`:35`), and stops at 32 results silently.
- **No NavMesh generator**; one exists only as a sample in `README.md`.
