# com.rubickanov.gameplaytags — список проблем

Аудит от 2026-04-17. Ниже — все найденные проблемы с привязкой к коду и планом правок. Сгруппированы в конце по принципу «одно касание кода — один коммит».

Общий вердикт: Runtime-ядро аккуратное, тесты Runtime/Unity хорошие. Есть один design-gap (иммутабельный реестр после Install), пара системных багов (aliasing в struct-обёртках, отсутствие валидации путей), и дыры в редакторе (perf в OnInspectorGUI, дропдаун берёт только первый ассет, undo при extract сломан, нет тестов на Editor).

---

## Design

### D1. Реестр неизменяем после `Install` — нет API добавить теги в runtime

**Где:** `Runtime/GameplayTagRegistry.cs` (весь), `Unity/GameplayTagAsset.cs:18`

Конструктор принимает `IReadOnlyList<string>` один раз, формирует immutable `_names/_parents/_depths` массивы. `Install` бросает если уже установлен. `Uninstall` просто гасит ссылку — закэшированные `GameplayTag` структуры остаются с устаревшими индексами, детектить нельзя.

Следствия:
- DLC / моды / отложенная загрузка контента — невозможны без полного рестарта.
- Несколько `GameplayTagAsset` в runtime: user должен сам собрать все пути до `Install`. README говорит «generator merges all assets», но в runtime мёржа нет.
- Hot-reload в Play Mode не работает.
- Editor-код сам обходит это (`GameplayTagDropdown.cs:33`, `GameplayTagAssetEditor.cs:166`) — создаёт non-installed реестры для своих нужд.
- Uninstall+Install с другим набором путей молча ломает кэшированные значения (включая `static readonly` поля в сгенерированном `GameTags`).

**Fix (выбрано):** мягкий additive API.
- `_names/_parents/_depths` → `List<string>`/`List<int>` с grow-on-demand.
- `_allTags`/`_allNames` — тоже `List<T>`.
- Существующий конструктор остаётся как bulk-init.
- Добавить `void AddTags(IReadOnlyList<string> paths)` — валидирует (см. B2), скипает уже существующие (с возвратом через `out List<string> added`? или без диагностики — см. вопрос).
- `Install` без параметров → `Install(GameplayTagRegistry)` как сейчас; тот же singleton, можно звать `GameplayTagRegistry.Instance.AddTags(...)` после установки.
- Индексы остаются стабильны (новые идут в конец).
- `Uninstall` — оставить, но пометить XMLDoc как «для тестов, не использовать в production» (ломает кэшированные теги).

---

## Critical

### C1. Struct-обёртки с mutable cache → aliasing

**Где:** `Unity/SerializedGameplayTag.cs:11`, `Unity/SerializedGameplayTagContainer.cs:12`

Оба — `struct`. `SerializedGameplayTagContainer` держит `GameplayTagContainer? _cachedContainer` — **ссылочный** тип. При копировании struct обе копии ссылаются на один и тот же контейнер. Мутации через `.Container.AddTag(...)` у получателя копии не отражаются в `_paths` владельца и ломают инвариант «Paths == source of truth». Кроме того, `Container` getter выдаёт mutable-контейнер — user ожидает, что `.Container.AddTag` что-то сделает с serialized state. Не делает.

**Fix (выбрано):** вариант B — struct остаются, но `.Container` возвращает **read-only view**.
- Новый тип `ReadOnlyGameplayTagContainer` (struct-обёртка вокруг `GameplayTagContainer`, только запросы: `Count`, `HasTag`, `HasTagExact`, `HasAll`, `HasAny`, `HasAllExact`, `HasAnyExact`, `IEnumerable<GameplayTag>`).
- `SerializedGameplayTagContainer.Container` возвращает `ReadOnlyGameplayTagContainer`.
- Add/Remove делается на mutable `GameplayTagContainer` у владельца MonoBehaviour.

---

### C2. `GameplayTagRegistry` не валидирует формат путей

**Где:** `Runtime/GameplayTagRegistry.cs:50-116`

Любая строка принимается. Тихо создаёт «теги» для `"A..B"`, `".A"`, `"A."`, `"A.B .C"`, и т.п. Валидирующий регекс есть только в `Editor/GameplayTagAssetEditor.cs:15` — программное создание (тесты, серверный билд, миграция) обходит.

