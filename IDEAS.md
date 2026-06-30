# Future package ideas

Planning notes — not committed work. The guiding principle: the highest-leverage new
packages are **not** new engines, but the "last mile" that glues the existing strong
primitives (`storage`, `ui`, `audio`, `localization`, `gameplaytags`, `gas`, `config`,
`acs` + `acs.netcode`, `steam-transport`) into the workflows every game re-implements.
Favor things that **compose** with what exists, stay **modular/customizable**, and are
cheap to maintain. Avoid reinventing free industry standards, and avoid building
infrastructure for a game that doesn't exist yet.

**Target direction:** casual Steam multiplayer party games ("rofl" co-op/PvP, ~4–16
players). At that scale NGO is a fine fit and interest management / AOI is **not** needed —
do not build it. Priorities below reflect this direction.

---

## 1. Session / Lobby — `com.rubickanov.session`

The missing "last mile" of the networking stack: from main menu to in-game. Transport
exists (`steam-transport`), replication exists (`acs.netcode`); there is no glue between them.

- Lobby create / browse / join, ready states, player roster, session lifecycle.
- Connection routing / handshake flow; join & leave mid-session handling.
- Transport-agnostic core (Steam lobby backend + dedicated/UnityTransport backend).

**Composes with:** `steam-transport` (Steam lobbies), `acs.netcode` (spin up replication
after connect), `ui` (lobby screen), `steam` (friends/invites).
**Why #1:** no party game exists without lobby + "join your friend via Steam." The single
most painful gap in the current netcode story.

---

## 2. Steam — `com.rubickanov.steam`

A clean wrapper over Steamworks.NET (already a dependency) for the things every Steam game
needs: **friends, names, avatars, rich presence, invites, achievements/stats, cloud saves,
overlay.**

- Raw Steamworks.NET is low-level and painful; every Steam game re-writes the same glue.
- For party games specifically: show player name + avatar in the lobby, invite a friend,
  "Playing X — 2/4" rich presence, simple achievements/stats.

**Composes with:** `steam-transport`, `session` (Steam lobbies), `ui` (avatars in lobby),
`storage` (cloud saves).
**Why high:** pairs directly with `session`; reused by literally every Steam title.

---

## 3. Codegen — `com.rubickanov.codegen`

A **separate, centralized** code-generation package that other packages depend on, instead
of each shipping its own ad-hoc generator.

Today `localization` and `gameplaytags` each have a bespoke generator. Pull the engine out:

- **Shared codegen framework** — file writing, identifier sanitization (handle case
  collisions like `Damage.fire` vs `Damage.Fire`), idempotent regeneration, "regenerate on
  asset change" hooks, a Project Settings panel.
- **Extension point** — an `ICodeGenerator` registry so any package contributes its generator
  centrally. `localization` and `gameplaytags` migrate onto it (one pipeline, one settings UI).
- **Built-in generators (type-safe Unity constants)** — scenes, layers, tags, sorting layers,
  animator parameter hashes, Addressable keys, Resources paths, input action names. Kills a
  whole class of stringly-typed bugs.

**Composes with:** `localization`, `gameplaytags`, `config` (Addressable addresses),
`input` (action-name constants).
**Why:** the pattern already exists twice; centralizing removes duplication and unlocks the
constants generators cheaply. Low maintenance, no clean free equivalent.

---

## 4. Settings — `com.rubickanov.settings`

Typed, persisted, reactive game settings (graphics / audio / input / language) bound to UI.
Collect everything *every* game needs — but **modular and customizable**, not a fixed monolith.

- Each setting category is a **pluggable module** (add/remove/replace freely); custom modules
  are first-class.
- Typed setting definitions with default + validation; reactive current value (R3).
- Persisted automatically; UI binding helpers for the settings screen.

**Composes with:** `storage` (persistence), `ui` (settings screen), `audio` (AudioMixer
volumes — already implemented), `localization` (language switch — already reactive),
`input` (rebinding). Most pieces already exist; this is the aggregator + bindings.
**Why:** literally every game builds this, and it's almost pure glue over existing packages.

---

## 5. Scenes — `com.rubickanov.scenes`

Additive scene-flow orchestrator: scene groups, additive load/unload, transitions, bootstrap.

