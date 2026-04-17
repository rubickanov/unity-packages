# Audit: `com.rubickanov.ui`

Результат детального ревью пакета. Все находки проверены чтением исходников (пути и строки указаны на текущее состояние). Сгруппированы в 6 батчей: Batch 1 — критичные баги лайфсайкла, Batch 2 — null-safety на границах, Batch 3 — контракты API, Batch 4 — документация/метаданные, Batch 5 — отсутствующая инфраструктура, Batch 6 — мелочи и easy wins.

В конце файла — сводка открытых вопросов, по которым нужно решение до фиксов.

---

## Batch 1 — Критичные баги жизненного цикла view

Связаны корректностью Bind / Unbind / Destroy / Show / Hide инвариантов. Вероятность проявления высокая, последствия — утечки подписок и рассинхрон состояния.

### B1.1 `UGUIView.UnbindAll` не реинициализирует `DisposableBag`

**Место:** `UGUI/UGUIView.cs:39-44`

**Проблема:** после `_disposables.Dispose()` поле остаётся в «disposed»-состоянии. На следующем `Bind` (повторный `Show`) `.AddTo(ref _disposables)` либо сразу диспозит элемент, либо нарушает инвариант R3 DisposableBag. Результат: все реактивные привязки тихо не работают после первого цикла Show → Hide → Show. В `UIToolkit/UIToolkitView.cs:42` поле корректно переинициализируется (`_disposables = new DisposableBag();`) — здесь этого нет.

**Решение:** добавить `_disposables = new DisposableBag();` сразу после `_disposables.Dispose();` в `UGUIView.UnbindAll`.

---

### B1.2 `UIToolkitView.CreateChild` не освобождает UXML-хендл

**Место:** `UIToolkit/UIToolkitView.cs:140-157`

**Проблема:** ребёнок создаётся через `ViewFactory.Create<TView>(UILayer.HUD)`, что кладёт хендл UXML в `UIToolkitViewFactory._uxmlHandles`. Затем код вызывает только `RemoveFromHierarchy()` и переподключает корень в `container`. `ViewFactory.Detach(childView)` не вызывается. При `childView.Destroy()` хендл тоже не освобождается — он живёт до уничтожения всей фабрики. Аналог в `UGUI/UGUIView.cs:166` — `ViewFactory.Detach(childView)` — сделан правильно.

**Решение:** добавить `ViewFactory.Detach(childView);` перед `uitkChild.Root.RemoveFromHierarchy();` в ветке `if (childView is UIToolkitViewBase uitkChild)`. Корень остаётся валидным — `CloneTree()` к тому моменту уже выполнен.

---

### B1.3 `UIService.Show` дублирует запись в popup-стеке при повторном показе

**Место:** `Runtime/UIService.cs:54-75`

**Проблема:** на popup-ветке (`else` блок на строке 67) нет проверки, что view уже в `_popupStack`. Повторный `Show<T>` без `Hide<T>` приводит к: (а) второму `OnBind` без предварительного `Unbind` — подписки дублируются, (б) одна и та же view в стеке дважды — `HideTop` снимет только одну запись, в стеке останется «фантом».

**Решение:** перед `await view.Bind(viewModel)` в popup-ветке добавить:

```csharp
if (_popupStack.Contains(view))
{
    view.Hide();               // триггерит OnHide → Unbind
    _popupStack.Remove(view);
}
```

Screen-ветка уже справляется через `_activeScreen?.Hide()`.

**Решение принято:** тихий re-bind (как описано выше).

---

### B1.4 `Destroy` не чистит подписки, если view была Bound, но не Shown

**Место:** `UIToolkit/UIToolkitViewBase.cs:65-69`, `UGUI/UGUIViewBase.cs:73-79`

**Проблема:** `Destroy` вызывает `Hide()`, а `Hide()` делает ранний return по `if (!IsVisible)`. Следовательно `OnHide` → `Unbind` не выполняются. Сценарий: `Register` → `OnInitialize` подписывается на что-то (например, через `TrackUnbind` или `_disposables`); позже `Unregister` до любого `Show` — ожидаемой очистки не происходит. Подписки переживают view.

Дополнительно: даже после `Bind` без `Show`, `ViewModel` и `_disposables` остаются привязанными.

