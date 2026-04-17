# com.rubickanov.config — список проблем

Аудит от 2026-04-17. Ниже — все найденные проблемы с привязкой к коду и планом правок. Сгруппированы в конце по принципу «одно касание кода — один коммит».

---

## Critical

### C1. Валидация проваливается молча — невалидный конфиг кэшируется и отдаётся

**Где:** `Runtime/ConfigService.cs:71-77`

```csharp
if (!config.Validate())
{
    _logger.LogWarning("{Type} validation failed", type.Name);
}
_cache[type] = new CachedConfig(config, handle);
return config;
```

`Validate()` возвращает `false`, но конфиг всё равно кладётся в кэш и возвращается. `Get<T>()` потом отдаёт тот же невалидный инстанс. Пример в README (`BalanceConfig.Validate` → `false` при `_maxHp <= 0`) вводит в заблуждение: выглядит как защита — на деле её нет.

**Fix:** при `Validate() == false` бросать `InvalidOperationException` и звать `Addressables.Release(handle)`. Fail-fast по умолчанию.

---

### C2. LINQ в runtime — нарушение правила репозитория

**Где:** `Runtime/ConfigDatabase.cs:2, 24` — `using System.Linq;` + `_items.ToDictionary(i => i.Id)`.

Memory-правило (`feedback_no_linq.md`) прямо запрещает LINQ в runtime-коде.

**Fix:** ручной цикл:
```csharp
_lookup = new Dictionary<string, TData>(_items.Count);
for (int i = 0; i < _items.Count; i++) _lookup[_items[i].Id] = _items[i];
```

---

### C3. Дубликаты `Id` в `ConfigDatabase<T>` взрывают рантайм на первом `Get`

**Где:** `Runtime/ConfigDatabase.cs:24`

`ToDictionary` бросает `ArgumentException` при дубликатах. Ошибка всплывает далеко от места, где данные заведены (в инспекторе). `ConfigBase.Validate()` этого не ловит.

**Fix:** при ручной инициализации словаря (см. C2) — собирать дубликаты и бросать понятное исключение с перечислением id. Либо переопределить `Validate()` в `ConfigDatabase<T>` с проверкой уникальности и непустоты `Id`.

---

### C4. README заявляет ZLogger, а подключён `Microsoft.Extensions.Logging.Abstractions`

**Где:** `README.md:9` vs `Runtime/Config.Runtime.asmdef:14-16`

Внутри кода — `ILogger<ConfigService>` и `ILoggerFactory` из MEL. ZLogger тут не при чём.

**Fix:** переписать секцию Dependencies в README, в Quick Start показать регистрацию `ILoggerFactory` (сейчас пользователь не догадается, что она нужна).

---

## Major

### M1. Гонка при одновременных `LoadAsync<T>()` одного типа

**Где:** `Runtime/ConfigService.cs:38-77`

Два awaiter-а могут стартовать `LoadAsync<Same>()` до того, как первый записал результат в `_cache`. Оба пройдут cache-miss, оба вызовут `Addressables.LoadAssetAsync`. Второй перезапишет `_cache[type]`, handle первого утечёт (не в кэше → `ReleaseAll` его не увидит).

**Fix:** таблица pending-тасков `Dictionary<Type, UniTask<ConfigBase>>`. Перед загрузкой — проверить pending, вернуть тот же task. После завершения — удалить из pending, записать в `_cache`.

---

### M2. Нет `CancellationToken` в публичном API

**Где:** `Runtime/IConfigService.cs:12, 24`

UniTask и Addressables полноценно поддерживают CT. Сейчас нельзя прервать загрузку при смене сцены / выходе из приложения / таймауте.

**Fix:** добавить `CancellationToken ct = default` в `LoadAsync<T>()` и `RefreshCatalogIfNeededAsync()`, прокинуть через `handle.WithCancellation(ct)`.

---

### M3. Статический `_attributeCache` течёт между тестами и assembly reload

**Где:** `Runtime/ConfigService.cs:16`

`private static readonly Dictionary<Type, RegisterConfigAttribute?>` никогда не чистится. Сейчас не ломает тесты (типы уникальные), но нарушает правило CLAUDE.md: «prefer per-test fixtures when SUT has static state».

**Fix:** сделать кэш инстансным (поле класса). Статика здесь заметного выигрыша не даёт.

---

### M4. `Dispose()` не переводит сервис в невалидное состояние

**Где:** `Runtime/ConfigService.cs:107-110`

После `Dispose()` можно звать `LoadAsync`, и он снова подгрузит конфиг. Нарушает конвенцию .NET (`ObjectDisposedException`).

**Fix:** флаг `_disposed` + `ThrowIfDisposed()` во всех публичных методах.

---

### M5. Покрытие тестами: главная логика `LoadAsync` не тестируется

**Где:** `Tests/ConfigServiceTests.cs`

Покрыта только ветка cache-hit через reflection-based `SeedCache`. Реальный путь через Addressables (успех, исключение при загрузке, провал валидации) и `RefreshCatalogIfNeededAsync` — 0 покрытия.

**Fix:** один из вариантов:
- `[UnityTest]` PlayMode-тесты с реальным Addressables (временный ScriptableObject с адресом),
- либо ввести `IAssetLoader` интерфейс — инвертировать зависимость от Addressables, тестировать через fake в EditMode.

