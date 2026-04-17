# DevConsole Config

Config extension for [DevConsole](../com.rubickanov.devconsole/). Auto-resolves [Config](../com.rubickanov.config/) database items by `Id` so console commands can declare strongly-typed parameters (`FruitConfig`, `WeaponConfig`, …) instead of `string[]` plus manual `db.Get(args[0])` plumbing.

## Dependencies

- `com.rubickanov.devconsole` — base DevConsole package
- `com.rubickanov.config` — Config package (provides `ConfigDatabase<T>` and `IIdentifiable`)

## Quick Start

After your databases have finished loading (e.g. via `IConfigService.LoadAsync<T>()`), register them once at bootstrap:

```csharp
using Rubickanov.DevConsole;
using Rubickanov.DevConsole.Config;

CommandRegistry.Instance.RegisterConfigDatabases(_fruitsDb, _weaponsDb, _seasonsDb);
```

That single call registers a parser **and** an autocomplete provider for each item type. Console commands can now declare typed parameters directly:

```csharp
[ConsoleCommand("give", "Give weapon to player", "Cheats")]
public string Give(WeaponConfig weapon, int amount = 1)
{
    _inventory.Add(weapon, amount);
    return $"Added {amount}x {weapon.Id}";
}
```

In the console:

- `give Sw<Tab>` — autocomplete shows weapon Ids whose Id starts with `Sw`.
- `give SwordOfFire 5` — calls `Give` with the resolved `WeaponConfig` asset.
- `give XYZ` — replies `Cannot parse 'XYZ' as WeaponConfig for 'weapon'.`

## Usage

### One-by-one registration

```csharp
CommandRegistry.Instance.RegisterConfigDatabase(_fruitsDb);
CommandRegistry.Instance.RegisterConfigDatabase(_weaponsDb);
```

### Bulk registration

`RegisterConfigDatabases` has overloads for 1–5 databases:

```csharp
CommandRegistry.Instance.RegisterConfigDatabases(
    _fruitsDb, _weaponsDb, _seasonsDb, _customersDb);
```

For more than five, chain `RegisterConfigDatabase` calls.

### Custom hint

If you want a different placeholder text in the usage string (`give <Weapon>` instead of `give <WeaponConfig>`), construct the provider yourself:

```csharp
CommandRegistry.Instance.RegisterParser<WeaponConfig>(input =>
{
    var w = _weaponsDb.Get(input);
    return w != null ? (true, w) : (false, default);
});
CommandRegistry.Instance.RegisterDefaultProvider<WeaponConfig>(
    new ConfigDatabaseAutoCompleteProvider<WeaponConfig>(_weaponsDb, "<Weapon>"));
```

### Builder integration

The typed `CommandGroupBuilder.Add<T>(...)` overloads pick up the registered providers automatically:

```csharp
CommandRegistry.Instance.Group("inventory", "Manage inventory", "Cheats", g =>
{
    g.Add<FruitConfig, int>("add", (fruit, n) => Inventory.Add(fruit, n), "Add fruits");
    g.Add<FruitConfig>("remove", fruit => Inventory.Remove(fruit), "Remove fruits");
    g.Add("clear", () => Inventory.Clear(), "Clear inventory");
});
```

## Design Notes

- **Synchronous by contract** — devconsole's argument parser is synchronous. Pre-load every database via `IConfigService.LoadAsync` *before* calling `RegisterConfigDatabase`; passing an unloaded database resolves to `null` for every Id.
- **Registration order** — `RegisterDefaultProvider` is consulted at command-registration time. Register databases *before* `CommandRegistry.Instance.Initialize()` (or before declaring commands that reference the type) so attribute-discovered commands pick up the provider. The parser lookup happens at execute time and has no such ordering constraint.
- **No singletons of its own** — extends `CommandRegistry.Instance` via plain extension methods.
