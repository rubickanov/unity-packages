# Localization Package — Issues & Work Plan

Результаты аудита пакета `com.rubickanov.localization`. Документ отслеживает все найденные проблемы и порядок их исправления. Ломающие изменения публичного API допустимы — делаем правильно.

**Зафиксированные решения по ключевым неопределённостям:**
- **M1 (`SetLocaleAsync`):** честный await через TaskCompletionSource + one-shot подписка на `SelectedLocaleChanged` + CancellationToken + timeout.
- **M7 (`LangLocale` fallback):** CultureInfo для неизвестных BCP-47 кодов; захардкоженные 28 языков остаются как override для консистентности имён.

---

## Находки

### Критические (реальные баги)

Нет. Архитектура в целом здравая: реактивные свойства через R3, disposed-флаг в сервисе, отписка от `LocalizationSettings.SelectedLocaleChanged` в `Dispose`, null-pattern для headless. Все проблемы ниже — либо угловые случаи, либо качество / консистентность.

### Мажорные (M)

- **M1. `SetLocaleAsync` не ждёт реальной смены локали** — `Runtime/LocalizationService.cs:140-166`
  Метод присваивает `LocalizationSettings.SelectedLocale = targetLocale` и сразу возвращает `UniTask.CompletedTask`. Unity применяет локаль асинхронно (подгрузка таблиц). Caller, делающий `await SetLocaleAsync("ru"); var text = GetString(key)`, получает потенциально ещё английскую строку. Метод ложно заявлен async.
  **Решение:**
  1. Внутри `SetLocaleAsync` создать `UniTaskCompletionSource`.
  2. Подписаться one-shot на `_onLocaleChanged` — при `locale.Identifier.Code == localeCode` → `tcs.TrySetResult()` + отписка.
  3. Принимать `CancellationToken` — привязать к `tcs` через `ct.Register(() => tcs.TrySetCanceled(ct))`.
  4. Опциональный таймаут (например 5 сек) через `UniTask.WhenAny(tcs.Task, UniTask.Delay(timeout, cancellationToken: ct))`.
  5. Присвоить `LocalizationSettings.SelectedLocale = targetLocale` уже ПОСЛЕ настройки подписки (иначе событие может прилететь синхронно до подписки).
  6. Возвращать `tcs.Task`.

- **M2. Race и потеря ошибок при `_storage?.SetString(...).Forget()`** — `Runtime/LocalizationService.cs:213`
  `OnSelectedLocaleChanged` при каждой смене локали стреляет fire-and-forget записью в Storage. `FileStorageService` / `EncryptedStorageService` делают реальный I/O — порядок параллельных записей не гарантирован. `.Forget()` глотает исключения (disk full, permissions, etc.).
  **Решение:** сериализовать через single in-flight chain:
  ```csharp
  private UniTask _pendingSave = UniTask.CompletedTask;

  _pendingSave = _pendingSave.ContinueWith(async () =>
  {
      try { await _storage.SetString(StorageKey, code); }
      catch (Exception ex) { _logger.ZLogError(ex, $"Failed to save locale '{code}'"); }
  });
  ```
  (Если `ContinueWith` в UniTask не доступен в нужной сигнатуре — эквивалент через локальный async метод + присваивание.)

- **M3. Сохранённая локаль не очищается при её отсутствии в `AvailableLocales`** — `Runtime/LocalizationService.cs:87-91`
  Если пользователь удалил языковой бандл, но сохранённый код остался, warning про "Saved locale '{code}' not found" будет сыпаться каждую сессию. Stale-ключ живёт вечно.
  **Решение:** в ветке "not found" записать пустую строку обратно в Storage (`_storage?.SetString(StorageKey, string.Empty).Forget()` — через `_pendingSave` chain из M2). В `RestoreSavedLocale` проверку `string.IsNullOrEmpty(savedLocaleCode)` уже делает — она и так отловит эту пустую строку в следующей сессии.
  **НЮАНС:** `IStorageService` не имеет `Remove` — вариант с пустой строкой не ломает контракт. Если позже добавить `Remove` в storage-пакет, переключиться на него.

