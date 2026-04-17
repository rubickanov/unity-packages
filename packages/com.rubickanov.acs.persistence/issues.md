# com.rubickanov.acs.persistence — Issues

Рабочий список задач по результатам аудита. Приоритет батчей сверху вниз:
HIGH → MEDIUM → LOW. Каждый батч — независимый PR, тестируется отдельно.

## Сводка

- **4 HIGH** — корректность на edge cases (null-поля, миграции, двойной restore мира).
- **10 MEDIUM** — thread-safety, stale doc-claims, validation, enum support.
- **8 LOW** — документация, логирование, мелкие улучшения.
- **11 пробелов в тестах**.

---

## Batch 1 — Robustness: Null/Uninitialized Fields (HIGH)

Общая проблема: код ходит по `FieldInfo.GetValue(aspect)` и сразу передаёт
результат в binding, не проверяя null. Если пользователь забыл `= new(...)` в
декларации `[PersistedState]`-поля, всё ломается скрытым способом.

### 1.1 Null raw field value в `PersistedFieldBindingFactory`
- **File:** `Runtime/Bindings/PersistedFieldBindingFactory.cs:9`
- **Проблема:** `var raw = info.Field.GetValue(aspect)` может вернуть null
  (поле объявлено без инициализатора). Далее `Activator.CreateInstance(bindingType, raw)`
  сохраняет null в binding, и при первом вызове `WriteValue` / `ReadValue` ловим NRE.
- **Severity:** HIGH
- **Fix:** В начале `Create` добавить проверку и бросать
  `InvalidOperationException` с сообщением вида «Aspect 'X' field 'Y' marked
  [PersistedState] is null. [PersistedState] fields must be initialized
  (e.g. '= new()')».

### 1.2 Unboxing null в value-type `ReactiveProperty<T>` бросает NRE
- **File:** `Runtime/Bindings/PersistedReactiveBinding.cs:21`
  + catch в `Extensions/EntityPersistenceExtensions.cs:116`
- **Проблема:** `(T)value` где T — value type, а value — null, даёт NRE.
  `catch (InvalidCastException)` его не ловит. Одно плохое поле ломает весь
  restore (контракт «одно плохое поле не портит весь restore» нарушен).
- **Severity:** HIGH
- **Fix:** В `PersistedReactiveBinding<T>.WriteValue` перед cast:
  `if (value == null && default(T) is not null) throw new InvalidCastException(...)`.
  Плюс расширить catch в `EntityPersistenceExtensions.Restore` на
  `NullReferenceException` как защиту в глубину.

### 1.3 Null-коллекция в binding → NRE на `Clear()`
- **File:** `Runtime/Bindings/PersistedListBinding.cs:27`,
  `PersistedHashSetBinding.cs:25`, `PersistedDictionaryBinding.cs:25`
- **Проблема:** если `ObservableList<T>` / `ObservableHashSet<T>` /
  `ObservableDictionary<K,V>` поле null, `_collection.Clear()` в `WriteValue`
  бросает NRE. `ReadValue` — тоже.
- **Severity:** HIGH (полностью перекрывается 1.1, если тот сделан)
- **Fix:** Защита в 1.1 — binding просто не создаётся. Доп. защита не требуется,
  но добавим `Debug.Assert(_collection != null)` в конструктор binding
  для dev-time.

---

## Batch 2 — Migration Consistency (HIGH / MEDIUM)

### 2.1 `ApplySnapshotMigrations` не продвигает `FormatVersion` пошагово
- **File:** `Runtime/Extensions/WorldPersistenceExtensions.cs:162-204`
- **Проблема:** `snapshot.FormatVersion = to` присваивается только в самом
  конце (line 203). Если чейн миграций сломается в середине (exception в
  `chain[i].Migrate`, line 194–199), уже применённые шаги остаются
  применёнными (snapshot мутирован), но `FormatVersion` остаётся на исходном
  `from`. Повторный restore применит миграции заново поверх уже мигрированных
  данных → повреждение.
