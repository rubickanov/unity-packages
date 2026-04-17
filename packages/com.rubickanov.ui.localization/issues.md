# UI Localization Package — Issues & Work Plan

Результаты аудита пакета `com.rubickanov.ui.localization`. Документ отслеживает все найденные проблемы и порядок их исправления. Ломающие изменения публичного API допустимы — делаем правильно.

**Зафиксированные решения по ключевым неопределённостям:**
- **M1 (`package.json dependencies`):** декларируем оба `com.rubickanov.*`-родителя (`ui`, `localization`); третьесторонние (R3, UniTask, Unity.Localization) — нет, консистентно с остальным репо.
- **m5 / m6 (feature-gaps):** параметризованные строки в UIToolkit-биндингах и хелперы `BindCurrentLocale` / `BindIsRTL` включены как минорные пункты с пометкой «feature» — не обязательны для первой итерации, но зафиксированы, чтобы не потерять.
- **«broken GUID 77221876cc6b8244180b96e320b1bcd4» (снят):** при верификации обнаружено, что этот GUID используется в 5 asmdef'ах репо (включая core `com.rubickanov.ui/UIToolkit`, `com.rubickanov.ui/UGUI`, `com.rubickanov.localization/Runtime`). Это reference на R3 (`org.nuget.r3`), установленный через NuGetForUnity — `.meta`-файлы не коммитятся в репо, потому `grep` по `.asmdef.meta` пустой. Ссылка валидна, не находка.

---

## Находки

### Критические (реальные баги)

Нет. Оба файла кода (`LocalizationViewExtensions.cs` — 21 строка; `UIToolkitLocalizationExtensions.cs` — 49 строк) — тонкие обёртки над parent-сервисом. Реальные баги, касающиеся локали, сидят в parent'е `com.rubickanov.localization` (см. его `issues.md`, M1 `SetLocaleAsync` timing, M5 `LocalizedValue.SetKey` на null-сервисе, M6 FormatException swallow) и автоматически исчезнут у потребителя после исправления родителя. В самом `ui.localization` — отсутствие декларации зависимостей, отсутствие README/Tests, асимметрия API, косметика.

### Мажорные (M)

- **M1. `package.json` не декларирует зависимости от `com.rubickanov.ui` и `com.rubickanov.localization`** — `package.json:1-9`
  Runtime asmdef (`Runtime/UI.Localization.Runtime.asmdef:4-8`) ссылается на `Localization.Runtime` (GUID `68edc6396f2e6bb879d95ff0a61595e5`). UIToolkit asmdef (`UIToolkit/UI.Localization.UIToolkit.asmdef:4-11`) ссылается на `UI.Runtime`, `UI.UIToolkit`, `Localization.Runtime`. `package.json` пуст. UPM-установка из git-URL упадёт на компиляции (паттерн совпадает с M8 в аудите parent `localization`).
  **Решение:**
  ```json
  {
      "name": "com.rubickanov.ui.localization",
      ...
      "dependencies": {
          "com.rubickanov.ui": "1.0.0",
          "com.rubickanov.localization": "1.0.0"
      }
  }
  ```
  Третьесторонние (R3, UniTask, Unity.Localization) не декларируем — консистентно со всем репо.

- **M2. Отсутствует `README.md`** — корень пакета
  Extension-пакет обязан иметь README 20–60 строк (`README_STANDARD.md:29, 36`). Потребитель должен понять из README: (1) пакет — мост между `ui` и `localization`, (2) две точки входа: backend-agnostic `CreateLocalized` (Runtime) и UIToolkit `BindLocalized` (UIToolkit), (3) типовой вызов на каждом варианте.
  **Решение:** по Extension-шаблону (`README_STANDARD.md:365-387`). Минимум:
  ```markdown
  # UI Localization

  Localization bindings for UI views. Extension for
  [UI](../com.rubickanov.ui/) and
  [Localization](../com.rubickanov.localization/).

  ## Dependencies

  - `com.rubickanov.ui` — view base classes (`UIToolkitView<TVM>`, `ViewModelBase`)
  - `com.rubickanov.localization` — `ILocalizationService`, `LocalizationKey`, `LocalizedValue`
  - `R3` — reactive subscriptions
  - `UniTask` — transitively through `com.rubickanov.ui`

  ## Quick Start

  ```csharp
  public sealed class MainMenuView : UIToolkitView<MainMenuViewModel>
  {
      private Label _title = default!;

      protected override void OnInitialize()
      {
          _title = Root.Q<Label>("title");
          this.BindLocalized(_title, LocKeys.MainMenu_Title);
      }
  }
  ```

  ## Usage

  ### Backend-agnostic (ViewModel layer)

  ```csharp
  var greeting = viewModel.CreateLocalized(loc, LocKeys.Hud_Welcome);
  // greeting.Value — reactive string; disposal is tracked by the ViewModel.
  ```

  ### UIToolkit bindings

  ```csharp
  this.BindLocalized(label, LocKeys.Pause_Title);
  this.BindLocalized(button, LocKeys.Pause_Resume);
  this.BindLocalized(label, loc => $"HP: {loc.GetString(LocKeys.Hud_Hp)}");
  ```
  ```

