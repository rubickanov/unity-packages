# Dev Console Package — Issues & Work Plan

Результаты аудита пакета `com.rubickanov.devconsole`. Документ отслеживает все найденные проблемы и порядок их исправления. Ломающие изменения публичного API допустимы — делаем правильно.

**Зафиксированные решения по ключевым неопределённостям:**
- **D1 (расположение интеграции с config):** новый отдельный пакет `com.rubickanov.devconsole.config` (зеркало `.netcode`). Никакой связи в core.
- **D2 (API кастомных парсеров аргументов — M2):** делегат `Func<string, (bool ok, T? value)>` через `RegisterParser<T>(...)`. Минимум кода, lambda-friendly, симметрично с runtime `Register(name, handler)`.
- **D3 (объём тестов — M6):** покрываем всё в этом же раунде — старый код (`Tokenize`, `TryParseArg`, `Execute`, alias, `GetSuggestions`, `RegisterGroup`) + новый API (`RegisterParser`, `RegisterDefaultProvider`, `RegisterTarget`, типизированные overload'ы builder'а) + integration-тесты `devconsole.config`.
- **D4 (логгер / владение `CommandRegistry`):** оставляем singleton + `Debug.Log` напрямую. Прецедент `com.rubickanov.audio` существует; это дев-инструмент. Breaking-change на `ILoggerFactory`/DI откладывается.
- **D5 (config-loading):** не трогаем `SceneTransitionFactory.LoadConfigsOperation` в juice-project — аудит и работа касаются только devconsole-стороны.
- **D6 (форма API регистрации):** **оба паттерна параллельно:**
  - **Builder** — `RegisterGroup` (+ shorthand `Group`) с **типизированными overload'ами** (`g.Add<FruitConfig, int>("add", (f, n) => …)`) для one-off и условной регистрации.
  - **Атрибуты** — `[ConsoleCommand]` на instance-методах + `RegisterTarget(this)` для больших command-классов вида `IStartable` сервисов.

**Целевой UX (после фиксов):**
```csharp
// bootstrap (один раз)
CommandRegistry.Instance.RegisterConfigDatabases(_fruitsDb, _seasonDb);

// команда — атрибут на instance-методе
public class InventoryCommands : IStartable
{
    public void Start() => CommandRegistry.Instance.RegisterTarget(this);

    [ConsoleCommand("inv.add", "Add fruit", "Cheats")]
    public string Add(FruitConfig fruit, int amount = 1)
        => $"Added {amount}x {fruit.Id}";
}

// или через builder с типизированным handler
CommandRegistry.Instance.Group("inv", "Inventory", "Cheats", g =>
{
    g.Add<FruitConfig, int>("add", (f, n) => _inv.Add(f, n), "Add");
    g.Add("clear", () => _inv.Clear(), "Clear");
});
```
В обоих случаях `FruitConfig` резолвится автоматом, автокомплит на `<Tab>` подтягивается из `ConfigDatabase`, ручной `_db.Get(args[0]); if (null) return …` уходит из всех handler'ов.

---

## Находки

### Критические (реальные баги)

Нет. Падений в типичных сценариях не нашёл — пакет стабильно работает на `juice-project`. Все находки ниже либо API-зазоры (закрытая для расширения архитектура), либо неполная валидация на публичных границах, либо нарушения политик репо (LINQ, отсутствие тестов, незаявленные зависимости).

### Мажорные (M)

- **M1. `package.json` не объявляет зависимости** — `package.json:1-9`
  asmdef ссылается на `Unity.InputSystem` (`Runtime/DevConsole.Runtime.asmdef:4`), README заявляет `com.unity.inputsystem` (`README.md:7`), но в `package.json` блока `dependencies` нет. UPM не подтянет InputSystem в свежий проект — потребитель получит ошибки компиляции. Сравни с `com.rubickanov.devconsole.netcode/package.json`, где зависимости объявлены явно.
  **Решение:**
  ```json
  "dependencies": {
      "com.unity.inputsystem": "1.7.0"
  }
  ```
  Точную версию подобрать по `unity-project-pckgs/Packages/manifest.json`.

