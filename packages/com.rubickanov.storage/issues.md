# Storage Package — Issues & Work Plan

Результаты аудита пакета `com.rubickanov.storage`. Документ отслеживает
все найденные проблемы и порядок их исправления. Ломающие изменения
публичного API допустимы — делаем правильно.

**Зафиксированные решения по ключевым неопределённостям:**
- **M5 (Multi-storage scoping):** не вводим keyed-DI и не прибиваемся к VContainer. В игровом коде консьюмер заводит маркер-интерфейсы (`ISettingsStorage : IStorageService`, `ISaveDataStorage : IStorageService`, `ISecureStorage : IStorageService`) и регистрирует их разными реализациями. В пакет добавляем `sealed class PrefixedStorageService : IStorageService` как namespacing-декоратор, чтобы несколько логических store'ов могли делить один физический бэкенд через префикс ключей. README получает отдельную секцию с обоими паттернами.
- **M2 (Corrupt-JSON recovery):** `FileStorageService` при ошибке парсинга переименовывает файл в `<path>.corrupt-<UTC-timestamp>.bak`, начинает сессию с пустого стора и логирует warning через опциональный `ILogger<FileStorageService>`. Пользователь теряет эпизод, но получает артефакт для разбора и не теряет весь прогресс бесшумно.

---

## Находки

### Критические (реальные баги)

Нет. Пакет маленький (1 интерфейс + 4 реализации), архитектура прозрачная: sync reads / async writes, decorator для шифрования, null-pattern для server-build. Все проблемы ниже — угловые случаи устойчивости, API-полнота, тесты и документация.

### Мажорные (M)

- **M1. `EncryptedStorageService.Decrypt` молча глотает криптоошибки** — `Runtime/EncryptedStorageService.cs:105-133`
  Оба `catch` (`CryptographicException`, `FormatException`) возвращают `null`, геттер — default. Если сменилась passphrase / повредился шифртекст / изменился формат — пользователь получает нули/пустоты без единого следа в логе. Отладить такое по факту невозможно: данные как будто есть (`HasKey` возвращает true — работает по inner storage), но не читаются.
  **Решение:**
  1. Добавить опциональный `ILogger<EncryptedStorageService>? logger = null` в конструктор (паттерн как в `LocalizationService` / `UnityAudioService` — null допустим, пакет не требует logging).
  2. В обоих catch логировать warning с типом исключения:
     ```csharp
     catch (CryptographicException ex)
     {
         _logger?.ZLogWarning($"Decryption failed for stored value: {ex.Message}");
         return null;
     }
     catch (FormatException ex)
     {
         _logger?.ZLogWarning($"Base64 decode failed for stored value: {ex.Message}");
         return null;
     }
     ```
  3. Сам `key` в лог не писать — это PII-риск; типа ошибки достаточно.

