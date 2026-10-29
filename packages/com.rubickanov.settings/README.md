# Settings

Player settings the game declares — live values kept by id in one save document, ready-made display, volume and look settings, and a menu model for a settings screen; the drawing stays the game's.

## Dependencies

- `com.rubickanov.save` — `ISaveStore` and `SaveDocument`: the file, its turns, `settings.broken.json`
- `UniTask` — async read and write (git URL, as for save and ui)
- `R3` — live values (NuGet `R3` 1.3.x, auto-referenced dll)
- `com.rubickanov.ui` 4.1.0+ — optional: the menu on `MenuNavigation`, the back stack and the pointer
- `com.rubickanov.audio` 3.1.0+ — optional: volumes on the mixer
- `com.rubickanov.input` 1.1.0+ — optional: the player's keys as rows, named for the device in hand

## Architecture

```
Setting<T> ──► SettingsStore ──► settings.json      (by dotted id; ids nobody declared kept as they were)
    ▲
    │ edits                 SettingsLayout ──► SettingsMenu ──► the game's screen
    │                         sections, rows      focus, steps, questions, cues
SettingsRow (Steps, Toggle, Choice, Action, Heading, Keys, a game's own)
                                   ▲
ControlBindings ──► ControlKeys ───┘ ActionKeysRow per action    (.Input; the keys stay in controls.json)
```

The game declares the settings (ids, defaults, ranges) on the store and the screen's sections and rows on a layout. The menu model takes keys, pad and pointer and tells the screen what to draw; the screen is the game's.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.Settings** | Yes | Values, store and file, text, layout and rows, menu model, display and look settings |
| **Rubickanov.Settings.UI** | Yes | `SettingsMenuInput`, `SettingsMenuScopes` and the unstyled `SettingsPanel` with its row views; compiled with `com.rubickanov.ui` (`RUBICKANOV_UI`) |
| **Rubickanov.Settings.Audio** | Yes | `MixerVolumes`; compiled with `com.rubickanov.audio` (`RUBICKANOV_AUDIO`) |
| **Rubickanov.Settings.Input** | Yes | `ControlKeys` and `ActionKeysRow`: key rows over `ControlBindings`, `ControlNames` and `ActiveDevice`; compiled with `com.rubickanov.input` (`RUBICKANOV_INPUT`) |
| **Rubickanov.Settings.Tests** | Editor | Store, file, menu model, display |
| **Rubickanov.Settings.Input.Tests** | Editor | Key rows on the menu: listening, swap, pad, reset (Input System test fixture) |

## Core Concepts

**Setting** — One stored value under a dotted id (`sound.master`), normalised on each set, live through R3 (`Live`). The dots nest it in the file.

**Bound** — A value the game keeps elsewhere (its own file); rows edit it, the store never writes it. With a default, a section's reset puts it back.

**Row** — What a section shows: reads its value each time it is asked, steps with left/right, acts on Enter. A game adds its own kind by deriving `SettingsRow`.

**Question** — The one thing the menu waits on over its rows: keep this screen, reset the section, swap a key. Back answers no.

## Quick Start

```csharp
var store = new SettingsStore(saveStore, new SaveKey(SaveAreas.Settings, "settings.json"));
StepSetting master = store.Steps("sound.master", 10);
var display = new DisplaySettings(store);
new DisplayApplier(display, new ScreenModes()).Start();
master.Live.Subscribe(step => audio.SetVolume("MasterVolume", master.Share));
store.ReadAsync().Forget();   // defaults till it is done, at once on desktop

var layout = new SettingsLayout()
    .Section("sound", rows => { rows.Steps(master); rows.ResetRow(); })
    .Section("display", rows => { display.AddRows(rows, new ScreenModes()); rows.ResetRow(); });
var menu = new SettingsMenu(layout, SettingsText.English(), () => Time.unscaledTime);
```

## Usage

### Declaring settings

```csharp
StepSetting music = store.Steps("sound.music", @default: 7);                 // 0..10
Setting<bool> subtitles = store.Toggle("sound.subtitles", true);
Setting<Difficulty> difficulty = store.Enum("game.difficulty", Difficulty.Normal);   // "normal" in the file
ChoiceSetting<int> fov = store.Choice("look.fov", 70, new[] { 60, 70, 80, 90 }, SettingFormats.Int);
Setting<float> gamma = store.Range("picture.gamma", 1f, 0.5f, 2f);
```

Ids are unique; a second declaration of one throws. A setting declared after the read takes its kept value at once.

### Listening and changing

```csharp
music.Live.Subscribe(step => audio.SetVolume("MusicVolume", music.Share));   // now, then each change
music.Value = 4;                    // from the menu, the console, code; an equal value changes nothing
store.Changed.Subscribe(setting => log.Info($"{setting.Id} = {setting.Text}"));
```

### A format for the game's own type