- **M2. Парсер аргументов закрыт для расширения** — `Runtime/Core/CommandRegistry.cs:313-379`
  `TryParseArg` — `private static`, hardcoded switch (`string/int/float/ulong/long/bool/enum/Vector3`). Нет API типа `RegisterParser<T>(...)`. Любая команда с custom-типом (config-ассет, GUID, доменный value-object) вынуждена принимать `string` и парсить вручную — ломает идею auto-discovery. Это же блокирует интеграцию с `com.rubickanov.config` (M11).
  **Решение:**
  1. Сделать `TryParseArg` методом инстанса (не static).
  2. Добавить:
     ```csharp
     public delegate bool ArgumentParserDelegate(string input, out object? result);

     private readonly Dictionary<Type, ArgumentParserDelegate> _customParsers = new();

     public CommandRegistry RegisterParser<T>(Func<string, (bool ok, T? value)> parser)
     {
         _customParsers[typeof(T)] = (string input, out object? result) =>
         {
             var (ok, val) = parser(input);
             result = val;
             return ok;
         };
         return this;
     }
     ```
  3. В `TryParseArg` сначала проверять `_customParsers.TryGetValue(targetType, out var p)` → fallback на встроенный switch.
  4. Существующая публичная поверхность не меняется — полностью additive.

- **M3. `[ConsoleCommand]` поддерживает только статические методы** — `Runtime/Core/CommandRegistry.cs:141-145`
  `BindingFlags.Static | Public | NonPublic` — instance-методы игнорируются discovery. Команды на `MonoBehaviour`/сервисе нельзя зарегистрировать через атрибут — только через ручной `Register(name, handler, …)`. Жёсткое ограничение, в README упомянуто мимоходом (`README.md:54`). Блокирует целевой UX «команды как методы IStartable-сервиса».
  **Решение:**
  Добавить новый API:
  ```csharp
  public CommandRegistry RegisterTarget(object target);
  public CommandRegistry UnregisterTarget(object target);
  ```
  - Сканирует `target.GetType()` на `[ConsoleCommand]` (instance), хранит пару `(MethodInfo, target)` в новом поле `RegisteredCommand.Target`.
  - В `ExecuteReflection` (`:300`): `cmd.Method.Invoke(cmd.Target, parsedArgs)` (для `Target == null` — текущее поведение со статикой).
  - `UnregisterTarget` нужен для команд на MonoBehaviour, чтобы при destroy убирать.
  - Discovery в `Initialize()` остаётся прежним (только static), instance-команды регистрируются явно через `RegisterTarget` — без магии глобального сканирования экземпляров.

- **M4. Молчаливое проглатывание `ReflectionTypeLoadException`** — `Runtime/Core/CommandRegistry.cs:148-150`
  ```csharp
  catch (ReflectionTypeLoadException) { }
  ```
  Любая сборка с битыми типами (типичная ситуация при экспериментах с генерацией кода / удалении пакетов) исчезает из discovery без следа. Не баг, но скрывает проблемы в дев-окружении.
  **Решение:**
  ```csharp
  catch (ReflectionTypeLoadException e)
  {
      Debug.LogWarning($"[DevConsole] Skipped assembly '{asmName}': {e.LoaderExceptions[0]?.Message}");
  }
  ```

- **M5. LINQ в горячем пути `SceneCommands`** — `Runtime/Commands/SceneCommands.cs:2,88`
  `using System.Linq;` + `.FirstOrDefault(t => …)` внутри handler'а команды `inspect` (см. вызов в `Commands/SceneCommands.cs:88`). По CLAUDE.md LINQ в Runtime запрещён вне cold-path. Команды могут вызываться часто (через alias/exec), и аллокация замыкания + boxed enumerator на каждый вызов — нарушение политики.
  **Решение:** заменить на простой `foreach` с ранним `break`. Удалить `using System.Linq;`.
  **НЮАНС:** `Runtime/Core/CommandRegistry.cs:4,157` тоже использует LINQ, но `:157` (`GetCustomAttributes<T>().ToArray()` в discovery) — cold path, ок. `:540-543` (`GroupBy/OrderBy` в команде `help`) — ручной билд help-вывода, тоже cold (вызывается по запросу пользователя). Оставить, но в комментарии issues или в README зафиксировать как осознанное исключение.

