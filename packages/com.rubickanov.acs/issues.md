# ACS — Issues

Найденные баги, lifecycle-риски и улучшения. Сгруппированы в батчи по связности: один батч — одна единица работы (общая тема, общие тесты, один PR).

Все ссылки на файлы относительно `packages/com.rubickanov.acs/`.

---

## Batch 1 — Component lifecycle integrity

Тема: сущность/компонент попадают в сломанное состояние на Awake/Enable. Три разных симптома одного класса проблем — lifecycle-ordering в Unity adapter.

### 1.1 `EntityComponent` не перевооружает `DisposableBag` при повторном `OnEnable`

**File:** `Runtime/Unity/Behavior/EntityComponent.cs:42-50`

`OnDisable` вызывает `_disposables.Dispose()` → R3-struct переходит в disposed-состояние. `OnEnable` зовёт `OnSubscribe(ref _disposables)` который добавляет подписки в уже-мёртвую сумку → item'ы диспозятся сразу при добавлении, подписок нет.

Симптом: компонент выключили через `SetActive(false)` и включили обратно — все подписки молча перестали работать. Пулинг, toggle-иерархии, любые off/on сценарии.

**Фикс:**
```csharp
protected virtual void OnEnable()
{
    _disposables = default;
    OnSubscribe(ref _disposables);
}
```

**Регрессионный тест:** `EntityComponent_DisableThenEnable_SubscriptionsFireOnNewEvents` — Subject.OnNext после второго enable должен попасть в handler. Сейчас должен падать, после фикса — проходить.

### 1.2 `SingletonMonoEntity` оставляет аспекты дубликата в реестре мира

**File:** `Runtime/Unity/Entities/SingletonMonoEntity.cs:42-60`

При обнаружении дубликата вызывается `Destroy(gameObject)` (отложено до конца кадра). Пока дубликат жив, сиблинг-`EntityComponent` на том же GameObject может отработать Awake, через `Context.Require<T>()` создать аспект и зарегистрировать дубликат в `World._registry._index[typeof(T)]`. `OnDestroy` из-за `_destroyedAsDuplicate` пропускает `base.OnDestroy` → Unregister не зовётся → дубликат залипает в per-aspect bucket до конца сессии, `Query<T>` итерирует мёртвую ссылку.

**Фикс:** убрать early-return в `OnDestroy` для case'а дубликата и всё-таки почистить реестр. Destroyed event при этом не фаерить (дубликат для внешнего мира "не существовал"):
```csharp
protected override void OnDestroy()
{
    if (_destroyedAsDuplicate)
    {
        // Cleanup only — no Destroyed fire, no Instance clear.
        World.Current?.Unregister(this, _store.AspectTypes);
        return;
    }
    if (Instance == this) Instance = null;
    base.OnDestroy();
}
```

Альтернативно — в `Awake` при обнаружении дубликата сразу `gameObject.SetActive(false)` перед `Destroy`, чтобы сиблинги не прошли `OnEnable`. Не закрывает окно между их `Awake` и scheduled `Destroy`, но хуже ничего.

**Регрессионный тест:** `SingletonMonoEntity_DuplicateWithSiblingEntityComponent_DoesNotLeakAspectInWorld` — в сцене два MonoWorld, у дубликата сиблинг с `[Aspect]` полем, после кадра `World.Query<T>()` должен итерироваться только по живому Instance.

### 1.3 `MonoEntity` без `[DefaultExecutionOrder]` — возможна регистрация с `EntityId.None`

**File:** `Runtime/Unity/Entities/MonoEntity.cs:13`

`MonoWorld` имеет `[DefaultExecutionOrder(-1000)]`, `MonoEntity` — нет. Если `EntityComponent` на дочернем GameObject Awake-ится раньше `MonoEntity` на родителе (Unity не гарантирует порядок между GameObjects), цепочка:

1. `Context.Require<T>()` создаёт аспект и вызывает `World.Current?.Register(this, typeof(T))`.
2. `MonoEntity.Awake` ещё не исполнен → `Id == EntityId.None`.
3. `World.AspectCreated` фаерит с `entity.Id == None`.
4. Подписчики (`acs.netcode`, `acs.persistence`), индексирующие по Id, получают сломанный ключ.

**Фикс:** `[DefaultExecutionOrder(-999)]` на `MonoEntity` (после `MonoWorld`, до пользовательских компонентов).

**Регрессионный тест:** тестовый `EntityComponent` с `[DefaultExecutionOrder(-1001)]` на ребёнке, MonoEntity на родителе. Проверить что при первом `AspectCreated` `entity.Id` уже валидный.

