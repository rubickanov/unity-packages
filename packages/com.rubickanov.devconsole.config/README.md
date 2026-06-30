# DevConsole Config

Config extension for [DevConsole](../com.rubickanov.devconsole/). Resolves [Config](../com.rubickanov.config/) database items by `Id` so console commands can declare strongly-typed parameters (`FruitConfig`, `WeaponConfig`, …) instead of `string[]` plus manual `db.Get(args[0])` plumbing.

## Dependencies

- `com.rubickanov.devconsole` — base DevConsole package (`CommandRegistry`, `IAutoCompleteProvider`)
- `com.rubickanov.config` — Config package (`ConfigDatabase<T>`, `ConfigBase`, `IIdentifiable`)

## Quick Start

Load your databases first (the console parser is synchronous, so they must be ready), then register them once at bootstrap:

```csharp
using Rubickanov.DevConsole;
using Rubickanov.DevConsole.Config;

CommandRegistry.Instance.RegisterConfigDatabases(_fruitsDb, _weaponsDb, _seasonsDb);
```

Each call registers a parser **and** an autocomplete provider for that item type. Console commands can now declare typed parameters directly:

```csharp
[ConsoleCommand("give", "Give weapon to player", "Cheats")]
public string Give(WeaponConfig weapon, int amount = 1)
{
    _inventory.Add(weapon, amount);
    return $"Added {amount}x {weapon.Id}";
}
```

In the console:

- `give Sw<Tab>` — autocomplete lists weapon Ids starting with `Sw`.
- `give SwordOfFire 5` — calls `Give` with the resolved `WeaponConfig` asset.
- `give XYZ` — replies `Cannot parse 'XYZ' as WeaponConfig for 'weapon'.`

## Usage

### Registering databases

Register one at a time, or in bulk (overloads cover 1–5 databases):

```csharp
CommandRegistry.Instance.RegisterConfigDatabase(_fruitsDb);

CommandRegistry.Instance.RegisterConfigDatabases(
    _fruitsDb, _weaponsDb, _seasonsDb, _customersDb);
```

For more than five, chain `RegisterConfigDatabase` calls — every method returns the registry.

Item types must derive from `ConfigBase` and implement `IIdentifiable`. The parser resolves arguments via `db.Get(input)`; autocomplete suggests `db.All` Ids, prefix-matched case-insensitively.

### Custom autocomplete hint

By default the usage string shows `<WeaponConfig>`. To override the placeholder, construct the provider yourself instead of using the extension method:

```csharp
CommandRegistry.Instance.RegisterParser<WeaponConfig>(input =>
{
    var weapon = _weaponsDb.Get(input);
    return weapon != null ? (true, weapon) : (false, default);
});
CommandRegistry.Instance.RegisterDefaultProvider<WeaponConfig>(
    new ConfigDatabaseAutoCompleteProvider<WeaponConfig>(_weaponsDb, "<Weapon>"));
```

### Builder-declared commands

The typed `CommandGroupBuilder.Add<T>(...)` overloads pick up registered providers automatically:

```csharp
CommandRegistry.Instance.Group("inventory", "Manage inventory", "Cheats", g =>
{
    g.Add<FruitConfig, int>("add", (fruit, n) => _inventory.Add(fruit, n), "Add fruits");
    g.Add<FruitConfig>("remove", fruit => _inventory.Remove(fruit), "Remove fruits");
    g.Add("clear", () => _inventory.Clear(), "Clear inventory");
});
```

## Design Notes

- **Synchronous by contract** — the console argument parser is synchronous. Pre-load every database via `IConfigService.LoadAsync` *before* calling `RegisterConfigDatabase`; an unloaded database resolves every Id to `null`.
- **Register before command discovery** — providers are read at command-registration time. Register databases before `CommandRegistry.Instance.Initialize()` (or before declaring commands of that type) so attribute-discovered commands get autocomplete. Parsing happens at execute time and has no ordering constraint.
- **No state of its own** — plain extension methods over `CommandRegistry`; nothing to dispose or reset.
