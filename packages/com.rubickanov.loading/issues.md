# Loading package — issues & fix plan

Findings from review on 2026-04-17. Grouped by theme so related fixes can be shipped together.

Status legend: ⬜ not started · 🔄 in progress · ✅ done

**Status (2026-04-17):** все группы A–K исправлены в одном проходе. Оставлено на будущее: `C.5` (extract `ISceneLoader` для unit-тестируемости) и `I.2` (agg-catch в ActivateDeferred — сознательно fail-fast). Финальный прогон Unity Test Runner — за вами, я не могу запустить batch-mode пока редактор держит проект.

---

## Group A — Error surfacing (🔴 critical)
Presenter has `SetError` in interface but service never calls it — dead API. Either wire it or remove it.

- [x] **A.1** Вызывать `_presenter.SetError(ex.Message)` (или прокинуть исключение целиком) в `catch` перед `return LoadResult.Fail(ex)`.
  - `Runtime/LoadingService.cs:61-65`
  - Тест: новая проверка в `LoadingServiceTests` — `Load_OperationThrows_CallsSetErrorOnPresenter`.
  - Альтернатива: удалить `SetError` из интерфейса, если намеренно хотим «ошибки только через `LoadResult`». Решение — wire, т.к. UI обычно хочет подсветить ошибку сам.

---

## Group B — `LoadResult` semantics (🔴 critical + 🟢 minor)
Текущее поведение: любой `OperationCanceledException` → `Ok`. Это не даёт вызывающему отличить «пользователь отменил» от «успех».

- [x] **B.1** Добавить `LoadResult.Cancelled` (разделение `Success` / `Failed` / `Cancelled`) либо ввести `bool Cancelled`. Разграничить: реэнтри-cancel (новый `Load` затёр старый) → `Ok`; внешний `ct.Cancel()` → `Cancelled`.
  - `Runtime/LoadResult.cs` — доработать модель.
  - `Runtime/LoadingService.cs:57-59` — проверять generation в catch: `if (ct.IsCancellationRequested && _loadGeneration == generation) return LoadResult.Cancelled;`
- [x] **B.2** `ToString()` у `LoadResult` — для удобного логирования.
- [x] **B.3** Обновить тесты: `Load_PreCancelledToken_ReturnsOk` → `ReturnsCancelled`; `Load_TokenCancelledDuringExecute_*` → различать реэнтри vs внешний cancel.
- [x] **B.4** Bump `package.json` до `1.1.0` (breaking для контракта).

---

## Group C — `LoadSceneOperation` hardening (🔴 + 🟠 + 🟡)
Класс сочетает несколько проблем разного уровня — логично фиксить одной итерацией.

- [x] **C.1** Null-check результата `SceneManager.LoadSceneAsync`: бросить `InvalidOperationException($"Scene '{_sceneName}' is not in Build Settings or does not exist.")`.
  - `Runtime/LoadSceneOperation.cs:28-29`
- [x] **C.2** Защитный бит готовности к активации: `_isReadyToActivate` выставляется в конце `Execute`, сбрасывается в начале. `Activate` без этого бита = no-op или throw.
  - `Runtime/LoadSceneOperation.cs:24-48`
- [x] **C.3** Перегрузка конструктора с `LoadSceneMode` (default `Single`) — поддержать `Additive`.
  - `Runtime/LoadSceneOperation.cs:19-28`
- [x] **C.4** (Опц., low-prio) Кастомный `description` в конструкторе, чтобы игра могла задать локализованную строку вместо `$"Loading {_sceneName}..."`. — идёт в пару с Group J.
- [ ] **C.5** (Опц., решить позже) Извлечь `ISceneLoader` для тестируемости — overkill для одного класса; оставлено на потом.

---

## Group D — Progress reporter: correctness + allocations (🔴 + 🟡)
Сейчас `new Progress<float>(p => ...)` создаётся на каждый шаг, захватывает `SynchronizationContext`, и поздний `Report` прилетевший после завершения своего шага перезапишет прогресс следующего.

- [x] **D.1** Ввести `ScopedProgress : IProgress<float>` — класс с полями `_baseProgress`, `_weight`, `_epoch` и ссылкой на `LoadingService._presenter` (или на callback). Метод `Reset(baseProgress, weight)` + `Invalidate()` — последний поднимает эпоху; `Report` делает early-return, если эпоха устарела.
  - Один инстанс на весь пайплайн, ресет между шагами — ноль аллокаций.
  - `Runtime/LoadingService.cs:97-102` — заменить `new Progress<float>(...)` на reusable reporter.
- [x] **D.2** Invalidate вызывается после `await op.Execute(...)` (в `finally`), чтобы операции, которые сохранили ссылку на прогресс, не сломали следующий шаг.
- [x] **D.3** Тесты: `Operation_ReportsProgressAfterItCompletes_IsIgnored` (эмулируем через кастомный `FakeOperation`, который сохраняет `IProgress<float>` и репортит после `await`).
- [x] **D.4** Убрать из тестов хак с `SynchronousContext` (`LoadingServiceTests.cs:24-27, 354-358`) — больше не нужен.

---

## Group E — Presenter lifecycle contract (🟠 + 🟢)
`Show()` уходит в параллель с `ExecuteOperations`, первый `Hide()` вызывается до `Show` всегда. Оба пункта не задокументированы.

- [x] **E.1** Явно задокументировать параллельность Show↔Execute в XML-doc `ILoadingPresenter.Show`: «UniTask ожидается параллельно с первыми операциями; презентер обязан принимать `SetProgress/SetDescription` до её завершения».
  - `Runtime/ILoadingPresenter.cs:12-13`