**Решение принято:** завести в `UIToolkitView<T>` и `UGUIView<T>` internal-метод `ForceUnbind()`, делающий то же, что приватный `Unbind()`, и вызывать его из `Destroy` в соответствующем `ViewBase`. `Hide` с ранним выходом не трогаем — контракт `Hide` остаётся прежним. `Destroy` сначала зовёт `ForceUnbind()`, затем `Hide()` (который вернётся рано, если не visible).

---

### B1.5 Исключение в `ShowAsync` оставляет `_activeScreen` рассогласованным

**Место:** `Runtime/UIService.cs:60-72`

**Проблема:** в screen-ветке `_activeScreen?.Hide()` уже отработал, но `view.Bind` / `view.ShowAsync` могут бросить. Присвоение `_activeScreen = view` никогда не произойдёт — `_activeScreen == null`, хотя новый view в частично-показанном состоянии (возможно visible, возможно Bound). Аналогично для popup — `_popupStack.Add(view)` не выполнится.

**Решение:** обернуть `await view.ShowAsync()` в `try/catch`. В `catch`: `view.Hide()`; для popup — убрать из стека, если успел попасть; `_activeScreen = null` для screen; `throw;`. Также: присвоение `_activeScreen` и `_popupStack.Add` делать **до** `ShowAsync`, чтобы стек был согласован при падении (и откатывать в catch).

---

## Batch 2 — Null-safety на границах (фабрики и сервисы)

Связаны невалидацией ресурсов/конфигурации на входе. Правки односложные, но сильно улучшают диагностику при мискофиге UIDocument / префабов.

### B2.1 `UIToolkitViewFactory` не валидирует наличие слоёв

**Место:** `UIToolkit/UIToolkitViewFactory.cs:24-28`

**Проблема:** `root.Q("screen-layer")` (и другие три) вернут `null`, если в UXML таких элементов нет. Первое же `Create` упадёт с безликим `NullReferenceException` в `GetLayerContainer(...).Add(...)`. В `UGUIViewFactory.FindLayer` (`UGUI/UGUIViewFactory.cs:71-79`) уже есть ровно нужная валидация с понятным сообщением — повторить здесь.

**Решение:** после четырёх `Q(...)` проверить каждый на `null` и бросить `InvalidOperationException` с перечислением обязательных имён (`screen-layer`, `hud-layer`, `popup-layer`, `overlay-layer`).

---

### B2.2 `TooltipService` не валидирует overlay-layer

**Место:** `UIToolkit/Tooltip/TooltipService.cs:20, 32`

**Проблема:** `document.rootVisualElement.Q("overlay-layer")` может быть `null`. На строке 32 сразу следует `_overlayLayer.Add(_container)` → NRE из конструктора.

**Решение:** проверить `_overlayLayer == null` и бросить `InvalidOperationException("TooltipService requires 'overlay-layer' element in UIDocument root.")`.

---

### B2.3 `UIToolkitViewFactory` не защищён от невалидной загрузки UXML

**Место:** `UIToolkit/UIToolkitViewFactory.cs:39-41`