---

## Batch 2 — World teardown integrity

Тема: мир диспозится / клирится — что видят сущности и подписчики на фазе unwind.

### 2.1 `MonoWorld.OnDestroy` снимает `World.Current` до `base.OnDestroy`

**File:** `Runtime/Unity/World/MonoWorld.cs:50-65`

Порядок сейчас: clear Current → Dispose world → base.OnDestroy → MonoEntity.OnDestroy (фаерит Destroyed, пытается Unregister на Current=null). Для самого MonoWorld не страшно — `_world.Dispose()` уже почистил реестр. Но контракт `Entity.Dispose`/`MonoEntity.OnDestroy` задокументирован как "Destroyed fires first so subscribers can still query the world while unwinding" — для любого другого MonoEntity, уничтожаемого **после** MonoWorld (порядок OnDestroy между GameObjects не гарантирован), этот контракт ломается.

**Фикс:**
```csharp
protected override void OnDestroy()
{
    if (Instance == this)
        _world.AspectCreated -= ForwardAspectCreated;

    base.OnDestroy();

    if (Instance == this)
        World.ClearCurrent(_world);
    _world.Dispose();
}
```

### 2.2 `World.Dispose()` не сбрасывает `World.Current`

**File:** `Runtime/Core/World/World.cs:279-289`

Если `world.Dispose()` вызван пока `Current == world`, статический слот остаётся занят disposed-инстансом. Следующий `World.Require<T>()` пройдёт через `GetCurrentOrThrow` (Current не null), создаст аспект на disposed-world — аспект повисает в пустоте.

**Фикс:** в `Dispose`:
```csharp
if (Current == this) ForceResetCurrent();
```

### 2.3 `Entity(world)` не защищён от disposed world

**File:** `Runtime/Core/Entities/Entity.cs:54-65, 87-102`

`Entity(World world)` кеширует `_world` в поле. Если world диспозят раньше entity, последующий `entity.Require<T>()` вызывает `_world.Register` на disposed-мире. `EntityRegistry.Register` не проверяет состояние — регистрирует в пустом словаре, аспект "живёт" в мёртвом реестре.

**Фикс:** добавить `_disposed` проверку в `World.Register` / `Register(IEntity)` / `Unregister` — бросать `ObjectDisposedException`. Это также разоблачит любой код, который диспозит world раньше сущностей.

**Регрессионный тест на весь батч:** `World_DisposeWhileCurrent_ClearsCurrentAndFutureAccessThrows`, `MonoWorld_OnDestroy_PeerMonoEntityDestroyedLater_CanStillQueryWorldFromDestroyedCallback`.

---

## Batch 3 — Cheap robustness wins

Тема: независимые точечные исправления, каждое — одна строка + один тест.

### 3.1 `EntityTickRunner` не изолирует исключения между тикерами

**File:** `Runtime/Unity/Behavior/EntityTickRunner.cs:50-62`

Одно брошенное исключение рвёт цикл → все последующие тикеры в кадре не тикают. Фрейм-просадка на всю сцену из-за одного бага в одной сущности.

**Фикс:**
```csharp
for (int i = 0; i < _scratch.Count; i++)
{
    try { _scratch[i].Tick(dt); }
    catch (Exception ex) { Debug.LogException(ex); }
}
```

### 3.2 `AspectInjector.CollectAspectFields` — лишняя аллокация

**File:** `Runtime/Core/Aspects/AspectInjector.cs:77`

`result.ToArray()` на `List<FieldInfo>` аллоцирует новый массив. Это `List<T>.ToArray` (BCL, не LINQ) — ограничение не про политику, а про лишний alloc на холодном пути.

**Фикс:**
```csharp
var array = new FieldInfo[result.Count];
result.CopyTo(array);
return array;
```

Мелочь; включена в батч чтобы одним PR закрыть все копеечные улучшения.

**Регрессионный тест:** `EntityTickRunner_ThrowingTickable_DoesNotSkipSubsequentTickables`.

---

## Batch 4 — Thread-safety decision

Тема: принять политику и применить её консистентно.

### 4.1 `AspectStore.GetOrAdd<T>` не потокобезопасен

**File:** `Runtime/Core/Entities/AspectStore.cs:26-39`

Два треда на одном `Require<T>` → каждый делает `new T()` → один перезаписывает другого → обе стороны возвращают свой инстанс, но в store живёт только один. Подписчики проигравшего инстанса пишут в отброшенный объект.

