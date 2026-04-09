---
description: Write or update a package README following README_STANDARD.md
argument-hint: <package-name>
---

Write or update the README for package `$ARGUMENTS` so that it fully conforms
to the conventions in `README_STANDARD.md` at the repo root.

## Target package

The argument `$ARGUMENTS` is the short package name (the part after
`com.rubickanov.`). Resolve the package directory as
`packages/com.rubickanov.$ARGUMENTS/`. If that folder does not exist, list the
available packages under `packages/` and ask the user which one they meant
instead of guessing.

## Required reading before you write anything

1. Read `README_STANDARD.md` in full — it is the source of truth for structure,
   tier classification, section order, formatting rules, anti-patterns, and
   templates.
2. Read `packages/com.rubickanov.$ARGUMENTS/README.md` if it exists. If it does
   not, you are creating a new one.
3. Read `packages/com.rubickanov.$ARGUMENTS/package.json` to get the
   `displayName`, `description`, and `dependencies`.
4. Inspect the package contents to understand the actual API surface:
   - Glob every `*.asmdef` under the package to enumerate assemblies and decide
     the Assemblies section (required only when there are 2+ assemblies).
   - Read the public types in `Runtime/` (and `Editor/`, `Unity/` if present).
     Prioritize interfaces, `public` classes, and `ScriptableObject` assets —
     these are what consumers interact with.
   - Note dependency packages used in `using` directives to cross-check
     `package.json` dependencies.
5. If a few sibling packages already have high-quality READMEs (e.g. `acs`,
   `gas`, `ui`, `audio`), skim one of them to match tone and code-example
   style for this repo.

## How to write the README

- Classify the package as **Core** or **Extension** per `README_STANDARD.md`
  ("Package Tiers" section). Extension packages are small addons to a core
  package (e.g. `acs.netcode`, `devconsole.netcode`, `ui.animations`) — they
  get the much shorter Extension template.
- Copy the appropriate template from `README_STANDARD.md` ("Templates"
  section) as the starting skeleton.
- Follow the exact section order from the "Section Order" table. Include every
  section marked **Required** for the tier; include optional sections only
  when they add information a reader would actually need.
- Every code block must use a language tag (`csharp`, `text`, etc.) and use
  realistic domain names (`health`, `poisonEffect`, `moveSpeed`) — never
  `foo` / `bar`.
- English prose. Code comments in code blocks may be Russian if the existing
  package code uses Russian comments.
- Re-read the **Anti-Patterns** section of `README_STANDARD.md` before
  finalizing and remove anything that matches (feature lists before
  explanation, "see `SomeClass` for details", inspector screenshots,
  changelog, dependencies at the bottom, etc.).

## Updating an existing README

If the README already exists:

- Diff it mentally against `README_STANDARD.md`. Do not do a full rewrite if
  the current content is already close to the standard — fix what is wrong
  and leave what is right.
- Common fixes: reorder sections to match the required order, add missing
  required sections, remove anti-pattern content, retag code blocks with
  `csharp`, rename `foo`/`bar` examples to domain-realistic ones, move
  Dependencies to the top if they were at the bottom.
- Preserve any accurate, useful prose the author already wrote — the goal is
  conformance, not erasure.

## Before finishing

Verify the final README against the checklist at the bottom of
`README_STANDARD.md` ("Applying This Standard"):

1. Is the tier classification correct?
2. Are all required sections present and in order?
3. Can a reader understand what the package does and use it without reading
   source code?

If the answer to #3 is "no", add the missing Usage examples before stopping.