- **M2. `FileStorageService.Deserialize` молча обрезает битый JSON и затирает файл частичными данными** — `Runtime/FileStorageService.cs:19-24, 109-170`
  Парсер использует ранний `break` на любой несоответствующей структуре (строки 119, 122, 126, 138, 169). При повреждении файла — `_data` остаётся частично заполненным тем, что успели распарсить. При следующем `SetFloat/SetInt/SetString/DeleteKey` сработает `SaveAsync`, и **файл на диске будет затёрт этим огрызком**. Полная бесшумная потеря данных. Конструктор тоже не оборачивает `File.ReadAllText` и `Deserialize` в try/catch — любое IO-исключение вылетает наружу без контекста.
  **Решение:** строгий парсер + quarantine-recovery в конструкторе.
  1. В `ReadJsonString` и `Deserialize` вместо `break` / `return null` бросать `InvalidDataException` с указанием позиции в JSON (оффсет восстанавливается по разнице длин span'а).
  2. Конструктор принимает опциональный `ILogger<FileStorageService>? logger = null`. Оборачиваем чтение:
     ```csharp
     if (File.Exists(filePath))
     {
         try
         {
             var json = File.ReadAllText(filePath, Encoding.UTF8);
             Deserialize(json);
         }
         catch (Exception ex) when (ex is InvalidDataException or IOException)
         {
             var bak = $"{filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
             try { File.Move(filePath, bak); } catch { /* best effort */ }
             _logger?.ZLogWarning($"Corrupted storage at {filePath}, backed up to {bak}: {ex.Message}");
             _data.Clear();
         }
     }
     ```
  3. `File.ReadAllText` остаётся sync — файл читается на старте один раз.

- **M3. `SaveAsync` не сериализует параллельные записи — гонка за файл** — `Runtime/FileStorageService.cs:78-88`
  Паттерн из README — `storage.SetFloat(...).Forget()` при каждом движении volume-слайдера или каждом тике autosave — запускает по `SaveAsync` на каждое изменение. Метод берёт снапшот JSON на main thread (OK), затем `UniTask.SwitchToThreadPool()` + `File.WriteAllTextAsync`. Два быстрых подряд вызова дают две параллельные задачи на thread pool, пишущие в один файл. На POSIX/NTFS результат undefined — можно получить данные из первой записи при том, что вторая завершилась позже. Fire-and-forget также глотает исключения записи (disk full, permissions).
  **Решение:** сериализовать записи через single in-flight chain — паттерн из аудита localization (M2):
  ```csharp
  private UniTask _pendingSave = UniTask.CompletedTask;

  private UniTask SaveAsync()
  {
      var json = Serialize();
      var dir = Path.GetDirectoryName(_filePath);
      if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
          Directory.CreateDirectory(dir);

      _pendingSave = _pendingSave.ContinueWith(async () =>
      {
          try
          {
              await UniTask.SwitchToThreadPool();
              await File.WriteAllTextAsync(_filePath, json, Encoding.UTF8);
              await UniTask.SwitchToMainThread();
          }
          catch (Exception ex)
          {
              _logger?.ZLogError(ex, $"Failed to save storage to {_filePath}");
          }
      });
      return _pendingSave;
  }
  ```
  После этого `.Forget()` остаётся безопасным (ошибки залогированы), `await storage.SetString(...)` ждёт фактического завершения записи в порядке вызовов.
  **НЮАНС:** если `ContinueWith` в текущей версии UniTask недоступен в нужной сигнатуре — эквивалент через локальную `async UniTask`-функцию с замыканием на предыдущий `_pendingSave`.

- **M4. `FileStorageService` и `EncryptedStorageService` не валидируют аргументы конструктора** — `Runtime/FileStorageService.cs:15-17`, `Runtime/EncryptedStorageService.cs:19-27`
  - `new FileStorageService(null)` → `_filePath = null`, `File.Exists(null)` вернёт `false`, потом `File.WriteAllTextAsync(null, ...)` бросит `ArgumentNullException` из глубины thread-pool'а — сообщение непрозрачное.
  - `new FileStorageService("")` → пишется в текущую рабочую директорию под именем пустой строки.
  - `new EncryptedStorageService(null, "p")` → NRE на первом вызове `_inner`.
  - `new EncryptedStorageService(inner, null)` → `Encoding.UTF8.GetBytes(null)` бросит ANE глубоко в `DeriveStableSalt` / `Rfc2898DeriveBytes`.
  **Решение:** fail-fast в конструкторах.
  ```csharp
  // FileStorageService
  public FileStorageService(string filePath, ILogger<FileStorageService>? logger = null)
  {
      if (string.IsNullOrWhiteSpace(filePath))
          throw new ArgumentException("File path must be non-empty.", nameof(filePath));
      _filePath = filePath;
      _logger = logger;
      // … остальная логика из M2
  }

  // EncryptedStorageService
  public EncryptedStorageService(IStorageService inner, string passphrase, ILogger<EncryptedStorageService>? logger = null)
  {
      if (inner is null) throw new ArgumentNullException(nameof(inner));
      if (string.IsNullOrEmpty(passphrase))
          throw new ArgumentException("Passphrase must be non-empty.", nameof(passphrase));
      _inner = inner;
      _logger = logger;
      // …
  }
  ```

- **M5. Нет выразимого паттерна для нескольких логических storage'ей в одном DI-scope** — `README.md` + новый файл `Runtime/PrefixedStorageService.cs`
  `IStorageService` — единственная абстракция. VContainer регистрирует одну реализацию на интерфейс в scope. Сегодня нельзя выразить "для настроек — PlayerPrefs, для прогресса — файл, для токенов — encrypted" без ручной инстансации в конкретных местах (минуя DI) или без дублирования scope'ов. Сам контракт не виноват — проблема в документации и в отсутствии namespacing-примитива.
  **Решение — два паттерна, которые композируются:**
  1. **Маркер-интерфейсы на стороне консьюмера.** Документируем в README без изменений кода пакета:
     ```csharp
     // В игровом коде
     public interface ISettingsStorage : IStorageService { }
     public interface ISaveDataStorage : IStorageService { }
     public interface ISecureStorage : IStorageService { }

     // В RootLifetimeScope
     builder.Register<PlayerPrefsStorageService>(Lifetime.Singleton).As<ISettingsStorage>();
     builder.RegisterInstance<ISaveDataStorage>(new FileStorageService(Path.Combine(persist, "save.json")));
     builder.RegisterInstance<ISecureStorage>(new EncryptedStorageService(new FileStorageService(tokenPath), passphrase));
     ```
     Консьюмеры инжектят `ISaveDataStorage` — типобезопасно, без строковых ключей, без зависимости от DI-контейнера. Смена backend'а — один свитч в регистрации.
  2. **`PrefixedStorageService` как namespacing-декоратор.** Новый файл `Runtime/PrefixedStorageService.cs`:
     ```csharp
     public sealed class PrefixedStorageService : IStorageService
     {
         private readonly IStorageService _inner;
         private readonly string _prefix;

         public PrefixedStorageService(IStorageService inner, string prefix)
         {
             if (inner is null) throw new ArgumentNullException(nameof(inner));
             if (string.IsNullOrEmpty(prefix))
                 throw new ArgumentException("Prefix must be non-empty.", nameof(prefix));
             _inner = inner;
             _prefix = prefix;
         }

         public float GetFloat(string key, float defaultValue = 0f) => _inner.GetFloat(_prefix + key, defaultValue);
         public UniTask SetFloat(string key, float value) => _inner.SetFloat(_prefix + key, value);
         public int GetInt(string key, int defaultValue = 0) => _inner.GetInt(_prefix + key, defaultValue);
         public UniTask SetInt(string key, int value) => _inner.SetInt(_prefix + key, value);
         public string GetString(string key, string defaultValue = "") => _inner.GetString(_prefix + key, defaultValue);
         public UniTask SetString(string key, string value) => _inner.SetString(_prefix + key, value);
         public bool HasKey(string key) => _inner.HasKey(_prefix + key);
         public UniTask DeleteKey(string key) => _inner.DeleteKey(_prefix + key);
     }
     ```
     Использование — когда хочется несколько логических store'ев на одном файле (или на одном PlayerPrefs) без коллизий:
     ```csharp
     var file = new FileStorageService(path);
     builder.RegisterInstance<ISettingsStorage>(new PrefixedStorageService(file, "settings."));
     builder.RegisterInstance<ISaveDataStorage>(new PrefixedStorageService(file, "save."));
     ```
  3. **README: новая секция `Multi-Storage Scoping`** между `Usage` и `Design Decisions`. Показывает оба паттерна, объясняет когда какой. Подчёркивает: роли (`settings`, `save`, `secure`) — концепты игрового кода, не пакета.
  **НЮАНС:** `PrefixedStorageService` принципиально не префиксует ключи внутри encrypted backend'а — префикс добавляется к ключу, по которому inner storage ищет значение. Это корректно: encryption бьёт только value, а не key. Композиция `new PrefixedStorageService(new EncryptedStorageService(file, pass), "save.")` работает ожидаемо.

- **M6. Нет тестов вообще** — `Tests/` отсутствует
  Пакет без Unity-зависимостей в Runtime-сборке (`noEngineReferences: true`), шифрование и файловый бэкенд легко тестируются обычными NUnit-тестами. Отсутствие покрытия делает любые правки в M1/M2/M3 рискованными.
  **Решение:** создать `Tests/Runtime/Storage.Runtime.Tests.asmdef` (gated `UNITY_INCLUDE_TESTS` + `includePlatforms: [Editor]`) и покрыть базовые сценарии. Минимальный набор (по одному behavior на тест, AAA):
  - `FileStorage_RoundTrip_WritesAndReadsValue` — SetFloat/SetInt/SetString → новый instance на том же пути читает те же значения.
  - `FileStorage_Deserialize_CorruptedJson_BacksUpAndStartsEmpty` — подложить мусорный файл, конструктор → `.bak` создан, `_data` пуст.
  - `FileStorage_Deserialize_EscapedCharacters_RoundTrip` — ключи/значения с `"`, `\`, `\n`, `\r`, `\t`.
  - `FileStorage_ConcurrentSaves_NoDataLoss` — два параллельных `SetString(...).Forget()`, `await Task.Delay`, читаем — оба значения на месте.
  - `EncryptedStorage_RoundTrip_AllTypes` — float/int/string через зашифрованный декоратор над in-memory backend.
  - `EncryptedStorage_WrongPassphrase_ReturnsDefault` — записать с одной passphrase, прочитать с другой → default.
  - `EncryptedStorage_DifferentValues_ProduceDifferentCiphertexts` — проверка рандомного IV.
  - `PrefixedStorage_RoundTrip_IsolatesKeys` — два `PrefixedStorageService` над одним in-memory backend с разными префиксами не видят ключей друг друга.
  - `NullStorage_AllMethods_ReturnDefaults` — smoke-тест.
  - Конструкторные проверки: `new FileStorageService(null)` → `ArgumentException`; `new EncryptedStorageService(null, "p")` → `ArgumentNullException`; `new PrefixedStorageService(inner, "")` → `ArgumentException`.
  Вспомогательный `InMemoryStorageService` для теста декораторов уже есть в `com.rubickanov.audio/Tests/InMemoryStorageService.cs` — его стоит скопировать в `Tests/Runtime/` как internal helper (зависимость от audio-пакета в тестах storage — плохо).

### Минорные (m)

- **m1. `FileStorageService.EscapeJson` аллоцирует до 5 промежуточных строк на каждый ключ/значение** — `Runtime/FileStorageService.cs:172-179`
  Цепочка `s.Replace("\\", ...).Replace("\"", ...).Replace("\n", ...).Replace("\r", ...).Replace("\t", ...)` на каждое применение создаёт новую строку. На save'е с сотнями ключей это измеряется.
  **Решение:** одноразовый проход через `StringBuilder`:
  ```csharp
  private static void AppendEscaped(StringBuilder sb, string s)
  {
      foreach (var c in s)
      {
          switch (c)
          {
              case '\\': sb.Append("\\\\"); break;
              case '"':  sb.Append("\\\""); break;
              case '\n': sb.Append("\\n"); break;
              case '\r': sb.Append("\\r"); break;
              case '\t': sb.Append("\\t"); break;
              default:   sb.Append(c); break;
          }
      }
  }
  ```
  Плюс убрать `EscapeJson`, заменить в `Serialize` на `AppendEscaped(sb, kvp.Key)` / `AppendEscaped(sb, kvp.Value)`.

- **m2. Публичные классы не `sealed`** — все четыре реализации
  `FileStorageService`, `EncryptedStorageService`, `NullStorageService`, `PlayerPrefsStorageService`. Наследование как расширение не предполагается (расширение = новый `IStorageService` + декораторы). По репо-конвенции — sealed по умолчанию.
  **Решение:** добавить `sealed` во все четыре класса.

- **m3. `FileStorageService` не задокументирован как "main thread only"** — `Runtime/FileStorageService.cs:1-10` + README
  Все мутации (`_data[key] = ...`) делаются на вызывающем потоке; `Serialize()` читает `_data` там же. Если кто-то позовёт `SetFloat` из фонового Task'а — race с main thread write. Контракт пакета — main-thread-only для мутирующих методов, но нигде не написан.
  **Решение:** одна строка в README Design Decisions: `**Thread-safety** — all methods must be called from a single thread (Unity main thread by convention). Concurrent mutations are not synchronized.`. Код не менять — добавление Lock усложняет на пустом месте.

- **m4. README не предупреждает, что fire-and-forget теряет ошибки записи на FileStorage** — `README.md:51-60`
  Сейчас буквально написано: "Fire-and-forget (settings, preferences)" с примером `storage.SetFloat(...).Forget()`. После фикса M3 ошибки будут логироваться, но сам контракт стоит упомянуть.
  **Решение:** в секции "Writing Values" добавить абзац: "Fire-and-forget перекладывает ошибки записи на логгер (если он передан в конструктор). Если важно получить подтверждение успешной записи — await."

- **m5. README Quick Start ограничен PlayerPrefs — не хватает двух-трёх строк для file backend** — `README.md:26-32`
  Quick Start — именно первые шаги. Сейчас показан один вариант, хотя file backend в проекте будет использоваться не реже.
  **Решение:** заменить Quick Start на две короткие альтернативы (PlayerPrefs и file), всё ещё под 20 строк.

- **m6. `DeriveStableSalt` — salt детерминирован от passphrase, что криптографически = "нет salt'а"** — `Runtime/EncryptedStorageService.cs:135-142`
  Настоящий salt должен быть уникальным per-installation / per-key, чтобы PBKDF2-вывод нельзя было предрассчитать радужной таблицей. Здесь `salt = SHA256(passphrase)[:16]` — одинаков для всех пользователей с одной passphrase. На практике для локального шифрования сохранения на клиенте этого достаточно (атакующий с доступом к диску пользователя уже имеет всё), но это надо зафиксировать в README.
  **Решение:** добавить в Design Decisions строку: "**Deterministic salt** — `EncryptedStorageService` derives its salt from the passphrase itself; there is no per-installation randomness. Adequate for local client-side encryption of non-critical data (settings, save slots); not a replacement for server-side secret management."

- **m7. PlayerPrefs: `PlayerPrefs.Save()` на каждый `Set*` — синхронный I/O на main thread, выдаваемый как async** — `Unity/PlayerPrefsStorageService.cs:11-46`
  Семантика `UniTask.CompletedTask` после блокирующего `PlayerPrefs.Save()` — ложь в контракте, но чинить это поведение сложно: `PlayerPrefs` не thread-safe, выгнать на thread-pool нельзя. Unity сама флашит PlayerPrefs на `OnApplicationQuit` / `OnApplicationPause`, поэтому per-call Save — это перестраховка от крашей. Ломать поведение (убрать Save, оставить только auto-flush) — слишком рискованно.
  **Решение:** документировать в README Design Decisions: "**PlayerPrefs writes are synchronous** — `PlayerPrefsStorageService` calls `PlayerPrefs.Save()` on every setter on the main thread. The returned `UniTask` is already completed. This trades async-uniformity for crash-safety (values survive process kill)."

- **m8. API gap: нет `Clear()` / массового удаления** — `Runtime/IStorageService.cs:5-15`
  Типичный сценарий "reset to factory defaults" / "logout and wipe local data" делать через пакет нечем — приходится помнить все ключи и вручную `DeleteKey`-ить каждый.
  **Решение:** расширить интерфейс одним методом:
  ```csharp
  UniTask Clear();
  ```
  Реализации:
  - `FileStorageService.Clear()` → `_data.Clear(); return SaveAsync();`.
  - `EncryptedStorageService.Clear()` → `_inner.Clear()` (прокидывается).
  - `NullStorageService.Clear()` → `UniTask.CompletedTask`.
  - `PrefixedStorageService.Clear()` → **не может тривиально очистить только свой префикс** без enumeration API на inner. Вариант: кидать `NotSupportedException` с сообщением "Clear() on a prefixed storage requires key enumeration; clear the inner storage instead."
  - `PlayerPrefsStorageService.Clear()` → `PlayerPrefs.DeleteAll()` (осторожно: чистит ВСЕ PlayerPrefs, не только этого storage'а — это известное ограничение PlayerPrefs API).
  Добавить в README Usage пример с предупреждением про global-nature `PlayerPrefs.DeleteAll`.

- **m9. Fallback логика в `Decrypt` не отличает "ещё не записано" от "повреждено"** — `Runtime/EncryptedStorageService.cs:29-74`
  Проверка `string.IsNullOrEmpty(raw)` перед `Decrypt` (строки 32, 51, 70) — OK для "ключа нет". Но если inner вернул реальную Base64-строку, которая не декриптуется — после фикса M1 будет warning в лог, а геттер всё равно вернёт default. Семантика "ключ был, но распаковать не смогли" неотличима от "ключа не было".
  **Решение:** принять это поведение как документированное в README (`decryption failures return default value; check the log for warnings`) + не трогать код. Реальный "ключ есть / нет" остаётся у `HasKey`.

---

## План батчей

Ломающие изменения API отмечены **(BREAKING)**. Проект молодой, все консьюмеры в одном репо — ломать можно.

### Batch 1 — Input validation + sealed + API cleanup
**Решает:** M4, m2, m8

- [ ] `FileStorageService.cs`: `sealed`, null/empty check на `filePath`, опциональный `ILogger<FileStorageService>?` в ctor (поле хранится, но в этом батче не используется — подготовка к M2).
- [ ] `EncryptedStorageService.cs`: `sealed`, null/empty checks на `inner`/`passphrase`, опциональный `ILogger<EncryptedStorageService>?` в ctor.
- [ ] `NullStorageService.cs`, `PlayerPrefsStorageService.cs`: `sealed`.
- [ ] `IStorageService.cs`: добавить `UniTask Clear();` **(BREAKING)**.
- [ ] `FileStorageService.Clear()`, `EncryptedStorageService.Clear()`, `NullStorageService.Clear()`, `PlayerPrefsStorageService.Clear()` — реализации.
- [ ] README Usage: пример `Clear()` с warning про `PlayerPrefs.DeleteAll` global.

### Batch 2 — PrefixedStorageService + multi-storage README section
**Решает:** M5, частично m8 (реализация `Clear` на prefixed)

- [ ] Новый `Runtime/PrefixedStorageService.cs` (sealed, null/empty checks, проксирование всех методов с префиксом).
- [ ] `PrefixedStorageService.Clear()` → `throw new NotSupportedException(...)` с объяснением.
- [ ] README: новая секция `Multi-Storage Scoping` после Usage:
  - Подсекция "Marker interfaces" — пример с `ISettingsStorage` / `ISaveDataStorage`.
  - Подсекция "Prefixed stores on a single backend" — пример с `PrefixedStorageService`.
  - Одна строка явно: "Роли (`settings`, `save`, `secure`) — концепты игрового кода, не пакета."

### Batch 3 — Error handling & quarantine recovery
**Решает:** M1, M2

- [ ] `EncryptedStorageService.Decrypt`: `_logger?.ZLogWarning` в обоих catch.
- [ ] `FileStorageService.Deserialize`: `break` → `throw new InvalidDataException(...)` с позицией.
- [ ] `FileStorageService` конструктор: try/catch на `InvalidDataException` / `IOException`, rename → `.bak`, warning через `_logger`, `_data.Clear()`.

### Batch 4 — Concurrent save serialization
**Решает:** M3

- [ ] `FileStorageService`: поле `private UniTask _pendingSave = UniTask.CompletedTask;`.
- [ ] `SaveAsync` переписать через chain, try/catch внутри с `_logger?.ZLogError`.
- [ ] Если `ContinueWith` недоступен в текущей сигнатуре UniTask — локальная `async UniTask`-функция с замыканием.

### Batch 5 — Polish + docs
**Решает:** m1, m3, m4, m5, m6, m7, m9

- [ ] `FileStorageService`: `EscapeJson` → `AppendEscaped(StringBuilder, string)`, инлайнить в `Serialize`.
- [ ] README Quick Start: добавить второй вариант (file backend).
- [ ] README "Writing Values": пара строк про fire-and-forget + логгер.
- [ ] README Design Decisions: добавить
  - `Thread-safety` (main thread only, no internal sync).
  - `PlayerPrefs writes are synchronous`.
  - `Deterministic salt` (EncryptedStorageService).
  - `Decryption failures return default` (log warning).

### Batch 6 — Tests
**Решает:** M6 + регрессионная сеть для всех предыдущих батчей

- [ ] `Tests/Runtime/Storage.Runtime.Tests.asmdef`:
  ```json
  {
      "name": "Storage.Runtime.Tests",
      "rootNamespace": "Rubickanov.Storage.Tests",
      "references": ["Storage.Runtime", "GUID:f51ebe6a0ceec4240a699833d6309b23", "UnityEngine.TestRunner", "UnityEditor.TestRunner", "nunit.framework"],
      "includePlatforms": ["Editor"],
      "defineConstraints": ["UNITY_INCLUDE_TESTS"],
      "noEngineReferences": false,
      "precompiledReferences": ["nunit.framework.dll"]
  }
  ```
- [ ] `Tests/Runtime/InMemoryStorageService.cs` — internal helper (копия из `com.rubickanov.audio/Tests/`).
- [ ] `Tests/Runtime/FileStorageServiceTests.cs` — 4 теста (roundtrip, corrupt-json → .bak, escaped chars, concurrent saves).
- [ ] `Tests/Runtime/EncryptedStorageServiceTests.cs` — 3 теста (roundtrip всех типов, wrong passphrase → default, random IV).
- [ ] `Tests/Runtime/PrefixedStorageServiceTests.cs` — 1 тест на изоляцию префиксов + конструкторные guard'ы.
- [ ] `Tests/Runtime/NullStorageServiceTests.cs` — smoke.
- [ ] `Tests/Runtime/ConstructorValidationTests.cs` — null/empty аргументы всех реализаций.
- [ ] Добавить `"com.rubickanov.storage"` в `testables` манифеста `unity-project-pckgs/Packages/manifest.json`, если отсутствует.

---

## Статус

План зафиксирован. Порядок батчей от простого к сложному с тестами в конце как регрессионная сеть: **1 → 2 → 3 → 4 → 5 → 6**.

Верификация:
- После Batch 1-2 — `unity-project-pckgs` открывается, Unity-компилятор не ругается, ручная регистрация в DI из README компилируется.
- После Batch 3 — подкладываем битый `.json` в `Application.persistentDataPath`, убеждаемся, что `.bak` появился + warning в логе.
- После Batch 4 — гоняем concurrent-save тест из Batch 6 (или smoke через два быстрых `SetFloat(...).Forget()` + read на другом instance).
- После Batch 6 — Test Runner зелёный для Storage.Runtime.Tests в режиме Play + Edit.