Не проблема для Unity (single-threaded). Проблема для headless-консумеров, декларируемых в README (`§ Pure Core`).

**Решение — коммит:** до появления реального headless-консумера, который упирается в это, пойти по пути документации: добавить в XML-doc `AspectStore`/`Entity`/`World` явное "not thread-safe; external synchronization required for concurrent `Require<T>` on the same instance". Реальный lock добавим когда появится реальный пользователь — преждевременная синхронизация стоит allocation'ов каждый `Require`.

Попутно — та же оговорка для `EntityRegistry.Register` / `RegisterById` (тоже не safe).

---

## Batch 5 — Breaking ergonomics (требует sign-off)

Тема: ловить класс ошибок ценой API-изменения.

### 5.1 `EntityComponent.Awake` не валидирует что derived class зовёт `base.Awake()`

**File:** `Runtime/Unity/Behavior/EntityComponent.cs:30-34`

Если наследник переопределил `Awake` и забыл `base.Awake()`, [Aspect]-инъекция не запускается, все поля `null`, первое использование — NRE. Документировано в README, но не ловится.

**Варианты:**
- (A) **Breaking:** `sealed override void Awake()` + `protected virtual void OnAwake()`. Ловит класс ошибок полностью, но мигрирует всех существующих наследников.
- (B) **Неломающий:** приватный `_awakeCalled` флаг в `base.Awake`, `Debug.LogError` в `OnEnable` если false. Не чинит NRE (он всё равно прилетит), но даёт понятное сообщение.

Решить отдельно — блокирующий вопрос перед реализацией этого батча.

---

## Batch 6 — Developer UX improvements

Тема: документированные, но неочевидные поведения, которые легко затенить.

### 6.1 `MonoEntity` не ре-регистрируется если `World.Current` появляется позже

**File:** `Runtime/Unity/Entities/MonoEntity.cs:70-88`

Сцена без MonoWorld → спавн MonoEntity → позже добавлен MonoWorld → entity не видим для `Query<T>` и `TryFindById`. Задокументировано ("If no world is set at Awake time, the entity is never retroactively registered"), но легко выстрелить в ногу.

**Варианты:**
- (A) статическое событие `World.CurrentChanged`, MonoEntity подписывается в Awake если Current=null, ре-регистрируется когда Current появляется. Неявная магия.
- (B) публичный `World.AdoptPendingEntities()` — вызывается вручную после MonoWorld.Awake. Явно, но требует глобального реестра "pending".

Решить отдельно.

---

## Batch 7 — RuntimeAspectDrawer deep audit

**File:** `Editor/RuntimeAspectDrawer.cs` (22KB)

Инспектор-пайплайн, не покрыт тестами. Потенциальные проблемы из поверхностного прохода:
- Возможный leak `SignalTracker`-подписок если drawer не диспозится (entity уничтожена, inspector-окно открыто).
- `FormatValue` создаёт строки на каждом repaint → GC-spike при листании аспектов с числовыми полями.
- `Expression.Compile` в `EnsureSubscribed` — по компилируемому делегату на каждое новое Subject-поле (кэшировать по типу поля).

Отдельный раунд аудита — неглубоко покрыто в этом проходе.

---

## Batch 8 — Docs polish

Мелкие уточнения в XML-комментариях:
- `World` реализует `IEntity` и регистрирует сам себя → `World.Query<T>()` может вернуть сам world. Упомянуть в docstring `Query<T>`.
- `EntityRef.TryResolve` с id из другого мира возвращает false — добавить пример.
- Батч 4 добавит thread-safety-оговорки в соответствующие классы; этот батч подчищает всё остальное.

---

## Порядок исполнения

1. **Batch 1** — критичные lifecycle-баги, высокий impact, каждый с чётким reproducer.
2. **Batch 2** — teardown integrity, один PR с перестановкой порядка и двумя гардами.
3. **Batch 3** — дешёвые независимые win'ы.
4. **Batch 4** — thread-safety policy (только docs).
5. **Batch 5** — после sign-off на вариант (A) vs (B).
6. **Batch 6** — после sign-off на вариант (A) vs (B).
7. **Batch 7** — отдельный раунд аудита, может породить свои issue.
8. **Batch 8** — подчистка после всего остального.

Batch'и 1–4 — чистые правки без API-изменений, можно идти подряд. 5 и 6 — блокированы решениями пользователя. 7 — зависит от отдельного аудита. 8 — в конце, чтобы синхронизироваться с финальным состоянием кода.