- **M6. Нет тестов** — пакет полностью без `Tests/`
  По CLAUDE.md и аудит-стандарту — это находка. По D3 покрываем в этом раунде. Самые ценные fixtures:
  1. **TokenizeTests** — кавычки, пустая строка, пробелы в кавычках, незакрытая кавычка, multiple spaces.
  2. **TryParseArgTests** — все встроенные типы + новый `RegisterParser` (M2): успех, неудача, fallback на builtin, инвариантная культура для float/Vector3.
  3. **ExecuteTests** — alias-рекурсия лимит 8, unknown command, missing required arg, optional arg defaults, `PreExecuteFilter` override.
  4. **RegisterGroupTests** — пустые args (выводит usage), неизвестный subcommand, передача args после subcommand, **новые типизированные overload'ы builder'а** (M12).
  5. **GetSuggestionsTests** — пустой input, префиксы команд, subcommand-aware, trim до `maxResults`, custom default-провайдер по типу (M10).
  6. **AliasRegistryTests** — `TryResolve`, лимит рекурсии, persistence через PlayerPrefs (с `PlayerPrefs.DeleteAll()` в `[TearDown]`).
  7. **RegisterTargetTests** (M3) — instance-методы регистрируются с правильным `Target`, `UnregisterTarget` снимает, instance-метод корректно вызывается через `cmd.Target`.

  **Решение:**
  1. Создать `Tests/DevConsole.Tests.asmdef` с `defineConstraints: ["UNITY_INCLUDE_TESTS"]`, `includePlatforms: ["Editor"]`, references на `DevConsole.Runtime`, `nunit.framework`, `UnityEditor.TestRunner`, `UnityEngine.TestRunner`.
  2. AAA-структура без phase-комментариев. Имена `Method_Scenario_ExpectedBehavior`. Per-test fixtures для PlayerPrefs-тестов.
  3. **Не подавлять** логи через `LogAssert.ignoreFailingMessages` — фиксить источник шума.

- **M7. Persistence без лимитов и без API очистки** — `Runtime/Core/AliasRegistry.cs`, `Runtime/Core/CommandHistory.cs`, `Runtime/Core/CommandBindings.cs`
  PlayerPrefs ключи (`DevConsole.Aliases`, `DevConsole.History`, `DevConsole.Bindings`) глобальны для всех проектов с одним `companyName/productName`. Нет API очистки, `CommandHistory` не лимитируется при сохранении (растёт неограниченно).
  **Решение:**
  1. В `CommandHistory.Save()` сохранять только последние N (по умолчанию 100) — вынести в `DevConsoleSettings.HistoryPersistLimit`.
  2. Добавить команды `alias_clear`, `history_clear`, `binding_clear` (или одну общую `devconsole_reset`).

- **M8. `CommandRegistry.Register(...)` не валидирует входы** — `Runtime/Core/CommandRegistry.cs:44-58, 61-69, 72-92`
  Нет проверок на `null`/empty `name`, `null` handler. Регистрация с пустым именем создаст команду с ключом `""`, которую невозможно вызвать. Регистрация с null handler упадёт уже в `Execute` без понятного контекста.
  **Решение:**
  ```csharp
  if (string.IsNullOrWhiteSpace(name))
      throw new ArgumentException("Command name must be non-empty.", nameof(name));
  if (handler == null)
      throw new ArgumentNullException(nameof(handler));
  ```
  Опционально: `Debug.LogWarning` при перезаписи существующей команды через runtime `Register` (сейчас warning есть только в `RegisterMethod:172-173`).

- **M9. `Vector3`-парсинг ломается на пробелах вокруг запятых** — `Runtime/Core/CommandRegistry.cs:362-371`
  `input.Split(',')` без `.Trim()`. Ввод `1, 2, 3` (естественный для пользователя) попадает в `float.Parse(" 2", InvariantCulture)`. Поведение зависит от `NumberStyles` — в худшем случае ошибка не очевидна (получит «Cannot parse '1, 2, 3' as Vector3»).
  **Решение:** перед `float.Parse` явно делать `p[i].Trim()`. Альтернативно — после M2 удалить hardcoded Vector3 и зарегистрировать через `RegisterParser<Vector3>` встроенно при `Initialize()`.

