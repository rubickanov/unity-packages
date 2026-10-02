# Codegen

Centralized code-generation framework for type-safe constants. Shared identifier sanitization, idempotent file writing, a generator registry, and a single Project Settings panel — so packages contribute generators instead of each re-implementing one.

## Dependencies

None. Editor-only package; depends only on the Unity Editor.

## Architecture

```
ICodeGenerator  (implemented per generator)
      │  discovered by
      ▼
CodeGeneratorRegistry ──► CodegenSettingsProvider   (one "Project / Rubickanov Codegen" panel)
      │                   CodegenPostprocessor       (one auto-regen hook)
      ▼
GeneratorConfig (per-generator, stored in CodegenSettings)
      │  drives
      ▼
IdentifierSanitizer · ConstantsClassBuilder · GeneratedFileWriter
```

Every generator implements `ICodeGenerator`. The registry discovers them via `TypeCache`, the settings panel renders one config block each, and the postprocessor regenerates the ones whose input assets changed. Output is written through `GeneratedFileWriter`, which skips the write when content is unchanged.

## Core Concepts

**ICodeGenerator** — One generator: a stable `Id`, a `DisplayName`, a default config, a `Generate(config)` method, and `HandlesAssetChange(...)` for auto-regeneration. Must have a public parameterless constructor so the registry can discover it.

**GeneratorConfig** — Per-generator settings (output path, namespace, class name, access modifier, partial, enabled, auto-regenerate), persisted centrally in `ProjectSettings/RubickanovCodegenSettings.asset`.

**IdentifierSanitizer** — Turns arbitrary strings into valid, unique C# identifiers. `Sanitize` handles invalid characters, leading digits, and keyword escaping; `MakeUnique` resolves per-scope collisions. The `lowercaseRemainder` flag is the one knob distinguishing the localization generator (`true`: `"DoT"` → `"Dot"`) from the gameplay tags generator (`false`: `"DoT"` → `"DoT"`).

## Quick Start

1. Open **Project Settings → Rubickanov Codegen**.
2. Expand a generator (e.g. **Layers**), tick **Enabled**, set its output path.
3. Click **Generate Layers** — or **Generate All Enabled** at the top.

Built-in generators ship disabled by default so installing the package never drops files into your project unasked.

## Usage

### Built-in generators

Dependency-free generators of type-safe Unity constants:

| Generator | Emits |
|-----------|-------|
| **Scenes** | Name + build-index constant per enabled build-settings scene |
| **Layers** | Name + index constant per layer |
| **Tags** | Name constant per tag |
| **Sorting Layers** | Name + id constant per sorting layer |
| **Animator Parameters** | A nested class per `AnimatorController`, with `Animator.StringToHash` of each parameter |
| **Resources Paths** | A constant per asset under an `Assets/.../Resources/` folder, keyed by its `Resources.Load` path |
| **Streaming Assets** | A constant per file under `Assets/StreamingAssets/`, keyed by its path relative to `Application.streamingAssetsPath` |
| **Shader Property IDs** | A cached `Shader.PropertyToID` per distinct property across `Assets/` shaders |
| **UI Toolkit Names** | A nested class per `.uxml`, with a constant per named element (`name="..."`) |

```csharp
SceneManager.LoadScene(Scenes.MainMenu);
if (other.CompareTag(Tags.Player)) { /* ... */ }
gameObject.layer = Layers.EnemyIndex;
animator.SetFloat(PlayerController.Speed, velocity);          // cached int hash
material.SetColor(ShaderProps.BaseColor, tint);              // cached int hash, no re-hashing
var theme = Resources.Load<AudioClip>(ResourcePaths.MusicTheme);
var configPath = Path.Combine(Application.streamingAssetsPath, StreamingAssets.ConfigDataJson);
var reload = Root.Q<Button>(UI.HudView.ReloadBtn);          // checked UXML element name
```

Scenes, Animator Parameters, Resources Paths, Streaming Assets, Shader Property IDs, and UI Toolkit Names auto-regenerate from asset events (scene list change; `.controller`, `Assets/.../Resources/`, `Assets/StreamingAssets/`, `.shader`/`.shadergraph`, `.uxml`). Layers, Tags, and Sorting Layers have no asset event, so regenerate them from the panel after editing Project Settings.

### Writing a custom generator

Implement `ICodeGenerator` (or extend `BuiltInConstantsGenerator` for simple constant classes) and it is discovered automatically.

```csharp
public sealed class InputActionsGenerator : ICodeGenerator
{
    public string Id => "inputActions";
    public string DisplayName => "Input Actions";

    public GeneratorConfig CreateDefaultConfig() => new()
    {
        Id = Id,
        OutputPath = "Assets/Codegen/InputActions.Generated.cs",
        Namespace = "Game.Generated",
        ClassName = "InputActions",
    };

    public void Generate(GeneratorConfig config)
    {
        var members = new List<ConstMember>();
        foreach (var action in CollectActions())
            members.Add(new ConstMember(action, "string", $"\"{action}\""));

        var code = ConstantsClassBuilder.Build(DisplayName, config, members);
        GeneratedFileWriter.Write(config.OutputPath, code);
    }

    public bool HandlesAssetChange(string[] imported, string[] deleted, string[] moved) => false;
}
```

### Reusing the primitives

The sanitizer and writer are usable standalone from any Editor generator:

```csharp
var used = new HashSet<string>();
var name = IdentifierSanitizer.MakeUnique(
    IdentifierSanitizer.Sanitize(rawName, lowercaseRemainder: false), used);

GeneratedFileWriter.Write("Assets/Codegen/MyStuff.Generated.cs", source);  // no-op if unchanged
```

## Design Decisions

- **Editor-only, no Runtime assembly** — generators run only in the editor; generated files live in the consuming project.
- **One settings store, not per-package assets** — the localization and gameplay tags generators used to ship their own `ScriptableSingleton` settings; both now share `CodegenSettings`, so there is one panel and one auto-regen hook. (Migration note: a custom output path previously set in the old per-generator settings asset resets to the generator's default on first run.)
- **Idempotent writes** — `GeneratedFileWriter` compares against the existing file and skips the write + reimport when nothing changed, so a no-op regeneration triggers no recompile.
- **`lowercaseRemainder` is a flag, not two code paths** — it is the only behavioural difference between the two migrated generators; keeping it parameterized lets one sanitizer serve both with byte-identical output.
- **Built-ins default to disabled** — discovery is automatic, but generation is opt-in so the package never writes files into a project without being asked.
```
