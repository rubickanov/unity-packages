# UI Loading Package — Issues & Work Plan

Результаты аудита пакета `com.rubickanov.ui.loading`. Документ отслеживает все найденные проблемы и порядок их исправления. Ломающие изменения публичного API допустимы — делаем правильно.

**Зафиксированные решения по ключевым неопределённостям:**
- **M1 (`package.json dependencies`):** декларируем оба `com.rubickanov.*`-родителя (`ui`, `loading`); третьесторонние (`UniTask`) — нет, консистентно с остальным репо.
- **m3 (scope-ownership в `Execute`):** текущая семантика — намеренный дизайн: scope возвращается `SceneViewScopeService.Begin()`, живёт до следующего `Begin()` или `Dispose()` сервиса. При исключении в `Execute` успешно зарегистрированные views **остаются** живыми — следующий scene-load их перезапишет. Фиксируем только в XML-doc, `Execute` **не** оборачиваем в try/finally.

---

## Находки

### Критические (реальные баги)

Нет. Единственный кандидат — потенциальная утечка scope при исключении в `Execute` — после проверки контракта `SceneViewScopeService` (`packages/com.rubickanov.ui/Runtime/SceneViewScopeService.cs:14-25`, `Begin()` сам диспозит предыдущий scope) признан намеренным дизайном. Partial-registration переживает до следующего scene-load; диспоз в `Execute` снял бы успешные регистрации и сломал бы штатное поведение. См. m3.

### Мажорные (M)

- **M1. `package.json` не декларирует зависимости от `com.rubickanov.ui` и `com.rubickanov.loading`** — `package.json:1-9`
  Код пакета использует `using Rubickanov.UI` (типы `IView`, `UILayer`, `SceneViewScopeService`, `ScopedViewRegistration`) и `using Rubickanov.Loading` (интерфейс `ILoadingOperation`). Asmdef (`Runtime/UI.Loading.asmdef:4-8`) корректно ссылается на оба по GUID — но `package.json` пуст. UPM-установка из git-URL у потребителя упадёт на компиляции (как и `localization` до M8 в своём аудите).
  **Решение:**
  ```json
  {
      "name": "com.rubickanov.ui.loading",
      ...
      "dependencies": {
          "com.rubickanov.ui": "1.0.0",
          "com.rubickanov.loading": "1.1.0"
      }
  }
  ```
  Версия `loading` — `1.1.0` (по `packages/com.rubickanov.loading/package.json:3`). `UniTask` не декларируем — консистентно с остальным репо.

- **M2. Отсутствует `README.md`** — корень пакета
  Extension-пакет обязан иметь README 20–60 строк (`README_STANDARD.md:29, 36`). Отсутствие документа означает, что потребитель должен читать исходник, чтобы понять: (1) как регистрировать views в loading-пайплайне, (2) что пакет — мост между `ui` и `loading`, (3) что операция одноразовая и scope живёт до следующего `Begin()`.
  **Решение:** написать README по Extension-шаблону из `README_STANDARD.md:365-387`. Минимальный каркас:
  ```markdown
  # UI Loading

  Bridge between the [UI](../com.rubickanov.ui/) framework and
  [Loading](../com.rubickanov.loading/) pipeline. Registers views as part of
  a loading operation so UI and scene content load together.

  ## Dependencies

  - `com.rubickanov.ui` — `IView`, `UILayer`, `SceneViewScopeService`
  - `com.rubickanov.loading` — `ILoadingOperation`
  - `UniTask` — async/await

  ## Quick Start

  ```csharp
  var op = new RegisterViewsOperation(scopeService)
      .Add<MainMenuView>(UILayer.Screen)
      .Add<HudView>(UILayer.Hud);

  await loadingService.Run(new ILoadingOperation[] { op });
  ```

  ## Usage

  ### Scope ownership

  `RegisterViewsOperation` takes a `SceneViewScopeService` and opens a fresh
  scope on `Execute`. The scope is owned by the scope service — on the next
  scene's `Begin()` the previous scope (and all its registrations) is
  disposed automatically. If `Execute` throws midway, already-registered
  views remain alive until the next `Begin()` / `Dispose()`.
  ```

- **M3. Конструктор `RegisterViewsOperation` не валидирует `scopeService`** — `Runtime/RegisterViewsOperation.cs:16-19`
  ```csharp
  public RegisterViewsOperation(SceneViewScopeService scopeService)
  {
      _scopeService = scopeService;
  }
  ```
  Передача `null` проходит; `NullReferenceException` всплывает позже — в `Execute` на `_scopeService.Begin()` (строка 31), уже в loading-пайплайне. Разрыв точки создания и точки отказа мешает диагностике.
  **Решение:**
  ```csharp
  public RegisterViewsOperation(SceneViewScopeService scopeService)
  {
      _scopeService = scopeService ?? throw new ArgumentNullException(nameof(scopeService));
  }
  ```

