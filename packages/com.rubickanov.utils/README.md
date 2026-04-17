# Utils

Shared utilities: deterministic random, circular buffer, GameObject pooling, evicting pool, and description attribute.

## Dependencies

None.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Utils.Runtime** | Yes | All utility types |
| **Utils.Editor** | Editor | DescriptionAttribute inspector rendering |

## Quick Start

No registration needed. All types are used directly.

```csharp
// Deterministic random (network-safe)
float spread = DeterministicRandom.Range(tick, seed, -0.5f, 0.5f);

// GameObject pool
var pool = new ObjectPool<ParticleSystem>(prefab, prewarm: 8);
var fx = pool.Get(hitPoint, Quaternion.identity);
pool.Release(fx, delay: 2f);
```

## Usage

### Deterministic Random

Hash-based (murmur3 finalizer) random number generation. Same inputs produce the same output on any machine -- safe for network synchronization and replay.

```csharp
// Float in [0, 1)
float f = DeterministicRandom.Float01(tick, seed);

// Float in [min, maxExclusive)
float spread = DeterministicRandom.Range(tick, seed, -0.5f, 0.5f);

// Int in [min, maxExclusive) -- throws ArgumentException if maxExclusive <= min
int index = DeterministicRandom.Int(tick, seed, 0, enemies.Length);

// Boolean (50/50)
bool crit = DeterministicRandom.Bool(attackerId, tick);

// Sign (-1f or 1f)
float dir = DeterministicRandom.Sign(tick, seed);

// Raw hash
uint hash = DeterministicRandom.Hash(a, b);
uint hash3 = DeterministicRandom.Hash(a, b, c);
```

All methods accept 2 or 3 `uint` keys. More keys give more degrees of freedom for independent random streams.

### Circular Buffer

Fixed-capacity ring buffer. Index wraps automatically via modulo on `uint` keys. Underflow is well-defined (`3u - 10u` wraps to a large positive number), so lookback from low ticks works without special-casing.

```csharp
var buffer = new CircularBuffer<SnapshotState>(capacity: 128u);

buffer.Add(snapshot, tick);
SnapshotState old = buffer.Get(tick - 10u);
buffer.Clear();

uint capacity = buffer.Capacity;
```

### Object Pool

GameObject pool with prewarm, placement, delayed release, callbacks, and statistics. Instances are parented under a `[Pools]` root in the hierarchy.

```csharp
var pool = new ObjectPool<ParticleSystem>(vfxPrefab, prewarm: 16, maxSize: 64);

// Get without placement
var fx = pool.Get();

// Get with position and rotation
var fx = pool.Get(hitPoint, Quaternion.LookRotation(normal));

// Release immediately
pool.Release(fx);

// Release after delay
pool.Release(fx, delay: 2f);

// Return all active instances
pool.ReleaseAll();
```

Constructor parameters:

```csharp
var pool = new ObjectPool<MuzzleFlash>(
    prefab,
    prewarm: 8,
    maxSize: 32,
    onGet: fx => fx.Play(),
    onRelease: fx => fx.Stop(),
    parent: weaponRoot       // optional, defaults to global [Pools] root
);
```

Statistics: `pool.ActiveCount`, `pool.PooledCount`, `pool.TotalCreated`.

### Evicting Pool

LRU pool that automatically evicts the oldest active item when `maxActive` is reached. Built on top of **ObjectPool**.

```csharp
// Immediate eviction (oldest item released to pool)
var pool = new EvictingPool<DecalProjector>(decalPrefab, maxActive: 64);
var decal = pool.Get(hitPoint, Quaternion.LookRotation(normal));
```

With a custom eviction callback for fade-out:

```csharp
var pool = new EvictingPool<DecalProjector>(
    decalPrefab,
    maxActive: 64,
    onEvict: (decal, release) =>
    {
        FadeOut(decal, duration: 0.5f, onComplete: () => release(decal));
    },
    evictBuffer: 8,   // extra pool slots for items mid-eviction
    prewarm: 16
);

// Return every active instance without invoking onEvict (e.g., on scene change)
pool.ReleaseAll();
```

### Description Attribute

Attaches a short text description to a MonoBehaviour, rendered in the Inspector by a custom editor.

```csharp
[Description("Moves the character based on input direction and speed")]
public class CharacterMovement : MonoBehaviour { }
```

### Application Extensions

```csharp
// Stops play mode in Editor, calls Application.Quit() in builds
ApplicationExtensions.Quit();
```

## File Structure

```
com.rubickanov.utils/
├── Runtime/
│   ├── Attributes/
│   │   └── DescriptionAttribute.cs
│   ├── Unity/
│   │   ├── ApplicationExtensions.cs
│   │   ├── EvictingPool.cs
│   │   └── ObjectPool.cs
│   ├── CircularBuffer.cs
│   └── DeterministicRandom.cs
├── Editor/
│   └── ComponentDescriptionEditor.cs
└── Tests/
    └── Editor/
        ├── CircularBufferTests.cs
        ├── DeterministicRandomTests.cs
        └── PoolTests.cs
```
