---
description: Rigorous audit of a package — find every real bug, convention violation, and improvement, then write a batched work plan to issues.md
argument-hint: <package-name>
---

Audit the package `com.rubickanov.$ARGUMENTS` end-to-end — implementation,
public API, tests, README, `package.json`, editor tooling — and produce a
structured work plan at `packages/com.rubickanov.$ARGUMENTS/issues.md`.

**Language rule (critical):** This prompt is English. Your user-facing prose
AND the contents of `issues.md` must be written in **Russian** — that is the
established convention for audit files in this repo (see recent
`cacc97e:packages/com.rubickanov.localization/issues.md`). Code, file paths,
XML-doc examples, and technical identifiers stay in their original form.

## 1 · Resolve target

`$ARGUMENTS` is the short name after `com.rubickanov.`. Resolve the package
to `packages/com.rubickanov.$ARGUMENTS/`. If that directory does not exist,
list the packages under `packages/` and ask the user which one they meant —
do not guess.

## 2 · Required reading (do this before making any claims)

1. `CLAUDE.md` at the repo root — working rules, LINQ policy, test rules.
2. `README_STANDARD.md` — tier classification, required sections, anti-patterns.
3. The target package in full:
   - Every file under `Runtime/` (and `Runtime.*/` siblings if present).
   - Every file under `Editor/` if present.
   - Every file under `Tests/` if present.
   - `package.json` + every `.asmdef`.
   - Existing `README.md`.
   - Existing `issues.md` if one is already there (the audit may be a
     re-audit; do not re-file findings that were resolved unless they
     regressed).
4. Skim **at least one** recent audit as a format reference — the
   `com.rubickanov.localization` audit at commit `cacc97e` is the freshest.
   Also skim `com.rubickanov.gas/issues.md` if it is still on disk for a
   larger example.
5. Skim 1–2 sibling packages of comparable role so you can notice
   inconsistencies with repo-wide patterns (null-service shape, reactive
   property exposure, disposed-guard style, logger injection, etc.).

## 3 · What to check

This is the minimum surface. Expand wherever the package has unusual
responsibilities (editor tooling, networking, codegen, etc.).

### 3.1 Real bugs & correctness
- Race conditions, fire-and-forget exceptions being swallowed, ordering of
  event subscription vs. mutation, async methods that don't actually await
  the thing they claim to await.
- `IDisposable` correctness — every subscription, reactive property, and
  external event handler released; double-dispose safety; disposed-guard on
  methods that mutate state.
- Null-service / headless variant parity with the real service (same
  interface surface, same dispose semantics, same observable shapes).
- Silent `catch` blocks — exceptions must be logged at the right level or
  rethrown.
- Argument validation at public boundaries (`ArgumentException` / `ArgumentNullException`
  for nulls, empty strings, invalid enums). Private methods trust their
  callers.
- Thread-safety of anything touched from background tasks / Unity jobs /
  netcode callbacks.
- Reactive property contracts — are `Observable<T>` properties returning a
  shared instance, or allocating per-getter?

### 3.2 Allocations & hot paths (CLAUDE.md LINQ policy)
- `System.Linq` in `Runtime/` — flag unless it is a documented cold-path
  (scan/reflection/spawn, cached, runs ≤ once per type per session).
- Per-call allocations in getters / Update-like paths: `new[]`, closures,
  `string.Split`, `Regex` without `RegexOptions.Compiled`, boxed enumerators,
  LINQ-less-but-still-allocating patterns.
- `static readonly` candidates that are currently expression-bodied
  properties or instance fields.

### 3.3 API design & consistency
- Public types `sealed` where they should be; `internal` types not leaking.
- Dependencies injected via constructor vs. `FindObjectOfType` / singletons.
- Consistency with sibling packages (same DI style, same logger injection,
  same null-pattern).
- Public `IEnumerable<T>` on collections (not `List<T>`) — see
  `feedback_no_linq.md`: keep `IEnumerable<T>` on public collections for test
  flexibility.

### 3.4 Tests
- Present? If the package has no `Tests/`, that is itself a finding — note
  what the highest-value tests would cover.
- `AssemblyInfo.cs` / `*.asmdef` correctly gated: `UNITY_INCLUDE_TESTS` +
  `includePlatforms: [Editor]`.
- AAA structure, `Method_Scenario_ExpectedBehavior` naming, one behavior per
  test, per-test fixtures when SUT has static state.
- Coverage gaps for public API (don't demand 100% — demand that the
  non-trivial branches have a test).

### 3.5 README conformance
- Tier classification correct (Core vs Extension).
- All required sections present and in the required order.
- No anti-patterns (feature list before explanation, "see `SomeClass` for
  details", inspector screenshots, changelog, dependencies at the bottom,
  `foo`/`bar` examples).
- Code blocks have `csharp` / `text` language tags.
- Prose in English; code comments may be Russian if the package code uses
  Russian comments.