- **M4. Отсутствует `Tests/`** — корень пакета
  Нет ни одного теста. Класс прост, но у него две важные инвариантности:
  - Прогресс `progress.Report((float)(i + 1) / _registrations.Count)` при `_registrations.Count == 0` даёт `NaN` — поведение не покрыто.
  - Порядок вызова `_registrations[i](scope)` эквивалентен порядку `Add<T>` (fluent API не перемешивает).
  - Исключение из одной регистрации пропагируется наружу, последующие регистрации не выполняются, scope остаётся в состоянии partial-registration.
  **Решение:** создать `Tests/UI.Loading.Tests.asmdef` (гейт `UNITY_INCLUDE_TESTS` + `includePlatforms: [Editor]`) + `RegisterViewsOperationTests`. Тесты:
  - `Execute_WithMultipleRegistrations_ReportsProgressInOrder` — через `IProgress<float>`-ловушку.
  - `Execute_WhenRegistrationThrows_PropagatesException` и parallelly: `AfterExecuteThrows_ScopeRemainsAliveUntilNextBegin`.
  - `Execute_WithCancellation_ThrowsOperationCanceledException`.
  - `Execute_WithNoRegistrations_CompletesWithoutReportingNaN` (зависит от m1).
  Фейковый `SceneViewScopeService` использовать нельзя (`sealed`? — в ui пакете проверить). Альтернатива: собрать настоящий `UIService` + `FakeViewFactory` (паттерн из `packages/com.rubickanov.ui/Tests/SceneViewScopeServiceTests.cs`).

### Минорные (m)

- **m1. Деление на ноль в `progress.Report` при пустом `_registrations`** — `Runtime/RegisterViewsOperation.cs:27-38`
  Если `Execute` вызван без единого `Add<T>`, цикл `for (int i = 0; i < 0; ...)` не выполняется, но `progress.Report(0f)` (строка 29) срабатывает — фактических проблем нет. Однако после фикса M4 надо убедиться, что `progress.Report(1f)` отправляется и в этом кейсе, чтобы пайплайн корректно считал операцию завершённой.
  **Решение:** в конце `Execute` — безусловный `progress.Report(1f)`; тест покрывает случай `_registrations.Count == 0`.
  ```csharp
  public async UniTask Execute(IProgress<float> progress, CancellationToken ct)
  {
      if (progress == null) throw new ArgumentNullException(nameof(progress));
      progress.Report(0f);
      var scope = _scopeService.Begin();
      for (int i = 0; i < _registrations.Count; i++)
      {
          ct.ThrowIfCancellationRequested();
          await _registrations[i](scope);
          progress.Report((float)(i + 1) / _registrations.Count);
      }
      progress.Report(1f);
  }
  ```

- **m2. `RegisterViewsOperation` не `sealed`** — `Runtime/RegisterViewsOperation.cs:9`
  Нет виртуальных методов, никакого protected state — наследование бессмысленно. Конвенция репо — `sealed` на concrete public-классах (см. все `sealed class` в `com.rubickanov.ui.animations`, `packages/com.rubickanov.ui/Runtime/UIService.cs`, etc.).
  **Решение:** `public sealed class RegisterViewsOperation : ILoadingOperation`.

- **m3. Документировать scope-ownership и partial-registration semantics** — `Runtime/RegisterViewsOperation.cs:9-39`
  Текущий код молча полагается на то, что `SceneViewScopeService.Begin()` сам диспозит старый scope. При исключении в `Execute` уже зарегистрированные views **остаются** живыми до следующего `Begin()`. Это намеренно (зафиксировано в решении) — но нигде не написано. Потребитель, читающий исходник, может решить, что это баг, и "починить" через `using var scope = ...`.
  **Решение:** XML-doc на классе и `Execute`:
  ```csharp
  /// <summary>
  /// Loading operation that registers UI views via a scene scope.
  /// Views are added in order through fluent <see cref="Add{T}"/> calls
  /// and registered sequentially during <see cref="Execute"/>.
  /// </summary>
  /// <remarks>
  /// The scope returned by <see cref="SceneViewScopeService.Begin"/> is
  /// owned by the scope service, not by this operation. It lives until
  /// the next <c>Begin()</c> (next scene load) or until the scope service
  /// is disposed. If <see cref="Execute"/> throws partway through,
  /// already-registered views remain alive — they will be replaced by
  /// the next scene's registrations.
  /// </remarks>
  public sealed class RegisterViewsOperation : ILoadingOperation { ... }
  ```

- **m4. `Description` хардкоднут как `"Loading UI..."`** — `Runtime/RegisterViewsOperation.cs:14`
  Если `LoadingPresenter` выводит `Description` в UI, строка на английском и не локализуется. Для игры с UI-локализацией это артефакт.
  **Решение:** сделать параметром конструктора с дефолтом:
  ```csharp
  private readonly string _description;
  public string Description => _description;

  public RegisterViewsOperation(SceneViewScopeService scopeService, string description = "Loading UI...")
  {
      _scopeService = scopeService ?? throw new ArgumentNullException(nameof(scopeService));
      _description = description ?? throw new ArgumentNullException(nameof(description));
  }
  ```
  Потребитель, использующий локализацию, передаст `loc.GetString(LocKeys.Loading_UI)`.

- **m5. Публичные коллекции: `_registrations` — `List<Func<...>>`, но наружу не торчит** — `Runtime/RegisterViewsOperation.cs:12`
  Проверяю консистентно с `feedback_no_linq.md`: на публичных коллекциях рекомендуется `IEnumerable<T>`. Здесь коллекция — internal state, нигде не экспонируется — ничего делать не надо. Это позитивная отметка, не находка. (Оставлено в списке, чтобы не перепроверять в следующем аудите.)