- **M4. Нет disposed-guard в `LocalizedValue.SetKey` / `SetArguments`** — `Runtime/LocalizedValue.cs:61-76`
  После `Dispose()` эти методы перезаписывают `_localizedString` / `_arguments`. `UpdateValue` ранним return защищён через `_disposed`, но состояние объекта некогерентно; хуже — `_resolver` (делегат на сервис) может быть вызван после dispose-а самого сервиса.
  **Решение:** в начале `SetKey` и `SetArguments` —
  ```csharp
  if (_disposed) throw new ObjectDisposedException(nameof(LocalizedValue));
  ```

- **M5. `SetKey` на LocalizedValue из `NullLocalizationService` ломает null-семантику** — `Runtime/LocalizedValue.cs:48-67`
  Static-конструктор (`LocalizedValue(string staticValue)`) оставляет `_localizedString = default` и `_resolver = null`. Пользователь, зовущий `SetKey(key)`, получает:
  ```csharp
  _localizedString = new LocalizedString(key.Table, key.Key); // реальный объект
  UpdateValue(); // → _localizedString.GetLocalizedString() на сервере без Unity.Localization
  ```
  На headless-билде без инициализированного `LocalizationSettings` это бросит или вернёт мусор.
  **Решение:** добавить `private readonly bool _isStatic` (true в static-конструкторе). `SetKey` и `SetArguments` в static-режиме — no-op. `_value` остаётся со статической пустой строкой.

- **M6. `FormatException` в `LocalizedValue.GetLocalizedString` глотается без логирования** — `Runtime/LocalizedValue.cs:93-100`
  `LocalizationService.GetString(key, args)` на `FormatException` логирует warning (`LocalizationService.cs:120-124`). Аналогичный try/catch в `LocalizedValue` молча возвращает неформатированную строку — отладка невозможна.
  **Решение:** принять `ILogger<LocalizedValue>` через internal-конструктор (сервис создаёт `LocalizedValue` и прокидывает логгер). Для static-режима логгер — nullable / `NullLogger`. В catch:
  ```csharp
  _logger?.ZLogWarning($"Format error in LocalizedValue (table={_localizedString.TableReference}, key={_localizedString.TableEntryReference}): {ex.Message}");
  ```

- **M7. `LangLocale`: хардкод 28 языков, нет fallback для неизвестных кодов** — `Runtime/LangLocale.cs:58-119`
  Для BCP-47 кода не из словаря `Name`/`NativeName` возвращается uppercase-кода (e.g., `"bg"` → `"BG"`). Список невозможно расширить без правки пакета.
  **Решение:** в `GetNameForCode` / `GetNativeNameForCode`, если `primaryCode` нет в `LanguageNames`, пытаться:
  ```csharp
  try
  {
      var culture = CultureInfo.GetCultureInfo(code); // полный код, не primary
      return culture.DisplayName; // или .NativeName для второго метода
  }
  catch (CultureNotFoundException)
  {
      return code.ToUpperInvariant(); // текущий fallback
  }
  ```
  Встроенные 28 языков остаются override'ом — они дают контроль над конкретными русскими именами ("Russian"/"Русский") вместо `.NET`-default'ов.

- **M8. `package.json` не объявляет зависимость от `com.rubickanov.storage`** — `package.json`
  Runtime asmdef жёстко ссылается на `Storage.Runtime`. Консьюмер, установивший только localization, получит compile errors — UPM не подтянет storage автоматически. Остальные пакеты репо (`com.rubickanov.gas`, `com.rubickanov.acs.netcode`) корректно декларируют rubickanov-зависимости.
  **Решение:**
  ```json
  {
      "name": "com.rubickanov.localization",
      ...
      "dependencies": {
          "com.rubickanov.storage": "1.0.0"
      }
  }
  ```
  Внешние (R3, UniTask, Unity.Localization, ZLogger) не декларируем — консистентно с остальным репо; предполагается что консьюмер настраивает их сам.