- **M10. Нужен «провайдер автокомплита по умолчанию для типа»** — `Runtime/Core/CommandRegistry.cs:154-184`
  Сейчас в `RegisterMethod` ровно две хардкод-привязки автокомплита по типу:
  ```csharp
  if (paramType.IsEnum) providers[i] = GetOrCreateProvider(typeof(EnumAutoCompleteProvider), paramType);
  else if (paramType == typeof(bool)) providers[i] = BoolAutoCompleteProvider.Instance;
  ```
  Чтобы автокомплит для `WeaponConfig` работал автоматически (без `[AutoComplete]` на каждой команде), нужен расширяемый словарь.
  **Решение:**
  ```csharp
  private readonly Dictionary<Type, IAutoCompleteProvider> _defaultProviders = new();

  public CommandRegistry RegisterDefaultProvider(Type type, IAutoCompleteProvider provider);
  public CommandRegistry RegisterDefaultProvider<T>(IAutoCompleteProvider provider);
  ```
  В `RegisterMethod:164-170` сначала проверять `_defaultProviders.TryGetValue(paramType, out var p)`, потом fallback на enum/bool. Та же логика — для команд через builder (M12).

  **НЮАНС:** провайдеры применяются на момент регистрации команды. Команды, найденные через `Initialize()` *до* `RegisterDefaultProvider`, не подхватят. Документировать: «регистрируйте default-провайдеры до `Initialize()` / в самом раннем bootstrap». Опционально (не критично) — после `RegisterDefaultProvider` ребилдить provider-массивы у уже зарегистрированных команд.

- **M11. Новый extension-пакет `com.rubickanov.devconsole.config`** (зависит от M2 + M10)
  Зеркалит структуру `.netcode`. Переносит `ConfigDatabaseAutoCompleteProvider` из `juice-project/Assets/Code/Utils/` в пакет (generic-вариант), добавляет one-line API регистрации.

  **Структура:**
  ```
  packages/com.rubickanov.devconsole.config/
  ├── package.json                            # depends: devconsole, config
  ├── README.md                               # extension-tier
  └── Runtime/
      ├── DevConsole.Config.Runtime.asmdef    # refs: DevConsole.Runtime, Config.Runtime
      ├── ConfigDatabaseAutoCompleteProvider.cs
      └── DevConsoleConfigExtensions.cs
  ```

  **`ConfigDatabaseAutoCompleteProvider<T>`:**
  ```csharp
  public sealed class ConfigDatabaseAutoCompleteProvider<T> : IAutoCompleteProvider
      where T : ConfigBase, IIdentifiable
  {
      private readonly ConfigDatabase<T> _db;
      private readonly string _hint;
      public ConfigDatabaseAutoCompleteProvider(ConfigDatabase<T> db, string? hint = null)
      {
          _db = db;
          _hint = hint ?? $"<{typeof(T).Name}>";
      }
      public string Hint => _hint;
      public void GetSuggestions(string partial, List<string> results)
      {
          var all = _db.All;
          for (int i = 0; i < all.Count; i++)
          {
              var id = all[i].Id;
              if (string.IsNullOrEmpty(partial) ||
                  id.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                  results.Add(id);
          }
      }
  }
  ```

  **Extension-API:**
  ```csharp
  public static class DevConsoleConfigExtensions
  {
      public static CommandRegistry RegisterConfigDatabase<T>(
          this CommandRegistry r, ConfigDatabase<T> db)
          where T : ConfigBase, IIdentifiable
      {
          r.RegisterParser<T>(input =>
          {
              var item = db.Get(input);
              return item != null ? (true, item) : (false, default);
          });
          r.RegisterDefaultProvider<T>(new ConfigDatabaseAutoCompleteProvider<T>(db));
          return r;
      }

      // Generic-overload'ы для bootstrap'а (до 4-5 штук — больше пусть чейнят):
      public static CommandRegistry RegisterConfigDatabases<T1>(
          this CommandRegistry r, ConfigDatabase<T1> db1)
          where T1 : ConfigBase, IIdentifiable
          => r.RegisterConfigDatabase(db1);

      public static CommandRegistry RegisterConfigDatabases<T1, T2>(
          this CommandRegistry r, ConfigDatabase<T1> db1, ConfigDatabase<T2> db2)
          where T1 : ConfigBase, IIdentifiable
          where T2 : ConfigBase, IIdentifiable
          => r.RegisterConfigDatabase(db1).RegisterConfigDatabase(db2);
  }
  ```

  **`package.json`:**
  ```json
  {
      "name": "com.rubickanov.devconsole.config",
      "version": "1.0.0",
      "displayName": "Dev Console — Config Extension",
      "description": "Auto-resolve com.rubickanov.config items by Id in console commands.",
      "unity": "2022.3",
      "dependencies": {
          "com.rubickanov.devconsole": "1.0.0",
          "com.rubickanov.config": "1.0.0"
      }
  }
  ```

  **Тесты:** integration-тест с фейковым `ConfigDatabase<TestData>`, проверка что parser+provider регистрируются, резолвят и автокомплитят ID; парсер возвращает `false` для несуществующего ID.

  **Async-нюанс:** `ConfigService.LoadAsync` асинхронный, парсер devconsole — синхронный. Контракт: пользователь сначала загружает базы через `ConfigService.LoadAsync`, потом передаёт уже загруженный `ConfigDatabase<T>` в `RegisterConfigDatabase`. Прописать в README extension-пакета.