- [x] **E.2** Явно задокументировать идемпотентность `Hide`: «может быть вызван без предшествующего Show».
  - `Runtime/ILoadingPresenter.cs:27-28`
- [x] **E.3** (Решение) Оставить текущее поведение «Hide до Show» или перенести защитный `Hide` в `finally` предыдущего `Load` через generation-gate. Первый вариант проще, второй чище. Склоняюсь к документированию, тесты уже зафиксировали контракт.

---

## Group F — `IDeferrableOperation : ILoadingOperation` (🟡)
Однострочный фикс, риск минимален.

- [x] **F.1** `public interface IDeferrableOperation : ILoadingOperation` — компилятор будет требовать оба.
  - `Runtime/IDeferrableOperation.cs:12`
  - Проверить существующих потребителей (`LoadSceneOperation` уже реализует оба — норм).

---

## Group G — Disposal & thread-safety (🟠)
Связанные проблемы: `_cts` утекает, сервис не disposable, нет защиты от concurrent `Load`.

- [x] **G.1** `LoadingService : IDisposable`. В `Dispose()`: cancel + dispose `_cts`.
  - `Runtime/LoadingService.cs`
- [x] **G.2** Либо `Interlocked.Increment(ref _loadGeneration)` + `lock` вокруг секции cancel/dispose/create `_cts`, либо XML-doc на классе: «not thread-safe; call from a single thread (Unity main thread)».
  - Склоняюсь ко второму варианту — проще, покрывает 99% сценариев в Unity.

---

## Group H — Progress continuity & empty pipeline UX (🟡)
Мелкие UX-фиксы про визуальное поведение прогресс-бара.

- [x] **H.1** В начале каждой итерации вызвать `_presenter.SetProgress(baseProgress)` — гарантия отсутствия «прыжка назад» когда операция не репортит.
  - `Runtime/LoadingService.cs:87-103`
  - Тест: `Load_OperationThatNeverReports_ProgressNeverGoesBackwards`.
- [x] **H.2** Ранний return при `operations.Count == 0` с `LoadResult.Ok`, без Show/Hide/описания.
  - Breaking для теста `Load_EmptyOperations_ReturnsOkAndSetsFinalProgressToOne` — обновить (assertIsTrue(result.Success) + ShowCount == 0).
  - Решение: делать — сейчас «Loading…» для пустого списка бессмысленно.

---

## Group I — Partial deferrable activation (🟡)
Контракт не описан: если один `Activate` бросил, предыдущие уже выполнены.

- [x] **I.1** XML-doc в `ILoadingService.Load` и `IDeferrableOperation.Activate`: «частичная активация возможна; операции обязаны быть безопасны к последующему сбою».
  - `Runtime/ILoadingService.cs`, `Runtime/IDeferrableOperation.cs`
- [ ] **I.2** (Сознательно не делаем) Агрегирующий try/catch в `ActivateDeferredOperations` — вопрос спорный: если активация упала, остановка пайплайна корректна. Оставили fail-fast, поведение задокументировано.

---

## Group J — Configurable default strings (🟡)
Пакет не зависит от `localization` намеренно, но захардкоженные английские строки неудобны.

- [x] **J.1** Параметр `string defaultDescription = "Loading..."` в конструкторе `LoadingService`.
  - `Runtime/LoadingService.cs:22-26, 42`
- [x] **J.2** Перегрузка `LoadSceneOperation(string sceneName, string description)` — идёт в паре с **C.4**.
  - `Runtime/LoadSceneOperation.cs`

---

## Group K — Observability (🟢)
Debug-логи полезны, но не блокеры.

- [x] **K.1** `ZLogDebug` на старт каждой операции с `op.Description` и тайм-стемпом; `ZLogDebug` на завершение с длительностью.
  - `Runtime/LoadingService.cs:87-103`

---

## Rollout plan

Порядок реализации (по убыванию критичности и по минимизации перетестирования):

1. **F** — однострочный, закрывает контракт интерфейсов.
2. **A** — критический баг UX (ошибка не видна), минимальный diff.
3. **C** — встроенная операция без защит — фиксим вместе пакетом.
4. **D** — переписываем прогресс-репортер (уберёт и хак с SyncContext в тестах).
5. **B** — `LoadResult.Cancelled` + bump до 1.1.0. Делаем после D, чтобы тесты чинить один раз.
6. **E** — только документация, быстрое.
7. **G** — disposable + thread-safety документация.
8. **H** — UX-фиксы.
9. **I**, **J**, **K** — когда дойдут руки.

## Файлы, которые будут меняться

- `Runtime/LoadingService.cs` — A, B, D, G, H, J, K (почти всё)
- `Runtime/LoadSceneOperation.cs` — C, J
- `Runtime/IDeferrableOperation.cs` — F, I
- `Runtime/ILoadingPresenter.cs` — E (только xml-doc), возможно A
- `Runtime/ILoadingService.cs` — I (xml-doc)
- `Runtime/LoadResult.cs` — B
- `Tests/LoadingServiceTests.cs` — A (new), B (update), D (new + cleanup), H (update + new)
- `Tests/LoadResultTests.cs` — B
- `Tests/Fakes/FakeOperation.cs` — новая фабрика `SavingProgressReporter` для D.3
- `package.json` — B.4 (version bump)
- `README.md` — отразить новые опции (LoadSceneMode, defaultDescription, Cancelled)

## Верификация

- Unity Test Runner из `unity-project-pckgs` — все существующие тесты зелёные плюс новые.
- README примеры вручную перечитать на согласованность с новым API.