- **Severity:** HIGH
- **Fix:** Инкрементировать `snapshot.FormatVersion = chain[i].FromFormatVersion + 1`
  после каждого успешного шага (аналогично тому, как сделано для
  `AspectData.Version` в `EntityPersistenceExtensions.TryMigrateAspect:184`).

### 2.2 `resolveOrSpawn`, возвращающий `world`, создаёт двойной restore
- **File:** `Runtime/Extensions/WorldPersistenceExtensions.cs:132-142`
- **Проблема:** если save layer (например, из-за бага в своей id-map) вернёт
  `world` из `resolveOrSpawn`:
  1. Сначала отресторим `snapshot.World` на world (line 124).
  2. Потом отресторим `pair.Value` (entity snapshot) опять на world (line 141)
     — двойной restore с потенциально разными данными.

  `SnapshotAll` сам гарантирует, что world не попадёт в `snapshot.Entities`
  (line 74), но restore должен быть defensive к некорректным сейвам.
- **Severity:** HIGH
- **Fix:** После `resolveOrSpawn` проверить `ReferenceEquals(entity, world)`
  и логировать error + skip («save-layer resolveOrSpawn must never return the
  World; world-scoped aspects live in snapshot.World»).

### 2.3 `TryMigrateAspect` мутирует input-`AspectData` незаметно
- **File:** `Runtime/Extensions/EntityPersistenceExtensions.cs:175, :184`
- **Проблема:** миграторы редактируют `data.Fields` in place, и код инкрементирует
  `data.Version`. Это значит что после `Restore(snap, registry)` переданный
  `snap` остался в «уже мигрированном» состоянии. Если save layer решит ещё
  раз использовать этот же `snap` — огребёт. Документация в методах
  `Snapshot()` / `Restore()` об этом молчит.
- **Severity:** MEDIUM
- **Fix:** Добавить явное упоминание в docstring `Restore`: «If migrations run,
  the snapshot is mutated in place — do not reuse the same AspectSnapshot for
  a second restore».

### 2.4 Snapshot-миграторы запускаются на все entity snapshots безусловно
- **File:** `Runtime/Extensions/WorldPersistenceExtensions.cs:191-192`
- **Проблема:** если в `WorldSnapshot.Entities` 10k сущностей, и FormatVersion
  gap = 3, мы вызываем N_migrators × N_entities миграторов — даже на
  сущностях, которые уже в новой форме. By-design, но стоит отметить стоимость
  в README.
- **Severity:** LOW
- **Fix:** Документация. Код не трогаем.

---

## Batch 3 — Documentation Mismatches (LOW, trivial)

Тривиальные правки — docstrings застряли в старой версии API.

### 3.1 `AspectSnapshot` doc говорит «Keyed by Type.FullName»
- **File:** `Runtime/Snapshot/AspectSnapshot.cs:7`, `:20`
- **Fix:** «Keyed by the stable snapshot key — `[PersistedKey]` when present,
  `Type.FullName` otherwise».

### 3.2 `PersistedFieldInfo.KeyType` doc неполон
- **File:** `Runtime/Scanner/PersistedFieldInfo.cs:19`
- **Fix:** «TKey for ObservableDictionary<TKey, TValue>; null for Reactive /
  List / HashSet kinds».

### 3.3 `PersistedStateAttribute` не поясняет наследование
- **File:** `Runtime/Attributes/PersistedStateAttribute.cs:20`
- **Fix:** Добавить: «[PersistedState] does not inherit through CLR attribute
  reflection, but the scanner walks the type hierarchy explicitly, so fields
  declared on base aspects are always included».

### 3.4 `WorldPersistenceExtensions.DefaultDispose` error message не направляет
- **File:** `Runtime/Extensions/WorldPersistenceExtensions.cs:223-225`
- **Fix:** Перефразировать: «Pass WorldRestoreOptions.DisposeMissing callback
  to handle '{type}'. Built-in fallback handles only IDisposable and
  UnityEngine.Component».

