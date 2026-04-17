# Utils Package — Issues & Work Plan

Результаты аудита пакета `com.rubickanov.utils`. Документ отслеживает все найденные проблемы и порядок их исправления. Ломающие изменения публичного API допустимы — делаем правильно.

**Зафиксированные решения по ключевым неопределённостям:**
- **C2 (индексы `CircularBuffer`):** сменить сигнатуру `Add`/`Get`/`Capacity` на `uint` — отрицательные индексы невозможны на уровне типа, `uint`-арифметика корректно wrap'ит при underflow. Breaking-change, но внешних консьюмеров у `CircularBuffer` в репо нет.
- **M3 (`DeterministicRandom.Int` invalid range):** throw `ArgumentException` при `maxExclusive <= min`. Строгий контракт на границе API — тихое возвращение `min` в netcode-симуляции = прямой источник десинка.

---

## Находки

### Критические (реальные баги)

- **C1. `using UnityEditor;` в Runtime-файле не обёрнут в `#if UNITY_EDITOR` — пакет не собирается в Player-билде** — `Runtime/Unity/ApplicationExtensions.cs:1`
  `Utils.Runtime.asmdef` имеет `includePlatforms: []` — компилируется и под Player. В Player-сборке `UnityEditor`-assembly недоступен, `using UnityEditor;` даёт CS0246 на всю единицу компиляции. Тело `Quit()` корректно гарджено через `#if UNITY_EDITOR`, но сам `using` — нет. Сиблинг `com.rubickanov.devconsole/Runtime/Commands/SystemCommands.cs:2-4` делает это правильно.
  **Решение:**
  ```csharp
  #if UNITY_EDITOR
  using UnityEditor;
  #endif
  ```

- **C2. `CircularBuffer.Get/Add` ломается на отрицательном `int`-индексе** — `Runtime/CircularBuffer.cs:15-16`
  В C# `-1 % 4 == -1` (остаток сохраняет знак делимого), поэтому `_buffer[-1]` кидает `IndexOutOfRangeException`. README (`README.md:66-67`) напрямую рекомендует паттерн `buffer.Get(tick - 10)`, который даёт отрицательный индекс при `tick < 10`. Любой lookback в первые 10 тиков = краш.
  **Решение (зафиксировано):** сменить сигнатуру типов на `uint` — отрицательные индексы становятся невозможны, `uint`-арифметика `tickU - 10u` при underflow даёт большое значение, которое по modulo-операции попадает в валидный слот (lookback в ring-buffer работает "бесплатно"). Заодно валидация capacity (M2), `Capacity` property (m3), `Array.Clear` вместо new-array (m1):
  ```csharp
  public sealed class CircularBuffer<T>
  {
      private readonly T[] _buffer;
      private readonly uint _capacity;

      public uint Capacity => _capacity;

      public CircularBuffer(uint capacity)
      {
          if (capacity == 0)
              throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
          _capacity = capacity;
          _buffer = new T[capacity];
      }

      public void Add(T item, uint index) => _buffer[index % _capacity] = item;
      public T Get(uint index) => _buffer[index % _capacity];
      public void Clear() => Array.Clear(_buffer, 0, (int)_capacity);
  }
  ```
  README-пример обновляется — `uint tick; buffer.Get(tick - 10u)` с коротким комментарием про wrap.

- **C3. `ObjectPool.Release(instance)` не отменяет отложенный release того же инстанса → premature release после re-acquire** — `Runtime/Unity/ObjectPool.cs:121-131, 173-228`
  Сценарий: `Release(inst, delay: 2f)` планирует запись в `PoolTimerRunner._pending`. До истечения delay вызывается немедленный `Release(inst)` → инстанс возвращён в пул. Следующий `Get()` может выдать тот же инстанс (Unity `ObjectPool` — LIFO). Через 2 секунды таймер просыпается и делает `_releaseCallback(inst)` → уже активный инстанс молча уходит обратно в пул. `PoolTimerRunner.Cancel(Component)` (`ObjectPool.cs:198-211`) существует, но нигде не вызывается — мёртвый код. Результат: в пулах с delayed-release (VFX, декали) эффекты "моргают" и исчезают без ошибок.
  **Решение:** в `Release(T instance)` вызвать `_timerRunner.Cancel` перед `_pool.Release`:
  ```csharp
  public void Release(T instance)
  {
      if (!_active.Remove(instance)) return;
      _timerRunner.Cancel(instance);
      _onRelease?.Invoke(instance);
      _pool.Release(instance);
  }
  ```
  В `ReleaseAll` уже стоит `_timerRunner.CancelAll()` — это OK.