- **M3. Отсутствует `Tests/`** — корень пакета
  Нет покрытия ни для `CreateLocalized`, ни для UIToolkit-биндингов. Минимально ценно:
  - `CreateLocalized_CallsTrackDisposable` — проверить, что `vm.TrackDisposable(value)` был вызван; при Unbind ViewModel `LocalizedValue.Dispose()` вызывается.
  - `BindLocalized_ReactsToLocaleChange` — фейковый `ILocalizationService` с `Subject<Locale> onLocaleChanged`; эмитим change → `label.text` обновляется на новое значение из `GetString`.
  - `BindLocalized_UnsubscribesOnViewUnbind` — проверить, что `_disposables` в `UIToolkitView` снимает подписку при Hide/Unbind (подписка должна сниматься через `BindObservable`).
  **Решение:** создать `Tests/UI.Localization.Tests.asmdef` (гейт `UNITY_INCLUDE_TESTS` + `includePlatforms: [Editor]`). Фейковый `ILocalizationService` — простая реализация интерфейса с управляемым `Subject<Locale>`. Для UIToolkit-тестов потребуется `VisualElement` + `Label`; при необходимости PlayMode-тесты или headless-UIElements.

- **M4. Нет валидации `LocalizationKey` в `BindLocalized`-extensions** — `UIToolkit/UIToolkitLocalizationExtensions.cs:13-20, 22-29, 31-38, 40-47`
  `default(LocalizationKey)` / пустой key проходит; `loc.GetString(key)` возвращает пустую строку (контракт родителя). Получаем молчаливый пустой label — сложно отличить от "забыли заполнить ключ" в локализационной таблице.
  **Решение:** в каждом `BindLocalized` добавить:
  ```csharp
  if (!key.IsValid) throw new ArgumentException("LocalizationKey is empty", nameof(key));
  ```
  (Альтернатива: `if (string.IsNullOrEmpty(key.Key) || string.IsNullOrEmpty(key.Table)) throw new ArgumentException(...)` — по текущему определению `LocalizationKey`.)

- **M5. Асимметрия DI между Runtime и UIToolkit вариантами** — `Runtime/LocalizationViewExtensions.cs:13-19` vs `UIToolkit/UIToolkitLocalizationExtensions.cs:17, 26, 35, 44`
  `CreateLocalized(this vm, ILocalizationService loc, key)` — сервис принимается параметром явно, хорошо для тестов.
  UIToolkit `BindLocalized` — сервис резолвится через `view.GetService<ILocalizationService>()`. Это удобно в prod, но ломает тесты: нельзя пробросить фейковый сервис в уже созданный view без собранного DI-контейнера.
  **Решение:** добавить оверлоуды, принимающие `ILocalizationService` явно — оставив `GetService`-варианты как convenience-обёртки:
  ```csharp
  public static void BindLocalized<TVM>(
      this UIToolkitView<TVM> view, Label label, LocalizationKey key, ILocalizationService loc)
      where TVM : ViewModelBase
  {
      if (loc == null) throw new ArgumentNullException(nameof(loc));
      if (!key.IsValid) throw new ArgumentException(...);
      label.text = loc.GetString(key);
      view.BindObservable(loc.OnLocaleChanged, _ => label.text = loc.GetString(key));
  }

  public static void BindLocalized<TVM>(
      this UIToolkitView<TVM> view, Label label, LocalizationKey key)
      where TVM : ViewModelBase
      => view.BindLocalized(label, key, view.GetService<ILocalizationService>());
  ```
  Аналогично для `Button`, `Func<ILocalizationService, string>` оверлоудов.

### Минорные (m)

- **m1. Closure-аллокация на каждом `BindLocalized`** — `UIToolkit/UIToolkitLocalizationExtensions.cs:19, 28, 37, 46`
  Лямбда `_ => label.text = loc.GetString(key)` захватывает 3 переменных (`loc`, `label`, `key`) + сам делегат. На один биндинг — один closure-объект + один delegate. В экранах со списками (например, инвентарь, список диалогов) — N биндингов × 2 аллокации. Для первой итерации не критично, R3 оптимизирован.
  **Решение:** опционально — pool-friendly observer-класс, хранящий `(label, loc, key)` как поля, и реализующий `IObserver<Locale>`. Не первая очередь; подходит для hot-path, если профилировщик покажет.