- **M9. `LocalizationKey` принимает пустые строки `Table`/`Key`** — `Runtime/LocalizationKey.cs:20-24`
  Конструктор throw'ит только на null. `new LocalizationKey("", "")` или `default(LocalizationKey)` валидны и проходят дальше в `LocalizationService.GetOrCreateLocalizedString` → `new LocalizedString("", "")` → `IsEmpty == true` → молчаливая пустая строка.
  **Решение:**
  ```csharp
  public LocalizationKey(string table, string key)
  {
      if (string.IsNullOrWhiteSpace(table))
          throw new ArgumentException("Table must be non-empty.", nameof(table));
      if (string.IsNullOrWhiteSpace(key))
          throw new ArgumentException("Key must be non-empty.", nameof(key));
      Table = table;
      Key = key;
  }

  public bool IsValid => !string.IsNullOrEmpty(Table) && !string.IsNullOrEmpty(Key);
  ```
  В `LocalizationService.GetOrCreateLocalizedString` добавить `if (!key.IsValid) throw new ArgumentException(...)`.

- **M10. `LocalizationKeysPostprocessor` определяет string-table ассеты по подстроке пути** — `Editor/LocalizationKeysPostprocessor.cs:48-56`
  Проверка: `path.Contains("Localization") || path.Contains("StringTable") || path.EndsWith(" Shared.asset")`. Ложное срабатывание на любом ассете с "Localization" в имени — например, `Assets/Art/LocalizationIcons.asset` триггерит перегенерацию.
  **Решение:** для `importedAssets` / `movedAssets` грузить ассет и проверять тип:
  ```csharp
  private static bool IsStringTableAsset(string path)
  {
      if (!path.EndsWith(".asset")) return false;
      var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
      return asset is StringTableCollection || asset is SharedTableData;
  }
  ```
  Для `deletedAssets` ассет уже недоступен — оставить best-effort подстрочную проверку с комментарием "deleted asset, type unknown".

### Минорные (m)

- **m1. `LangLocale.Empty` аллоцирует struct на каждый доступ** — `Runtime/LangLocale.cs:40`
  `public static LangLocale Empty => new(...)` — expression-bodied property создаёт новый struct каждый вызов.
  **Решение:** `public static readonly LangLocale Empty = new(string.Empty, string.Empty, string.Empty);`.

- **m2. `GetNameForCode` / `GetNativeNameForCode` делают `.Split('-')[0]` на каждый вызов** — `Runtime/LangLocale.cs:99, 114`
  Аллокация массива `string[]` на каждый вызов (часто из UI-биндингов).
  **Решение:**
  ```csharp
  var dash = code.IndexOf('-');
  var primary = dash < 0 ? code : code.Substring(0, dash);
  ```

- **m3. `NullLocalizationService` — не `sealed`, не `IDisposable`** — `Runtime/NullLocalizationService.cs:12`
  `LocalizationService` sealed + IDisposable. `NullLocalizationService` — просто class без Dispose. Два `ReactiveProperty<>` никогда не закрываются.
  **Решение:**
  ```csharp
  public sealed class NullLocalizationService : ILocalizationService, IDisposable
  {
      public void Dispose()
      {
          _currentLocale.Dispose();
          _isRtl.Dispose();
      }
  }
  ```

- **m4. `Observable.Empty<Locale>()` создаёт новый экземпляр на каждый геттер** — `Runtime/NullLocalizationService.cs:24`
  **Решение:**
  ```csharp
  private static readonly Observable<Locale> EmptyObservable = Observable.Empty<Locale>();
  public Observable<Locale> OnLocaleChanged => EmptyObservable;
  ```

- **m5. `EditorApplication.delayCall += GenerateKeys` не дедуплицируется** — `Editor/LocalizationKeysPostprocessor.cs:44`
  При bulk-импорте (Reimport All, массовое изменение таблиц) генератор планируется N раз.
  **Решение:** обёртка с флагом:
  ```csharp
  private static bool _pendingRegeneration;

  if (shouldRegenerate && !_pendingRegeneration)
  {
      _pendingRegeneration = true;
      EditorApplication.delayCall += OnDelayedRegenerate;
  }

  private static void OnDelayedRegenerate()
  {
      _pendingRegeneration = false;
      LocalizationKeysGenerator.GenerateKeys();
  }
  ```