### Мажорные (M)

- **M1. `ObjectPool` не валидирует конструктор** — `Runtime/Unity/ObjectPool.cs:57-69`
  `_container = new GameObject($"Pool [{prefab.name}]").transform;` — NRE при `prefab == null`, ошибка в интерполяции `.name`, а не на границе API. Отрицательный `prewarm` / `maxSize <= 0` тоже не отлавливаются.
  **Решение:**
  ```csharp
  if (prefab == null) throw new ArgumentNullException(nameof(prefab));
  if (prewarm < 0) throw new ArgumentOutOfRangeException(nameof(prewarm));
  if (maxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
  ```

- **M2. `CircularBuffer(0)` создаёт массив длины 0 → первый `Add/Get` = DivideByZero** — `Runtime/CircularBuffer.cs:9-13`
  После C2 (переход на `uint`) отрицательный capacity невозможен, но нулевой — всё ещё валидный с точки зрения компилятора и ломает инвариант ring-buffer'а.
  **Решение:** входит в снипет C2 — `if (capacity == 0) throw new ArgumentOutOfRangeException(...)`. Фиксируется одним коммитом вместе с C2.

- **M3. `DeterministicRandom.Int` кидает DivideByZero / возвращает мусор при некорректном диапазоне** — `Runtime/DeterministicRandom.cs:66-75`
  `Int(a, b, 5, 5)` → `(uint)(5 - 5) == 0` → `hash % 0` = `DivideByZeroException`. `Int(a, b, 10, 5)` → `(uint)(5 - 10) == 4294967291u` (underflow) → `hash % 4294967291 + 10` = почти случайное большое число, никаких исключений. Метод тихо возвращает мусор и ломает детерминизм. Тест `Int_AlwaysInBounds` эти ветки не покрывает. Аналогично `Int(a, b, c, min, maxExclusive)` (строки 71-74).
  **Решение (зафиксировано):** throw `ArgumentException` при `maxExclusive <= min` в обеих перегрузках:
  ```csharp
  public static int Int(uint a, uint b, int min, int maxExclusive)
  {
      if (maxExclusive <= min)
          throw new ArgumentException(
              $"maxExclusive ({maxExclusive}) must be greater than min ({min}).",
              nameof(maxExclusive));
      return min + (int)(Hash(a, b) % (uint)(maxExclusive - min));
  }
  ```

- **M4. `ObjectPool.Dispose` / `EvictingPool.Dispose` не идемпотентны, нет disposed-guard на публичных методах** — `Runtime/Unity/ObjectPool.cs:152-170`, `Runtime/Unity/EvictingPool.cs:94-99`
  Второй `Dispose()` → `_pool.Dispose()` (Unity `ObjectPool<T>.Dispose` кидает на повторе) + `Object.Destroy(_container.gameObject)` на уже уничтоженном объекте (Unity логирует warning). После `Dispose` вызов `Get/Release/ReleaseAll` идёт в disposed inner-pool, получает невнятный `InvalidOperationException` вместо стандартного `ObjectDisposedException`.
  **Решение:**
  ```csharp
  private bool _disposed;

  private void ThrowIfDisposed()
  {
      if (_disposed) throw new ObjectDisposedException(nameof(ObjectPool<T>));
  }

  public void Dispose()
  {
      if (_disposed) return;
      _disposed = true;
      // ... существующий cleanup
  }
  ```
  Во всех `Get/Release/ReleaseAll` — `ThrowIfDisposed()` первой строкой. Аналогично в `EvictingPool`.