**Проблема:** если `_loadUxml` вернёт `(null, null)` (или хендл без asset'а при ошибке Addressables), `asset.CloneTree()` даст NRE без указания, какой UXML не загрузился.

**Решение:** после `var (asset, handle) = await _loadUxml(uitkView.UxmlName);` проверить `asset == null` и бросить с сообщением вида `"Failed to load UXML '{UxmlName}' for view {type.Name}"`.

---

### B2.4 `UGUIViewFactory` не проверяет загруженный префаб

**Место:** `UGUI/UGUIViewFactory.cs:35-36`

**Проблема:** `Instantiate(null, ...)` даст `MissingReferenceException` с неинформативным сообщением. В логе невозможно понять, какой префаб не загрузился.

**Решение:** проверить `prefab == null` перед `Instantiate`, бросить `InvalidOperationException` с именем префаба и типа view.

---

## Batch 3 — API-контракты и согласованность

Публичные API ведут себя неочевидно или непоследовательно. Требуют решения пользователя по формату API.

### B3.1 `IUIService.Get<T>` бросает, а остальные методы используют Try-паттерн

**Место:** `Runtime/UIService.cs:52`, `Runtime/IUIService.cs:9`

**Проблема:** `Get<T>` напрямую индексирует словарь → `KeyNotFoundException` без сообщения о конкретном типе. `Hide<T>` и `HideAsync<T>` используют `TryGetValue` и тихо выходят. Поведение рассинхронизировано.

**Решение принято:** оставить throwing, заменить индексатор на явный `throw new InvalidOperationException($"View {typeof(T).Name} is not registered in UIService")`. `TryGet<T>` сейчас не добавляем — по запросу позже.

---

### B3.2 `IViewServiceResolver.Resolve<T>` возвращает nullable, но потребители кидают

**Место:** `Runtime/IViewServiceResolver.cs:5`, `UIToolkit/UIToolkitView.cs:51-53`, `UGUI/UGUIView.cs:50-52`

**Проблема:** сигнатура `T?`, оба потребителя делают `?? throw InvalidOperationException`. Семантика «не найдено → null» фактически не используется — эффективно это required-контракт. Для пользовательского кода nullable создаёт false-friendly API.

**Решение принято:** добавить на интерфейс второй метод `T Require<T>() where T : class` с default-имплементацией через `Resolve<T>() ?? throw InvalidOperationException(...)`. `GetService<T>` в `UIToolkitView<T>` и `UGUIView<T>` переключить на `Require<T>`. `Resolve<T>` остаётся `T?` — честный опциональный путь для тех, кому он нужен.

---

### B3.3 `NullUIService.Get<T>` возвращает `default!`

**Место:** `Runtime/NullUIService.cs:9`

**Проблема:** null-сервис имеет смысл для методов-мутаторов (no-op), но `Get<T>` возвращающий `null` с `!` ломает контракт «не возвращаем null». Потребитель получит NRE где-то ниже по стеку без полезной диагностики.

**Решение:** бросать `NotSupportedException("UI service is not available (NullUIService).")`.

---

### B3.4 `HideAll` работает только синхронно, без анимации

**Место:** `Runtime/UIService.cs:159-171`

**Проблема:** нет `HideAllAsync`. При выходе из сцены анимации всех открытых popup'ов + активного экрана срезаются. Функционально не баг, но визуально плохо и непоследовательно с наличием `HideAsync`/`HideTopAsync`.

**Решение принято:** добавить `UniTask HideAllAsync(float duration = 0.3f)` в `IUIService` и `UIService`. Реализация — параллельная: собрать список views (все popup'ы + `_activeScreen`, если есть), запустить `UniTask.WhenAll` над их `HideAsync(duration)`, затем очистить стек и `_activeScreen = null`, вызвать visibility-callback.

---

## Batch 4 — Документация и метаданные

Лёгкие правки README и `package.json`.

### B4.1 README ссылается на несуществующие классы

**Место:** `README.md:316, 343-346`

**Проблема:** упоминаются `ConfirmPopup`, `AlertPopup`, `ConfirmViewModel`, `AlertViewModel`. Их в коде нет — функционал заменён на `DynamicPopup` + `DynamicDialogViewModel` + `DialogBuilder` (`UIToolkit/DialogBuilder.cs`, `UIToolkit/DynamicPopup.cs`).

**Решение:** переписать раздел «Design Decisions» (строка ~316) и дерево файлов (см. B6.6) под актуальный API. Добавить пример использования `DialogBuilder.CreateDialog(...)`.

---

### B4.2 README не упоминает `SceneViewScopeService` и `IViewServiceResolver`

**Место:** `README.md` + `Runtime/SceneViewScopeService.cs`, `Runtime/IViewServiceResolver.cs`

**Проблема:** публичный API не документирован. Пользователю неоткуда узнать про scene-scoped регистрацию и сервис-резолв.

**Решение:** добавить две короткие секции с примерами: «Scene-scoped registration» (использование `SceneViewScopeService.Begin()` / `Dispose()`) и «Service resolution» (адаптер `IViewServiceResolver` над DI-контейнером, пример `GetService<T>()` в view).

---

### B4.3 `package.json` не декларирует зависимости

**Место:** `package.json`

**Проблема:** код использует R3 (asmdef reference `GUID:f51ebe6a0ceec4240a699833d6309b23`) и UniTask. В `package.json` нет поля `dependencies`. При установке свежим git-URL подтянутся только исходники — зависимости придётся руками ставить.

**Решение принято:** пройти по `packages/*/package.json` в монорепо, выявить паттерн (git-URL / semver / scoped registry), применить тот же формат. Если паттерна нет — поднять отдельный вопрос перед имплементацией.

---

## Batch 5 — Отсутствующая инфраструктура

Крупные самостоятельные куски, которых просто нет.

### B5.1 Нет `Tests/` папки

**Место:** корень пакета

**Проблема:** 0 тестов на ~1500 строк рантайм-кода. Нетестируемо: стек-логика `UIService` (screen/popup, visibility callback), `ViewModelBase` (disposal, double-dispose), `ScopedViewRegistration`, `SceneViewScopeService.Begin` (замена активного scope). Конвенция монорепо (из `CLAUDE.md`) — у каждого пакета есть `Tests/` c asmdef под `UNITY_INCLUDE_TESTS`.

**Решение:** создать `Tests/` + `UI.Tests.asmdef` (`includePlatforms: ["Editor"]`, `defineConstraints: ["UNITY_INCLUDE_TESTS"]`, references на `UI.Runtime`, `UI.UGUI`, `UI.UIToolkit` + `nunit.framework.dll`). Первоочередное покрытие:

- `UIServiceTests` — c фейковыми `IViewFactory` + `IView` (screen-switch, popup-стек, `Hide` на незарегистрированной, `HideAll`).
- `ViewModelBaseTests` — `CreateProperty/Command/Subject` диспозятся, double-dispose безопасен.
- `ScopedViewRegistrationTests` — порядок очистки, поведение при exception.
- `SceneViewScopeServiceTests` — `Begin` дважды диспозит предыдущий scope.

Backend-специфика (UIToolkit/UGUI) пока отложить — требует PlayMode-инфраструктуры и реального UIDocument/Canvas.

**Решение принято:** отдельной итерацией после фиксов Batch 1–3. Сначала привести рантайм в корректное состояние, потом закрепить тестами.

---

### B5.2 Нет `Editor/` папки

**Место:** корень пакета

**Проблема:** у пакета нет кастомных инспекторов, валидаторов UXML/префабов, отладочных окон. Для backend-agnostic UI-фреймворка это не критично.

**Решение:** оставить как «возможное улучшение» без конкретного плана. Если появится запрос — первые кандидаты: валидатор имён UXML по `UxmlName` / префабов по `PrefabName`, runtime-инспектор стека экранов.

---

### B5.3 Нет `Samples~/` папки

**Место:** корень пакета

**Проблема:** весь learning curve на README. Нет runnable примеров.

**Решение:** опционально — минимальный sample с одним экраном и одним диалогом для каждого бекенда. Низкий приоритет.

---

## Batch 6 — Мелочи и easy wins

Лёгкие локальные правки без архитектурных последствий.

### B6.1 `DynamicDialogViewModel.CompleteWithLast` упадёт на пустом `Buttons`

**Место:** `UIToolkit/DynamicDialogViewModel.cs:29-30`, потребитель — `UIToolkit/DynamicPopup.cs:101`

**Проблема:** `Buttons[^1]` → `ArgumentOutOfRangeException` при пустом списке кнопок. Сценарий реален: info-диалог без кнопок с закрытием по `Esc` (Navigation Cancel).

**Решение принято:** graceful — в `CompleteWithLast`: `if (Buttons.Count == 0) { _completion.TrySetResult(new DialogResult("", null)); return; }`. Поддерживает info-диалоги с ESC-close без кнопок.

---

### B6.2 `ScopedViewRegistration.Dispose` итерируется в прямом порядке

**Место:** `Runtime/ScopedViewRegistration.cs:20-24`

**Проблема:** ошибка в первом action прервёт `foreach`, остальные registrations не очистятся. Для вложенных зависимостей LIFO-порядок безопаснее (как у `using`-стеков). Обычно неважно, но при частичном сбое теряются очистки.

**Решение принято:** итерировать в обратном порядке (LIFO), собирать исключения в `List<Exception>`, в конце — если список непуст, бросить `AggregateException(exceptions)`. Все cleanup'ы выполняются, ни один не скрыт.

---

### B6.3 `DynamicPopup` хардкодит CSS-классы

**Место:** `UIToolkit/DynamicPopup.cs` (8+ `AddToClassList`)

**Проблема:** нет способа переопределить имена классов без наследования. Низкий приоритет: обычно имена классов в проекте — один стандарт.

**Решение принято:** сразу вынести имена в статический `DialogStyle` (публичные строковые поля с дефолтами). `DynamicPopup` читает из него. Пользовательский проект может переопределить поля до создания первого диалога (или, если сделать `DialogStyle` mutable instance-based — через конструктор сервиса). Дефолт — статический класс с публичными `static` полями (`public static string Overlay = "dialog-overlay";` и т.д.), внутри `DynamicPopup` заменить литералы на `DialogStyle.Overlay` и пр.

---

### B6.4 `TooltipManipulator.CancelScheduledShow` полагается на `Pause()` без явной отмены

**Место:** `UIToolkit/Tooltip/TooltipManipulator.cs:99-106`

**Проблема:** `IVisualElementScheduledItem.Pause()` приостанавливает выполнение, но нет гарантии, что Unity-шный планировщик не выполнит задачу в граничном кейсе (если между `StartingIn` и `Pause` прошёл один тик). Вероятность низкая, но защита дешёвая.

**Решение принято:** добавить поле `_cancelled` (bool) — `true` в `CancelScheduledShow`, проверять в начале `ShowTooltip` (ранний return), сбрасывать в `false` в `OnPointerEnter` перед `schedule.Execute`. +2 строки.

---

### B6.5 `UIToolkitAnimationTarget` делает лишние чтения парных осей в сеттерах

**Место:** `UIToolkit/UIToolkitAnimationTarget.cs:20-42`

**Проблема:** сеттер `TranslateX` читает `TranslateY` (и аналогично `ScaleX` → `ScaleY`) через доступ к `_element.style` каждый раз. Для анимированной каждый кадр view — ненужные lookup'ы на hot path.

**Решение:** хранить локальные `Vector3 _translate` и `Vector3 _scale` (инициализировать в конструкторе), писать в `style` сразу скомпонованным значением. Значения восстанавливать при `ResetAnimationState` вместе со стилем.

---

### B6.6 README-дерево файлов расходится с реальностью

**Место:** `README.md:330-356`

**Проблема:** перечислены `ConfirmPopup.cs`, `AlertPopup.cs`, `ConfirmViewModel.cs`, `AlertViewModel.cs` — их нет. Не упомянуты `DialogBuilder.cs`, `DialogResult.cs`, `DynamicPopup.cs`, `DynamicDialogViewModel.cs`, `SceneViewScopeService.cs`, `IViewServiceResolver.cs`, `NullDialogService.cs`, `NoneAnimation.cs`, вся `UIToolkit/Tooltip/` папка.

**Решение:** синхронизировать с реальным содержимым `Runtime/`, `UGUI/`, `UIToolkit/`, `UIToolkit/Tooltip/` (см. `ls` на текущий день).

---

## Принятые решения

Все решения зафиксированы в блоках **Решение принято** выше. Сводка:

| # | Где | Решение |
|---|-----|---------|
| 1 | B1.3 | Тихий re-bind при повторном `Show<T>` popup (с предварительным `Hide` + `Remove`). |
| 2 | B1.4 | Internal `ForceUnbind()` в `UIToolkitView<T>` / `UGUIView<T>`, `Destroy` зовёт его перед `Hide`. |
| 3 | B3.1 | `Get<T>` остаётся throwing, но с явным `InvalidOperationException` и именем типа. `TryGet<T>` не добавляем. |
| 4 | B3.2 | Добавить `T Require<T>()` на `IViewServiceResolver` (default через `Resolve` + throw), потребители переключить на него. `Resolve<T>` остаётся `T?`. |
| 5 | B3.4 | `HideAllAsync` параллельно через `UniTask.WhenAll`. |
| 6 | B4.3 | Сверить формат `dependencies` с другими пакетами монорепо и следовать паттерну. Если единого нет — отдельный вопрос перед правкой. |
| 7 | B5.1 | `Tests/` — отдельной итерацией после фиксов Batch 1–3. |
| 8 | B6.1 | Graceful в `CompleteWithLast` (пустой `DialogResult` при пустом `Buttons`). |
| 9 | B6.2 | LIFO-порядок + `AggregateException` в конце. |
| 10 | B6.3 | Сразу вынести CSS-классы в статический `DialogStyle`. |
| 11 | B6.4 | Добавить флаг `_cancelled` в `TooltipManipulator`. |

---

## Порядок имплементации

Отдельная итерация с патчами по батчам в таком порядке:

1. **Batch 1** — критичные баги лайфсайкла (B1.1 → B1.5).
2. **Batch 2** — null-safety на границах (B2.1 → B2.4).
3. **Batch 3** — контракты API (B3.1 → B3.4).
4. **Batch 6** — мелочи (B6.1 → B6.5, B6.6 — вместе с B4.* как часть README).
5. **Batch 4** — документация + `package.json` (B4.1 → B4.3).
6. **Batch 5** — `Tests/` (B5.1). `Editor/` и `Samples~/` оставлены как nice-to-have без немедленного плана.
