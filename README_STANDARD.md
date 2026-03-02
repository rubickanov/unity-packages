# README Standard

Style guide and template for package READMEs in this repository.

## Principles

- **Answer "what does this do?" in 10 seconds.** Title + one-line description must be enough.
- **Show, don't describe.** A code example beats a paragraph of explanation.
- **Realistic examples.** Use domain-relevant names (`health`, `poisonEffect`, `moveSpeed`), not `foo`/`bar`.
- **Concise, technical, no fluff.** No marketing language, no "powerful", "flexible", "easy-to-use".
- **English prose, code comments can be Russian.** README text is English since package names are English.

## Package Tiers

Not every package needs a 300-line README. Scale the depth to the package.

### Core Package

Standalone package with its own concepts and API surface. Most packages fall here.

**Expected size:** 50–300 lines depending on API complexity.

Examples: ACS, GAS, UI, DevConsole, GameplayTags, BehaviorTree, Audio, Storage, Loading, Localization, Logging, StateMachine, EQS, Character Motor, Steam Transport, Utils.

### Extension Package

Small addon that extends a core package. Adds a few types, no independent concepts.

**Expected size:** 20–60 lines.

Examples: ACS.Netcode, DevConsole.Netcode, UI.Animations.

## Section Order

Sections must appear in this order. Sections marked **required** must be present.

| # | Section | Core | Extension | Purpose |
|---|---------|------|-----------|---------|
| 1 | Title + Description | Required | Required | What this package does, in one sentence |
| 2 | Dependencies | Required | Required | Other packages this depends on |
| 3 | Architecture | Required | Skip | High-level structure: type hierarchy or data flow |
| 4 | Assemblies | If 2+ assemblies | If 2+ assemblies | Assembly table with engine refs and description |
| 5 | Core Concepts | When non-obvious | Skip | Key abstractions the user must understand |
| 6 | Quick Start | Required | Required | Minimal steps to get running |
| 7 | Usage | Required | Required | Primary API with code examples |
| 8 | Examples | Optional | Skip | Realistic scenarios beyond basic usage |
| 9 | Integration | Optional | Skip | How game code bridges this package |
| 10 | Design Decisions | Optional | Skip | "Why" behind non-obvious choices |
| 11 | File Structure | Optional | Skip | Directory tree for large packages |

**Extension packages** must name their parent in the description line (e.g., "Netcode extension for DevConsole") and list it first in Dependencies.

## Section Guidelines

### 1. Title + Description

```markdown
# Package Name

One sentence describing what this package does and its key characteristic.
```

- `# Title` is the human-readable name, not the `com.rubickanov.*` identifier.
- Description is one sentence. Mention the key differentiator if relevant.
- No badges, no shields, no version numbers.

Good:

```markdown
# Gameplay Ability System (GAS)

Data-driven gameplay effects system. Attribute modifiers with duration, periodicity, stacking, and tag-based conditions.
```

Bad:

```markdown
# com.rubickanov.gas

A powerful and flexible gameplay ability system for Unity that provides
an easy-to-use API for creating and managing gameplay effects.
```

### 2. Dependencies

```markdown
## Dependencies

- `com.rubickanov.gameplaytags` — tag-based attribute identification
- `UniTask` — async/await
```

- List `com.rubickanov.*` dependencies first, then third-party.
- Brief note on what each dependency is used for.
- Omit Unity engine itself (implied).
- If no dependencies, write "None".
- Unity version requirements go at the end of this section if needed.

### 3. Architecture

Show the type hierarchy or data flow. Use ASCII art for structure.

**Type hierarchy** (for packages with interface + implementations):

```markdown
## Architecture

​```
IAudioService
├── UnityAudioService    — AudioMixer-based, SFX source pooling
└── NullAudioService     — no-op for server/headless builds
​```
```

**Data flow** (for packages with a pipeline):

```markdown
## Architecture

​```
GameplayEffectAsset (ScriptableObject)
        │
        ▼
    EffectDef (immutable definition)
        │
        ▼
    EffectSpec (runtime instance)
        │
        ▼
  EffectController ──► ActiveEffect
​```
```

- Keep diagrams under 15 lines.
- Follow the diagram with a brief explanation only if the diagram is not self-explanatory.

### 4. Assemblies

Use a table when the package has 2 or more assemblies.

```markdown
## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **GAS.Runtime** | No | Core logic, pure C# |
| **GAS.Unity** | Yes | ScriptableObject wrappers, inspector integration |
| **GAS.Editor** | Editor | Custom inspectors, property drawers |
```

- Bold the assembly name.
- "Engine Refs" column: No / Yes / Editor.
- One-line description per assembly.

### 5. Core Concepts

Explain the 2–4 key abstractions a user must understand before using the API.

```markdown
## Core Concepts

**Aspect** — Pure data container. Only holds reactive fields and event signals.

**Component** — Single unit of behavior that reads and writes aspects.
```

- Bold term, em-dash, one-sentence definition.
- Follow with a code example if the concept has a non-obvious API.
- Skip this section if the package API is straightforward (e.g., Storage, Loading).

### 6. Quick Start

The shortest path from "I installed this" to "it works".

- Numbered steps for multi-step setup.
- One code block showing the minimal working example.
- For DI-based packages, show the container registration.
- Keep under 20 lines of prose + code.