- **M5. `ObjectPool`, `EvictingPool`, `CircularBuffer`, `DescriptionAttribute` не `sealed`** — `Runtime/Unity/ObjectPool.cs:30`, `Runtime/Unity/EvictingPool.cs:17`, `Runtime/CircularBuffer.cs:4`, `Runtime/Attributes/DescriptionAttribute.cs:9`
  Классы не спроектированы под наследование (приватные поля, нет virtual-членов). `Attribute`-наследники по соглашению `sealed` (CA1813). В других аудитах репо (`localization` m3) `sealed` применяется единообразно.
  **Решение:** `public sealed class` всем четырём. Брейкинг только для тех, кто наследовался, — таких в репо нет (`grep` по пакетам).

- **M6. Нет тестов для `ObjectPool`, `EvictingPool`, `DescriptionAttribute`, `ApplicationExtensions.Quit` — самый рискованный код в пакете не покрыт** — `Tests/Editor/`
  Покрыты только `CircularBuffer` и `DeterministicRandom` (хэш/диапазоны). `ObjectPool` с жизненным циклом `GameObject`, delayed release, `Dispose` — ноль тестов. Регрессии C3 и M4 ловятся только в runtime игры.
  **Решение:** минимум-сценарии — оформить как `[UnityTest]` (PlayMode через корутины) в существующем `Tests/Editor/` или в новом `Tests/Runtime/` (оба требуют `includePlatforms: [Editor]` + `UNITY_INCLUDE_TESTS`):
  - `Release_ImmediateAfterDelayed_CancelsTimer` — регрессия C3: `pool.Release(fx, delay:1f); pool.Release(fx); pool.Get(); yield return new WaitForSeconds(1.5f); Assert.True(active).`
  - `Dispose_Twice_DoesNotThrow` — регрессия M4.
  - `Get_AfterDispose_ThrowsObjectDisposed` — регрессия M4.
  - `Constructor_NullPrefab_Throws` — регрессия M1.
  - `EvictingPool_FullCapacity_EvictsOldestFIFO` — текущая логика без тестов.
  - `EvictingPool_ReleaseDuringFadeOut_DoesNotDoubleRelease` — инвариант `onEvict`-коллбэка.
  - `DescriptionAttribute_ReadViaReflection_ReturnsText` — reflection, EditMode.

- **M7. `ComponentDescriptionEditor` аллоцирует `GUIStyle` каждый `OnInspectorGUI`** — `Editor/ComponentDescriptionEditor.cs:28-34`
  `new GUIStyle(EditorStyles.miniLabel) { ... }` внутри `OnInspectorGUI` → аллокация каждый кадр отрисовки инспектора (десятки кадров/сек при активном окне).
  **Решение:** ленивая инициализация в `static` поле. `EditorStyles.miniLabel` недоступен в статическом инициализаторе (`s_Current is null`), поэтому lazy:
  ```csharp
  private static GUIStyle? _style;

  private static GUIStyle GetStyle() => _style ??= new GUIStyle(EditorStyles.miniLabel)
  {
      wordWrap = true,
      fontStyle = FontStyle.Italic,
      normal = { textColor = new Color(1f, 1f, 1f, 0.35f) }
  };
  ```

- **M8. `EvictingPool` не имеет `ReleaseAll()` — inconsistent с `ObjectPool`** — `Runtime/Unity/EvictingPool.cs:17-113`
  `ObjectPool.ReleaseAll()` есть, у `EvictingPool` — нет. Пользователь, желающий массово сбросить все декали при смене уровня, должен либо `Dispose + new`, либо перебирать по внешним коллекциям — ключей для `Release(item)` у него нет.
  **Решение:**
  ```csharp
  /// <summary>
  /// Returns all active items to the pool immediately.
  /// Bypasses the onEvict callback — intended for scene teardown / bulk clear.
  /// </summary>
  public void ReleaseAll()
  {
      foreach (var kvp in _nodeMap)
          _pool.Release(kvp.Key);
      _active.Clear();
      _nodeMap.Clear();
  }
  ```

### Минорные (m)

- **m1. `CircularBuffer.Clear` аллоцирует новый массив** — `Runtime/CircularBuffer.cs:17`
  `_buffer = new T[_capacity]` — каждый `Clear` выделяет память + работа GC. `Array.Clear` обнулит слоты без аллокаций (reference → null, value → default — эквивалентно). Тест `ReferenceType_ReturnsNullAfterClear` (`CircularBufferTests.cs:77-85`) продолжит проходить.
  **Решение:** входит в снипет C2 — `public void Clear() => Array.Clear(_buffer, 0, (int)_capacity);` + `_buffer` становится `readonly`.