- **m6. `Regex.Replace` с compile-at-call в `SanitizeIdentifier`** — `Editor/LocalizationKeysGenerator.cs:181`
  **Решение:** `private static readonly Regex IdentifierPattern = new(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);`, использовать `IdentifierPattern.Replace(input, "_")`.

- **m7. `IsCSharpKeyword` создаёт новый HashSet на каждый вызов** — `Editor/LocalizationKeysGenerator.cs:218-233`
  **Решение:** вынести в `private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal) { "abstract", ... };`.

- **m8. `SetLocaleAsync` логирует "Locale changed to: {code}" до фактической смены** — `Runtime/LocalizationService.cs:164`
  Info-лог выдаётся сразу после присваивания, до реального применения локали (см. M1). Вводит в заблуждение при отладке.
  **Решение:** убрать `ZLogInformation` из `SetLocaleAsync`. В `OnSelectedLocaleChanged` уже есть `ZLogDebug($"Locale changed event: {code}, RTL: {isRtl}")` — поднять его до `ZLogInformation` (или оставить debug, если шумно).

- **m9. StorageKey `"locale"` не префиксован — риск коллизии с другими сервисами** — `Runtime/LocalizationService.cs:24`
  `"locale"` — слишком общее имя. Другой сервис может случайно записать/прочитать этот ключ.
  **Решение:** `"localization.locale"` или `"Localization_Locale"`. Ломает существующие сохранения — добавить однократный migration в `RestoreSavedLocale`: если нет нового ключа, но есть старый `"locale"` — прочитать старый, сохранить под новым, удалить старый (через пустую строку).
  **АЛЬТЕРНАТИВА:** оставить как есть, задокументировав в xmldoc на константе. Выбрать по вкусу — это не баг, а профилактика.

- **m10. `RestoreSavedLocale` дублирует установку `_currentLocale`** — `Runtime/LocalizationService.cs:78-81`
  В ветке "found":
  ```csharp
  LocalizationSettings.SelectedLocale = locale;                     // триггерит OnSelectedLocaleChanged
  _currentLocale.Value = new LangLocale(savedLocaleCode);           // дубль того, что сделает колбэк
  _isRtl.Value = IsRtlLocale(savedLocaleCode);                      // дубль
  ```
  `OnSelectedLocaleChanged` от Unity синхронно (или почти) вызовется и сделает то же самое. Подписчики `_onLocaleChanged` получат **один** уведомление (это делает колбэк), но `_currentLocale` / `_isRtl` перезаписываются дважды → транзиентный рассинхрон и лишние нотификации R3-подписчиков.
  **Решение:** в ветке "found" убрать ручное присваивание `_currentLocale` / `_isRtl` — единственный источник правды = колбэк Unity. В ветках "no saved" и "not found" (где `SelectedLocale` не меняется) ручное присваивание оставить.

- **m11. `GetAvailableLocales` аллоцирует массив на каждый вызов** — `Runtime/LocalizationService.cs:173-185`
  UI-биндинг может вызывать часто (переоткрытие меню).
  **Решение:** кэшировать `LangLocale[]` в поле, заполнять один раз в `InitializeAsync` после `await LocalizationSettings.InitializationOperation`. `AvailableLocales` на практике стабилен после init.
  ```csharp
  private LangLocale[] _cachedAvailableLocales = Array.Empty<LangLocale>();

  // в InitializeAsync после await:
  var locales = LocalizationSettings.AvailableLocales.Locales;
  _cachedAvailableLocales = new LangLocale[locales.Count];
  for (var i = 0; i < locales.Count; i++)
      _cachedAvailableLocales[i] = new LangLocale(locales[i].Identifier.Code);

  public LangLocale[] GetAvailableLocales() => _cachedAvailableLocales;
  ```
  Возврат самого массива (без копии) — компромисс: консьюмер может модифицировать. Если критично — возвращать `IReadOnlyList<LangLocale>` и менять сигнатуру интерфейса (ломающее).