- **M12. Типизированные overload'ы builder'а** (зависит от M2 + M10) — `Runtime/Core/CommandGroupBuilder.cs`
  Сейчас `CommandGroupBuilder.Add(name, Func<string[], string?> handler, desc, params IAutoCompleteProvider?[])` — handler принимает `string[]` и сам всё парсит/валидирует. Это и есть основной источник «тяжести» builder'а (см. `juice-project/Assets/Code/Gameplay/Inventory/Commands/InventoryCommands.cs` — handler `Add(string[] args)` сам делает `_fruitsDb.Get(args[0])` + проверку null). После M2/M10 даём типизированные overload'ы:
  ```csharp
  // Действия (void)
  public CommandGroupBuilder Add(string name, Action handler, string desc = "");
  public CommandGroupBuilder Add<T1>(string name, Action<T1> handler, string desc = "");
  public CommandGroupBuilder Add<T1, T2>(string name, Action<T1, T2> handler, string desc = "");
  public CommandGroupBuilder Add<T1, T2, T3>(string name, Action<T1, T2, T3> handler, string desc = "");

  // Функции (string?)
  public CommandGroupBuilder Add(string name, Func<string?> handler, string desc = "");
  public CommandGroupBuilder Add<T1>(string name, Func<T1, string?> handler, string desc = "");
  public CommandGroupBuilder Add<T1, T2>(string name, Func<T1, T2, string?> handler, string desc = "");
  public CommandGroupBuilder Add<T1, T2, T3>(string name, Func<T1, T2, T3, string?> handler, string desc = "");

  // Существующий «raw» overload остаётся для совместимости и edge-cases:
  public CommandGroupBuilder Add(string name, Func<string[], string?> handler,
                                 string desc = "", params IAutoCompleteProvider?[] providers);
  ```
  Внутри: типизированный overload оборачивает handler в `Func<string[], string?>`, который через `_customParsers` (M2) / встроенные парсит каждый аргумент к нужному типу; при ошибке — возвращает `"Cannot parse 'X' as Tn for argument N"`. Автокомплит-провайдеры берутся из `_defaultProviders[typeof(Tn)]` (M10) автоматом.

  Дополнительно — shorthand `CommandRegistry.Group(...)` как алиас на `RegisterGroup(...)` (короче и читаемее). `RegisterGroup` оставить (backward-compat).

- **M13. `RebuildSortedKeys` пересобирается на каждом `Register/RegisterGroup`** — `Runtime/Core/CommandRegistry.cs:57,91,120-125`
  `new string[_commands.Count]` + `Array.Sort` на каждый вызов. При discovery десятков команд — десятки реаллокаций. Не критично (один раз на старте), но cheap fix.
  **Решение:** флаг `_sortedKeysDirty = true`, ленивый ребилд в `GetSuggestions`/`help`.

### Минорные (m)

- **m1. `Tokenize(string)` static-overload аллоцирует `List<string>` + `.ToArray()`** — `Runtime/Core/CommandRegistry.cs:477-482`
  Используется в `Execute` (`:234`) — на каждое выполнение команды создаётся `List<string>` + копия в массив. Существует zero-alloc overload `Tokenize(string, List<string>)` (`:485`), но `Execute` им не пользуется.
  **Решение:** в `Execute` использовать reusable buffer (отдельный от `_tokenBuffer` — `Execute` рекурсивен через alias expansion). Завести явный stack буферов или просто переиспользовать после копирования индексов в `args`.