**Fix (выбрано):** перенести регекс в Runtime (`GameplayTagRegistry.TagPathRegex`, `public static readonly`, `RegexOptions.Compiled`). В конструкторе и `AddTags` (см. D1) — бросать `ArgumentException` на невалидных путях. Whitespace-скип (текущее поведение) сохраняем для null/empty/whitespace-only; невалидные-но-непустые — throw.

---

### C3. `GameplayTagContainer.Enumerator` — нет version-check

**Где:** `Runtime/GameplayTagContainer.cs:177-204`

Держит `List<int>` по ссылке. Мутация контейнера во время `foreach` (add/remove) молча пропускает элементы или лезет за границы. `List<T>.Enumerator` такое бросает — мы эту защиту теряем.

**Fix:** `_version`-счётчик в контейнере, инкремент в `AddTag`/`RemoveTag`/`Clear`, проверка в `Enumerator.MoveNext` и `Current` — `InvalidOperationException` при расхождении.

---

## Major

### M1. `GameplayTagDropdown` — три проблемы разом

**Где:** `Editor/GameplayTagDropdown.cs:29-33, 62-85, 102-110`

1. **O(n²)** при определении non-leaf-узлов: для каждого `kvp in nodeMap` полный перебор `names` с `StartsWith`. Fix: заводить `HashSet<string> nonLeafKeys` и заполнять его во время первого прохода (когда добавляется второй уровень вложенности у `parentItem`).
2. **Берёт только первый ассет** (`FindTagAsset` возвращает первый из `FindAssets("t:GameplayTagAsset")`). README обещает «all assets merged» — но только в генераторе, не в дропдауне. Fix: мёржить пути всех ассетов.
3. **Тихий fallback при отсутствии ассета**: возвращается root с одним «None». Пользователь видит пустую выпадашку. Fix: добавить disabled-item «No GameplayTagAsset found. Create via Assets > Create > Config > Gameplay Tags».

Плюс: реестр строится заново на каждое открытие (из путей ассета). Можно кэшировать с инвалидацией по хэшу путей — но это минорно, перенесено в «не делаем сейчас».

---

### M2. `GameplayTagAssetEditor` — аллокации на каждый `OnInspectorGUI` + сломанный undo в `ExtractTags`

**Где:** `Editor/GameplayTagAssetEditor.cs`

1. **Строка 28**: `asset.TagPaths.ToList()` на каждый кадр инспектора — новый `List<string>`. Fix: кэшировать рядом с `_cachedRegistry`/`_cachedNames`, инвалидировать там же.
2. **Строка 77**: `name.Split('.').Length - 1` на каждый отрисованный тег. Fix: статический helper `CountDots(string)` через `IndexOf`-loop.
3. **`ExtractTags` (122-153)**: `Undo.RecordObject(source, ...)` стоит **после** `AssetDatabase.CreateAsset`, поэтому при Undo новый asset-файл остаётся осиротевшим на диске. Fix: `Undo.RegisterCreatedObjectUndo(newAsset, "Extract Gameplay Tags")` после `CreateAsset`, `RecordObject(source, ...)` до `SetTagPaths`.

---

### M3. `GameplayTagsPostprocessor` — не коалесцирует regenerate и криво детектит удалённые ассеты

**Где:** `Editor/GameplayTagsPostprocessor.cs:42-56`

1. При импорте нескольких ассетов `delayCall += GenerateTags` добавляется N раз → N регенераций. Fix: статический флаг `_pendingRegeneration`, ставим `delayCall` только если не стоит, сбрасываем в callback.
2. Для удалённых ассетов тип уже недоступен, поэтому код использует `path.Contains("GameplayTag")` — ложно сработает на `MyGameplayTagHelper.asset` и т.п. Fix: ограничиться `EndsWith(".asset")` и триггерить регенерацию по любому удалённому `.asset` (генератор дёшев); либо кэшировать известные GUID-ы в static (сложнее).

---

### M4. Нет тестов на Editor-слой

**Где:** `Tests/` — покрытие: Runtime + Unity (wrappers, ScriptableObject) хорошее, Editor — 0 тестов.

