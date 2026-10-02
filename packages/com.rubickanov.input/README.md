# Input

What Unity's Input System leaves to every game, on its own action types: the device in hand, keys named for the player's layout and pad family, an action's keys for hints, rebind slots with conflicts and swaps, and maps blocked by reason.

## Dependencies

- `com.unity.inputsystem` — the actions, devices and rebinding this works on
- `UniTask` — `ControlBindings.ListenAsync`
- `R3` — the `Changed` observables
- `com.rubickanov.save` (optional) — enables **Rubickanov.Input.Save**, which keeps the player's keys in a save store

## Architecture

```
your InputActionAsset or generated GameInput (IInputActionCollection2)
   │
   ├── InputBlocker ── maps on, off while any reason blocks them (InputScope)
   ├── ActiveDevice ── keyboard or which pad, from every device's events ◄── IPadFamilySource (Steam)
   │        │
   │        ▼
   ├── ControlNames ── an action's keys for the device in hand, overrides included ── KeyNames (rules)
   └── ControlBindings ── RebindSlots, Set/Swap/Reset, ListenAsync, ToJson/LoadJson
            │ Changed
            ▼
      SavedControlBindings (Rubickanov.Input.Save) ── controls.json in ISaveStore
```

Nothing here wraps an action: the game keeps reading its own `GameInput.Player.Jump`. The package never names a map or an action; the game hands them in as data.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.Input** | Yes | Device in hand, key names, action names, rebinding, blocking |
| **Rubickanov.Input.Save** | Yes | Keeps `ControlBindings` in `com.rubickanov.save`'s store; compiled only when that package is present |

## Core Concepts

**Scheme** — `ControlScheme.Keys` (keyboard and mouse) or `ControlScheme.Pad`. A binding belongs to one by its group, the asset's control scheme that needs a gamepad or a keyboard/mouse; an asset without control schemes goes by the device in each binding's path.

**Pad family** — whose buttons a pad has: `Xbox`, `PlayStation`, `Nintendo`, `SteamDeck`. Read from the layout unless an `IPadFamilySource` knows better.

**Key name** — `KeyName`: English caps text (`SPACE`, `LMB`, `CROSS`) plus device, control path and family, enough to draw a glyph instead of the text.

**Slot** — `RebindSlot`: one rebindable key, a binding of one scheme or one part of a composite. Ids are `jump`, `move.up`, `jump.pad`.

**Scope** — `InputScope`: which maps a block takes, `InputScope.All` or `InputScope.Of("Player")`.

## Quick Start

With VContainer, in the app's root scope:

```csharp
builder.Register<GameInput>(Lifetime.Singleton).AsSelf().As<IInputActionCollection2>();
builder.Register<InputBlocker>(Lifetime.Singleton);
builder.Register<ActiveDevice>(Lifetime.Singleton);      // takes any IPadFamilySource registered, or none
builder.Register<ControlNames>(Lifetime.Singleton);
builder.RegisterInstance(new RebindOptions("Player/Move", "Player/Jump", "Player/Sprint"));
builder.Register<ControlBindings>(Lifetime.Singleton);
builder.Register<SavedControlBindings>(Lifetime.Singleton);   // with com.rubickanov.save and an ISaveStore
builder.RegisterBuildCallback(r => r.Resolve<SavedControlBindings>()); // keys read before anything names them
```

Without a container the same objects are built with `new`, in that order.

## Usage

### Naming an action's keys for a hint

```csharp
hint.text = $"{names.Text("Player/Jump")} JUMP";          // "SPACE JUMP", "A JUMP", "CROSS JUMP"
IReadOnlyList<KeyName> move = names.Keys(input.Player.Move); // W A S D, or LS on a pad
string pad = names.Text("UI/Submit", ControlScheme.Pad);    // either scheme on request
string up = names.Text("UI/Navigate", "up");                // one part: UP, or LS UP on a pad

names.Changed.Subscribe(_ => RefreshHints()).AddTo(this);   // pad picked up, key rebound, layout switched
```

A letter takes the layout's own letter when it is Latin (AZERTY's W is `Z`) and the US one otherwise, so a Russian layout's F stays `F`, not `А`. `names.FixedKeys("<Keyboard>/enter")` names a key that is no action's, empty while the player is on the pad. `names.IsDown(key)` tells whether the player holds it now, for a glyph that sinks.