- **m2. `string.Join` в alias-расширении** — `Runtime/Core/CommandRegistry.cs:248`
  `aliasCommand + " " + string.Join(" ", args)` — две лишние аллокации. Cold path (alias не в каждом фрейме), но напрашивается `StringBuilder`.

- **m3. `new object?[parameters.Length]` на каждое выполнение reflection-команды** — `Runtime/Core/CommandRegistry.cs:281`
  Per-execution alloc + boxing primitives. Большинство команд имеют 0-3 параметра. Сознательно оставляем — преждевременная оптимизация для дев-инструмента.
  **Решение:** не править. Зафиксировать в `## Design Decisions` README как осознанное решение.

- **m4. `package.json.description` устарела** — `package.json:5`
  Текущий: `"In-game developer console with auto-discovery, autocomplete, and ScriptableObject integration."` — упоминание «ScriptableObject integration» не отражает реальности (никаких SO-команд нет, только settings). Сбивает с толку.
  **Решение:** `"In-game developer console with attribute-based auto-discovery, autocomplete, subcommands, and persistent history."`

- **m5. README File Structure устарел** — `README.md:240-266`
  Не указаны `AliasRegistry.cs`, `CommandBindings.cs`, папка `Commands/` (6 модулей), uxml/uss в `UI/`, файлы `Editor/`.
  **Решение:** удалить блок (по `README_STANDARD.md` File Structure опционален) либо привести в актуальный вид.

- **m6. README Quick Start неполон** — `README.md:42-48`
  Сказано «Add a `UIDocument` component … Attach `DevConsoleUIToolkit` component». Не сказано, нужно ли руками назначить `DevConsoleUI.uxml` в `UIDocument` или `DevConsoleUIToolkit` сам найдёт.
  **Решение:** проверить в песочнице, либо упростить инструкцию, либо явно показать как подцепить uxml.

- **m7. README не описывает `ProviderArgs` в `[AutoComplete]`** — `README.md:90-94`
  Пример показывает `StaticListProvider("easy", "normal", ...)`, но не объяснено, что аргументы после `typeof(...)` пробрасываются в конструктор провайдера через `Activator.CreateInstance`.
  **Решение:** добавить одно предложение про `Activator.CreateInstance`.

- **m8. README не упоминает где живут settings** — `README.md:217-222`
  Сказано «Project Settings > Dev Console», но не сказано что значения пишутся в `ProjectSettings/DevConsoleSettings.json` (а не в `.asset`). Полезно для CI/git.

- **m9. `EnumAutoCompleteProvider`/`StaticListProvider` не кешируются по аргументам** — `Runtime/Core/CommandRegistry.cs:188-198`
  Если `[AutoComplete(0, typeof(StaticListProvider), "a","b","c")]` стоит на 50 командах — создастся 50 инстансов с одинаковыми args. Не критично (создаются один раз при discovery), но cache по `(Type, args)` уменьшил бы footprint.
  **Решение:** ключ кеша — `(Type, string.Join("|", args))`. Опционально.

- **m10. `IAutoCompleteProvider.Hint` использует default interface implementation** — `Runtime/AutoComplete/IAutoCompleteProvider.cs`
  `string? Hint => null;` — DIM, требует C# 8+. `Runtime/csc.rsp` ставит `langVersion:10`. Unity 2021.3 LTS поддерживает C# 9 на ранних патчах, C# 10 — на более свежих. Стоит проверить, что минимально-поддерживаемая версия (`unity` в `package.json`) подхватывает на чистой инсталляции.
  **Решение:** проверить в песочнице. Если что — заменить на explicit getter в каждом провайдере (4 встроенных).

- **m11. `unity` минимум `2021.3` vs `config` `2022.3`** — `package.json:6`
  `com.rubickanov.config` объявляет `"2022.3"`. Сам `devconsole` остаётся `2021.3`. Extension `devconsole.config` (M11) ставит `"2022.3"` (равно `config`).

---

## Порядок исполнения

Группировка по «структурное → косметика» и fix-в-один-присест:

