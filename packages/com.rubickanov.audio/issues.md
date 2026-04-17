# Audio Package — Issues & Work Plan

Результаты аудита пакета `com.rubickanov.audio`. Документ отслеживает все найденные проблемы и порядок их исправления. Ломающие изменения публичного API допустимы — делаем правильно.

---

## Находки

### Критические (реальные баги)

- **C1. Crossfade race при мид-фейд вызове `PlayMusic`** — `UnityAudioService.cs:265-292`
  Новый `PlayMusic` во время активного crossfade: `_musicSourceAActive` переключается, бывший `incoming` становится `outgoing` с промежуточной громкостью, отменённая задача `CrossfadeAsync` возвращается по токену и не сбрасывает громкость (cleanup на строках 310-313 обходится через `return` на 301). Новый `incoming` имеет остаточную громкость от прошлого fade-out, `Play()` запускает трек на неопределённой громкости.

- **C2. Async fire-and-forget без обработки исключений** — `UnityAudioService.cs:159-163, 222-241, 294-314`
  `ReturnAfterPlayAsync`, `FollowAndReturnAsync`, `CrossfadeAsync` запускаются через `.Forget()`. Если внешне уничтожается `source`/`follow`/`_root`, обращение к свойствам UnityObject кинет исключение мимо try/catch → `UnobservedTaskException`. Источник не возвращается в пул → утечка, пул деградирует.

- **C3. `ReturnAfterPlayAsync` не ловит `OperationCanceledException`** — `UnityAudioService.cs:159-163`
  При `Dispose()` токен отменяется, `WaitWhile` кидает OCE, `ReturnSource` не вызывается. В общем случае это терпимо (root всё равно уничтожен), но ошибка попадает в UnobservedTask.

### Мажорные

- **M1. `UntrackHandle` — O(n) линейный поиск по значениям словаря** — `UnityAudioService.cs:143-157`
  Вызывается на каждой эвикции/возврате/стопе. Решение: обратный словарь `Dictionary<AudioSource, int>` → O(1).

- **M2. Случайный pitch применяется к музыке** — `UnityAudioService.cs:274`
  `ApplyPitch(incoming, in music)` меняет тональность музыкального трека при каждом вызове. Нежелательно. Нужен отдельный `MusicConfig` без pitch variation.

- **M3. Эвикция пула молча инвалидирует хендлы** — `UnityAudioService.cs:97-118`
  Самый старый активный source вытесняется без уведомления потребителя. `SoundHandle` продолжает `IsValid=true`, `StopSound` молча no-op. Особенно болезненно для `PlayLoop` — loop может быть убит другим SFX.

- **M4. Incoming source играет до сброса громкости** — `UnityAudioService.cs:273-275, 290`
  `incoming.Play()` вызывается до установки финальной громкости — один кадр играется на предыдущей громкости. В non-crossfade ветке `incoming.volume = 1f` задаётся после `Play()`.

- **M5. Хардкод имён параметров AudioMixer** — `UnityAudioService.cs:17-19`
  `MasterVolume`, `MusicVolume`, `SFXVolume` как константы. Если пользователь назвал параметры иначе — `SetFloat` молча не применит. Нужны поля в `AudioServiceConfig`.

- **M6. Нет тестов** — корень пакета
  Нулевое покрытие: пул, хендлы, crossfade, громкость/персист, Dispose.

- **M7. Нет null-check `config` в конструкторе** — `UnityAudioService.cs:50-56`
  `config.Mixer` на строке 53 кинет NRE без понятного сообщения.

- **M8. `NullAudioService` возвращает 0 для громкостей** — `NullAudioService.cs:20-22`
  Settings UI на сервере/хедлесс покажет «0% звук». Должен возвращать 1f или хранить последние установленные значения.

### Минорные