Второй вариант делает архитектуру тестируемой целиком.

---

## Minor

### m1. Phase-label комментарии в тестах нарушают стиль репо

**Где:** `Tests/ConfigDatabaseTests.cs` — `// Arrange`, `// Act`, `// Assert` повсюду.

CLAUDE.md: «No phase-label comments — blank line separation between phases is enough». Остальные тесты чистые.

**Fix:** удалить комментарии, оставить пустые строки между фазами.

---

### m2. Ленивый `_lookup` в `ConfigDatabase` не инвалидируется

**Где:** `Runtime/ConfigDatabase.cs:17, 24`

Тест `Get_LookupBuiltLazily_DoesNotReflectPostCreationChanges` фиксирует это как «документированное поведение». Потенциальная ловушка при Inspector-мутациях.

**Fix (опционально):** `OnValidate()` обнуляет `_lookup`. Либо оставить как есть — поведение задокументировано тестом.

---

### m3. Нет `TryGet<T>` / `IsLoaded<T>()`

Единственный способ проверить — поймать `InvalidOperationException`.

**Fix:** добавить `bool TryGet<T>(out T config)` в `IConfigService` и `ConfigService`.

---

### m4. README Quick Start не показывает регистрацию `ILoggerFactory`

Связан с C4. Без `ILoggerFactory` `ConfigService` не строится, а в примере регистрируется только `IConfigService`.

**Fix:** добавить строчку регистрации фабрики в Quick Start.

---

### m5. Нет XML-документации на `ConfigService`, `RegisterConfigAttribute.Address`, `CachedConfig`

Мелочь; публичный surface (`IConfigService`, `ConfigBase`, `ConfigDatabase`) задокументирован.

**Fix:** дописать summary к остальным публичным членам.

---

## Что сознательно НЕ трогаем

- **«No hot reload»** — явный design decision из README.
- **Отсутствие fallback-конфигов** — ответственность игрового кода.
- **Привязка к MEL** — это и есть логгинг-абстракция, любой backend подключается снаружи.
- **«ConfigService нарушает SRP»** — отвергаю: 130 строк thin coordinator, дробление сделает хуже.

---

## План работ (группировка — одно касание кода, один коммит)

### Шаг 1 — `ConfigDatabase` полный пересмотр
Покрывает **C2 + C3 + m2** и новый тест на дубликаты.
- Убрать `System.Linq`, переписать построение `_lookup` вручную.
- Добавить валидацию дубликатов/пустых `Id` через `Validate()` override.
- Опционально: `OnValidate()` обнуляет `_lookup`.
- Тест на дубликаты (ожидаем понятное исключение).

### Шаг 2 — Fail-fast на валидации
Покрывает **C1**.
- `LoadAsync<T>`: при `Validate() == false` → `Release(handle)` + `throw InvalidOperationException`.
- Тест: `ScriptableObject` с `Validate() → false`, seed в кэш, `LoadAsync` — нет, тест нужен на путь через Addressables; либо через будущий fake-loader (если делаем M5 раньше). Пока — test через модификацию пути, сопряжённого с кэшем, не прокатит. Тест появится после Шага 4.

### Шаг 3 — Lifecycle и статика в `ConfigService`
Покрывает **M3 + M4**.
- `_attributeCache` → инстансный.
- Флаг `_disposed`, `ThrowIfDisposed()` во всех публичных методах.
- Обновить `SeedCache` в тестах если что-то сломается.
- Тесты на `LoadAsync/Get/ReleaseAll/RefreshCatalogIfNeededAsync` после `Dispose` → `ObjectDisposedException`.

### Шаг 4 — Тестируемость + async-гонка + CT
Покрывает **M1 + M2 + M5** одним кластером. Это самое объёмное — стоит выделить в одну задачу целиком.
- Ввести `IAssetLoader` интерфейс (`UniTask<T> LoadAsync<T>(string address, CancellationToken ct)` + `Release`).
- Реализация по умолчанию `AddressablesAssetLoader`.
- `ConfigService` принимает `IAssetLoader` в конструктор.
- `CancellationToken` в `LoadAsync` и `RefreshCatalogIfNeededAsync` (на интерфейсе и имплементации).
- Pending-таблица для race: `Dictionary<Type, UniTask<ConfigBase>>`.
- EditMode-тесты с `FakeAssetLoader`: happy path, исключение при загрузке, провал валидации (C1 теперь тестируется), гонка из двух параллельных `LoadAsync<Same>`.

### Шаг 5 — API polish
Покрывает **m3**.
- `TryGet<T>(out T)` в `IConfigService` и `ConfigService`.
- Тест на true/false ветки.

### Шаг 6 — Документация и стиль
Покрывает **C4 + m1 + m4 + m5**.
- README: Dependencies (MEL вместо ZLogger), Quick Start (+ регистрация `ILoggerFactory`, `IAssetLoader`, `CancellationToken`).
- Убрать `// Arrange/Act/Assert` из `ConfigDatabaseTests`.
- XML-док на оставшиеся публичные члены.

---

## Verification

- `tests-run` по сборке `Config.Tests` после каждого шага.
- Если в процессе Шага 4 появится fake-loader — гонять все PlayMode-сценарии Addressables через него в EditMode.
- Проверить, что пакет всё ещё компилируется в `unity-project-pckgs` (локальная ссылка в manifest уже есть).