### Knowing the device in hand

```csharp
if (device.Scheme == ControlScheme.Pad && device.Family == PadFamily.PlayStation) ShowCrossGlyphs();

void OnPointerMove(PointerMoveEvent _) => device.Pointed(); // the mouse on a menu item counts as the keys
device.Force(PadFamily.Nintendo);                            // preview another family's names; null follows again
```

A pad control counts past `ActiveDevice.Actuation` (0.5), so stick drift does not switch. A pad pressing within `Together` (0.2 s) of the pad in hand is Steam Input's Xbox copy of it and never takes the hand.

### Blocking input by reason

```csharp
public static readonly InputScope Gameplay = InputScope.Of("Player");

blocker.Set(pauseMenu, true, Gameplay);  // the menus keep working
blocker.Set(console, true);              // every map: the console types
blocker.Set(console, false);             // the pause menu's block stays
bool menusOff = blocker.IsMapBlocked(input.UI);
```

Every map is on from construction; a map is off while any reason's scope covers it. A game that switches maps by mode blocks the other mode's map under a reason of its own, so the blocker stays the one writer of `Enable`/`Disable`.

### Rebinding

```csharp
RebindSlot jump = bindings.Find("jump");
RebindResult result = await bindings.ListenAsync(jump, ct); // Esc or Start backs out; every map is blocked meanwhile

if (result.Kind == RebindKind.Taken && await AskSwap(result.Slot, result.Other))
    result = bindings.Swap(result);                         // the other slot takes jump's old key

bindings.Set(jump, "<Keyboard>/f");                         // Bound, Taken, Unchanged or Refused
bindings.Reset(jump);
bindings.ResetAll(ControlScheme.Pad);
```

A keyboard slot takes keys (not Esc, Win/Cmd, Menu) and mouse buttons; a pad slot takes pad buttons, not the ways of sticks and the d-pad. A key another slot of the same scheme has comes back `Taken` and changes nothing until `Swap`. `RebindOptions` changes the cancel keys and the exclusions.

### Keeping the keys

With **Rubickanov.Input.Save**, `SavedControlBindings` reads `controls.json` at construction and writes it whole after each change; a file it cannot read is kept aside as `controls.broken.json`. Without it, the JSON goes anywhere:

```csharp
bindings.Changed.Subscribe(_ => PlayerPrefs.SetString("controls", bindings.ToJson()));
try { bindings.LoadJson(PlayerPrefs.GetString("controls", "")); }
catch (FormatException) { /* not the keys' JSON: defaults stay */ }
```

The JSON holds only the keys that differ from the defaults, by slot id. A slot that is gone or a key it no longer takes is dropped with a warning; two slots of a scheme on one key send that scheme back to its defaults.

## Integration

A pad family Unity's layout cannot tell, such as a Steam Deck's own controls, which Steam hands over as an Xbox pad:

```csharp
public sealed class SteamPlatform : IPadFamilySource
{
    public PadFamily? FamilyOf(Gamepad pad) =>
        OnSteamDeck && KeyNames.FamilyOf(pad) == PadFamily.Xbox ? PadFamily.SteamDeck : null;
}
```

Registered as `IPadFamilySource`, it is asked before the layout; `null` falls through to the layout.

## Design Decisions

- **Unity's types in, no action wrapper** — the game reads its generated class as before; the package only takes `IInputActionCollection2` (a generated class or an `InputActionAsset`), so a game adopts it without changing how it reads input.
- **Map and action names as data** — `InputScope`, `RebindOptions` and the action names in `ControlNames` are strings the game owns; nothing in the package knows a game's maps.
- **Every map on unless blocked** — one writer of map state; modes are blocks too, so a console and a ship mode never fight over `Enable`.
- **Rebind slots per scheme with Taken** — Unity's rebinding binds any key it hears; a key two actions share fires both, so a taken key asks the player instead.
- **JSON by slot id, own reader** — survives the asset's binding ids changing, and needs no JSON library; `SaveBindingOverridesAsJson` keys by binding id and keeps keys no slot owns.
- **English key names whatever the layout's script** — hints and glyphs are drawn for Latin caps; the layout's own Latin letter is still used, so AZERTY players see `ZQSD`.
- **Pad copies by timing** — Steam Input exposes the pad it reads plus an Xbox copy, and Unity sees both; a press from two pads within 0.2 s is one pad.