1. **Расширяемость core** (M2 + M10 + M3) — разблокирует целевой UX и extension-пакет. + новые тесты M6 параллельно.
2. **Builder типизированные overload'ы** (M12) — поверх M2/M10.
3. **Зависимости и API-полировка** (M1, M4, M8, M9) — все мелкие фиксы CommandRegistry в один присест.
4. **Persistence** (M7) — лимит истории + clear-команды.
5. **LINQ cleanup** (M5).
6. **Микро-оптимизации** (M13, m1, m2, m9) — опционально; m3 не делаем (см. m3).
7. **Extension-пакет** (M11) — после M2 + M10.
8. **README** (m4, m5, m6, m7, m8 + новая секция «Custom Type Parsers» + новая «Instance Commands via RegisterTarget» + ссылка на `devconsole.config`).
9. **Совместимость** (m10, m11) — финальная проверка.

---

## Verification

1. `unity-project-pckgs/` открывается в Unity, компиляция чистая.
2. Test Runner — все новые тесты `DevConsole.Tests` и `DevConsole.Config.Tests` зелёные.
3. В песочнице — реалистичный сценарий:
   - Создать `WeaponConfig : ConfigBase, IIdentifiable` и `WeaponDatabase : ConfigDatabase<WeaponConfig>` с `[RegisterConfig("Configs/WeaponDatabase")]`.
   - Положить ассет с парой items в Addressables.
   - Composition root: `await configService.LoadAsync<WeaponDatabase>(); CommandRegistry.Instance.RegisterConfigDatabase(weaponDb);`.
   - Команда через атрибут (instance):
     ```csharp
     public class CheatCommands : IStartable
     {
         public void Start() => CommandRegistry.Instance.RegisterTarget(this);

         [ConsoleCommand("give", "Give weapon", "Cheats")]
         public string Give(WeaponConfig weapon, int amount = 1) => $"Granted {amount}x {weapon.Id}";
     }
     ```
   - Команда через builder:
     ```csharp
     CommandRegistry.Instance.Group("inv", "Inventory", "Cheats", g =>
     {
         g.Add<WeaponConfig, int>("add", (w, n) => Inventory.Add(w, n), "Add");
     });
     ```
   - Проверить: `give Sw<Tab>` показывает IDs из `weaponDb.All`; `give SwordOfFire 5` корректно резолвит и вызывает метод; невалидный ID → `"Cannot parse 'XXX' as WeaponConfig for 'weapon'"`.
4. `link.sh local` для `devconsole.config` подтягивает оба пакета без правок manifest.
5. `docs/generate.sh` — обновлённый README попадает в DocFX без ошибок.
6. Smoke-тест в juice-project (опционально, после релиза в этом репо): обновить `InventoryCommands` и `WorldStateCommands` на новый стиль, удалить локальные `ConfigDatabaseAutoCompleteProvider.cs` если полностью покрылся.

---

## Затрагиваемые файлы

**Изменения в `com.rubickanov.devconsole`:**
- `package.json` — добавить `dependencies` (M1), правка `description` (m4).
- `Runtime/Core/CommandRegistry.cs` — M2, M3, M4, M8, M9, M10, M13, опционально m1/m2.
- `Runtime/Core/CommandGroupBuilder.cs` — M12.
- `Runtime/Core/RegisteredCommand.cs` — поле `Target` (M3).
- `Runtime/Commands/SceneCommands.cs` — M5.
- `Runtime/Core/AliasRegistry.cs`, `Runtime/Core/CommandHistory.cs`, `Runtime/Core/CommandBindings.cs` — M7.
- `README.md` — m4-m8 + новые секции про custom parsers, instance commands, ссылку на extension.
- **Новая папка** `Tests/` с `DevConsole.Tests.asmdef` и тест-fixtures (M6).

**Новый пакет `com.rubickanov.devconsole.config`:**
- `package.json`, `README.md`, `Runtime/DevConsole.Config.Runtime.asmdef`,
- `Runtime/ConfigDatabaseAutoCompleteProvider.cs`,
- `Runtime/DevConsoleConfigExtensions.cs`,
- `Tests/DevConsole.Config.Tests.asmdef` + integration-тест.

**Manifest `unity-project-pckgs/Packages/manifest.json`:**
- Добавить `"com.rubickanov.devconsole.config": "file:../../packages/com.rubickanov.devconsole.config"` + в `testables`.
