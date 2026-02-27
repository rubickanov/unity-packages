# Behavior Tree

Serializable behavior tree framework for Unity with a visual graph editor. Zero external dependencies.

## Core Concepts

**BTNode** — Abstract base class for all nodes. Serialized via `[SerializeReference]` for polymorphic storage in a single ScriptableObject.

**Blackboard** — Typed key-value store shared across all nodes during a tick. Nodes communicate through `BlackboardKey<T>` instead of direct references.

**BTContext** — Per-tick struct passed to every node: owner reference, blackboard, delta time, elapsed time, tick counter.

**BehaviorTreeAsset** — ScriptableObject that holds the serialized node graph. Call `CreateInstance()` to get a cloned runtime copy.

**BehaviorTreeRunner** — MonoBehaviour that owns a runtime tree instance and its blackboard. Call `Tick()` each frame.

## Package Structure

| Assembly | Description | Dependencies |
|---|---|---|
| `BehaviorTree.Runtime` | Core framework (nodes, blackboard, runner) | Unity only |
| `BehaviorTree.Editor` | Visual graph editor, inspector, search window | BehaviorTree.Runtime |

## Node Types

### Composites
- **BTSequence** — Ticks children left-to-right. Fails on first failure, succeeds when all succeed.
- **BTSelector** — Ticks children left-to-right. Succeeds on first success, fails when all fail.

### Decorators
- **BTInverter** — Flips child result (Success <-> Failure).
- **BTCooldown** — Blocks child execution until a duration expires after last completion.
- **BTSubtree** — Runs another `BehaviorTreeAsset` as a nested subtree.

### Leaves
- **BTLeafAction** — Serializable base class for custom actions. Override `OnExecute(BTContext)`.
- **BTLeafCondition** — Serializable base class for custom conditions. Override `OnEvaluate(BTContext)`.
- **BTAction** — Inline action via `Func<BTContext, BTStatus>` delegate (code-only, not serializable).
- **BTCondition** — Inline condition via `Func<BTContext, bool>` delegate (code-only, not serializable).

## Quick Start

### Define a Custom Action

```csharp
[Serializable]
[BTNodeDescription("Find Target", "Actions", "Finds the nearest enemy within range.")]
public class BTFindTarget : BTLeafAction
{
    [SerializeField] private float _detectionRange = 10f;

    protected override BTStatus OnExecute(BTContext ctx)
    {
        // find target logic
        ctx.Blackboard.Set(AIKeys.Target, target);
        return BTStatus.Success;
    }
}
```

### Define Blackboard Keys

```csharp
public static class AIKeys
{
    public static readonly BlackboardKey<Transform> Target = new("Target");
    public static readonly BlackboardKey<Vector3> MoveDestination = new("MoveDestination");
}
```

### Run the Tree

Add `BehaviorTreeRunner` to a GameObject, assign a `BehaviorTreeAsset`, and tick it:

```csharp
_runner.Tick(owner: this, deltaTime: Time.deltaTime, tick: _tick++);
```

## Custom Nodes

Every custom node must have a `[BTNodeDescription]` attribute to appear in the visual editor's search window:

```csharp
[BTNodeDescription("Node Name", "Category", "Short description.")]
```

## Requirements

- Unity 2022.3+
