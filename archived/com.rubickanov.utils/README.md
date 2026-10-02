# Utils

Shared utilities: deterministic hash-based random, fixed-capacity ring buffer, GameObject pooling (plain and LRU-evicting), and a `[Description]` inspector attribute.

## Dependencies

None. Runtime code uses only the engine; the object pool builds on `UnityEngine.Pool.ObjectPool<T>`.

## Architecture

The package is a flat collection of independent utilities — there is no shared runtime or service to register. Use each type directly.

| Type | Assembly | Purpose |
|------|----------|---------|
| **DeterministicRandom** | Runtime | Stateless hash-based RNG keyed on `uint` values |
| **CircularBuffer\<T>** | Runtime | Fixed-capacity ring buffer, modulo-indexed |
| **ObjectPool\<T>** | Runtime | GameObject pool with prewarm, placement, delayed release |
| **EvictingPool\<T>** | Runtime | LRU pool over `ObjectPool<T>` that evicts the oldest active item |
| **DescriptionAttribute** | Runtime | Marks a MonoBehaviour with inspector text |
| **ApplicationExtensions** | Runtime | `Quit()` that works in Editor and builds |
| **ComponentDescriptionEditor** | Editor | Renders `[Description]` text above the default inspector |

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.Utils.Runtime** | Yes | All runtime utility types |
| **Rubickanov.Utils.Editor** | Editor | Renders `DescriptionAttribute` in the inspector |

## Quick Start

No registration needed. All types are used directly.

```csharp
// Deterministic random (network-safe), keyed on tick + seed
float spread = DeterministicRandom.Range(tick, seed, -0.5f, 0.5f);

// GameObject pool
var pool = new ObjectPool<ParticleSystem>(hitFxPrefab, prewarm: 8);
var fx = pool.Get(hitPoint, Quaternion.identity);
pool.Release(fx, delay: 2f);
```

## Usage

### Deterministic Random

Stateless RNG built on the murmur3 finalizer. The same keys produce the same output on any machine and any run, so results are safe for network synchronization, replays, and procedural generation. Nothing is stored between calls — the keys *are* the state.

```csharp
// Float in [0, 1)
float f = DeterministicRandom.Float01(tick, seed);

// Float in [min, maxExclusive)
float spread = DeterministicRandom.Range(tick, seed, -0.5f, 0.5f);

// Int in [min, maxExclusive) — throws ArgumentException if maxExclusive <= min
int index = DeterministicRandom.Int(tick, seed, 0, enemies.Length);

// Boolean (50/50)
bool crit = DeterministicRandom.Bool(attackerId, tick);

// Sign (-1f or 1f)
float kickback = DeterministicRandom.Sign(tick, seed) * recoil;

// Raw hash
uint h2 = DeterministicRandom.Hash(a, b);
uint h3 = DeterministicRandom.Hash(a, b, c);
uint h4 = DeterministicRandom.Hash(a, b, c, d);
```

`Float01`, `Range`, `Int`, `Bool`, and `Sign` each take two or three `uint` keys. Add a key to spawn an independent random stream — e.g. key one roll on `(tick, entityId)` and another on `(tick, entityId, channel)` so they never correlate.

### Circular Buffer

Fixed-capacity ring buffer. Indices wrap modulo `Capacity`, so any `uint` lands in a valid slot. `uint` underflow is well-defined (`tick - 10u` when `tick < 10` wraps to a large value that still maps in range), so look-back from low ticks needs no special-casing.

```csharp
var history = new CircularBuffer<SnapshotState>(capacity: 128u);

history.Add(snapshot, tick);
SnapshotState tenTicksAgo = history.Get(tick - 10u);
history.Clear();                 // resets slots to default(T), no allocation

uint capacity = history.Capacity;
```

The constructor throws `ArgumentOutOfRangeException` if `capacity` is zero.

### Object Pool

Generic pool for `Component`-derived prefabs, with prewarm, placement, delayed release, get/release callbacks, and live statistics. Each pool creates its own `Pool [<prefab>]` container parented under a single `[Pools]` root (kept across scene loads with `DontDestroyOnLoad` in play mode).

```csharp
var pool = new ObjectPool<ParticleSystem>(hitFxPrefab, prewarm: 16, maxSize: 64);

// Get without changing the transform
var fx = pool.Get();

// Get and place
var fx = pool.Get(hitPoint, Quaternion.LookRotation(normal));

// Release now, or after a delay in seconds
pool.Release(fx);
pool.Release(fx, delay: 2f);

// Return every active instance, cancelling pending delayed releases
pool.ReleaseAll();
```

Full constructor with callbacks:

```csharp
var pool = new ObjectPool<MuzzleFlash>(
    muzzleFlashPrefab,
    prewarm: 8,
    maxSize: 32,
    onGet: flash => flash.Play(),     // after activation
    onRelease: flash => flash.Stop(), // before deactivation
    parent: weaponRoot);              // optional, defaults to the global [Pools] root
```

Statistics: `pool.ActiveCount`, `pool.PooledCount`, `pool.TotalCreated`.

`ObjectPool<T>` is `IDisposable`. `Dispose()` releases all active instances and destroys the pool's container — call it when the owning system shuts down.

```csharp
pool.Dispose();
```

### Evicting Pool

LRU pool that caps the number of *active* items. When `Get` is called at `maxActive`, the oldest active item is evicted first. Built on top of `ObjectPool<T>`.

```csharp
// No callback: the oldest item is released straight back to the pool
var decals = new EvictingPool<DecalProjector>(decalPrefab, maxActive: 64);
var decal = decals.Get(hitPoint, Quaternion.LookRotation(normal));
```

Pass `onEvict` to defer the release — e.g. to fade an item out before it returns. The callback receives the evicted item and a release delegate; call the delegate when the item is done.

```csharp
var decals = new EvictingPool<DecalProjector>(
    decalPrefab,
    maxActive: 64,
    onEvict: (decal, release) =>
        FadeOut(decal, duration: 0.5f, onComplete: () => release(decal)),
    evictBuffer: 8,   // extra pool slots to hold items still mid-eviction
    prewarm: 16);

decals.Release(decal);  // normal release from game code
decals.ReleaseAll();    // bulk clear (scene teardown); bypasses onEvict
```

Statistics: `decals.ActiveCount`, `decals.PooledCount`. `EvictingPool<T>` is also `IDisposable`.

### Description Attribute

Attaches inspector-only text to a MonoBehaviour. `ComponentDescriptionEditor` renders it as a dimmed italic label above the default inspector for every component, so no per-script editor is needed.

```csharp
[Description("Moves the character based on input direction and speed")]
public class CharacterMovement : MonoBehaviour { }
```

### Application Quit

```csharp
// Stops play mode in the Editor, calls Application.Quit() in a build
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
└── Editor/
    └── ComponentDescriptionEditor.cs
```