- **m2. Нет equality-check перед `label.text = loc.GetString(key)`** — `UIToolkit/UIToolkitLocalizationExtensions.cs:19, 28, 37, 46`
  Unity UIToolkit не оптимизирует присваивание одинаковой строки — при каждом `OnLocaleChanged` mark dirty вызывается даже если текст не изменился. Микро-оптимизация с быстрым эффектом:
  ```csharp
  view.BindObservable(loc.OnLocaleChanged, _ =>
  {
      var next = loc.GetString(key);
      if (label.text != next) label.text = next;
  });
  ```
  Работает корректно, если `GetString` детерминирован для одной и той же локали (так и есть в `LocalizationService`).

- **m3. `LocalizationViewExtensions.CreateLocalized` без null-guard на `loc`** — `Runtime/LocalizationViewExtensions.cs:13-19`
  Передача `null` — `NullReferenceException` внутри `loc.Localize(key)`. Место отказа разнесено с точкой вызова.
  **Решение:**
  ```csharp
  public static LocalizedValue CreateLocalized(
      this ViewModelBase vm, ILocalizationService loc, LocalizationKey key)
  {
      if (vm == null) throw new ArgumentNullException(nameof(vm));
      if (loc == null) throw new ArgumentNullException(nameof(loc));
      var value = loc.Localize(key);
      vm.TrackDisposable(value);
      return value;
  }
  ```

- **m4. XML-doc на точках входа минимальные и неполные** — `Runtime/LocalizationViewExtensions.cs:5-12`, `UIToolkit/UIToolkitLocalizationExtensions.cs:8-11`
  Runtime-вариант упоминает disposal tracking, но не говорит о требовании, что `ILocalizationService.InitializeAsync()` уже отработал. UIToolkit-вариант вообще без `/// <summary>` на каждом методе — один общий summary на класс.
  **Решение:** добавить `/// <summary>` на каждый `BindLocalized`-оверлоуд с указанием поведения (обновление при `OnLocaleChanged`, disposal через `view._disposables`). В `CreateLocalized` упомянуть `InitializeAsync()` как предусловие.

- **m5. (feature) Параметризованные строки в UIToolkit-биндингах** — `UIToolkit/UIToolkitLocalizationExtensions.cs`
  Текущие `BindLocalized(Label, LocalizationKey)` вызывают `GetString(key)` без `args`. Для динамических сообщений ("Вы получили {0} золота") нужен оверлоад, возвращающий формат-args. `Func<ILocalizationService, string>`-вариант уже позволяет это сделать вручную, но громоздко.
  **Решение (feature):** добавить оверлоуды:
  ```csharp
  public static void BindLocalized<TVM>(
      this UIToolkitView<TVM> view, Label label,
      LocalizationKey key, Func<object[]> argsFactory)
      where TVM : ViewModelBase
  {
      if (argsFactory == null) throw new ArgumentNullException(nameof(argsFactory));
      var loc = view.GetService<ILocalizationService>();
      label.text = loc.GetString(key, argsFactory());
      view.BindObservable(loc.OnLocaleChanged, _ => label.text = loc.GetString(key, argsFactory()));
  }
  ```
  Аналогично для `Button`. Работа ортогональна M5 (DI-симметрия) — объединить с ним в один патч.

- **m6. (feature) Хелперы `BindCurrentLocale` / `BindIsRTL`** — `UIToolkit/UIToolkitLocalizationExtensions.cs`
  Для RTL-языков (арабский, иврит) UI должен переключать `FlexDirection` / шрифты / иконки. Parent `ILocalizationService` предоставляет `CurrentLocale` и — при фиксе в parent — `IsRTL`. Хелпер:
  ```csharp
  public static void BindIsRTL<TVM>(
      this UIToolkitView<TVM> view, VisualElement element)
      where TVM : ViewModelBase
  {
      var loc = view.GetService<ILocalizationService>();
      void Apply(bool rtl) =>
          element.style.flexDirection = rtl ? FlexDirection.RowReverse : FlexDirection.Row;
      Apply(loc.IsRTL);
      view.BindObservable(loc.OnLocaleChanged, _ => Apply(loc.IsRTL));
  }
  ```
  Зависит от публичного `IsRTL` в parent (на момент аудита — не проверен в коде parent'а; если отсутствует, feature блокируется до соответствующего расширения parent'а).

- **m7. `LocalizationViewExtensions` и `UIToolkitLocalizationExtensions` не помечены `sealed`** — `Runtime/LocalizationViewExtensions.cs:8`, `UIToolkit/UIToolkitLocalizationExtensions.cs:11`
  Это `static class` — CLR и так запрещает наследование и инстанцирование. `sealed` избыточен, но в репо часть `static class` имеют явный `sealed` (см. `com.rubickanov.utils`), часть — нет. Консистентность — косметическая. **Решение:** привести к одному стилю, сверить с большинством; скорее всего — убрать требование, оставить `static class` без `sealed`.