---

## Batch 4 — Performance

### 4.1 `Activator.CreateInstance` на каждое поле → Compiled delegates
- **File:** `Runtime/Bindings/PersistedFieldBindingFactory.cs:16-31`
  + вызовы в `EntityPersistenceExtensions.cs:50, :111`
- **Проблема:** для 10k сущностей × 5 полей → 50k Activator-вызовов на
  snapshot и 50k на restore. Binding — тонкий wrapper над `raw` полем, но
  аллокация всё равно давит GC.
- **Решение (compile-once delegates):** в `PersistedFieldBindingFactory`
  добавить `ConcurrentDictionary<(PersistedFieldKind, Type, Type), Func<object, PersistedFieldBinding>>`.
  При первом обращении строить делегат через
  `Expression.Lambda<Func<object, PersistedFieldBinding>>(Expression.New(ctor, cast))`
  — закешировать. ~5–10× быстрее Activator, без boxing параметров.
- **Severity:** MEDIUM
- **Effort:** ~40 строк кода, минимальный риск — тесты покрывают round-trip
  снапшота.

### 4.1b [FUTURE WORK] Убрать binding-объект полностью
Перенесено в `FUTURE_WORK.md`. Триггер — профайлер на реальной игре
покажет >1ms на snapshot / restore.

### 4.2 `PersistedKeyRegistry.TryResolve` fallback-скан по сборкам на каждый miss
- **File:** `Runtime/Scanner/PersistedKeyRegistry.cs:104-112`
- **Анализ:** negative miss кешируется (line 114), positive hit после первого
  запроса тоже кешируется (line 110). Значит скан идёт только при первом
  обращении к каждому key. **Не баг, просто медленно на cold start.**
- **Fix:** В `GetOrBuildReverseIndex` одновременно регистрировать `Type.FullName`
  всех `IEntityAspect`-типов — убирает fallback-loop. Код короче и чище,
  заодно упрощает 4.3.
- **Severity:** LOW

### 4.3 `RestoreAll` аллоцирует `HashSet<IEntity>` даже при `Ignore`
- **File:** `Runtime/Extensions/WorldPersistenceExtensions.cs:126`
- **Проблема:** `var restored = new HashSet<IEntity>()` и `restored.Add(entity)`
  в цикле делаются всегда, но используются только в ветке `DisposeMissing`.
  Для типичного use-case (Ignore) — мусор.
- **Severity:** LOW
- **Fix:** `HashSet<IEntity> restored = options.Missing == MissingEntityPolicy.DisposeMissing ? new HashSet<IEntity>() : null;`
  + `restored?.Add(entity)`.

### 4.4 Collection bindings аллоцируют `List` / `HashSet` / `Dictionary` на каждый `ReadValue`
By-design (detachable POCO). Оставляем.

---

## Batch 5 — Attribute / Key Validation + Enum Support (MEDIUM)

### 5.1 `[PersistedKey]` и `[PersistedAlias]` не trim'ят whitespace
- **File:** `PersistedKeyAttribute.cs:21-23`, `PersistedAliasAttribute.cs:18-20`
- **Проблема:** `[PersistedKey("  hero  ")]` принимается, но никогда не совпадёт
  с `"hero"` в lookup. Silent bug.
- **Severity:** LOW
- **Fix:** `key = (key ?? "").Trim(); if (string.IsNullOrEmpty(key)) throw ...`.

### 5.2 Collision detection логируется один раз глобально
- **File:** `PersistedKeyRegistry.cs:25, :163-168`
- **Проблема:** `_reverseIndexErrored` — статический флаг. Первая коллизия
  логируется, все последующие — нет. Хотим видеть все.
- **Severity:** LOW
- **Fix:** Убрать `_reverseIndexErrored` — коллизии редкие и важные, все должны
  быть видны.

