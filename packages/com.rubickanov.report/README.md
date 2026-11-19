# Report

Bug reports over the Sentry Unity SDK: errors, exceptions, hangs and native crashes once a session each, the end of the
log attached, a player's feedback with the moment's screenshot, the frame rate as metrics; no IP, no machine name.

## Dependencies

- `io.sentry.unity` 4.11.0 — **the game provides it** (a git URL can't be a package dependency):
  `"io.sentry.unity": "https://github.com/getsentry/unity.git#4.11.0"` in the game's manifest. The runtime assembly
  references it by name (`io.sentry.unity.runtime`).
- `com.rubickanov.log` — the `Report` log channel.
- `com.rubickanov.save` — `InstallId` keeps its id in the game's save store (`UniTask` comes with it).

Not in `unity-project-pckgs` (no Sentry there); its tests run from a game linked `local`.

## Architecture

```
Sentry wizard's SentryOptions.asset ──OptionsConfiguration──► ReportGate (SDK's own start off in play)
                     │
game ──Reporter.Start(ReportStart)──► LoadSentryUnityOptions + our values ──► Sentry.Unity.SentrySdk.Init
                                                                    │
Unity log / exceptions / ANR / native ──► SDK ──BeforeSend──► Fingerprint → OnceASession → LogTail attached
                                                                    │                      └─► Broke (main thread, Tick)
game's report window ──Moment.Take──► JPEG ──Reporter.SendFeedback(note, shot)──► feedback linked to LastProblem
```

## Assemblies

| Assembly | Platforms | What |
|---|---|---|
| `Rubickanov.Report.Runtime` | all | `Reporter` and its parts |
| `Rubickanov.Report.Editor` | Editor | `SentryOptionsSetup`: the options fields the reporter relies on, for the game's builder |
| `Rubickanov.Report.Tests` | Editor | fingerprints, dedupe and cap, log tail scrub, feedback link, start rule |

## Core Concepts

- **The game starts the SDK, not the SDK itself.** `ReportGate` (a `SentryOptionsConfiguration` on the wizard's options)
  sets `Enabled = false` in play, so the SDK's start at `SubsystemRegistration` does nothing; `Reporter.Start` loads the
  same options, applies its own values and calls `Init`. At build time the gate passes the options untouched.
- **No DSN, no reports.** An options asset with an empty DSN keeps the reporter off with one Info line; nothing throws.
- **Once a session.** `Fingerprint` names a kind of error (exception type + first frame of the game's own code, or the
  cleaned message with numbers and ids folded); `OnceASession` lets each kind through once and stops after
  `ReportConfig.PerSessionCap` kinds. The duplicates die on the player's machine, not in Sentry's quota.
- **Privacy.** `SendDefaultPii` off, no server or device name (also in the native layer's scope), the user is
  `InstallId` — a random id kept in the save store, not `SystemInfo.deviceUniqueIdentifier`. The player's home, data
  and save folders (the user name sits in them) become `~` (`PathScrub`) in the log tail, every breadcrumb, the
  error line and exception texts.
- **Busy.** `Reporter.Busy(true)` while a scene loads: a hang then is dropped.

## Quick Start

1. Add `io.sentry.unity` to the manifest, run Tools → Sentry once (it writes `Assets/Resources/Sentry/SentryOptions.asset`).
2. Make a `ReportConfig` and a `ReportGate` asset (Create → Rubickanov), then in an editor builder:

```csharp
var options = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SentryOptionsSetup.OptionsPath);
SentryOptionsSetup.Apply(options, gate);
```

3. One reporter for the app:

```csharp
var reporter = new Reporter(config);
var install = new InstallId(saveStore, new SaveKey(MyAreas.Report, "report.json"), Application.version);
reporter.Start(new ReportStart
{
    Environment = ReportEnvironment.Pick(null, Application.isBatchMode, automated: false, config.Environment),
    Release = "mygame@" + Application.version,
    UserId = install.Value,
    CacheDir = Path.Combine(saveRoot, "report-cache"),
});
```

## Usage

```csharp
// Every frame, main thread.
reporter.Busy(sceneFlow.Loading);
reporter.Tick();                                   // raises Broke for new problems
reporter.Frames.Tick(Time.unscaledDeltaTime, Application.isFocused);

reporter.Broke += problem => tape.Show(problem.Title);
reporter.Tag("scene", sceneName);

// F8: take the moment before drawing the window, then send.
Moment.Take(config.ShotWidth, config.ShotQuality, shot => OpenWindow(shot));
(SentryId id, SendResult result) = await reporter.SendFeedback(note, shot);   // Sent, Queued, Off, Refused
if (result == SendResult.Queued) result = await reporter.Resend(id);

// Extra files on every feedback.
reporter.FeedbackFiles.Add(() => new FeedbackFile("bench.json", Encoding.UTF8.GetBytes(json), "application/json"));

reporter.Stop();                                    // the player turned reports off
```

`ReportStart.LinuxNative` keeps sentry-native on under Mono on Linux (off by default there: breakpad would turn
Mono's null-reference signal into a crash). IL2CPP always has it on.

## Design Decisions

- **No wrapper over the SDK.** Only `Sentry.Unity.SentrySdk` starts and stops it: its `Close` resets its statics and the
  native layer, so `Start` → `Stop` → `Start` in one process works.
- **The window is the game's.** The package takes the shot and sends; drawing, words and keys stay in each game.