```csharp
sealed class TintFormat : SettingFormat<Color32>
{
    public override SettingsJsonValue Write(Color32 c) => SettingsJsonValue.Of($"#{c.r:x2}{c.g:x2}{c.b:x2}");
    public override bool TryRead(SettingsJsonValue v, out Color32 c) { c = default; return v.Kind == SettingsJsonKind.String && TryParse(v.String!, out c); }
    public override string Show(Color32 c) => $"#{c.r:x2}{c.g:x2}{c.b:x2}";
    public override bool TryParse(string text, out Color32 c) { /* "#rrggbb" */ }
}

Setting<Color32> tint = store.Add(new Setting<Color32>("look.tint", new Color32(255, 200, 0, 255), new TintFormat()));
```

### A value the game keeps itself

```csharp
var voiceMode = new Bound<VoiceMode>(() => voice.Mode, mode => voice.Mode = mode, VoiceMode.PushToTalk);
rows.Choice("voice.mode", voiceMode, new[] { VoiceMode.PushToTalk, VoiceMode.Open }, m => "voice.mode/" + m);
```

### Ready-made settings

```csharp
var display = new DisplaySettings(store, "display", WindowMode.Fullscreen, vsync: true, frameCap: 60);
var applier = new DisplayApplier(display, new ScreenModes(), log: line => Log.Info(line));   // asked, then got a frame later
applier.Start();

var look = new LookSettings(store);                         // look.mouse, look.pad (5 = as authored), look.invertY
camera.Speed = authoredSpeed * LookSettings.Scale(look.Mouse.Value);

var volumes = new MixerVolumes(store, audio, ("sound.master", "MasterVolume"), ("sound.music", "MusicVolume"));
volumes.Start();
```

Each adds its rows: `display.AddRows(rows, modes)`, `look.AddRows(rows, () => device.IsPad)`, `volumes.AddRows(rows)`.

### Laying out the screen

```csharp
var layout = new SettingsLayout()
    .Section("sound", rows =>
    {
        volumes.AddRows(rows);
        rows.Heading("sound.voice");
        rows.Choice("voice.mode", voiceMode, VoiceModes, m => "voice.mode/" + m);
        rows.Choice("voice.mic", micName, () => Microphones(), name => name);   // options read when the section opens
        rows.ResetRow();
    })
    .Section("display", rows =>
    {
        display.AddRows(rows, modes);
        rows.Toggle(showFps);
        rows.ResetRow();
    }, onReset: () => statsPanel.ResetLayout());
```

Rows: `Steps`, `Toggle`, `Choice` (keys of options, or shown as they are), `Action`, `Heading`, `Add` (a game's own kind), `Rows(() => …)` (made again on `Rebuild`), `ResetRow` (asks, then resets every row of the section and `onReset`), `ApplyRow` (puts the section's held picks on). On a row: `.Visible(when)`, `.Enabled(when)`, `.KeepOrRevert()`, `.Resets(values)`.

### Driving the menu

```csharp
menu.Step(SettingsStep.Down);       // Up/Down focus, Left/Right value, Previous/NextSection
menu.Submit();                      // Enter
if (!menu.Back()) Close();          // true when it answered a question no or called a listening off
menu.Tick();                        // each frame: the keep countdown
menu.Point(row); menu.Click(row); menu.Nudge(row, -1); menu.SetStep(row, step); menu.ClickSection(i); menu.Answer(true);
menu.Refresh();                     // on whatever Visible/Enabled read (the device in hand)

menu.Changed.Subscribe(_ => Draw());
menu.Cues.Subscribe(cue => PlayCue(cue));     // Focus, Change, Select, Back, Denied, Section
```

Drawing reads `Sections`, `SectionIndex`, `Rows`, `Focus`, `Question` and `QuestionText`, `Description`, `Listening`; a row's label is `text.Get(row.Id)`, a choice's options `choice.OptionTexts(text)`.

With `com.rubickanov.ui`, one handle takes the menu keys, the pointer and Back:

```csharp
_scope = ui.OpenSettingsScope(menu, navigation, Close);   // Back: the menu first, then Close
void Close() { _scope.Dispose(); menu.Dispose(); store.SaveAsync().Forget(); }
```

### A ready panel

`SettingsPanel` (a `VisualElement`) draws a menu: section tabs, a scroll of rows, the focused row's description and the question with its countdown and answers. It carries BEM classes only, no styles: the game's USS gives the look.

```csharp
var panel = new SettingsPanel(menu, words);                  // redraws on menu.Changed
panel.Draw<SliderRow>((row, m, text) => new MySliderView(row, m, text));   // a game's own kind, or another look
panel.KeyCap = (row, key) => MakeGlyph(row, key);            // keys as glyphs; null draws text caps
root.Add(panel); /* … */ panel.Dispose();
```

`KeyCap` gets the row and the key's index; for an `ActionKeysRow`, `row.Name(key)` is the input package's `KeyName` (text, device, control path, pad family), enough for a glyph or a class per button. Setting it draws the rows again. The focused row is scrolled into view with the headings right above it, once it is laid out.

