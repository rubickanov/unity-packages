# Environment Query System (EQS)

Data-driven spatial query system for AI. Generates candidate positions or actors, scores them with weighted tests, and returns the best match. Supports time budgeting to spread scoring across multiple frames.

## Dependencies

None.

Optional assemblies depend on:
- `UniTask` — async `RunAsync()` extension (EQS.UniTask)
- `com.rubickanov.behaviortree` — BT leaf node (EQS.BehaviorTree)

## Architecture

```
EQSQueryConfig (ScriptableObject)
    ├── EQSGenerator          — produces candidate EQSItems
    │   ├── CircleGenerator
    │   ├── GridGenerator
    │   └── SphereOverlapGenerator
    │
    └── EQSTest[]             — scores/filters each item
        ├── DistanceTest
        ├── DotProductTest
        └── LineOfSightTest

EQSQuery  ──  Start(context) → Tick(budgetMs) → GetResult()
              └── RunSync(context) for single-frame execution
```

**EQSQuery** generates items via the configured generator, then runs each test in order. Tests score items on a 0..1 scale (negative = filtered out). Scores are accumulated with per-test weights, normalized optionally, and pruned by domination. Results are sorted by score descending.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **EQS.Runtime** | Yes | Core query engine, generators, tests, debugger |
| **EQS.Editor** | Editor | Custom inspectors for EQSQueryConfig and EQSQueryDebugger |
| **EQS.UniTask** | Yes | Async `RunAsync()` extension method |
| **EQS.BehaviorTree** | Yes | `BTRunEQSQuery` leaf node for behavior trees |

## Core Concepts

**EQSGenerator** — Produces a list of candidate **EQSItem** positions (or actors). Subclass to implement custom generators. Three built-in: **CircleGenerator**, **GridGenerator**, **SphereOverlapGenerator**.

**EQSTest** — Scores a single item on a 0..1 scale. Return negative to filter out. Override `ScoreBatch()` and set `PreferBatch` for vectorized operations. Built-in: **DistanceTest**, **DotProductTest**, **LineOfSightTest**.

**EQSQueryContext** — Provides the querier's position, forward direction, optional reference position, and optional user data.

**EQSTestScoreMode** — Controls how a test's raw score is applied: `Score` (higher = better), `InverseScore` (1 - score), `FilterOnly` (no score contribution, only filters).

## Quick Start

1. Create a query config asset via **Create > EQS > Query Config**.
2. In the Inspector, pick a generator and add tests with weights.
3. Run the query from code.

```csharp
var query = new EQSQuery(queryConfig);
var context = new EQSQueryContext(transform.position, transform.forward);
var result = query.RunSync(context);

if (result.TryGetBest(out var best))
    agent.SetDestination(best.Position);
```

## Usage

### Synchronous Query

```csharp
var query = new EQSQuery(coverQueryConfig);
var context = new EQSQueryContext(
    transform.position,
    transform.forward,
    gameObject,
    referencePosition: enemyTransform.position);

var result = query.RunSync(context);

if (result.TryGetBest(out var best))
    Debug.Log($"Best cover: {best.Position}, score: {best.Score}");
```

### Time-Budgeted Query

Spread scoring across multiple frames to avoid spikes:

```csharp
private EQSQuery _query;

private void Start()
{
    _query = new EQSQuery(patrolQueryConfig);
    _query.Start(new EQSQueryContext(transform.position, transform.forward));
}

private void Update()
{
    if (_query.Status == EQSQueryStatus.Scoring)
    {
        if (_query.Tick(budgetMs: 0.5f))
        {
            var result = _query.GetResult();
            if (result.TryGetBest(out var best))
                MoveToPosition(best.Position);
        }
    }
}
```

### Async Query (UniTask)

Requires the **EQS.UniTask** assembly:

```csharp
var query = new EQSQuery(queryConfig);
var context = new EQSQueryContext(transform.position, transform.forward);

var result = await query.RunAsync(context, budgetMs: 0.5f, destroyCancellationToken);

if (result.TryGetBest(out var best))
    agent.SetDestination(best.Position);
```

### Top N Results

```csharp
var result = query.RunSync(context);
var topThree = result.TopN(3, minScore: 0.2f);

foreach (var item in topThree)
    Debug.Log($"Position: {item.Position}, Score: {item.Score}");
```

### Behavior Tree Integration

Requires the **EQS.BehaviorTree** assembly. **BTRunEQSQuery** runs the query over multiple ticks and stores the best position in the blackboard:

```csharp
// Set reference position before running the query
ctx.Blackboard.Set(EQSBlackboardKeys.ReferencePosition, enemyPosition);

// After BTRunEQSQuery succeeds:
if (ctx.Blackboard.TryGet(EQSBlackboardKeys.BestPosition, out Vector3 pos))
    agent.SetDestination(pos);
```

### Custom Generator

```csharp
[Serializable]
public class NavMeshGenerator : EQSGenerator
{
    [SerializeField] private float _radius = 10f;
    [SerializeField] private int _sampleCount = 16;

    public override void Generate(EQSQueryContext context, List<EQSItem> results)
    {
        for (int i = 0; i < _sampleCount; i++)
        {
            var randomPoint = context.QuerierPosition + Random.insideUnitSphere * _radius;
            if (NavMesh.SamplePosition(randomPoint, out var hit, _radius, NavMesh.AllAreas))
                results.Add(new EQSItem(hit.position));
        }
    }
}
```

### Custom Test

```csharp
[Serializable]
public class CoverTest : EQSTest
{
    [SerializeField] private LayerMask _coverMask = ~0;

    public override float Score(EQSQueryContext context, in EQSItem item)
    {
        var threatPos = context.ReferencePosition ?? context.QuerierPosition;
        var dir = threatPos - item.Position;
        bool hasCover = Physics.Raycast(item.Position, dir.normalized, dir.magnitude, _coverMask);
        return hasCover ? 1f : -1f;
    }
}
```

### Scene Debugger

Add **EQSQueryDebugger** to a GameObject to visualize query results as colored Gizmos (green = best, red = worst). Enable `Auto Refresh` for continuous updates or click "Run Query" in the Inspector.

## Design Decisions

- **[SerializeReference] for generators and tests** — Allows adding custom types without ScriptableObject boilerplate. The Inspector discovers all implementations via reflection.
- **Time budgeting via Tick()** — Avoids frame spikes for large queries. The query resumes where it left off each frame.
- **Early exit by domination** — Items that cannot mathematically beat the current best are pruned before remaining tests run.
- **Batch scoring opt-in** — Tests that benefit from batch operations (e.g., Physics batch raycasts) override `ScoreBatch()` and set `PreferBatch = true`.