### 5.3 Enum в `[PersistedState]` — explicit opt-in через `[PersistedEnum]`
- **File:** `Runtime/Scanner/PersistenceScanner.cs:153-156` (IsAllowedValueType)
- **Проблема:** сейчас enums тихо принимаются через `IsValueType`. Если
  пользователь переименует enum value или добавит новый перед существующими
  — старый сейв сломается тихо (int value «смещён»), что очень болезненно
  для save-файлов.
- **Решение (новая утилита пакета):**

  Новые артефакты:
  ```
  Runtime/Attributes/PersistedEnumAttribute.cs       // [PersistedEnum] field-level marker
  Runtime/Bindings/PersistedEnumBinding.cs           // ReactiveProperty<TEnum> binding
  Runtime/Bindings/PersistedEnumMode.cs              // ByName (default) | ByValue
  ```

  ```csharp
  [AttributeUsage(AttributeTargets.Field, Inherited = false)]
  public sealed class PersistedEnumAttribute : Attribute
  {
      public PersistedEnumMode Mode { get; }
      public PersistedEnumAttribute(PersistedEnumMode mode = PersistedEnumMode.ByName) { Mode = mode; }
  }

  public enum PersistedEnumMode
  {
      // Default. Snapshot хранит enum как string (member name) — устойчиво
      // к перенумерации, только rename ломает (тогда migrator обязателен).
      ByName = 0,
      // Snapshot хранит enum как underlying int — компактнее, но reorder = поломка.
      ByValue = 1,
  }
  ```

  Scanner:
  - Default: `IsAllowedValueType(t)` возвращает
    `t.IsValueType && !t.IsEnum || t == typeof(string)`.
  - Если field — `ReactiveProperty<TEnum>` И имеет `[PersistedEnum]` →
    классификация как специальный `PersistedFieldKind.Enum`, хранится
    `PersistedFieldInfo` с ValueType = TEnum и Mode.
  - Без `[PersistedEnum]` и с enum-типом → scanner логирует конкретную ошибку:
    «Enum 'MyEnum' in field 'X' requires [PersistedEnum] — decide ByName
    (default, safe for reorder) or ByValue (compact). Field skipped».

  Binding (`PersistedEnumBinding<TEnum>`):
  - ByName: `ReadValue()` → `TEnum.ToString()`; `WriteValue(string)` →
    `Enum.Parse<TEnum>(...)` + LogWarning если имя не найдено (value
    останется default).
  - ByValue: `ReadValue()` → `Convert.ToInt64(enumValue)`; `WriteValue` →
    `(TEnum)Enum.ToObject(typeof(TEnum), (long)value)` + LogWarning если
    значение не определено в enum (Enum.IsDefined check).

  (Только `ReactiveProperty<TEnum>` для первой итерации; коллекции enum —
  TODO, добавим если попросят.)

- **Severity:** MEDIUM
- **Effort:** ~60–80 строк нового кода + 3–4 теста. README section.

### 5.4 `PersistenceScanner.TryClassify` error message для не-generic type
Оставляем — текущее сообщение достаточно информативное.

### 5.5 `Nullable<T>` — задокументировать round-trip
- **File:** `Runtime/Scanner/PersistenceScanner.cs:155`, README
- `ReactiveProperty<int?>` проходит `IsAllowedValueType`. Поведение зависит
  от сериализатора.
- **Fix:** Добавить test + упоминание в README («Nullable<T> forwards to the
  underlying serializer; nulls survive as long as the serializer preserves them»).

---

## Batch 6 — Thread Safety (senior approach)

### Сценарии использования потоков в persistence-пайплайне

1. **Main-thread autosave (самый частый):** `World.SnapshotAll()` →
   `JsonUtility.ToJson()` → `File.WriteAllBytes()`. Всё на main thread.
   Без проблем.