- **m1. `SoundConfig.Resource` объявлен как `AudioResource?`** — `SoundConfig.cs:13`
  Nullable-аннотация на Unity Object вводит в заблуждение (UnityObject имеет лайфтайм-null, не совпадающий с C# null).

- **m2. `Range(0, 0.3)` на `_pitchVariation`** — `SoundConfig.cs:11`
  Магическое число без объяснения. Либо расширить до 0.5, либо задокументировать.

- **m3. `_musicSourceA.volume` не задан явно** — `UnityAudioService.cs:61-63`
  B получает `= 0f`, A использует дефолт AudioSource. Для симметрии задать явно.

- **m4. `Dispose()` не async** — `UnityAudioService.cs:361-368`
  `Object.Destroy(_root)` отложено до следующего кадра. При немедленной выгрузке сцены могут быть артефакты. Минимум — задокументировать.

- **m5. Overflow `_nextHandleId`** — `UnityAudioService.cs:34, 138`
  После int.MaxValue — коллизии. Сценарий малореалистичен, но легко закрыть через `long`.

- **m6. `ApplyVolume` игнорирует результат `SetFloat`** — `UnityAudioService.cs:354-359`
  `SetFloat` возвращает false, если параметр не exposed. Результат не проверяется.

- **m7. README не упоминает main-thread requirement**

- **m8. README не документирует имена mixer-параметров**

- **m9. Crossfade duration только глобальный из config** — нет per-call overload.

- **m10. Гонка `PlaySFXAttached` при destroy `follow`** — `UnityAudioService.cs:222-241`
  В single-threaded маловероятно, но лечится через try/catch (см. C2).

### Отсутствующие фичи

- **F1.** Ducking (приглушение SFX во время музыки/диалога)
- **F2.** Fade-in/fade-out для SFX и loop
- **F3.** Дополнительные mixer-шины (UI, Dialog, Ambient)
- **F4.** Mixer snapshot-переходы
- **F5.** Named loop slots — решает M3 для loops

---

## Порядок работы (батчи)

Батчи упорядочены по зависимости: ранние создают API-фундамент для поздних. Каждый батч — логически связанный коммит.

### Batch 1 — Configs: split SoundConfig/MusicConfig + extended AudioServiceConfig
**Решает:** M2, M5, m1, m2, F3

- [x] Изменить `SoundConfig.cs`: убрать `?` с `Resource`, расширить pitch range до 0.5
- [x] Создать `MusicConfig.cs` (без pitch variation)
- [x] Расширить `AudioServiceConfig.cs`: поля для имён mixer-параметров (Master/Music/SFX + опционально UI/Dialog/Ambient), опциональные `AudioMixerGroup` для доп. шин
- [x] В `UnityAudioService` читать имена из config, убрать константы `MasterVolumeParam` etc.
- [x] `IAudioService.PlayMusic(in MusicConfig)` (было `in SoundConfig`)

### Batch 2 — Init hygiene & small safety fixes
**Решает:** M7, m3, m5, m6

- [x] `ArgumentNullException` на `config` в конструкторе `UnityAudioService`
- [x] Явно задать `_musicSourceA.volume = 1f`
- [x] `_nextHandleId` → `long`, `SoundHandle._id` → `long`
- [x] `ApplyVolume` логирует warning, если `SetFloat` вернул false

### Batch 3 — Perf: UntrackHandle в O(1)
**Решает:** M1

- [x] Добавить `Dictionary<AudioSource, int> _sourceHandles` (обратный мэппинг)
- [x] `TrackHandle`/`UntrackHandle` — обновить обе стороны
- [x] `StopSound` использует прямой lookup

### Batch 4 — Core bug fixes: async error handling + crossfade race
**Решает:** C1, C2, C3, M4, m10

- [x] Обернуть `ReturnAfterPlayAsync`, `FollowAndReturnAsync`, `CrossfadeAsync` в try/catch (OCE тихо, Exception — лог + cleanup)
- [x] Переписать `PlayMusic`/`CrossfadeAsync`:
  - `incoming.volume = 0f` до `Play()`
  - `incoming.pitch = 1f` (без случайного pitch)
  - Crossfade стартует с фактической громкости outgoing (`outgoingStartVolume`), а не 1f
  - Cleanup корректно работает и при нормальном завершении, и при re-entry через новый `PlayMusic`
- [x] Проверить guard `source != null` во всех местах внутри async методов

### Batch 5 — Named loop slots (убирает silent eviction для loops)
**Решает:** F5, M3 (для loops)

- [x] Добавить в `IAudioService`: `PlayLoop(string slot, in SoundConfig, float volumeScale)`, `StopLoop(string slot)`, `IsLoopPlaying(string slot)`
- [x] Удалить старый `PlayLoop(in SoundConfig)` (breaking change)
- [x] В `UnityAudioService` — `Dictionary<string, AudioSource> _loopSources`, loop-sources не входят в пул и не подлежат эвикции
- [x] На Dispose — уничтожить все loop sources

### Batch 6 — Fade in/out для SFX
**Решает:** F2

- [x] Параметр `fadeIn` в `PlaySFX*` методы (default 0)
- [x] Параметр `fadeOut` в `StopSound` (default 0)
- [x] Async fade через UniTask (учитывается в `ReturnAfterPlayAsync`)
- [x] Обновить named loop API с fade-параметрами

### Batch 7 — Ducking
**Решает:** F1

- [x] `IAudioService.DuckSFX(float amount01, float duration, float attackTime, float releaseTime)`
- [x] Реализация через временное изменение `SfxVolumeParam` на mixer без перезаписи `_sfxVolume`
- [x] CancellationToken для повторного вызова (отмена предыдущего duck)

### Batch 8 — Mixer snapshot transitions + per-call crossfade duration
**Решает:** F4, m9

- [x] `IAudioService.TransitionToSnapshot(string name, float duration)` через `_mixer.FindSnapshot(name).TransitionTo(duration)`
- [x] `PlayMusic(in MusicConfig, float? crossfadeDuration = null)` — optional override длительности

### Batch 9 — NullAudioService
**Решает:** M8

- [x] Хранить последние установленные громкости в полях, возвращать их в геттерах
- [x] Реализовать no-op для всех новых методов интерфейса (fade, loop slots, duck, snapshots)

### Batch 10 — Tests
**Решает:** M6

- [x] `Tests/Audio.Tests.asmdef` с `defineConstraints: ["UNITY_INCLUDE_TESTS"]` + `includePlatforms: ["Editor"]`
- [x] Ссылки на Audio.Runtime, UniTask, Storage.Runtime, nunit, TestRunner
- [x] `UnityAudioServiceTests`:
  - `Constructor_NullConfig_Throws`
  - `PlaySFX_InvalidConfig_ReturnsInvalidHandle`
  - `PlaySFX_PoolExhausted_EvictsOldestAndInvalidatesHandle`
  - `StopSound_ValidHandle_StopsSource`
  - `StopSound_AlreadyStoppedHandle_NoOp`
  - `PlayLoop_SameSlotTwice_ReplacesWithNew`
  - `StopLoop_StopsCorrectSlot`
  - `PlayMusic_MidCrossfade_HandlesHandoffCleanly`
  - `SetMasterVolume_ClampedTo01`
  - `SetXxxVolume_WithStorage_PersistsValue`
  - `Dispose_CancelsPendingTasks`
- [x] `NullAudioServiceTests`:
  - `AllPlayMethods_ReturnInvalidHandle`
  - `SetThenGetVolume_ReturnsSetValue`
- [x] In-memory mock для `IStorageService`

### Batch 11 — README
**Решает:** m4, m7, m8

- [x] Секция "Thread safety" — main-thread requirement
- [x] Задокументировать, что имена mixer-параметров настраиваются через config
- [x] Обновить Usage под новый API: `MusicConfig`, named loop slots, fade-in/out, ducking, snapshots, доп. шины
- [x] Design Decisions — объяснить split SoundConfig/MusicConfig и named slots
- [x] Упомянуть отложенный `Object.Destroy` в `Dispose`

---

## Статус

Все батчи выполнены. Проверка: открыть Unity, убедиться в отсутствии ошибок компиляции, прогнать `Audio.Tests` в Test Runner, выполнить smoke-тесты из плана.