- **m2. `CircularBuffer.Capacity` property отсутствует** — `Runtime/CircularBuffer.cs:4-18`
  Пользователь, пишущий `for (uint i = 0; i < buffer.Capacity; i++)`, должен хранить копию capacity сам.
  **Решение:** входит в снипет C2 (`public uint Capacity => _capacity;`).

- **m3. `CircularBuffer` без XML-doc на публичных методах** — `Runtime/CircularBuffer.cs:9-17`
  Есть только `<summary>` на классе. `Add(item, index)` — неочевидно, что `index` — это не позиция в массиве, а ключ (тик), который wrap'ится.
  **Решение:** XML-doc на конструктор, `Add`, `Get`, `Clear`, `Capacity` — явно указать modulo-wrap семантику и `uint`-underflow trick для lookback.

- **m4. Нет 3-ключевых перегрузок `Bool`/`Sign`** — `Runtime/DeterministicRandom.cs:78-88`
  `Hash/Float01/Range/Int` имеют версии на 2+3 ключа, `Bool`/`Sign` — только 2-ключевые. Пользователю, желающему `Bool(entityId, actionId, tick)`, приходится обходиться через `Bool(Hash(a, b), c)`.
  **Решение:** добавить перегрузки:
  ```csharp
  public static bool Bool(uint a, uint b, uint c) => (Hash(a, b, c) & 1u) == 1u;
  public static float Sign(uint a, uint b, uint c) => Bool(a, b, c) ? 1f : -1f;
  ```

- **m5. XML-doc `Int` говорит `[min, max)` и называет параметр `maxExclusive`; `Range` — просто `max`** — `Runtime/DeterministicRandom.cs:54-75`
  Несогласованность. `Range(min, max)` c полуоткрытым диапазоном читается как `max`-inclusive без явной документации.
  **Решение:** унифицировать — переименовать `Range` параметр в `maxExclusive`, явно сказать `[min, maxExclusive)` в XML-doc'ах.

- **m6. Несогласованный `#nullable enable`** — `Runtime/Unity/{ObjectPool,EvictingPool}.cs`, `Editor/ComponentDescriptionEditor.cs:1`
  В `Editor/ComponentDescriptionEditor.cs` директива есть. В `Runtime/Unity/*.cs` используются `T?` / `Action<T>?` / `Transform?` без директивы — работает только при project-level nullable-context (его в этом репо нет). В `Runtime/CircularBuffer.cs`, `Runtime/DeterministicRandom.cs` — без `?`, без директивы.
  **Решение:** `#nullable enable` первой строкой в каждый `Runtime/**/*.cs` и `Editor/**/*.cs`. Файлы без reference-типов (`DeterministicRandom`) не пострадают.

- **m7. `PoolTimerRunner.Cancel` — dead code до фикса C3** — `Runtime/Unity/ObjectPool.cs:198-211`
  Метод существует и корректен, но нигде не вызывается. После C3 становится частью `Release(T)`. Упоминание ради cross-reference — отдельного коммита не требует.

- **m8. `Release(instance, delay)` не дедуплицирует по инстансу** — `Runtime/Unity/ObjectPool.cs:128-131, 187-194`
  `Release(inst, 1f); Release(inst, 2f);` — в `_pending` две записи. Первый таймер сработает корректно, второй попадёт на `!_active.Remove` (идемпотентный guard) и выйдет. Не баг — расход памяти и CPU.
  **Решение:** в `Schedule` обновлять существующую запись перед добавлением (O(n), `n` обычно < 64):
  ```csharp
  public void Schedule(Component instance, float delay)
  {
      float releaseTime = Time.time + delay;
      for (int i = 0; i < _pending.Count; i++)
      {
          if (ReferenceEquals(_pending[i].Instance, instance))
          {
              _pending[i] = new PendingRelease { Instance = instance, ReleaseTime = releaseTime };
              return;
          }
      }
      _pending.Add(new PendingRelease { Instance = instance, ReleaseTime = releaseTime });
  }
  ```

