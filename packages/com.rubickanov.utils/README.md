# com.rubickanov.utils

Shared utilities: deterministic random, circular buffer, object pooling, description attribute with custom inspector.

## Key Types

| Type | Description |
|------|-------------|
| `DeterministicRandom` | Hash-based (murmur3) random — same output for same inputs on any machine, safe for netcode |
| `CircularBuffer<T>` | Fixed-capacity ring buffer with index-based access |
| `ObjectPool<T>` | GameObject pool with prewarm, position/rotation placement, and delayed release |
| `EvictingPool<T>` | LRU pool that evicts the oldest active item when capacity is reached, with customizable eviction callback |
| `DescriptionAttribute` | Marks a MonoBehaviour with a text description displayed in the Inspector |
| `ApplicationExtensions` | `Quit()` helper — stops play mode in Editor, `Application.Quit()` in builds |

## Usage

```csharp
// Deterministic random (network-safe)
float spread = DeterministicRandom.Range(tick, seed, -0.5f, 0.5f);
bool crit = DeterministicRandom.Bool(attackerId, tick);

// Object pool
var pool = new ObjectPool<ParticleSystem>(prefab, prewarm: 8);
var fx = pool.Get(position, rotation);
pool.Release(fx, delay: 2f);

// Evicting pool (LRU — oldest item evicted at capacity)
var pool = new EvictingPool<DecalProjector>(prefab, maxActive: 64,
    onEvict: (item, release) => { /* fade out, then call release(item) */ });
var decal = pool.Get(position, rotation);

// Description attribute (shown in Inspector)
[Description("Moves the character based on input")]
public class CharacterMovement : MonoBehaviour { }
```

## Editor

`ComponentDescriptionEditor` — custom inspector that renders `[Description("...")]` text above the default inspector for any MonoBehaviour.
