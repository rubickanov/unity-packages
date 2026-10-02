# Behavior Tree

Serializable behavior tree framework for Unity. Visual graph editor, blackboard, subtrees, and `[SerializeReference]`-based polymorphic serialization.

## Dependencies

None.

## Architecture

```
BehaviorTreeAsset (ScriptableObject, serialized graph)
        │
        │ CreateInstance()
        ▼
    BTNode (cloned runtime tree)
        │
        │ Tick(BTContext)
        ▼
    BTStatus { Success, Failure, Running }
```

**BehaviorTreeRunner** owns a runtime tree clone and its **Blackboard**. Call `Tick()` each frame to drive execution.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **BehaviorTree.Runtime** | Yes | Nodes, blackboard, runner, tree asset |
| **BehaviorTree.Editor** | Editor | Visual graph editor, node search window, auto-layout |

## Core Concepts

**BTNode** — Abstract base class for all nodes. Serialized via `[SerializeReference]`. Override `OnTick(BTContext)` to implement logic. Returns **BTStatus** (Success, Failure, Running).

**Blackboard** — Typed key-value store shared across all nodes during a tick. Nodes communicate through `BlackboardKey<T>` instead of direct references.

**BTContext** — Per-tick struct passed to every node: `Owner`, `Blackboard`, `DeltaTime`, `Time`, `Tick`.

**BehaviorTreeAsset** — ScriptableObject holding the serialized node graph. `CreateInstance()` returns a deep-cloned runtime copy.

## Quick Start

1. Create a tree asset: **Assets > Create > AI > Behavior Tree**.
2. Open the visual editor (double-click the asset) and build the graph.
3. Add **BehaviorTreeRunner** to a GameObject and assign the asset.
4. Tick the runner from your update loop:

```csharp
_runner.Tick(owner: this, deltaTime: Time.deltaTime, tick: _tick++);
```

## Usage

### Custom Actions

Subclass **BTLeafAction** and override `OnExecute()`. Mark with `[BTNodeDescription]` to appear in the editor search window:

```csharp
[Serializable]
[BTNodeDescription("Find Target", "Actions", "Finds the nearest enemy within range.")]
public class BTFindTarget : BTLeafAction
{
    [SerializeField] private float _detectionRange = 10f;

    protected override BTStatus OnExecute(BTContext ctx)
    {
        var owner = (AIController)ctx.Owner!;
        var target = owner.FindNearest(_detectionRange);

        if (target == null)
            return BTStatus.Failure;

        ctx.Blackboard.Set(AIKeys.Target, target);
        return BTStatus.Success;
    }
}
```

### Custom Conditions

Subclass **BTLeafCondition** and override `OnEvaluate()`. Returns `true` for Success, `false` for Failure:

```csharp
[Serializable]
[BTNodeDescription("Has Target", "Conditions", "Checks if a target exists on the blackboard.")]
public class BTHasTarget : BTLeafCondition
{
    protected override bool OnEvaluate(BTContext ctx)
    {
        return ctx.Blackboard.Has(AIKeys.Target);
    }
}
```

### Blackboard Keys

Define keys as static fields. The type parameter ensures type-safe `Set`/`Get`:

```csharp
public static class AIKeys
{
    public static readonly BlackboardKey<Transform> Target = new("Target");
    public static readonly BlackboardKey<Vector3> MoveDestination = new("MoveDestination");
    public static readonly BlackboardKey<float> AlertLevel = new("AlertLevel");
}
```

```csharp
ctx.Blackboard.Set(AIKeys.Target, enemy);
var target = ctx.Blackboard.Get(AIKeys.Target);

if (ctx.Blackboard.TryGet(AIKeys.AlertLevel, out var level))
    /* use level */;
```

### Code-Built Trees

For trees assembled in code (not serializable), use **BTAction**, **BTCondition**, and composite constructors directly:

```csharp
var tree = new BTSequence(
    new BTCondition(ctx => ctx.Blackboard.Has(AIKeys.Target)),
    new BTAction(ctx =>
    {
        var target = ctx.Blackboard.Get(AIKeys.Target);
        /* chase logic */
        return BTStatus.Running;
    })
);
```

### Initializing the Runner

From an asset (typical):

```csharp
[SerializeField] private BehaviorTreeRunner _runner = default!;

void Start()
{
    _runner.EnsureInitialized();
}
```

Directly with a root node:

```csharp
_runner.Initialize(myRootNode);
```

### Built-in Node Types

**Composites:**

| Node | Behavior |
|------|----------|
| **BTSequence** | Ticks children left-to-right. Fails on first failure, succeeds when all succeed. Resumes from running child. |
| **BTSelector** | Ticks children left-to-right. Succeeds on first success, fails when all fail. Aborts previous running child on switch. |

**Decorators:**

| Node | Behavior |
|------|----------|
| **BTInverter** | Flips child result (Success becomes Failure and vice versa). |
| **BTCooldown** | Blocks child execution until a configurable duration expires after last completion. |

**Subtree:**

| Node | Behavior |
|------|----------|
| **BTSubtree** | Runs another **BehaviorTreeAsset** as a nested subtree. Clones on first tick. |

**BTSubtree** runs the nested asset on the **same** `BTContext` as its parent — the
subtree shares the parent's `Owner` and `Blackboard`. There is no isolated blackboard:
subtree keys and parent keys live in one namespace and can overwrite each other.

## Examples

### Patrol-or-Chase AI

```csharp
[Serializable]
[BTNodeDescription("Move To", "Actions", "Moves owner toward blackboard target.")]
public class BTMoveTo : BTLeafAction
{
    [SerializeField] private float _arrivalDistance = 0.5f;

    protected override BTStatus OnExecute(BTContext ctx)
    {
        var owner = (AIController)ctx.Owner!;

        if (!ctx.Blackboard.TryGet(AIKeys.MoveDestination, out var destination))
            return BTStatus.Failure;

        if (Vector3.Distance(owner.Position, destination) < _arrivalDistance)
            return BTStatus.Success;

        owner.MoveToward(destination, ctx.DeltaTime);
        return BTStatus.Running;
    }
}
```

The visual editor composes this into a selector: try chase (if target exists), otherwise patrol.

## Design Decisions

- **`[SerializeReference]` over ScriptableObject-per-node** — the entire tree lives in a single asset file. No folder explosion, simpler version control.
- **BTContext is a struct** — avoids allocation per tick. Passed by value to every node.
- **Clone on CreateInstance** — runtime trees are independent copies. Multiple agents can share the same asset without state interference. Clones keep the source node GUIDs so the editor can match runtime nodes to their views.
- **BTLeafAction/BTLeafCondition split** — conditions return `bool` (cleaner API) while actions return **BTStatus** (supports Running).
- **Blackboard stores values as `object`** — value types (`float`, `Vector3`, …) box on `Set`. A deliberate trade-off for a simple type-safe API without reflection or codegen; keys per agent are few and `Set` is rarely called every tick for every key.

## Play-Mode Visualization

Open the editor window on the asset a **BehaviorTreeRunner** is running and node
states are highlighted live during play mode: running (yellow), success (green),
failure (red). The window picks the first runner in the scene whose assigned asset
matches the open one.