- **m9. README: `Tests/Editor/` выглядит пустым в File Structure** — `README.md:150-167`
  Раздел File Structure показывает `Tests/` → `Editor/` без перечисления тест-файлов. Либо перечислить `CircularBufferTests.cs` / `DeterministicRandomTests.cs`, либо убрать вложение (шаблон допускает опускать глубокие уровни).
  **Решение:** после добавления тестов в M6 обновить секцию с учётом нового layout (возможно `Tests/Runtime/` появится рядом с `Tests/Editor/`). Если останется только `Editor/` — просто не вложить, показав `└── Tests/`.

---

## Batches

### Batch 1 — Build-fix (C1)
Один `#if UNITY_EDITOR` вокруг `using UnityEditor;` в `ApplicationExtensions.cs`. Разблокирует Player-билд. Отдельный коммит — минимум риска.

### Batch 2 — `CircularBuffer` (C2, M2, m1, m2, m3)
Один файл, все правки связаны решением C2. Breaking: `int → uint`, валидация capacity в конструкторе, `Capacity` property, `Array.Clear`, XML-doc на публичные члены. Обновить `CircularBufferTests.cs` под `uint`-API + добавить тесты на:
- Конструктор с `capacity == 0` → `ArgumentOutOfRangeException`.
- `Add/Get` с `uint`-underflow (`tick < lookback`) wrap'ится корректно.
- `Capacity` возвращает переданное значение.
Обновить README-пример под `uint tick`.

### Batch 3 — `DeterministicRandom` (M3, m4, m5)
Один файл, валидация диапазона `Int` + throw в обеих перегрузках, 3-ключ `Bool`/`Sign`, унификация naming. Обновить `DeterministicRandomTests.cs`:
- `Int_MaxEqualsMin_Throws` и `Int_MaxLessThanMin_Throws` (регрессия M3).
- `Bool3`/`Sign3` детерминизм и разбиение (аналогично уже имеющимся 2-ключ тестам).

### Batch 4 — `ObjectPool` / `EvictingPool` correctness (C3, M1, M4, M8, m7, m8)
Два связанных файла. Порядок правок внутри:
1. `ObjectPool.Release(T)` → вызов `_timerRunner.Cancel` (C3).
2. `ObjectPool` конструктор валидация (M1).
3. `PoolTimerRunner.Schedule` дедупликация (m8).
4. `ObjectPool` / `EvictingPool` disposed-флаг + `ThrowIfDisposed` + идемпотентный `Dispose` (M4).
5. `EvictingPool.ReleaseAll` (M8) — с XML-doc про `onEvict`-bypass.
m7 (mention только) не требует правок.

### Batch 5 — `sealed` + `#nullable enable` (M5, m6)
Косметика через пакет, безопасно после Batch 1-4:
- `public sealed class` для `CircularBuffer`, `ObjectPool`, `EvictingPool`, `DescriptionAttribute`.
- `#nullable enable` первой строкой во всех `Runtime/**/*.cs` и `Editor/**/*.cs` (где ещё нет).

### Batch 6 — Editor polish (M7)
`ComponentDescriptionEditor.cs` — lazy-cached `GUIStyle`. Один файл.

### Batch 7 — Tests (M6)
Новые `[UnityTest]` для `ObjectPool`/`EvictingPool` (PlayMode-корутины), EditMode для `DescriptionAttribute`. Конкретные кейсы — см. M6. Может быть в существующем `Tests/Editor/` asmdef (если оставить `[UnityTest]`-based) или в новом `Tests/Runtime/`. Рекомендую оставить в `Tests/Editor/` — один asmdef проще.

### Batch 8 — README (m9)
После появления новых тестов обновить `File Structure` и секцию `Tests`, перепроверить, что `Quick Start` / `Usage` примеры компилируются под новый API (`uint tick` для `CircularBuffer`, `ArgumentException` на неверный `Int`-диапазон упомянуть в Usage если уместно).

---

## Статус

План зафиксирован. Начинать с **Batch 1** (тривиально, критично для Player-билда). Далее 2 → 3 → 4 → 5 → 6 → 7 → 8.

Верификация каждого батча: `unity-project-pckgs` открывается без ошибок компиляции; после Batch 4 — smoke-тест сцены с `ObjectPool` и `Release(inst, 2f) + Release(inst) + Get()` последовательностью; после Batch 7 — зелёный Test Runner на всех EditMode- и PlayMode-тестах.