### 7. Usage

The primary API reference with code examples. This is the most important section.

- Group by use case, not by class name. Headings describe what the user wants to do: "Playing SFX", "Removing Effects", "Reactive Binding".
- Every public API entry point gets a code example.
- Show DI registration if the package is consumed via dependency injection.
- Show both common case and edge cases where relevant (e.g., fire-and-forget vs awaited writes in Storage).

**Code example rules:**

- Use `csharp` language tag on all code blocks.
- Realistic names from a game domain.
- Minimal code that demonstrates the point — no boilerplate.
- Inline comments for non-obvious lines only. No comments for obvious lines.
- If a method returns a handle/token, show both "ignore it" and "use it" patterns.

### 8. Examples

Realistic scenarios that go beyond basic API calls. Best for packages with combinatorial usage (GAS effect configurations, BehaviorTree node compositions, GameplayTags matching).

- Each example gets a heading describing the scenario (e.g., "Poison DOT, 5 Seconds").
- Show the configuration/setup, then explain the behavior in 1–2 sentences.

### 9. Integration

How game code connects this framework package to entity/component/DI systems.

- Only needed for framework packages that are deliberately decoupled from game code.
- Show the bridge component or wiring code.
- One example is enough.

### 10. Design Decisions

Explain "why" for non-obvious architectural choices.

```markdown
## Design Decisions

- **IView has no Root property** — keeps the interface backend-agnostic. UIToolkitViewBase adds VisualElement Root.
- **UxmlLoader delegate instead of IAssetService** — avoids hard dependency on asset loading strategy.
```

- Bullet list. Bold the decision, em-dash, rationale.
- Only include decisions a reader would question. Skip obvious ones.

### 11. File Structure

Directory tree for packages with non-trivial folder organization.

```markdown
## File Structure

​```
com.rubickanov.gas/
├── Runtime/
│   ├── Attributes/
│   ├── Effects/
│   └── Calculation/
├── Unity/
└── Editor/
​```
```

- Omit `package.json`, `README.md`, `.meta` files — they are implied.
- Skip this section for packages with a flat structure.

## Formatting Rules

### Tables

Use tables for:
- Type catalogs (Key Types)
- Assembly listings
- Enum/policy breakdowns

Do not use tables for:
- Step-by-step instructions (use numbered lists)
- Prose explanations

### Emphasis

| Format | Use for | Example |
|--------|---------|---------|
| **Bold** | Type names in prose, decision headings | **EffectController** |
| `Code` | Method names, field names, parameter values, package IDs | `ApplyEffect()` |

Avoid *italic* in READMEs — it adds nothing over bold and is harder to scan.

### Code Blocks

- Always specify language: ```` ```csharp ````, ```` ```text ````.
- No line numbers.
- Max ~25 lines per block. Split longer examples with prose between them.

### Headings

- `#` — Package title only. One per file.
- `##` — Top-level sections (Architecture, Usage, Examples).
- `###` — Subsections within a top-level section.
- Do not use `####`. If you need it, restructure.

## Anti-Patterns

- **Feature list before explanation.** Don't start with a bullet list of features before the reader knows what the package does. Features are demonstrated through Usage and Examples.
- **"See `SomeClass` for details."** The README is the documentation. Don't defer to source code.
- **Duplicate content between Core Concepts and Usage.** Concepts define terms; Usage shows API calls.
- **Listing every public type.** Focus on types a consumer interacts with directly.
- **Screenshots of Inspector UI.** They rot when the UI changes and cannot be searched.
- **Changelog in README.** Use git history or a separate `CHANGELOG.md`.
- **Dependencies at the bottom.** Dependencies go near the top so the reader knows upfront what they need.

## Templates

### Core Package

```markdown
# Package Name

One-sentence description of what this package does.

## Dependencies

- `com.rubickanov.other` — what it is used for
- `ThirdParty` — what it is used for

## Architecture

​```
IServiceInterface
├── RealImplementation    — primary implementation
└── NullImplementation    — no-op for server builds
​```

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Pkg.Runtime** | No | Core logic |
| **Pkg.Unity** | Yes | Inspector integration |
| **Pkg.Editor** | Editor | Custom inspectors |

## Core Concepts

**Term** — Definition in one sentence.

## Quick Start

1. Register in your LifetimeScope.
2. Inject and use.

​```csharp
// Minimal working example
​```

## Usage

### Doing X

​```csharp
// Code showing how to do X
​```

### Doing Y

​```csharp
// Code showing how to do Y
​```

## Examples

### Scenario Name

​```csharp
// Realistic scenario
​```

Brief explanation of what happens.

## Design Decisions

- **Decision** — Rationale.
```

### Extension Package

```markdown
# Extension Name

One-sentence description. Extension for [Parent Package](../com.rubickanov.parent/).

## Dependencies

- `com.rubickanov.parent` — base package
- `com.unity.something` — what it is used for

## Quick Start

​```csharp
// How to enable/use the extension
​```

## Usage

​```csharp
// Primary usage example showing what the extension adds
​```
```

## Applying This Standard

When writing or updating a README:

1. Classify the package as Core or Extension.
2. Copy the appropriate template.
3. Fill in required sections. Delete optional sections that don't apply.
4. Verify: can a reader understand what the package does and use it without reading source code?