- **m12. `LocalizationGeneratorSettings.OutputPath` не валидируется** — `Editor/LocalizationGeneratorSettings.cs:13`
  Пустой или некорректный путь — `WriteToFile` либо падает внутри `Path.GetDirectoryName(null)`, либо пишет в случайное место.
  **Решение:**
  1. В `LocalizationGeneratorSettingsProvider.OnGUI` после `ApplyModifiedProperties` — проверка `string.IsNullOrWhiteSpace(OutputPath)` → `EditorGUILayout.HelpBox("Output path is required", MessageType.Error)`.
  2. В `LocalizationKeysGenerator.GenerateKeys` — early return:
     ```csharp
     if (string.IsNullOrWhiteSpace(Settings.OutputPath))
     {
         Debug.LogError("[LocalizationKeysGenerator] OutputPath is empty. Configure in Project Settings / Localization Generator.");
         return;
     }
     ```

### Отсутствующие фичи / пробелы (F)

- **F1. Нет тестов (0% покрытия).**
  Unit-тесты (чистые, без PlayMode):
  - `LocalizationKey`: Equals, HashCode, `==`/`!=`, null-throw, empty/whitespace-throw (после M9), `IsValid`, `default(LocalizationKey)` не ломает сервис.
  - `LangLocale`: Equals по Code case-insensitive, `Empty`, `IsEmpty`, primary-code parsing (`"ru-RU"` → `"ru"`), CultureInfo fallback для неизвестного кода (после M7).
  - `LocalizationKeysGenerator` internals — вынести `SanitizeIdentifier`, `ToPascalCase`, `KeyTreeNode` в internal static класс + `[assembly: InternalsVisibleTo(...)]` для Editor-тестов:
    - `SanitizeIdentifier`: ключевые слова → `@`, цифра в начале → `_`, спецсимволы → `_`, пустой ввод → `_`.
    - `ToPascalCase`: `snake_case` → `SnakeCase`, `UPPER_CASE` → `UpperCase`, пустой ввод.
    - `KeyTreeNode.Insert`: `"ui.menu.play"` → правильная иерархия, mixed flat + nested.

  Integration-тесты (PlayMode или Edit+`UnityEngine.Localization.Samples` test-helpers):
  - `LocalizationService.InitializeAsync` + `RestoreSavedLocale` — три ветки (no saved / found / not found), корректное состояние `_currentLocale` / `_isRtl` после каждой.
  - После M1: `await SetLocaleAsync("ru")` завершается только после `OnLocaleChanged` пришёл с кодом `"ru"`.
  - После F2: `SetLocaleAsync(code, ct)` с cancelled token бросает `OperationCanceledException`.
  - `Dispose` → подписка на `LocalizationSettings.SelectedLocaleChanged` снята; повторный `Dispose` — no-op.
  - `LocalizedValue`: auto-update на смену локали, `SetKey` использует кэш (тот же `LocalizedString` instance для повторного ключа), `Dispose` отписывает от subscription.
  - После M5: `NullLocalizationService.Localize(...).SetKey(other)` — no-op, `Value.CurrentValue` остаётся пустой строкой.

  Структура: `Tests/Runtime/` + `Tests/Editor/` с asmdef'ами под `UNITY_INCLUDE_TESTS` + `includePlatforms: [Editor]` (как в остальных пакетах репо).

- **F2. Нет `CancellationToken` на `InitializeAsync` / `SetLocaleAsync`.**
  Тянется с M1. После фикса M1 CancellationToken становится естественной частью сигнатуры `SetLocaleAsync(string, CancellationToken = default)`. `InitializeAsync` — через `.AttachExternalCancellation(ct)` на `LocalizationSettings.InitializationOperation`.
  **Решение:** обновить интерфейс `ILocalizationService`:
  ```csharp
  UniTask InitializeAsync(CancellationToken ct = default);
  UniTask SetLocaleAsync(string localeCode, CancellationToken ct = default);
  UniTask SetLocaleAsync(LangLocale locale, CancellationToken ct = default);
  ```
  Update `NullLocalizationService` соответственно.

---

## Batches

### Batch 1 — Packaging + docs (тривиально)
**Решает:** M8, m9 (опц.), consistency с остальным репо

- [ ] `package.json`: добавить
  ```json
  "dependencies": { "com.rubickanov.storage": "1.0.0" }
  ```