2. **Background serialization:** `SnapshotAll()` на main thread (собирает
   POCO), потом `Task.Run(() => serializer.Serialize(snap))` на background
   + `File.WriteAllBytesAsync`. POCO безопасно ходит между потоками после
   создания (не мутируется). Здесь `SnapshotAll` на main thread → статические
   кеши пишутся в один поток, safe.
3. **Background preload + main-thread restore:** `File.ReadAllBytesAsync` →
   десериализация на background → возврат на main thread → `RestoreAll()`.
   Безопасно: restore всегда main thread.
4. **Параллельные SnapshotAll из разных миров (real-world редкий кейс):**
   procedurally generated rooms / headless simulation на разных тредах.
   Два `Snapshot()`-вызова одновременно поднимают `PersistenceScanner.Cache`
   / `PersistedKeyRegistry` → **race condition на write в `Dictionary<K,V>`**.

### Pros & cons вариантов

| Вариант | Pros | Cons |
|---|---|---|
| Main-thread only + docs | Ноль overhead, простота, консистентно с UnityEngine API. | Сценарии 2/3 формально unsafe (хотя на практике safe, т.к. cache — write-once); сценарий 4 ломается. |
| `ConcurrentDictionary` везде | Все сценарии безопасны, lookup-cost ≈ Dictionary (lock-free read paths). | +минимальная память/аллокация при `TryAdd`; минимальный рост кода. |
| `Lazy<T>` + `ConcurrentDictionary` | То же + явная singleton-инициализация reverse-index (одновременные первые вызовы не делают двойной assembly-scan). | Чуть больше кода. |

### Решение: «senior choice» — ConcurrentDictionary + Lazy для однократного build

Write-once read-many кеши переводим на `ConcurrentDictionary`:
- `PersistenceScanner.Cache` → `ConcurrentDictionary<Type, PersistedFieldInfo[]>`
- `PersistedKeyRegistry.KeyByType` → `ConcurrentDictionary<Type, string>`
- `PersistedKeyRegistry.VersionByType` → `ConcurrentDictionary<Type, int>`
- `EntityPersistenceExtensions.RequireMethods` → `ConcurrentDictionary<Type, MethodInfo>`
- `PersistedFieldBindingFactory` compiled-delegate кеш (Batch 4.1) →
  `ConcurrentDictionary<(Kind,Type,Type), Func<...>>`

Reverse-index получает отдельный `Lazy<Dictionary<string, Type>>` с
`LazyThreadSafetyMode.ExecutionAndPublication` — гарантирует что при
одновременных первых вызовах assembly-scan выполнится ровно раз.
Negative/positive caching для fallback работает через отдельный
`ConcurrentDictionary` над Lazy-built базой.

`PersistenceMigrationRegistry` **остаётся instance-level, без локов**, но
docstring явно говорит: «register all migrators on bootstrap (main thread)
before calling any Restore; reads during Restore are lock-free and safe
from any thread after registration is done». Это стандартный pattern
«build once, read many».