- Every public entry point a consumer would touch has at least one usage
  example.

### 3.6 `package.json` & assembly definitions
- `dependencies` lists every `com.rubickanov.*` package referenced by
  any `.asmdef` in the package. Missing declarations = UPM breakage for
  consumers.
- `displayName` / `description` match what the package actually is.
- Version string is consistent with repo baseline (default `1.0.0` unless
  the package has documented otherwise — e.g., statemachine is `2.0.0`).
- `.asmdef` `references` match `using` directives; no unused references;
  `autoReferenced`, `allowUnsafeCode`, `defineConstraints` set intentionally.

### 3.7 Editor tooling (if `Editor/` exists)
- Asset detection uses real type checks (`AssetDatabase.LoadAssetAtPath<T>`),
  not substring matching on paths.
- `EditorApplication.delayCall` / `AssetPostprocessor` batches are
  de-duplicated across bulk imports.
- Regex is compiled once (`static readonly Regex … RegexOptions.Compiled`).
- Reflection / codegen is cached per-type-per-session.

## 4 · How to classify & batch findings

Three severity buckets, using the exact headings from prior audits:

- **Критические (реальные баги)** — Will produce wrong behavior, crashes, or
  data loss in plausible real usage. If none, write "Нет." and one sentence
  of justification.
- **Мажорные (M)** — Correctness/consistency issues that are not yet
  user-visible bugs but will become one. Number them `M1`, `M2`, …
- **Минорные (m)** — Allocations, style, API polish, docs conformance.
  Number them `m1`, `m2`, …

**Batching inside each bucket:** group findings that share a root cause or
would be fixed in one sitting. Ordering rules, in priority:

1. Same file / same subsystem → adjacent.
2. Same class of problem (e.g., all async-contract bugs, all allocation hot
   paths, all README anti-patterns) → adjacent.
3. Easier / smaller fixes later within a bucket — so the reader can see
   structural work first.

Cross-references are encouraged: if `M3` only matters after `M1` is fixed,
say so in the body.

## 5 · Entry format

Each finding follows the format established in prior audits:

```markdown
- **M1. Short imperative headline** — `relative/path/File.cs:Ln-Ln`
  One paragraph explaining the actual defect and why it matters. Cite the
  exact symbol / branch. No hand-waving.
  **Решение:**
  1. Concrete step, or
  2. A short code snippet showing the corrected pattern.
  **НЮАНС / АЛЬТЕРНАТИВА:** (only when relevant) a second viable path and
  the tradeoff.
```

Rules:
- File paths are relative to the package root.
- Line ranges must be real — open the file and read them.
- Every finding has a concrete `**Решение:**` block. "Надо подумать" is not
  a resolution — if you truly do not have a preferred fix, escalate to
  step 6 instead of writing a vague resolution.
- Russian prose, English identifiers and code.

## 6 · Decisions that need the user

Some findings have multiple reasonable fixes with meaningful tradeoffs
(breaking API change vs. additive, new dependency vs. hand-rolled, migration
strategy for persisted data, etc.). For those:

1. Do **not** pre-commit a choice in the file.
2. Before writing `issues.md`, list each open decision with:
   - The finding ID (`M4`, `m7`, …).
   - 2–3 options with the tradeoff for each.
   - Your recommendation and a one-line rationale.
3. Ask the user, in Russian, which option to lock in. Block on their answer.
4. Once they answer, record the locked-in choices at the top of
   `issues.md` under a **"Зафиксированные решения по ключевым
   неопределённостям"** block, mirroring the localization audit.

Do not ask the user about findings where the fix is unambiguous — that is
noise. The bar is "a competent engineer would reasonably pick differently".

## 7 · Output file structure

Write to `packages/com.rubickanov.$ARGUMENTS/issues.md` using this skeleton:

```markdown
# {Human Package Name} Package — Issues & Work Plan

Результаты аудита пакета `com.rubickanov.{short-name}`. Документ отслеживает
все найденные проблемы и порядок их исправления. Ломающие изменения
публичного API допустимы — делаем правильно.

**Зафиксированные решения по ключевым неопределённостям:**
- **{ID} ({краткое описание}):** {locked-in choice from step 6}
- …

---

## Находки

### Критические (реальные баги)

{findings or "Нет." + justification}

### Мажорные (M)

- **M1. …** — `path:Lx-Ly`
  …

### Минорные (m)

- **m1. …** — `path:Lx-Ly`
  …
```

If the package already has an `issues.md`, overwrite it only after
explicitly telling the user which findings from the old file are being
dropped (resolved) and which are being carried over.

## 8 · Final turn

After writing the file, give the user a short Russian summary:

- Counts per severity bucket.
- Top 3 things you would fix first and why.
- Any decisions you asked about and how they were resolved.

Do **not** start fixing the code in the same turn. This command produces
the plan only; the user drives execution against `issues.md` afterwards.