- [ ] (Опц., m9) Переименовать `StorageKey` в `"localization.locale"` + one-shot миграция в `RestoreSavedLocale` (чтение старого `"locale"` при отсутствии нового, запись под новым, clear старого).
- [ ] README: добавить `com.rubickanov.storage` в секцию Dependencies (сейчас упомянут, но не как декларированная зависимость).

### Batch 2 — `LangLocale` + `NullLocalizationService` polish
**Решает:** M7, m1, m2, m3, m4

- [ ] `LangLocale.cs`:
  - `Empty` → `static readonly` поле.
  - `GetNameForCode` / `GetNativeNameForCode` — `IndexOf('-')` + `Substring` вместо `Split`.
  - Fallback через `CultureInfo.GetCultureInfo(code)` для не-словарных кодов, catch `CultureNotFoundException` → uppercase-код.
- [ ] `NullLocalizationService.cs`:
  - `public sealed class ... : ILocalizationService, IDisposable`.
  - `Dispose()` с `_currentLocale.Dispose(); _isRtl.Dispose();`.
  - `private static readonly Observable<Locale> EmptyObservable = Observable.Empty<Locale>();`, `OnLocaleChanged => EmptyObservable`.

### Batch 3 — Input validation
**Решает:** M9

- [ ] `LocalizationKey.cs`:
  - Конструктор throw'ит `ArgumentException` при `string.IsNullOrWhiteSpace` для `table` / `key`.
  - `public bool IsValid => !string.IsNullOrEmpty(Table) && !string.IsNullOrEmpty(Key);`.
- [ ] `LocalizationService.GetOrCreateLocalizedString`: `if (!key.IsValid) throw new ArgumentException(...)` как defense-in-depth для `default(LocalizationKey)`.

### Batch 4 — `LocalizedValue` lifecycle + logging
**Решает:** M4, M5, M6

- [ ] `LocalizedValue.cs`:
  - `private readonly bool _isStatic` — true в static-ctor, false в reactive-ctor.
  - `SetKey` и `SetArguments`:
    - `if (_disposed) throw new ObjectDisposedException(...)`.
    - `if (_isStatic) return;` (no-op на null-сервисе).
  - Internal reactive-ctor принимает `ILogger? logger = null`, хранит в поле.
  - `GetLocalizedString` catch — `_logger?.ZLogWarning(...)`.
- [ ] `LocalizationService.Localize`: прокинуть `_logger` (или `loggerFactory.CreateLogger<LocalizedValue>()`) в конструктор `LocalizedValue`.

### Batch 5 — `LocalizationService` корректность (крупнейший)
**Решает:** M1, M2, M3, m8, m10, m11, F2

- [ ] `ILocalizationService`: добавить `CancellationToken ct = default` в `InitializeAsync`, `SetLocaleAsync(string, ...)`, `SetLocaleAsync(LangLocale, ...)`. Обновить `NullLocalizationService` (просто `return UniTask.CompletedTask`).
- [ ] `LocalizationService.SetLocaleAsync` (M1, m8):
  - Удалить existing `ZLogInformation` "Locale changed to".
  - Создать `UniTaskCompletionSource` + one-shot подписка на `_onLocaleChanged` с фильтром по `Identifier.Code`.
  - Связать с `CancellationToken`.
  - Опциональный timeout через `UniTask.WhenAny`.
  - Присвоить `LocalizationSettings.SelectedLocale` после настройки подписки.
  - Вернуть `tcs.Task`.
- [ ] `LocalizationService.OnSelectedLocaleChanged` (M2, m8):
  - Поднять существующий `ZLogDebug` "Locale changed event" до `ZLogInformation` (или оставить debug, решить по вкусу).
  - `_storage.SetString(...)` через сериализованный chain `_pendingSave`, try/catch внутри с `_logger.ZLogError` на exception.
- [ ] `LocalizationService.RestoreSavedLocale` (M3, m10):
  - В ветке "found" убрать ручное присваивание `_currentLocale.Value` / `_isRtl.Value` — колбэк Unity сделает.
  - В ветке "not found" — дополнительно `_storage?.SetString(StorageKey, string.Empty).Forget()` (через `_pendingSave` chain) для очистки stale-значения.
