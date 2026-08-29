# ACS Reactive

Computed (derived) reactive properties for ACS aspects. Extension for [ACS](../com.rubickanov.acs/).

## Dependencies

> `R3` comes from NuGet, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `com.rubickanov.acs` — aspects that host the computed fields
- `R3` — `ReactiveProperty<T>` / `ReadOnlyReactiveProperty<T>` the values are built from

## Quick Start

Declare a `ComputedProperty<T>` field on an aspect, mark it `[Computed]`, and wire it from its
sources in the constructor:

```csharp
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Reactive;

public class HealthAspect : IEntityAspect
{
    public readonly ReactiveProperty<float> Health = new(100f);
    public readonly ReactiveProperty<float> MaxHealth = new(100f);

    [Computed] public readonly ComputedProperty<float> HealthPercent;
    [Computed] public readonly ComputedProperty<bool>  IsDead;

    public HealthAspect()
    {
        HealthPercent = ComputedProperty.From(Health, MaxHealth, (h, max) => max > 0f ? h / max : 0f);
        IsDead        = ComputedProperty.From(Health, h => h <= 0f);
    }
}
```

`HealthPercent` recomputes whenever `Health` or `MaxHealth` changes — no manual `Subscribe`, no
write-back. This replaces the hand-rolled boilerplate:

```csharp
// Without acs.reactive — a Subscribe + write-back per derived value
Health.CombineLatest(MaxHealth, (h, max) => h / max)
    .Subscribe(v => HealthPercent.Value = v)
    .AddTo(ref disposables);
```

## Usage

### Reading and observing

```csharp
float pct = _health.HealthPercent.CurrentValue;                 // read the current value

_health.HealthPercent.Property                                  // observe changes (R3)
    .Subscribe(p => _bar.fillAmount = p)
    .AddTo(ref disposables);
```

`From` has overloads for 1–4 sources. For more, compute from an intermediate `ComputedProperty`
or aggregate the inputs into one source.

### Disposal

A computed holds live subscriptions to its sources.

- **Sources on the same entity** (the common case) — no cleanup needed; the computed and its
  sources are collected together when the entity is dropped.
- **Sources on another entity or the `World`** — that source keeps the computed (and its owner)
  alive. Call `Dispose()` when the owning entity is destroyed:

```csharp
public void Dispose() => HealthPercent.Dispose();
```

## Design Decisions

- **No `[Computed]` auto-wiring** — without source generators the binding is written by hand in
  the constructor. `[Computed]` is a marker for tooling (`acs.debug` badges it, a future
  `acs.codegen` can generate the wiring) and documents intent.
- **`ComputedProperty<T>` instead of returning `ReadOnlyReactiveProperty<T>`** — gives an explicit
  `Dispose()` that releases the source subscriptions. Aspects have no lifecycle of their own, so
  cross-entity computeds would otherwise leak.
- **Built on `ReactiveProperty` + `Subscribe`** — recompute is driven by plain source
  subscriptions rather than `CombineLatest`, keeping the value seeded synchronously and disposal
  fully owned by the computed.