- **Persistent "systems" scene + additive content** — keep the DI root and services
  (`audio`, `localization`, `config`, networking) alive across level changes instead of
  destroying/re-initializing them on every `LoadScene(Single)`.
- **Minigames as scenes** — for party games, load each minigame additively over the systems
  scene; swap minigames without tearing down core services. (This is the concrete reason it
  matters for this genre — it was previously parked.)
- Also covers multi-scene levels (team workflow, no merge conflicts) and seamless transitions.

**Composes with:** `loading` (async load + progress), `statemachine` (flow states),
`ui.loading` (loading screen), `match` flow if built.
**Why:** unlocks the systems-scene pattern, which fits the DI/service-heavy architecture and
the "many minigames" structure of party games.

---

## 6. Input — `com.rubickanov.input`

A reactive (R3) layer over Unity's Input System, **including local/couch co-op**.

- **Context stack** (gameplay / UI / menu) so input maps switch cleanly by game state.
- **Local / couch co-op** — device assignment, join-by-button, per-player input (party
  games are often online *and* on the couch).
- Rebinding + a rebinding UI; input buffering.

**Composes with:** `statemachine` (context per state), `settings` (rebinds persisted),
`ui` (rebind screen), `codegen` (action-name constants), `session` (map local players to
session slots).
**Note:** Unity's Input System covers the basics; the value here is the context stack,
couch-coop assignment, and reactive surface.

---

## 7. Synced randomness — `com.rubickanov.netcode.random` (small)

Networked seed distribution layered on the existing `utils.DeterministicRandom`, so every
client produces the same sequence — same minigame layout / map / spawn order for everyone.

- Server picks a seed, distributes it; clients seed `DeterministicRandom` identically.
- Per-round / per-match seed scoping; deterministic draws for fairness and replays.

**Composes with:** `utils` (DeterministicRandom — already exists), `acs.netcode` (seed sync),
`match` flow (per-round seed).
**Why:** party games need fair, identical randomness across clients; the deterministic RNG
already exists, only the network seed handshake is missing. Cheap, neat.

---

## 8. Crash / log reporting — `com.rubickanov.logging.reporting`

Upload logs and error reports to a webhook (Discord / Sentry / custom endpoint) when a
player hits a crash or unhandled exception.

- Hook `Application.logMessageReceived` / `AppDomain.UnhandledException`; on error, package
  up the recent log buffer + the rotated log file and POST it to a configured webhook.
- Attach context: build version, platform, and Steam ID / player name when available.
- Throttle / dedupe so one bad frame doesn't spam the webhook.

**Composes with:** `logging` (file logs + `UnityLogInterceptor` already exist — this rides on
top), `steam` (player identity for context).
**Why / when:** once a build ships to friends you're blind to crashes on their machines
without it. Pull-based — build it when you actually have testers, not before.

Recorded so this isn't re-litigated later:

- **Game feel / juice (`feel`)** — individual effects (screenshake, hitstop, rumble) are
  trivial (~10 lines each), write them inline per game. The value of MMFeedbacks/Feel is
  orchestration + designer-facing tooling, which a solo programmer doesn't need. (Multiplayer
  note: do hitstop **visually only** — `Time.timeScale = 0` breaks the networked sim/physics
  tick.)
- **Inventory / dialogue / quest** — high value but game-specific and opinionated; build only
  inside a concrete project.
- **Event bus / pooling / timers** — already covered by R3 + `utils`; would duplicate.
- **DI / tweening** — use VContainer + LitMotion; don't reinvent.
- **AI (utility AI, BT, EQS)** — `behaviortree` and `eqs` were archived in favor of Unity's
  free `com.unity.behavior`; don't climb back in without a game that needs it.
- **Interest management / AOI** — only matters at large player counts; not needed for ~4–16
  party games.

---

## Considered, not selected yet

- **Match / round flow (`com.rubickanov.match`)** — round lifecycle, cross-round scoring, win
  conditions, return-to-lobby. The skeleton of most party games; composes with `statemachine`
  + `session` + `acs.netcode`. Slightly game-specific — pays off from the second party game.
  Promote when a concrete party game is in progress.
</content>