- [ ] `LocalizationService.InitializeAsync` (m11, F2):
  - Принимать `CancellationToken ct`.
  - После `await LocalizationSettings.InitializationOperation.AttachExternalCancellation(ct)` — заполнить `_cachedAvailableLocales`.
- [ ] `LocalizationService.GetAvailableLocales` (m11): возвращать `_cachedAvailableLocales` (или `Array.Empty<LangLocale>()` если ещё не init'ed).

### Batch 6 — Editor tooling
**Решает:** M10, m5, m6, m7, m12

- [ ] `LocalizationKeysPostprocessor.cs` (M10, m5):
  - `IsStringTableAsset` через `AssetDatabase.LoadAssetAtPath<Object>(path) is StringTableCollection or SharedTableData` для `importedAssets`.
  - Fallback подстрочная проверка остаётся только для `deletedAssets` (ассет уже не доступен) + комментарий.
  - Dedup через `static bool _pendingRegeneration` + обёрточный метод.
- [ ] `LocalizationKeysGenerator.cs`:
  - m6: `static readonly Regex IdentifierPattern = new(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);`.
  - m7: `static readonly HashSet<string> CSharpKeywords` с case-sensitive StringComparer (проверка делает `.ToLowerInvariant()` — можно оставить, но лучше выровнять: считать keyword'ом только lowercase).
- [ ] `LocalizationGeneratorSettings.cs` / `Provider` (m12):
  - Runtime-валидация OutputPath: early return с `Debug.LogError` в `GenerateKeys` при пустом пути.
  - `HelpBox` в `OnGUI`.

### Batch 7 — Tests
**Решает:** F1 + регрессии от Batches 1-6

- [ ] `Tests/Editor/LocalizationKeyTests.cs` — Equals/HashCode/throw.
- [ ] `Tests/Editor/LangLocaleTests.cs` — Empty, Equals по Code, primary-code parsing, CultureInfo fallback для `"bg"` / `"sk"` / etc.
- [ ] Вынести `SanitizeIdentifier`, `ToPascalCase`, `KeyTreeNode` в `internal static class KeyGeneratorInternals` + `InternalsVisibleTo("Localization.Editor.Tests")`.
- [ ] `Tests/Editor/KeyGeneratorInternalsTests.cs` — покрыть edge cases санитайзера и дерева.
- [ ] `Tests/Runtime/LocalizationServiceTests.cs` (PlayMode, требует stub `LocalizationSettings`):
  - Initialize + Restore (3 ветки).
  - SetLocaleAsync awaits до смены.
  - SetLocaleAsync cancellation.
  - Dispose + повторный Dispose.
- [ ] `Tests/Runtime/LocalizedValueTests.cs`:
  - Auto-update на OnLocaleChanged.
  - SetKey через resolver = cache hit.
  - Dispose после вызова SetKey → ObjectDisposedException.
- [ ] `Tests/Runtime/NullLocalizationServiceTests.cs`:
  - Все методы возвращают пустое / completed task.
  - `Localize(x).SetKey(y)` no-op, `Value.CurrentValue` остаётся пустой.
- [ ] Создать asmdef'ы:
  - `Tests/Runtime/Localization.Runtime.Tests.asmdef` — `UNITY_INCLUDE_TESTS` + `includePlatforms: [Editor]`.
  - `Tests/Editor/Localization.Editor.Tests.asmdef` — аналогично.

---

## Статус

План зафиксирован. Начинать с **Batch 1** (тривиально, без риска). Порядок батчей отражает ожидаемую лёгкость: 1 → 2 → 3 → 4 → 6 (Editor независим) → 5 (крупнейший, LocalizationService) → 7 (тесты как регрессионная сеть для всего).

Верификация каждого батча: `unity-project-pckgs` открывается без ошибок компиляции; после Batch 5 — smoke-тест с `await loc.SetLocaleAsync("ru")` и проверкой что `GetString` сразу после возвращает переведённую строку; после Batch 7 — зелёный Test Runner.
