# Environment Query System

Data-driven spatial query system for AI. Generates candidate positions or actors, scores them with weighted tests, and returns the best match.

## Features

- **Generators**: CircleGenerator, GridGenerator, SphereOverlapGenerator
- **Tests**: DistanceTest, DotProductTest, LineOfSightTest
- **Time budgeting**: spread scoring across multiple frames via `Tick(budgetMs)`
- **Batch scoring**: override `ScoreBatch()` for vectorized operations
- **Early exit**: dominated items pruned automatically

## Usage

```csharp
// Create query from config asset
var query = new EQSQuery(queryConfig);

// Run synchronously
var result = query.RunSync(new EQSQueryContext(position, forward));

// Get best result
if (result.TryGetBest(out var best))
    Debug.Log($"Best position: {best.Position}, score: {best.Score}");
```

## Optional Extensions

- **EQS.UniTask** — async `RunAsync()` extension method
- **EQS.BehaviorTree** — `BTRunEQSQuery` node for behavior trees