`GameplayTagsGenerator.GenerateCode` — идеальный кандидат на unit-тесты (pure function: список путей → C#-строка). Сейчас метод `private` и завязан на `Settings` через DI-подобный pattern, но легко извлекается.

**Fix:** отрефакторить `GameplayTagsGenerator` так, чтобы `GenerateCode(IReadOnlyList<string> names, string @namespace, string className)` был `public static`, без зависимости от `Settings`/`AssetDatabase`. Тесты: snapshot-like — input фиксированный набор путей, output сравниваем с expected C#-строкой.

---

### M5. Тесты `SerializedGameplayTagContainer` сидят на reflection

**Где:** `Tests/SerializedGameplayTagContainerTests.cs:21-29, 102-106`

Reflection для установки `_paths` — хрупко к рефакторингу. Причина: нет способа задать `_paths` из кода (только Unity-сериализацией).

**Fix:** `internal SerializedGameplayTagContainer(string[] paths)` + `[InternalsVisibleTo("GameplayTags.Tests")]` в `Unity.asmdef`. Reflection в тестах уходит.

---

## Minor

### m1. Конструктор реестра аллоцирует через `Split`/`Join`

**Где:** `Runtime/GameplayTagRegistry.cs:63-68, 93`

`Split('.')` + `string.Join(".", parts, 0, i)` на каждый входной путь + `Split` ради `Depth`. Для 10к тегов — десятки тысяч временных аллокаций при инициализации. Один раз на startup, не критично, но можно сделать через ручной `IndexOf('.')`-loop.

### m2. `GameplayTagsGenerator.IsCSharpKeyword` — `new HashSet` на каждый вызов

**Где:** `Editor/GameplayTagsGenerator.cs:165-183`

Хэш-сет инициализируется внутри метода, метод вызывается по разу на каждый сегмент каждого тега. Fix: `private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal) { ... };` вне метода. Бонус: не звать `ToLowerInvariant` — ключевые слова в lowercase, входной идентификатор — PascalCase после `ToPascalCase`, совпадения не будет; но на всякий случай — компаратор и отдельная логика.

### m3. `GetAllTags`/`GetAllNames` возвращают внутренние `List<T>`

**Где:** `Runtime/GameplayTagRegistry.cs:189-192`

`IReadOnlyList<T>` корректен, но consumer может привести к `List<T>`. Fix: обернуть в `ReadOnlyCollection<T>` (один раз в конструкторе). При переходе на mutable registry (D1) — пересмотреть: либо возвращать snapshot-копию, либо `ReadOnlyCollection<T>` поверх mutable list.

### m4. `GameplayTagContainerPropertyDrawer` — magic number `15f`

**Где:** `Editor/GameplayTagContainerPropertyDrawer.cs:47, 49, 74, 76`

`EditorGUI.indentLevel * 15f` захардкожен 4 раза. Fix: `private const float IndentWidth = 15f;`.

### m5. `GameplayTag.Matches` бросает если реестр не установлен, а `ToString` — нет

**Где:** `Runtime/GameplayTag.cs:32`

Асимметрия. `None.Matches(x)` возвращает false без обращения к реестру, а `validTag.Matches(x)` без реестра бросит `InvalidOperationException` через `GameplayTagRegistry.Instance`. `ToString` в том же файле предусматривает fallback.

**Fix (выбрано):** оставить throw, задокументировать XMLDoc явно — «requires installed registry». Возврат false тихо маскировал бы баг (забыли install — все queries бессмысленно false).

### m6. Генератор — нет опций (internal/partial/file-scoped)

**Где:** `Editor/GameplayTagsGenerator.cs:56-66`

Всегда `public static class`, block-scoped namespace. Fix: добавить в `GameplayTagsGeneratorSettings`:
- `AccessModifier` enum (Public / Internal) — дефолт Public.
- `bool MakePartial` — дефолт false.

File-scoped namespace — пропускаем (стилистика, не функциональность).

### m7. `GameplayTagRegistry.Install`/`Uninstall` не thread-safe

**Где:** `Runtime/GameplayTagRegistry.cs:22-35`

Единственный теоретический вектор — серверный билд с async startup. В Unity main-thread не страдает. **Fix:** задокументировать «main thread only» в XMLDoc `Install`/`Uninstall`. Lock не добавляем (лишний оверхед без повода).

---

## Что сознательно НЕ трогаем сейчас

- **`GameplayTagDropdown` кэш реестра** — перестраивает реестр на каждое открытие. Для типовых объёмов (<1000 тегов) — незаметно. Если станет тормозить — вернуться к этому.
- **Struct-based test isolation через DI** (`IGameplayTagRegistryProvider`) — массивный рефакторинг ради возможности параллельных тестов. Не окупается.
- **`GameplayTag.None` в HashSet/Dictionary** — не баг, поведение корректное; документация избыточна.
- **`GameplayTagContainer` union/intersect операторы** — нет потребности в кодовой базе.
- **`Get` с suggestions** («did you mean...?») — polish без цены.

---

## План работ (группировка — один шаг, один коммит)

### Шаг 1 — Валидация путей (C2 + m1)
Оба про `GameplayTagRegistry` конструктор.
- Вынести `TagPathRegex` в Runtime (`public static readonly`, `Compiled`). Использовать в `GameplayTagAssetEditor` через ссылку на Runtime (убрать дубль).
- Валидация в конструкторе: null/empty/whitespace — skip (сохраняем поведение), невалидные — `ArgumentException` с перечислением.
- Переписать prefix-генерацию без `Split`/`Join`: ручной проход через `IndexOf('.')`. Depth считать через `CountDots`.
- Тесты: `Constructor_InvalidPath_Throws` на `"A..B"`, `".A"`, `"A."`, `"A B"`, `"1A"`, `"A-B"`, пустой сегмент. Существующий тест на whitespace/null не ломается.

### Шаг 2 — Additive API реестра (D1)
- Заменить массивы в `GameplayTagRegistry` на `List<T>` с grow-on-demand.
- Добавить `AddTags(IReadOnlyList<string> paths)` с валидацией из Шага 1. Новые пути идут в конец, существующие скипаются.
- Конструктор становится тонкой обёрткой: пустой state + `AddTags(paths)`.
- Пометить `Uninstall` в XMLDoc как «test-only; invalidates cached tag indices».
- Тесты:
  - `AddTags_NewPaths_GetsAppendedWithNewIndices`.
  - `AddTags_ExistingPath_IsNoOp` (индекс не меняется, counts не дублируются).
  - `AddTags_CreatesMissingParents` (как и конструктор).
  - `AddTags_InvalidPath_Throws`.
  - `AddTags_AfterInstall_VisibleThroughInstance`.
- Обновить/удалить редакторские ad-hoc `new GameplayTagRegistry(...)` в `GameplayTagDropdown` и `GameplayTagAssetEditor` — либо оставить как есть (non-installed для UI), либо перевести на cache + mutable state (см. Шаг 4).

### Шаг 3 — Контейнер: enumerator version-check (C3 + m3)
- `_version` в `GameplayTagContainer`, инкремент в `AddTag`/`RemoveTag`/`Clear`, проверка в `Enumerator.MoveNext`/`Current`.
- `GetAllTags`/`GetAllNames` → `ReadOnlyCollection<T>` (создать один раз в конструкторе регистри; либо при первом обращении, но mutable-регистри из Шага 2 усложняет — нужно инвалидировать при `AddTags`; выбираем второе, ленивая инвалидация).
- Тесты:
  - `Enumerator_MutationDuringIteration_Throws` (Add и Remove в середине foreach).
  - `GetAllTags_MutatedAfterCall_ReflectsNewTags` (после AddTags; старая ссылка тоже видит или снапшот? — выбираем: `GetAllTags` всегда даёт снимок-на-момент-вызова через ReadOnlyCollection поверх текущего state, новые вызовы видят новое).

### Шаг 4 — Serialized wrappers + read-only container (C1 + M5)
Это объёмный шаг, но всё в одной оси «Unity-слой».
- Новый `Runtime/ReadOnlyGameplayTagContainer.cs` — struct-обёртка вокруг `GameplayTagContainer` с только-запросами.
- `SerializedGameplayTagContainer.Container` теперь возвращает `ReadOnlyGameplayTagContainer`.
- `internal SerializedGameplayTagContainer(string[] paths)` + `[InternalsVisibleTo("GameplayTags.Tests")]` в `Unity.asmdef`.
- В тестах (`SerializedGameplayTagContainerTests`) — убрать reflection, использовать новый ctor.
- Существующие call-site в проекте (если есть): `.Container.HasTag(...)` → работает, `.Container.AddTag(...)` → compile error — это фича, заменяется на изменение MonoBehaviour-поля через Editor или API.
- Тесты:
  - Существующие продолжают проходить (через новый ctor).
  - Новый: `SerializedGameplayTagContainer_CopyAndRead_DoesNotAliasCachedState` — показать, что мутация контейнера через API невозможна (compile-time check).
  - `ReadOnlyGameplayTagContainer_HasTag_DelegatesToWrapped` и прочие трансляции.

### Шаг 5 — Editor: dropdown (M1 все пункты)
- `HashSet<string> nonLeafKeys` — заполняем на первом проходе, второй проход O(n).
- `FindTagAssets()` (множественное число) — мержим пути всех ассетов (`SelectMany` — это Editor-код, LINQ допустим).
- Fallback item при отсутствии ассетов.

### Шаг 6 — Editor: asset inspector + postprocessor (M2 + M3 + m4)
Всё в Editor/, но три разных файла.
- `GameplayTagAssetEditor`: кэш списка путей; helper `CountDots`; фикс undo в `ExtractTags` (порядок `CreateAsset` → `RegisterCreatedObjectUndo` → `RecordObject(source)` → `SetTagPaths`).
- `GameplayTagsPostprocessor`: `_pendingRegeneration`-флаг для коалесцирования; детект удалённых → `EndsWith(".asset")` без substring-хака.
- `GameplayTagContainerPropertyDrawer`: `const IndentWidth = 15f`.

### Шаг 7 — Генератор + тесты на генератор (M4 + m2 + m6)
- `GameplayTagsGenerator.CSharpKeywords` → `static readonly HashSet<string>`.
- Извлечь `public static string GenerateCode(IReadOnlyList<string> names, string @namespace, string className, AccessModifier access, bool makePartial)` — pure-функция, не зависит от Settings/AssetDatabase.
- `[MenuItem]`-entry и Settings-provider продолжают дёргать pure-функцию с аргументами из Settings.
- `AccessModifier` enum + новое поле `_makePartial` в `GameplayTagsGeneratorSettings`.
- Тесты (новый файл `Tests/GameplayTagsGeneratorTests.cs`):
  - `GenerateCode_LeafOnly_ProducesFlatConstants`.
  - `GenerateCode_NestedHierarchy_ProducesNestedClasses`.
  - `GenerateCode_InternalAccess_EmitsInternalModifier`.
  - `GenerateCode_KeywordSegment_PrefixesAtSign` (e.g. `class` → `@class`).
  - `GenerateCode_DigitStartingSegment_PrefixesUnderscore`.
- Тест-asmdef уже `UNITY_INCLUDE_TESTS`-гейтед; обновить references чтобы видел `GameplayTags.Editor`.

### Шаг 8 — Документация (m5 + m7 + README)
- XMLDoc на `GameplayTag.Matches` — «requires installed registry, throws InvalidOperationException if not installed».
- XMLDoc на `Install`/`Uninstall` — «main thread only».
- XMLDoc на `Uninstall` — «test-only; invalidates cached tag indices».
- README: дописать секцию «Adding Tags at Runtime» (пример `AddTags`), сакцентировать что runtime-мёрж нескольких ассетов теперь поддерживается напрямую.

---

## Verification

- После каждого шага — прогон `GameplayTags.Tests` в Unity Test Runner (EditMode, т.к. `defineConstraints: UNITY_INCLUDE_TESTS` + `includePlatforms: [Editor]` уже настроены).
- После Шагов 4/5/6 — ручной прогон:
  - Создать `GameplayTagAsset`, добавить теги в инспекторе, попробовать Extract, нажать Undo — ассет и source должны откатиться согласованно.
  - Выбрать `SerializedGameplayTagContainer` в инспекторе, открыть дропдаун при нескольких ассетах → все теги видны.
  - Удалить ассет, проверить что регенерация отработала один раз.
- После Шага 7 — запустить генератор вручную, убедиться что `internal`/`partial` опции эмитятся корректно.
- Финально — `unity-project-pckgs` должен компилироваться (package подключён локально, testables включает его).
