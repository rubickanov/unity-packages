# com.rubickanov.acs.persistence — Future Work

Отложенные оптимизации. Делаются, когда есть конкретный профайлер-сигнал,
а не «на всякий случай». Каждый пункт — уже решённый дизайн с триггером,
по которому можно принять решение «пора» без повторного обсуждения.

---

## 4.1b — Убрать binding-объект полностью

### Контекст

После Batch 4.1 (`PR-6`) аллокация `PersistedFieldBinding` всё ещё происходит
один раз на поле на каждый `Snapshot()` / `Restore()`. Сам binding —
лёгкий wrapper над `raw`-полем аспекта (`ReactiveProperty<T>` / `ObservableList<T>` /
…), но на большом мире (10k сущностей × 5 persisted полей = 50k объектов
на snapshot и столько же на restore) GC-давление остаётся заметным.

### Идея

Заменить полиморфный `PersistedFieldBinding` на пару делегатов прямо в
`PersistedFieldInfo`:

```csharp
public sealed class PersistedFieldInfo
{
    public string Name { get; }
    public PersistedFieldKind Kind { get; }
    public Type ValueType { get; }
    public Type KeyType { get; }

    // Новые — хранятся в cache один раз на тип аспекта.
    public Func<object, object> Read { get; }   // aspectInstance → detachable POCO
    public Action<object, object> Write { get; } // aspectInstance, POCO → apply
}
```

`Read` и `Write` компилируются через `Expression` один раз при первом scan'е
типа. В runtime hot path — ноль аллокаций, никакого `Activator.CreateInstance`,
никаких wrapper-объектов.

### Что это даёт

- ~2–3× быстрее `PersistedFieldBinding` на большом мире (помимо выигрыша
  от Batch 4.1 compiled delegates).
- **Нулевой garbage на snapshot/restore** вне самих detachable POCO. List /
  Dictionary / HashSet, возвращаемые на read, остаются неизбежной
  аллокацией (by-design detachable POCO).
- Упрощает иерархию binding-классов — весь полиморфизм уходит в `Expression`-код,
  а не в классы.

### Что это стоит

- Полный refactor внутренностей `Runtime/Bindings/` — `PersistedFieldBinding`
  и 4 наследника исчезают. Логика переезжает в билдеры Expression'ов внутри
  `PersistedFieldBindingFactory` (или, логичнее, переименованного
  `PersistedFieldDelegateBuilder`).
- Тесты уровня binding (если есть прямые) переписываются под новый API.
  Интеграционные тесты snapshot-round-trip остаются без изменений.
- Риск: Expression-build для `ObservableDictionary<K,V>.Add(k, v)` и
  `ObservableHashSet<T>.Add(t)` требует аккуратной работы с generic-методами
  через reflection — но это решаемо; пример есть в R3/ObservableCollections
  internals.

### Триггер

**Делаем, если профайлер на реальной игре покажет:**
- `>1ms` суммарно на `Snapshot()` / `RestoreAll()` при типичной нагрузке
  (сейв каждые N минут, ~1–10k сущностей), ИЛИ
- `>100KB GC allocations` на один вызов snapshot/restore, которые видны как
  spike в Unity Profiler GC.Alloc колонке.

Пока эти цифры не подтверждены — оптимизация преждевременна. Batch 4.1
(compiled delegates в factory) уже снимает основную часть overhead'а, и
для большинства игр её достаточно.

### Что сделать до того, как начать

1. Включить deep profiling в Unity Profiler на сцене с максимальным
   количеством persisted сущностей.
2. Запустить autosave N раз, собрать средние цифры по `Snapshot()` и
   `RestoreAll()`.
3. Сохранить `.data` профайлер-файл рядом с этим `FUTURE_WORK.md` (в
   отдельный `profiling/` подкаталог, `.gitignore`'нутый) — для baseline'а.
4. Открыть PR на базе измеренных цифр, а не «кажется, что медленно».

### Связанные изменения в public API

`PersistedFieldBinding` — `internal`, поэтому удаление не break'ает наружный
контракт. Но если пользовательский код инспектировал `PersistedFieldInfo`
через новый `PersistenceDebug` API (Batch 8) — `Read` / `Write` должны
появиться в dump'е. Это add-only, ничего не ломает.