Rationale: ConcurrentDictionary в .NET Standard 2.1 реализует практически
lock-free reads; overhead vs обычного Dictionary в benchmark-тестах <5% на
lookup, а write path редкий (один раз за жизнь scan'а). Для write-once
cache это абсолютно правильный инструмент.

- **Severity:** MEDIUM (defence-in-depth для async save/load).
- **Effort:** механическая замена типов + `Lazy<>` wrapper для reverse-index;
  ~30 строк изменений, нулевой API impact.

---

## Batch 7 — Missing Tests (MEDIUM effort)

Формулировки в стиле `Method_Scenario_ExpectedBehavior`:

### 7.1 `Restore_AspectMigratorThrows_LogsErrorAndSkipsAspect`
Покрывает `EntityPersistenceExtensions.cs:173-184` (catch block).

### 7.2 `Restore_NullValueIntoValueTypeReactiveProperty_LogsAndSkips`
Покрывает fix 1.2 — гарантия, что null не обрушит весь restore.

### 7.3 `Restore_TypeMismatchIntIntoFloat_LogsAndSkipsField`
Покрывает `InvalidCastException` catch в `EntityPersistenceExtensions.cs:116`.

### 7.4 `Scan_ReactivePropertyOfEnumWithoutAttribute_LogsErrorAndSkips`
Покрывает новое поведение из 5.3 (enum требует `[PersistedEnum]`).
+ `Snapshot_EnumByName_WritesMemberName` + `Restore_EnumByName_ResolvesBackToEnum`
для обоих режимов (ByName, ByValue).
+ `Restore_EnumByName_UnknownMember_LogsWarning`.

### 7.5 `Scan_ReactivePropertyOfNullableInt_IsAllowed`
+ round-trip с null и с value. Покрывает 5.5.

### 7.6 `Snapshot_SameStateTwice_ProducesIdenticalKeyOrder`
Гарантия детерминизма (упомянута в README, не тестируется напрямую).

### 7.7 `RestoreAll_SnapshotMigratorThrowsMidChain_FormatVersionReflectsPartialProgress`
Покрывает fix 2.1 — проверяем что FormatVersion продвинулся до точки падения.

### 7.8 `RestoreAll_ResolveOrSpawnReturnsWorld_LogsErrorAndSkips`
Покрывает fix 2.2.

### 7.9 `RestoreAll_CustomDisposeMissingCallback_InvokedForEachMissingEntity`
Покрывает `WorldRestoreOptions.DisposeMissing` override (упомянут в README,
не тестируется).

### 7.10 `Restore_CollectionBindingRestoresWithNullValue_ClearsCollection`
Проверка, что `WriteValue(null)` = `Clear()` без добавления.

### 7.11 `Restore_HashSetAndDictionaryEvents_FireOnRestore`
R3 observers на `ObservableHashSet.ObserveAdd()` /
`ObservableDictionary.ObserveAdd()`. Сейчас покрыт только `ObservableList`.

---

## Batch 8 — Validate + Debug API

### 8.1 `PersistenceDebug.ValidateAspect`
Публичный метод fail-fast для bootstrap-тайма:
```csharp
public static class PersistenceDebug
{
    // Бросает первый же конкретный эксепшн при обнаружении проблемы
    // (null field, unsupported type, enum without [PersistedEnum], и т.д.)
    public static void ValidateAspect(Type aspectType);
    public static void ValidateAspect<T>() where T : IEntityAspect, new();

    // Сканит assembly, валидирует все IEntityAspect-типы разом — для одного
    // вызова на bootstrap: «убеди меня что все мои [PersistedState] поля ок».
    public static IReadOnlyList<string> ValidateAllAspects(Assembly assembly = null);
}
```
Внутри использует тот же сканер, что и `Scan()`, но собирает ошибки в строки
вместо `Debug.LogError`.

### 8.2 `PersistenceDebug` — диагностическая поверхность
- `ListPersistedKeys()` → `IReadOnlyList<(string Key, Type Type, int Version, string[] Aliases)>`
  — дамп reverse-индекса.
- `FindKeyCollisions()` → `IReadOnlyList<(string Key, Type[] Claimants)>` —
  поиск коллизий без ожидания первого restore.
- `GetCacheStats()` → `(int ScannedTypes, int TotalFields, int ReverseIndexSize)`
  — sanity check в dev-билдах.
- `DumpAspect(Type)` → человекочитаемый вывод полей аспекта с
  ValueType/KeyType/Kind для inspector-хуков.

### 8.3 Polymorphic aspects — документационное non-feature
Явно упомянуть в README: «Aspect keys are CLR-type-specific. Derived aspect
with `[PersistedKey]` отличным от базового — другой сейв. Polymorphism is
not supported by design.»

- **Severity:** MEDIUM (add-only API, обратно-совместимо).
- **Effort:** ~80–100 строк + 4–6 тестов + README section.

---

## План реализации (после approve issues.md)

Порядок батчей подобран так, чтобы каждый PR был маленьким, тестировался
независимо, и не блокировал следующий.

1. **PR-1 (Batch 1)** — Null/uninitialized field robustness. Fixes 1.1/1.2/1.3
   + tests 7.2, 7.10.
2. **PR-2 (Batch 2)** — Migration consistency. Fixes 2.1/2.2/2.3 + tests 7.7, 7.8.
3. **PR-3 (Batch 3 + 5.1/5.2)** — Docs + attribute trimming + collision logging.
   Тривиальные правки.
4. **PR-4 (Batch 5.3 enum support)** — `[PersistedEnum]` + `PersistedEnumBinding`
   + tests 7.4.
5. **PR-5 (Batch 6 thread safety)** — ConcurrentDictionary migration +
   `Lazy<>` reverse-index.
6. **PR-6 (Batch 4.1 + 4.2 + 4.3)** — Compiled delegates, fallback-loop cleanup,
   lazy HashSet. Создаём `FUTURE_WORK.md` с описанием 4.1b.
7. **PR-7 (Batch 8)** — `PersistenceDebug` API + tests.
8. **PR-8 (Batch 7 оставшиеся)** — Tests 7.1, 7.3, 7.5, 7.6, 7.9, 7.11.

Параллелизация: PR-3 независим, можно влить первым. PR-1 и PR-2 независимы
друг от друга. PR-4 стоит после PR-1 (null-защита упрощает enum binding).
PR-5 независим. PR-6 желательно после PR-5 (один кеш-формат). PR-7 независим.

---

## Verification

- После каждого PR: `Unity Editor → Test Runner → ACS.Tests.Persistence` —
  все тесты зелёные, не должно быть регрессий в `ACS.Tests`.
- Smoke test: в `unity-project-pckgs/` создать `PlayerAspect` с
  `[PersistedState]` всех поддерживаемых типов (+ enum с `[PersistedEnum]`),
  snapshot → `JsonUtility.ToJson` → десериализация → restore → сверка значений.
- Для Batch 6: добавить stress-test NUnit с `Parallel.For` по 1000 итерациям
  `Snapshot()/Restore()` на разных потоках — не должно быть исключений.

## Критические файлы

| Файл | Батчи |
|---|---|
| `Runtime/Bindings/PersistedFieldBindingFactory.cs` | 1.1, 4.1 |
| `Runtime/Bindings/PersistedReactiveBinding.cs` | 1.2 |
| `Runtime/Bindings/Persisted{List,HashSet,Dictionary}Binding.cs` | 1.3 |
| `Runtime/Bindings/PersistedEnumBinding.cs` (новый) | 5.3 |
| `Runtime/Attributes/PersistedEnumAttribute.cs` (новый) | 5.3 |
| `Runtime/Extensions/WorldPersistenceExtensions.cs` | 2.1, 2.2, 4.3 |
| `Runtime/Extensions/EntityPersistenceExtensions.cs` | 2.3, 1.2 catch, 6 |
| `Runtime/Snapshot/AspectSnapshot.cs` | 3.1 |
| `Runtime/Scanner/PersistenceScanner.cs` | 5.3, 6 |
| `Runtime/Scanner/PersistedKeyRegistry.cs` | 5.2, 4.2, 6 |
| `Runtime/Scanner/PersistedFieldInfo.cs` | 3.2, 5.3 (add `Mode`) |
| `Runtime/Attributes/PersistedKeyAttribute.cs`, `PersistedAliasAttribute.cs` | 5.1 |
| `Runtime/Attributes/PersistedStateAttribute.cs` | 3.3 |
| `Runtime/Debug/PersistenceDebug.cs` (новый) | 8.1, 8.2 |
| `README.md` | 3, 5.5, 8.3, enum section |
| `Tests/*.cs` | Batch 7 |