Classes: `settings` (`settings--asking` while a question is up), `settings__sections`, `settings__section(--current)`, `settings__rows`, `settings__row` (`--focused`, `--disabled`, `--held`, `--heading`, `--<kind>`: steps, toggle, choice, heading, keys, action, apply, custom), `settings__label`, `settings__value`, `settings__about`, `settings__question`, `settings__question-text`, `settings__countdown`, `settings__answers`, `settings__answer` (`--yes`, `--no`, `--chosen`); per kind `settings-steps__step(--on)`, `settings-steps__number`, `settings-toggle__option(--on)`, `settings-choice__arrow(--left|--right|--end)`, `settings-choice__option`, `settings-keys__key` (`--focused`, `--changed`, `--listening`). Hover focuses a row or an answer, a click sets a step, an option, an arrow, a tab or an answer.

### Keep or revert

A `KeepOrRevert()` choice (window, resolution) only picks: the row shows the pick (`IsHeld`, class `settings__row--held`) and the screen stays as it is. The section's `ApplyRow` (`settings.apply`, off while nothing is held) puts every held pick on at once and asks `settings.keep.question`; it goes back after `SettingsMenu.KeepSeconds` (10) unanswered, on "revert", on Back and when the menu is disposed. Picks never applied are dropped when the section opens again or the rows are rebuilt. A section reset puts values on at once and asks too. A keep-or-revert row of a game's own kind (not a choice) still asks on each change. `DisplaySettings.AddRows` adds the APPLY row under its two.

### Words

```csharp
var text = SettingsText.English()
    .With(new SettingsText()
        .Section("sound", "SOUND")
        .Row("sound.master", "MASTER", "Everything you hear.")
        .Option("voice.mode", "PushToTalk", "PUSH TO TALK"));
```

A label is the row's id, its line `id.about`, an option `id/option`, a section `section.id`, the package's words `settings.*`. A translation is another table of the same keys.

### Keys

With `com.rubickanov.input`, `ControlKeys` turns the player's keys into rows: a heading (`keys.heading.keys` KEYS or `keys.heading.pad` BUTTONS) and an `ActionKeysRow` per rebindable action of the scheme in hand, its ways in reading order (W A S D). The keys stay kept by the input package (`SavedControlBindings`, `controls.json`); settings.json holds none of them.

```csharp
var keys = new ControlKeys(bindings, names, device, "UI/Submit");   // one per app; Submit is named in the rows' line
layout.Section("controls", s =>
{
    look.AddRows(s, () => device.Scheme == ControlScheme.Pad);
    keys.AddRows(s);                  // the rows of the device in hand; the section's reset puts that scheme back
    s.ResetRow();
});
keys.Changed.Subscribe(_ => menu.Rebuild());   // device switched, key renamed or rebound
```

Left and right pick one of a row's keys, Enter or a click on a key listens for its new one (`settings.listen` / `settings.listen.pad` under the rows). What comes of it:

| Result | The menu |
|---|---|
| Bound, Unchanged | the key shows, cue `Select` |
| Taken | `settings.swap.question` ("E IS ON INTERACT. SWAP?", YES first); yes swaps the two keys |
| Refused | nothing changes, cue `Denied` |
| Cancelled (Esc, Start, Back, the menu's end) | nothing changes, cue `Back` |

`Rebuild` while a key is listened for waits until it comes, so the listened row never goes from under it; rows of a scheme are made once, so the screen keeps their views and picked keys. A row's label is `keys.<Map>/<Action>` (`keys.Player/Jump`), the game's to word. Maps or actions the game never reads at once may share keys: that is the input package's `RebindOptions.Apart`. `KeysRow` itself is in the core: a game without the input package derives its own (`ListenAsync` is called on Enter and cancelled by Back).

### Saving and migrating

```csharp
var store = new SettingsStore(saveStore, key, new SettingsFileOptions
{
    Format = 2,
    Migrate = (from, values) =>
    {
        if (from < 2) values.Rename("controls.mouse", "look.mouse");
        values.Remove("picture.grain");
        return true;
    },
    LogError = message => Log.Error(message),
});
await store.SaveAsync();            // when the screen closes; also on quit (SaveOnQuit)
```

### Console

```csharp
foreach (ISetting setting in store.All) Print($"{setting.Id} = {setting.Text}");
store.Find("display.window")?.TrySet("windowed");
store.SaveAsync().Forget();
```

## Design Decisions

- **Values by id, not by class** — the file is read into dotted ids; declared settings take theirs and the rest are written back as they were, so a file outlives builds that know more or fewer settings. A new meaning is a new id; `Migrate` moves old ones.
- **The menu model is in the core** — it has its own steps and cues and no ui reference, so it is tested without ui and a game with another UI uses it as it is; `.UI` is a 70-line adapter.
- **Written when the screen closes and on quit** — not on every step; the text is compared with the last read or written, so there are no empty writes.
- **Own small JSON** — the core needs only save, R3 and UniTask.
- **Keys stay the input package's** — `ControlKeys` only shows and changes `ControlBindings`; their file, their checks (taken, refused) and their names are the input package's, so a game without the settings screen keeps the same keys.
- **A broken file is kept aside** — `settings.broken.json`, both paths in the log, the defaults start; one value of the wrong kind only warns and keeps that setting's default.
- **Defaults are the game as it was** — the first start switches nothing; the display applier sets nothing that already is.
- **`ScreenSize.Native`** — "the screen's own" is kept as `native`, shown as the size it is now, and a revert puts `native` back.
