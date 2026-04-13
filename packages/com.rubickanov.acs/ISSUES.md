# ACS — Issues

Ревью пакета `com.rubickanov.acs` (без расширений). Дата: 2026-04-12.

Покрытие: весь `Runtime/*`, весь `Editor/*`, `README.md`, `IDEAS.md`, `package.json`, `Runtime/ACS.Runtime.asmdef`, `Runtime/csc.rsp`, `Runtime/AssemblyInfo.cs`. `Tests/` намеренно не проверялся.

Формат каждой находки: severity + категория, путь к коду, описание проблемы и её последствий, предлагаемый фикс. Если в описании фигурирует `file:line` — это ссылка на текущее состояние кода.

---

## HIGH

### H1. `EntityExtensions.AttachLogic` — XML-doc обещает idempotency, которой в коде нет ✅ resolved 2026-04-13

> Резолюция: путь «сделать код действительно идемпотентным» не работает без расширения API — ручной `logic.Dispose()` идёт мимо AttachLogic, а предложенный в исходном разборе `fired`-флаг взводится только внутри обработчика, т.е. не ловит сценарий «manual Dispose → entity Destroyed». Поправили документацию: XML-doc у `AttachLogic` и у `IEntityLogic` теперь явно требуют идемпотентный `Dispose` (стандартный `if (_disposed) return;` паттерн). Тест `AttachLogic_ManualDispose_ThenEntityDispose_DoesNotDisposeAgain` переименован в `_FrameworkStillFiresDisposeOnce` — исходное имя врало, а ассерт `DisposeCount == 2` корректно фиксирует «фреймворк зовёт Dispose ровно один раз по Destroyed».

**Файл:** `Runtime/EntityExtensions.cs:14-17, 25-41`
**Категория:** bug (docs ↔ code), возможный double-dispose

XML-комментарий обещает:
> The call is idempotent against double-dispose: even if the caller invokes `logic.Dispose()` manually, the subsequent `IEntity.Destroyed` hook is a no-op because the subscription is removed when it fires.

Код:
```csharp
Action<IEntity>? handler = null;
handler = _ =>
{
    entity.Destroyed -= handler!;  // отписка ВНУТРИ handler'а
    logic.Dispose();
};
entity.Destroyed += handler;
```

Handler снимает подписку только когда она **сработала** (в ветке `Destroyed`). Если пользователь вызвал `logic.Dispose()` сам (до destroy entity), handler остаётся подписан и при последующем уничтожении entity вызовет `logic.Dispose()` **второй раз**.

**Воспроизведение:**
```csharp
var entity = new Entity();
var logic = entity.AttachLogic(new MyLogic(entity));
logic.Dispose();    // первый dispose
entity.Dispose();   // handler fires → logic.Dispose() повторно
```

**Последствие:** double-dispose. Если `MyLogic.Dispose()` делает `_sub.Dispose()` без guard — `ObjectDisposedException` или повторный Dispose у R3-подписки.

**Фикс:** либо поправить doc ("двойной Dispose безопасен только если сам logic идемпотентен"), либо добавить флаг в замыкание:
```csharp
bool fired = false;
handler = _ => {
    if (fired) return;
    fired = true;
    entity.Destroyed -= handler!;
    logic.Dispose();
};
```

---

### H3. `World.Awake` reconciliation-скан — почти всегда мёртвый код ✅ resolved 2026-04-13

**Файл:** `Runtime/World.cs:37-57`
**Категория:** suspicious / dead code

```csharp
[DefaultExecutionOrder(-1000)]
public class World : SingletonMonoEntity<World>
{
    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;

        // Safety net: pick up entities that created aspects before this World existed.
        var contexts = Object.FindObjectsByType<MonoEntity>(FindObjectsSortMode.None);
        for (int i = 0; i < contexts.Length; i++)
        {
            var context = contexts[i];
            if (context == this) continue;
            foreach (var aspect in context.GetAllAspects())
                _core.Register(context, aspect.GetType());
        }
    }
}
```

`DefaultExecutionOrder(-1000)` означает, что `World.Awake` отработает **раньше** Awake любого обычного компонента. Аспекты в `MonoEntity._aspects` создаются лениво через `Require`, а `Require` дёргается из `EntityComponent.Awake` через `AspectInjector.Inject`. На момент скана **у всех entity `GetAllAspects()` вернёт пусто**.

Для additive-сцен `World.Awake` не перезапускается (сингелтон уже жив), так что после первой сцены скан тоже не сработает.

Код ловит только entity с `DefaultExecutionOrder` **меньше** -1000, вручную создавшую аспект ещё до aspect-injection. В канонической модели такого никто не делает.

**Последствие:** `FindObjectsByType<MonoEntity>` + перебор — не бесплатный, читатель думает что тут safety net, а её нет.

**Фикс:** удалить скан. Либо реально сделать: отложенная регистрация через событие «Require до живого World» с применением при Awake World.

**Resolution:** override `Awake` удалён в `World.cs` — singleton install теперь полностью на `SingletonMonoEntity<World>`; регистрация entity идёт через `MonoEntity.Require<T>` → `World.Instance?.Register`. Контракт «`World` должен существовать до первого `Require`» зафиксирован в XML-doc `World`. Тест `WorldAwakeAfterEntities_StillIndexesExistingAspects` удалён как упражнявший отсутствующий safety net.

---

### H2. Чистая `Entity` не интегрирована с `WorldCore` — скрытая асимметрия и утечки ✅ resolved 2026-04-13

**Файлы:** `Runtime/Entity.cs:27-35, 63-71`, `Runtime/WorldCore.cs`, `Runtime/MonoEntity.cs:39-48, 82-88`
**Категория:** bug / API-smell

`MonoEntity.Require<T>` автоматически регистрирует entity в `World.Instance._core`:
```csharp
public T Require<T>() where T : class, IEntityAspect, new()
{
    // ...
    World.Instance?.Register(this, type);
    return instance;
}
```
`MonoEntity.OnDestroy` вызывает `World.Instance?.Unregister(this)`.

`Entity` (POCO) — **ничего** из этого не делает:
```csharp
public T Require<T>() where T : class, IEntityAspect, new()
{
    var type = typeof(T);
    if (_aspects.TryGetValue(type, out var existing))
        return (T)existing;
    var instance = new T();
    _aspects[type] = instance;
    return instance;           // никакой регистрации
}

public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    Destroyed?.Invoke(this);
    Destroyed = null;
    _aspects.Clear();          // никакого Unregister
}
```

`README` при этом описывает путь "pure-C# queries":
```csharp
var core = new WorldCore();
core.Register(entity, typeof(HealthAspect));   // вручную, каждый раз
```

То есть пользователю вручную зеркалить то, что `MonoEntity` делает само. Забыл — `Query<T>()` молча пусто. Забыл `Unregister` в `Destroyed` — entity висит в бакетах вечно, удерживает ссылки.

`IDEAS.md:1126-1128` помечает Pure core как "✅ реализовано 2026-04-12" — сегодня. То есть рефактор свежий и недодумка вполне может быть следствием того, что эту часть ещё не дошли закрыть.

**Последствие:** pure-C# режим почти невозможно использовать без boilerplate; тихие утечки в долгоживущих симуляциях.

**Фикс (любой):**
- `Entity` принимает опциональный `WorldCore` в конструкторе и сам Register/Unregister.
- Или `IEntity.AspectAdded(Type)` event, на который `WorldCore.Attach(entity)` подписывается.
- Или просто громкая фраза в XML-doc `Entity` и в `README`, что для pure-C# режима регистрация — обязанность вызывающего.

**Resolution:** выбран вариант 1 — `new Entity(WorldCore)` auto-register/unregister, параметрless ctor оставлен для ручного пути. Ordering в `Dispose` зеркалит `MonoEntity.OnDestroy`: `Destroyed?.Invoke` до `core.Unregister`, чтобы подписчики успевали на последний `Query`. README обновлён, регрессии покрыты `Tests/EntityWorldCoreAutoWireTests.cs`.

---

### H4. Hot-path рефлексия `Invoke` + `SetValue` в `AspectInjector.Inject` ✅ resolved 2026-04-13

**Файл:** `Runtime/AspectInjector.cs:22-46`
**Категория:** perf

```csharp
for (int i = 0; i < fields.Length; i++)
{
    var field = fields[i];
    var aspectType = field.FieldType;

    if (!RequireCache.TryGetValue(aspectType, out var requireGeneric))
    {
        requireGeneric = RequireMethod.MakeGenericMethod(aspectType);
        RequireCache[aspectType] = requireGeneric;
    }

    var aspect = requireGeneric.Invoke(context, null);  // reflection invoke
    field.SetValue(component, aspect);                   // reflection set
}
```

Закешированы `FieldInfo[]` и `MethodInfo`. Но **каждый вызов `Invoke` / `SetValue` — полноценная рефлексия**: argument packing, security checks, virtual dispatch. На .NET ≈ 200–500 нс на `MethodInfo.Invoke` + 100–200 нс на `FieldInfo.SetValue`. При сцене в 1000 entity × 3 компонента × 3 поля — порядки единиц миллисекунд на старт.

Автор знает про проблему: `IDEAS.md:354-416` планирует пакет `acs.codegen` ровно для замены этой рефлексии на source generators. Значит это осознанный долг.

**Фикс (промежуточный, без codegen):**
```csharp
// Один раз на (fieldInfo, componentType):
var ctxParam = Expression.Parameter(typeof(IEntity));
var compParam = Expression.Parameter(typeof(object));
var castedComp = Expression.Convert(compParam, componentType);
var requireCall = Expression.Call(
    ctxParam, RequireMethod.MakeGenericMethod(aspectType));
var assign = Expression.Assign(
    Expression.Field(castedComp, field), requireCall);
var setter = Expression.Lambda<Action<IEntity, object>>(
    assign, ctxParam, compParam).Compile();
```
Результат — обычный делегат. 10–50× быстрее `Invoke`+`SetValue`.

---

### H5. `EntityQuery<T…>.GetEnumerator` аллоцирует итератор на каждый `foreach` ✅ resolved 2026-04-13

**Файл:** `Runtime/EntityQuery.cs` — все 8 арностей
**Категория:** perf

Структуры объявлены `readonly struct` специально ради zero-alloc:
```csharp
public readonly struct EntityQuery<T1, T2> : IEnumerable<(IEntity, T1, T2)>
    where T1 : class, IEntityAspect
    where T2 : class, IEntityAspect
{
    private readonly EntityRegistry? _registry;
    internal EntityQuery(EntityRegistry? registry) { _registry = registry; }

    public IEnumerator<(IEntity, T1, T2)> GetEnumerator()
    {
        if (_registry == null) yield break;
        foreach (var entity in _registry.GetAllWith(typeof(T1)))
        {
            if (entity.TryGet<T1>(out var first) && entity.TryGet<T2>(out var second))
                yield return (entity, first, second);
        }
    }
}
```

Ключевое: `yield return` в методе заставляет компилятор сгенерировать **class** state-machine (не struct), имплементирующий `IEnumerator<T>`. Каждый `foreach (var x in World.Query<A, B>())` аллоцирует этот класс в heap. Итерация через интерфейс `IEnumerable<T>` добавляет virtual dispatch.

Плюс каждый item — ValueTuple. Для арности 7–8 размер кортежа растёт, stack copy дорожает.

Для фреймворка, где `Query<…>` предполагается частым гостем в Update, это регулярный gen0 garbage.

**Фикс:** явный struct-enumerator, возвращаемый напрямую (без IEnumerable-интерфейсов):
```csharp
public readonly struct EntityQuery<T1, T2>
{
    private readonly HashSet<IEntity>? _bucket;
    internal EntityQuery(EntityRegistry? r) { _bucket = r?.GetBucketOrNull(typeof(T1)); }

    public Enumerator GetEnumerator() => new(_bucket);

    public struct Enumerator
    {
        private HashSet<IEntity>.Enumerator _inner;
        private bool _hasBucket;
        public (IEntity, T1, T2) Current { get; private set; }

        public Enumerator(HashSet<IEntity>? bucket) { _hasBucket = bucket != null; /*…*/ }

        public bool MoveNext()
        {
            if (!_hasBucket) return false;
            while (_inner.MoveNext())
            {
                var e = _inner.Current;
                if (e.TryGet<T1>(out var a) && e.TryGet<T2>(out var b))
                {
                    Current = (e, a, b);
                    return true;
                }
            }
            return false;
        }
    }
}
```
`foreach` использует "duck-typed" паттерн (`GetEnumerator().MoveNext() + Current`) — интерфейс не нужен.

---

### H6. `EntityRegistry.Unregister` перебирает ВСЕ бакеты при уничтожении entity ✅ resolved 2026-04-13

> Резолюция: выбран вариант «break signature» (vs. внутренний reverse-index). `EntityRegistry.Unregister` теперь принимает `Dictionary<Type, object>.KeyCollection aspectTypes` — конкретный тип, а не `IEnumerable<Type>`, чтобы `foreach` duck-type'ил struct-enumerator и хот-path despawn'а оставался zero-alloc (no-LINQ constraint). В `IEntity` добавлен `AspectTypes { get; }`, реализованный в `MonoEntity` и `Entity` как `_aspects.Keys`. `MonoEntity.OnDestroy` и `Entity.Dispose` передают `_aspects.Keys` напрямую. Обёртки в `WorldCore`/`World` обновлены. Новый тест `Unregister_OnlyTouchesBucketsForProvidedTypes_LeavesOthersIntact` пинит, что не переданные типы не трогаются.

**Файл:** `Runtime/EntityRegistry.cs:35-39`
**Категория:** perf

```csharp
public void Unregister(IEntity entity)
{
    foreach (var set in _index.Values)
        set.Remove(entity);
}
```

O(количество типов аспектов в мире). Если в мире 50 типов аспектов, а entity имела 2 — всё равно 50 попыток `HashSet.Remove`. `Remove` у HashSet — O(1), но lookup-ы мимо ++ итерация `Dictionary.Values` не бесплатны, плюс cache-miss-heavy доступ.

У нас **уже есть** знание, какие именно типы были у entity — это ключи `MonoEntity._aspects` (`Runtime/MonoEntity.cs:25`).

**Фикс:**
```csharp
// В EntityRegistry:
public void Unregister(IEntity entity, IEnumerable<Type> aspectTypes)
{
    foreach (var t in aspectTypes)
        if (_index.TryGetValue(t, out var set))
            set.Remove(entity);
}

// В MonoEntity.OnDestroy:
World.Instance?.Unregister(this, _aspects.Keys);
```

Альтернатива — внутренний обратный индекс `Dictionary<IEntity, HashSet<Type>>` в Registry, чтобы сохранить текущую сигнатуру.

---

## MEDIUM

### M1. `MonoEntity.OnContextInitialized` — static event, никогда не очищается, имя обманчиво ✅ resolved 2026-04-13

> Резолюция: `OnContextInitialized` переименован в `OnAwakeCompleted` — имя честно описывает lifecycle-момент (в `Start`, после всех Awake), без ложного обещания «все аспекты entity созданы». Для реакции на лениво создаваемые аспекты добавлен второй event `OnAspectCreated(IEntity, Type)`, фирящийся изнутри `Require<T>` после регистрации в `World` (только когда создаётся новый инстанс, не при возврате существующего). Оба static event'а сбрасываются в `null` через `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` — это снимает подписочный leak между play-сессиями независимо от настройки Domain Reload. README секция «Extension Hook» переписана под оба события, с примерами per-entity и per-aspect использования. Покрыто тестами в `Tests/MonoEntityTests.cs`: `Require_CreatesNewAspect_FiresOnAspectCreated`, `Require_ReturnsExistingAspect_DoesNotFireOnAspectCreated`, `Start_AfterAwake_FiresOnAwakeCompleted`. Подписчиков у старого имени в коде пакетов не было — breaking-shim не потребовался.

**Файл:** `Runtime/MonoEntity.cs:20, 77-80`
**Категория:** bug / API-smell

```csharp
public static event Action<MonoEntity>? OnContextInitialized;
// ...
private void Start()
{
    OnContextInitialized?.Invoke(this);
}
```

Два независимых замечания:

**(a) static event не чистится.**
Domain reload в редакторе (или выход из процесса) обнуляет static state. Но между Play-сессиями внутри одного domain — подписчики остаются. Для расширений, подписывающихся в `InitializeOnLoad` или подобном, риск дубликации setup'а.

**(b) имя обещает больше, чем даёт.**
«OnContextInitialized» → читатель ожидает «все аспекты entity созданы». По факту event фирится в `Start`, а аспекты создаются **лениво** через `Require`. Компонент, делающий `Require` в `OnEnable` / `Update` / откладывающий — добавляет аспект **после** события.

`README.md:278-287` прямо использует это как extension hook:
> `MonoEntity.OnContextInitialized` fires once in `Start` after every component's `Awake` has run and aspects have been created.

Это верно только для аспектов, созданных в Awake (обычный случай с `AspectInjector.Inject`). Для всего остального — нет.

**Фикс:**
- Переименовать в `OnAwakeCompleted` / `OnInitialAspectsCreated`.
- Или добавить второй event `OnAspectCreated(IEntity, Type)`, фирящийся из `Require`.

---

### M2. `EntityComponent.Context` не кеширует null и молча теряется ✅ resolved 2026-04-13

> Резолюция: чек переехал не в `Awake`, а в сам геттер `Context` — так любой путь обращения (не только `AspectInjector.Inject` внутри базового `Awake`) получает внятный `InvalidOperationException` с именем типа и GameObject'а. Это же закрывает микро-перформанс-дыру: null просто не может быть закеширован, геттер бросает раньше. Добавлен `Tests/EntityComponentTests.cs` с тремя сценариями (отсутствует родитель → throw, родитель есть → возвращается, повторный вызов возвращает закешированный инстанс).

**Файл:** `Runtime/EntityComponent.cs:13, 16`
**Категория:** bug-prone / эргономика ошибок

```csharp
private MonoEntity? _context;

protected MonoEntity Context => _context ??= GetComponentInParent<MonoEntity>();
```

Если родителя-`MonoEntity` нет, `GetComponentInParent` вернёт null, `??=` попытается присвоить null в `_context`, но `_context` уже null — ничего не меняется, каждое следующее обращение снова дёргает `GetComponentInParent` (микро-перформанс-дыра). В `Awake`:
```csharp
protected virtual void Awake()
{
    EntityInjector.Inject?.Invoke(gameObject);
    AspectInjector.Inject(Context, this);  // Context = null → NRE внутри
}
```
`AspectInjector.Inject` ждёт не-null `IEntity` — падает с невыразительным `NullReferenceException`, в стеке нет указания что родитель не найден.

**Фикс:**
```csharp
protected virtual void Awake()
{
    if (Context == null)
        throw new InvalidOperationException(
            $"EntityComponent '{GetType().Name}' on '{gameObject.name}' requires a MonoEntity in parent hierarchy.");
    // ...
}
```

---

### M3. `EntityRegistry.Empty` — shared mutable HashSet, спрятанный за IReadOnlyCollection ✅ resolved 2026-04-13

**Файл:** `Runtime/EntityRegistry.cs:13, 41-49`
**Категория:** suspicious

```csharp
private static readonly HashSet<IEntity> Empty = new();
// ...
public IReadOnlyCollection<IEntity> GetAllWith(Type aspectType)
{
    return _index.TryGetValue(aspectType, out var set) ? set : Empty;
}
```
XML-doc: *"owned by the registry — do not mutate it"*. Только словесный контракт.

Тип возврата — `IReadOnlyCollection<IEntity>`, но cast обратно тривиален:
```csharp
var empty = registry.GetAllWith(typeof(Foo));
((HashSet<IEntity>)empty).Add(someEntity); // компилируется и работает
```
Мутация отравляет `Empty` на весь домен — для всех реестров всех сцен и тестов (static readonly).

Текущая вероятность аварии ≈ ноль (никто не кастит назад). Но мина заложена.

**Фикс:** `Array.Empty<IEntity>()` через лёгкий адаптер, либо `ImmutableHashSet<IEntity>.Empty`, либо `new HashSet<IEntity>()` (аллокация только на miss, но miss-путь обычно реже hit).

---

### M4. `RuntimeAspectDrawer.SignalTracker` — утечка подписок между domain reloads ✅ resolved 2026-04-13

> Резолюция: `SignalTracker` сделан per-instance полем `RuntimeAspectDrawer` вместо static. Подписки живут ровно столько, сколько живёт drawer; закрытие одного инспектора больше не срывает подписки соседнего (`Dispose()` зовёт `_signalTracker.DisposeAll()` только для своего набора). `Subscribed`-HashSet слит с `_subscriptions` в единый `Dictionary<int, IDisposable>`. `RecordFire` стал instance-методом — в `EnsureSubscribed` `this` захватывается через `Expression.Constant(this)`. `_cachedSubscribeGeneric` оставлен static: `MethodInfo` не удерживает инстансов `Subject`, а AppDomain на domain reload пересоздаётся. Из `[InitializeOnLoadMethod] ClearCache` убран вызов `SignalTracker.ClearAll()` — per-instance trackers отпускаются вместе с инспекторами.

**Файл:** `Editor/RuntimeAspectDrawer.cs:326-344, 346-392`
**Категория:** perf / leak (editor-only)

```csharp
private static class SignalTracker
{
    private static readonly List<IDisposable> Subscriptions = new();
    // ...
    public static void EnsureSubscribed(int key, object subjectInstance)
    {
        if (Subscribed.Contains(key)) return;
        Subscribed.Add(key);
        // ... compile Action<T> через Expression, Subscribe ...
        Subscriptions.Add(disposable);
    }

    public static void ClearAll()
    {
        foreach (var sub in Subscriptions) sub.Dispose();
        // ...
    }
}
```

`ClearAll` вызывается только из `[InitializeOnLoadMethod]` статического метода `RuntimeAspectDrawer.ClearCache`, который фирится на domain reload (рекомпиляция, вход в Play Mode при Enter-Play-Mode-Options disabled).

Между domain reloads: инспектор открыл entity A → подписки на все её Subject-поля. Открыл B → подписки на B. Подписки A **остаются** живыми, даже если A больше никем не просматривается.

`SignalTracker` — static, подписки держат замыкание, замыкание держит `key`, подписка на `Subject<T>` держит сам `Subject`. Subject не соберётся GC'ом пока `SignalTracker.Subscriptions` живёт → entity, удерживающие Subject через аспекты, тоже.

**Фикс:** хранить `Dictionary<int, IDisposable>` и чистить по ключу из `RuntimeAspectDrawer.Dispose()` (строка 86) — он уже вызывается из `MonoEntityEditor.OnDisable`.

---

### M5. Аллокации на каждый repaint инспектора в play-mode ✅ resolved 2026-04-13

> Резолюция: в `RuntimeAspectDrawer` все стили (`DimStyle`, `HeaderStyle`, `NameStyle`, `ValueStyle`) переведены на static lazy-init с ресетом в `ClearCache`; `ValueStyle` переиспользуется для reactive/signal/plain — меняется только `normal.textColor`. Список aspect'ов вынесен в `_aspectsBuffer` (private readonly), `Clear()` в начале `Draw`. В `MonoEntityEditor` стили (`HeaderStyle`, `FieldStyle`, `BindingStyle`) тоже lazy-static с `[InitializeOnLoadMethod] ResetStyles`, hex-строки `ReadHex`/`WriteHex` посчитаны один раз статически. Per-field `List<string>` + `string.Join` убран: строка binding-бейджей собирается один раз в `RebuildBindingLabelCache` (вызов из `Refresh`) через `StringBuilder` и кешируется по ссылке `field.Bindings`; `DrawField` делает только `TryGetValue`.

**Файлы:** `Editor/RuntimeAspectDrawer.cs`, `Editor/MonoEntityEditor.cs`
**Категория:** perf (editor)

`MonoEntityEditor.RequiresConstantRepaint()` возвращает `Application.isPlaying` (строка 32). В play-mode `OnInspectorGUI` бежит ~60 Гц для каждого открытого MonoEntity-инспектора.

**В `RuntimeAspectDrawer.Draw`:**
- `var aspects = new List<(Type, object)>();` (строка 59)
- `new GUIStyle(EditorStyles.miniLabel) { ... }` (65)
- `new GUIStyle(EditorStyles.boldLabel) { ... }` (73)
- `aspects.Sort((a, b) => string.Compare(a.type.Name, b.type.Name, ...))` (80) — сортировка строк каждый кадр.

**В `RuntimeAspectDrawer.DrawField` / `DrawReactiveValue` / `DrawSignalLabel` / `DrawPlainValue`:**
- `new GUIStyle(...)` на строках 116, 163, 181, 206 — **на каждое поле каждого аспекта каждый кадр**.

**В `MonoEntityEditor.OnInspectorGUI` / `DrawField`:**
- `new GUIStyle(EditorStyles.boldLabel) { ... }` (57)
- `new GUIStyle(EditorStyles.miniLabel) { ... }` (98, 104) — два стиля в `DrawField`
- `var parts = new List<string>(); ... EditorGUILayout.LabelField(string.Join("  ", parts), ...);` (113-122) — новая list + join-строка на каждое поле.

Суммарно для entity с 5 аспектами × 4 поля — 80+ аллокаций `GUIStyle` за кадр, плюс `List`, плюс строки. GC gen0 в play-mode и без того нагружен.

**Фикс:** все стили — `static readonly` lazy-init, list'ы — переиспользуемые private fields. Style copy constructor стандартен, так что инициализация через `static GUIStyle _miniLabel = new(EditorStyles.miniLabel) { … }` корректна.

---

### M6. `AspectUsageAnalyzer.EnsureAspectFields` сканит ВСЕ `MonoScript` в проекте ✅ resolved 2026-04-13

**Файл:** `Editor/AspectUsageAnalyzer.cs:113-140`
**Категория:** perf (editor)

```csharp
private static void EnsureAspectFields()
{
    if (_aspectFieldsLoaded) return;
    _aspectFieldsLoaded = true;

    string[] guids = AssetDatabase.FindAssets("t:MonoScript");
    foreach (string guid in guids)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (!path.Contains("Aspect")) continue;

        string source = File.ReadAllText(path);
        if (!source.Contains("IEntityAspect")) continue;
        // ... regex-парсинг полей ...
    }
}
```

Флаг `_aspectFieldsLoaded` сбрасывается в `ClearCache()` (`[InitializeOnLoadMethod]`, строка 106) — значит **после каждой рекомпиляции** при первом открытии инспектора:
1. `AssetDatabase.FindAssets("t:MonoScript")` — по всему проекту.
2. `File.ReadAllText` на каждый файл с «Aspect» в пути.
3. Regex для каждого.

Для большого проекта (1000+ скриптов) — лаг UX после каждой компиляции.

Плюс `path.Contains("Aspect")` — false positives: Unity-шный `AspectRatioFitter`, любой `FlaspectWhatever.cs` тоже попадут на чтение.

**Фикс:** кеш в `SessionState` или отдельном кеш-файле по `(guid, importTime)`. При первом входе после compile инвалидировать только изменённые скрипты.

---

### M7. `AspectUsageAnalyzer` — regex'ы дают false positives ✅ resolved 2026-04-13

**Файл:** `Editor/AspectUsageAnalyzer.cs:180-194`
**Категория:** dirty / bug

```csharp
private static bool IsFieldWritten(string source, string fieldVar, string fieldName)
{
    string escaped = Regex.Escape(fieldVar) + @"\." + Regex.Escape(fieldName);
    return Regex.IsMatch(source, escaped + @"\.Value\s*[\+\-\*\/]?=")
           || Regex.IsMatch(source, escaped + @"\.OnNext\(")
           || Regex.IsMatch(source, escaped + @"\b\s*[\+\-\*\/]?=[^=]");
}

private static bool IsFieldRead(string source, string fieldVar, string fieldName)
{
    string escaped = Regex.Escape(fieldVar) + @"\." + Regex.Escape(fieldName);
    return Regex.IsMatch(source, escaped + @"\.Subscribe\(")
           || Regex.IsMatch(source, escaped + @"\.Value(?!\s*=)")
           || Regex.IsMatch(source, escaped + @"\b(?!\.Value)(?!\.OnNext)(?!\.Subscribe)(?!\s*=[^=])");
}
```

**Bug 1 — substring matching по имени поля.**
`Regex.Escape(fieldName)` не завёрнут в `\b` справа. Если у аспекта есть `Health` и `HealthPoints`, regex для `Health` сматчится внутри `_aspect.HealthPoints.Value`. В инспекторе — фантомные binding'и.

```csharp
public class HealthAspect : IEntityAspect
{
    public readonly ReactiveProperty<float> Health = new(100);
    public readonly ReactiveProperty<float> HealthPoints = new(100);
}
// использование _aspect.HealthPoints.Value → инспектор помечает и Health
```

**Bug 2 — fuzzy write detection.**
Третий паттерн `escaped + @"\b\s*[\+\-\*\/]?=[^=]"` считает любое `fieldVar.field.<что-то>=…` присваиванием:
```csharp
_aspect.Position.Local.x = 5;
// помечает Position как Write, хотя пишется во вложенный объект
```

**Bug 3 — `ParseRequiredAspects` видит только `Context.Require<X>()`.**
```csharp
var requireMatches = Regex.Matches(source, @"Context\.Require<(\w+)>\(\)");
```
`World.Require<X>()`, `entity.Require<X>()`, `someField.Require<X>()` — не ловятся. Ограничение молчаливое.

**Фикс:** обернуть `fieldName` в `\b` с обеих сторон; для Write ограничить матч последним сегментом chain'а; расширить `ParseRequiredAspects` до `(\w+(?:\.\w+)*)\.Require<(\w+)>\(\)`.

---

### M8. `IsFieldRead` / `IsFieldWritten` — 3 свежих regex на поле, без компиляции ✅ resolved 2026-04-13

**Файл:** `Editor/AspectUsageAnalyzer.cs:180-194`
**Категория:** perf (editor)

Regex'ы создаются через статический `Regex.IsMatch(input, pattern)` — это кеш на 15 последних паттернов (`Regex.CacheSize`). Ключ кеша — `(pattern, options)`. У нас pattern содержит конкретную пару `(fieldVar, fieldName)` — т.е. каждая уникальная пара даёт новый pattern. На entity с 10 компонентами × 5 полей × 3 regex = 150 паттернов → кеш переполняется. Плюс ни один regex не помечен `RegexOptions.Compiled`.

`AnalyzeEntity` вызывается только из `OnEnable` инспектора (не constant repaint), так что это не hot-path каждого кадра, но при частом переключении между MonoEntity-ями — ощутимый лаг.

**Фикс:** для 90% случаев regex не нужен — хватит `IndexOf` + ручная проверка word boundary.

---

## LOW

### L1. `Entity.Dispose` обнуляет `Destroyed = null` — скрытая zombie-подписка ✅ resolved 2026-04-13

> Резолюция: `Destroyed = null` убрано из `Entity.Dispose` — симметрия с `MonoEntity.OnDestroy` восстановлена. XML-doc `Entity.Dispose` больше не обещает «drops all Destroyed subscribers»; `IEntity.Destroyed` явно документирует, что подписка после destroy легальна, но молча инертна (`_disposed == true`). Добавлен тест `Dispose_ThenSubscribe_HandlerNeverFires`.

**Файл:** `Runtime/Entity.cs:63-71`
**Категория:** suspicious / docs gap

```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    Destroyed?.Invoke(this);
    Destroyed = null;           // не упомянуто в doc'е
    _aspects.Clear();
}
```

Если пользователь подписывается на `Destroyed` **после** `Dispose` — `null += handler` валиден в C# (создаёт новое событие с одним подписчиком). Подписка существует, но никогда не сработает — `_disposed == true`.

В `MonoEntity.OnDestroy` такого зануления нет. Асимметрия.

**Фикс:** документировать в XML-doc `Destroyed`, либо убрать `Destroyed = null` (подписчики всё равно соберутся GC'ом вместе с `Entity`).

---

### L2. `README.md:141` — «always call `base.Awake()`» двусмыслен ✅ resolved 2026-04-13

> Резолюция: одностройчник в секции «Component Lifecycle» разбит на два абзаца с примерами. Первый — про `EntityComponent`: `base.Awake()` триггерит `[Aspect]`-инъекцию. Второй — про `SingletonMonoEntity<T>` (включая `World`): `base.Awake()` ставит статический `Instance`; пропуск оставит `Instance` равным `null`. `World : SingletonMonoEntity<World>` подтверждено H3-резолюцией.

**Файл:** `README.md:141`
**Категория:** dirty / docs

> If you override Awake, always call `base.Awake()` — that is what triggers aspect injection.

Правда только для `EntityComponent` (там `base.Awake()` делает `AspectInjector.Inject`). Для подклассов `MonoEntity` / `SingletonMonoEntity` критично другое — `Instance = this` в `SingletonMonoEntity.Awake`. Если override без `base.Awake()`, сингелтон не установится, `World.Instance` останется null.

README не разделяет эти два случая.

**Фикс:** разбить на два абзаца — про `EntityComponent` и про `SingletonMonoEntity`.

---

### L3. `MonoEntity.Awake` — пустой virtual, комментарий не про то ✅ resolved 2026-04-13

> Резолюция: из XML-doc убран некорректный causal claim про лень аспектов («keep the base class free of Awake behavior so aspects remain lazy») — лень обеспечивается кодом `Require<T>` (TryGetValue/new T()), а не пустотой `Awake`. Новый текст: «Empty by default — the base class has no Awake behavior of its own.»

**Файл:** `Runtime/MonoEntity.cs:27-34`
**Категория:** dirty / docs

```csharp
/// <summary>
/// Hook for derived classes (e.g. <see cref="SingletonMonoEntity{T}"/>) to run
/// initialization before Start. Keep the base class free of Awake behavior so aspects
/// remain lazy — callers only pay for what they use.
/// </summary>
protected virtual void Awake() { }
```

«Aspects remain lazy» обеспечивается кодом `Require` (`if (TryGetValue) return; else new T()`), а не пустотой `Awake`. Реальная причина пустоты — просто hook под override.

**Фикс:** убрать вторую половину фразы.

---

### L4. `EntityTickRunner` — комментарий про scratch неточен ✅ resolved 2026-04-13

> Резолюция: XML-doc'и `Register`/`Unregister` явно зафиксировали snapshot-семантику. `Unregister` во время `Tick` (self или sibling) не убирает tickable из текущего кадра — отписка вступает в силу со следующего. Симметрично обновлён `Register` — «from the next frame onward». Поведенческих изменений нет.

**Файл:** `Runtime/EntityTickRunner.cs:16-19, 44-56`
**Категория:** dirty / docs

```csharp
// Reused scratch buffer so a tickable that registers or unregisters a
// sibling mid-Tick doesn't corrupt iteration.
```

«Не ломает итерацию» — правда. Умалчивается: **unregister'енный mid-tick tickable всё равно получит Tick в текущем кадре** — scratch хранит ссылки на момент старта Update, удаление из `_tickables` не удаляет из `_scratch`.

Для пользователя, ожидающего «снял с регистрации — немедленно перестал тикать», сюрприз.

**Фикс:** в XML-doc `Unregister` добавить: *«вступает в силу со следующего кадра»*.

---

### L5. `SingletonMonoEntity` — дубликат фирит `Destroyed` при уничтожении ✅ resolved 2026-04-13

> Резолюция: добавлен `private bool _destroyedAsDuplicate` в `SingletonMonoEntity<T>`. При обнаружении дубликата в `Awake` флаг выставляется перед `Destroy(gameObject)`; `OnDestroy` short-circuits до `base.OnDestroy()` когда флаг true — `Destroyed` не фирится, `World.Unregister` не вызывается. Флаг держится в `SingletonMonoEntity<T>` (singleton-specific concern); `MonoEntity` не трогается. Добавлен `Tests/SingletonMonoEntityTests.cs` с 3 тестами (дубликат не фирит, дубликат не чистит Instance, оригинал всё ещё фирит Destroyed). Тесты используют reflection для вызова `Awake`/`OnDestroy` в EditMode (паттерн из `MonoEntityTests`).

**Файлы:** `Runtime/SingletonMonoEntity.cs:20-29`, `Runtime/MonoEntity.cs:82-88`
**Категория:** suspicious

```csharp
protected override void Awake()
{
    if (Instance != null && Instance != this)
    {
        Destroy(gameObject);
        return;
    }
    Instance = (T)this;
    base.Awake();
}
```

Дубликат вызывает `Destroy(gameObject)`. Unity в конце кадра вызовет `OnDestroy`, унаследованный от `MonoEntity`:
```csharp
protected virtual void OnDestroy()
{
    Destroyed?.Invoke(this);
    World.Instance?.Unregister(this);
}
```

Дубликат никогда не был `Instance`, никогда не регистрировал аспектов, но `Destroyed` на нём фирится. Крайне маловероятно, чтобы кто-то успел подписаться (нужно в рамках одного кадра), но семантически грязно.

**Фикс:** внутренний флаг `_destroyedAsDuplicate` и skip `Destroyed`/`Unregister` в `OnDestroy`.

---

### L6. `MonoEntity.GetAllAspects` отдаёт живой `Values` словаря ✅ resolved 2026-04-13

> Резолюция: и `MonoEntity.GetAllAspects`, и `Entity.GetAllAspects` теперь возвращают snapshot (`object[]`, заполненный через `Dictionary.ValueCollection.CopyTo`). Подпись `IEnumerable<object>` сохранена — не breaking. Ручной copy вместо `.ToArray()` — LINQ в runtime пакете запрещён (feedback memory). Живые потребители (`RuntimeAspectDrawer`, `AspectReplicator`, тесты) уже копировали в свои буферы — дополнительный snapshot погоды не делает.

**Файл:** `Runtime/MonoEntity.cs:75`
**Категория:** suspicious

```csharp
public IEnumerable<object> GetAllAspects() => _aspects.Values;
```

Прямая ссылка на `Dictionary<Type, object>.ValueCollection`. Итерация + мутация в одном потоке → `InvalidOperationException: collection was modified`.

Сейчас единственный in-package потребитель — `World.Awake` scan (строка 54), не мутирует. Но API публичный. Если кто-то сделает:
```csharp
foreach (var aspect in entity.GetAllAspects())
{
    if (aspect is SomeTrigger t && t.NeedsFallback)
        entity.Require<FallbackAspect>();  // модифицирует _aspects
}
```
— runtime-ошибка.

**Фикс:** либо `.ToArray()` (аллокация, но безопасно), либо в XML-doc явно запретить мутации во время итерации.

---

### L7. `EntityInjector` — публичный mutable static делегат ✅ resolved 2026-04-13

> Резолюция: публичное поле `Inject` заменено на пару `SetInjector(Action<GameObject>)` / `ClearInjector()` + `Invoke(GameObject)`. Политика — **log warning + overwrite** при повторной установке с другим делегатом; same-delegate двойная установка — silent no-op (для hot-reload workflows с отключённым domain reload). `SetInjector(null)` бросает `ArgumentNullException`. Call sites обновлены: `Runtime/EntityComponent.cs:32` и `com.rubickanov.acs.netcode/Runtime/EntityNetworkComponent.cs:24` теперь зовут `EntityInjector.Invoke(gameObject)`. Тесты (`Tests/EntityInjectorTests.cs`) переписаны на новый API. README пример обновлён. Breaking change (source-only) задокументирован. `Invoke` оставлен `public` — используется из соседнего пакета.

**Файл:** `Runtime/EntityInjector.cs`
**Категория:** API-smell

```csharp
public static Action<GameObject>? Inject;
```

Любой код может перезаписать/занулить. Два DI-пакета — молча перетирают друг друга.

**Фикс (опционально):** методы `SetInjector` / `ClearInjector` с internal storage + лог/exception при double-set. Не критично, но чище.

---

### L8. `RuntimeAspectDrawer.ClassifyField` — `StartsWith("Subject")` ✅ resolved 2026-04-13

> Резолюция: `ClassifyField` теперь сравнивает generic-definition точно — `def == typeof(ReactiveProperty<>)` / `def == typeof(Subject<>)`. Walk-up loop в `EnsureSubscribed` (проверка базовых типов на Subject) также переведён на строгое `typeof(Subject<>)`. Только эти два типа из R3 — строгая замена, без расширения охвата (`Observable`, `ReadOnlyReactiveProperty` намеренно не добавлены). `ACS.Editor.asmdef` получил `"overrideReferences": true` + `"precompiledReferences": ["R3.dll"]` — без этого `typeof(R3.*<>)` не резолвится в Editor assembly.

**Файл:** `Editor/RuntimeAspectDrawer.cs:299-306`
**Категория:** dirty

```csharp
private static FieldKind ClassifyField(Type fieldType)
{
    if (!fieldType.IsGenericType) return FieldKind.Plain;
    string name = fieldType.GetGenericTypeDefinition().Name;
    if (name.StartsWith("ReactiveProperty")) return FieldKind.ReactiveProperty;
    if (name.StartsWith("Subject")) return FieldKind.Subject;
    return FieldKind.Plain;
}
```

Классификация по префиксу имени в любом namespace. `SubjectLike<T>` в чужом namespace попадёт в `Signal`.

**Фикс:** `GetGenericTypeDefinition() == typeof(R3.Subject<>)`.

---

### L9. `SignalTracker.MakeKey` — теоретические коллизии ✅ resolved 2026-04-13

> Резолюция: заведён `readonly struct SignalKey : IEquatable<SignalKey>` с полями `(object Instance, string Field)`. `Equals` — `ReferenceEquals(Instance, ...)` + ordinal-string `==`, `GetHashCode` — `HashCode.Combine(RuntimeHelpers.GetHashCode(Instance), Field)`. Словари `_valueTracker`, `_fireTimes`, `_subscriptions` мигрированы с `Dictionary<int, ...>` на `Dictionary<SignalKey, ...>`. Хелпер `MakeKey` удалён; callers делают `new SignalKey(instance, fieldName)` напрямую. Ключ в `Expression.Constant(key, typeof(SignalKey))` передаётся с явным типом — один boxing на build expression tree, не на горячем пути.

**Файл:** `Editor/RuntimeAspectDrawer.cs:232-235`
**Категория:** suspicious

```csharp
private static int MakeKey(object instance, string fieldName)
{
    return HashCode.Combine(RuntimeHelpers.GetHashCode(instance), fieldName);
}
```

Два разных aspect-instance + одинаковое имя поля → теоретически одинаковый int key → `EnsureSubscribed` решит «уже подписано», пропустит. Вероятность ничтожна (32-bit hash). Правильнее — `Dictionary<(object, string), …>` или `ConditionalWeakTable<object, Dictionary<string, …>>`.

---

### L10. `AspectUsageAnalyzer.ParseRequiredAspects` — `.Distinct().ToList()` ✅ resolved 2026-04-13

> Резолюция: убран `.Distinct().ToList()` — теперь один проход с `HashSet<string> seen` для дедупа и `List<string> result` для сохранения insertion order (детерминизм диагностик). Текущий обогащённый regex для receiver chain (`\w+(?:\.\w+)*\.Require<X>()`) сохранён. `using System.Linq;` остался — используется в `AnalyzeEntity` (`OrderBy`).

**Файл:** `Editor/AspectUsageAnalyzer.cs:167`
**Категория:** dirty

```csharp
return result.Distinct().ToList();
```

Два прохода по списку. Проще — собирать в `HashSet<string>` сразу и конвертить в list на выходе. Микро, но чище.

---

### L11. `IEntityComponent` — маркер без потребителей (почти) ❌ won't-fix 2026-04-13

> Резолюция (won't-fix): посылка issue устарела. `com.rubickanov.acs.netcode` содержит `EntityNetworkComponent : NetworkBehaviour, IEntityComponent`, а `AspectReplicator` делает `GetComponentsInChildren<IEntityComponent>()` для обхода network-компонентов — т.е. маркер **активно** используется из соседнего пакета как shared contract между ACS и ACS.Netcode. Сделать `internal` нельзя — сломает acs.netcode. Оставляем публичным, статус-кво корректен.

**Файлы:** `Runtime/IEntityComponent.cs`, `Editor/MonoEntityEditor.cs:41`
**Категория:** API-smell

```csharp
public interface IEntityComponent { }
```

Единственный потребитель в пакете:
```csharp
foreach (var c in context.GetComponentsInChildren<MonoBehaviour>(true))
    if (c is IEntityComponent) types.Add(c.GetType());
```

Т.е. любой pure-C# класс, реализующий `IEntityComponent` (не MonoBehaviour), никогда не найдётся — поиск идёт через `GetComponentsInChildren<MonoBehaviour>`. Интерфейс фактически — маркер для MonoBehaviour-наследников.

**Фикс:** либо сделать `internal` до появления потребителей, либо в XML-doc явно написать, что для non-MonoBehaviour он бесполезен.

---

### L12. `AspectInjector` — кеши не thread-safe ✅ resolved 2026-04-13

> Резолюция: оба кеша переведены на `ConcurrentDictionary<,>` с `GetOrAdd` (атомарный lookup-or-build). `FieldCache` — `ConcurrentDictionary<Type, FieldInfo[]>`, `RequireDelegateCache` — `ConcurrentDictionary<Type, Func<IEntity, object>>` (имя/тип в оригинальном блоке issue были устаревшими — `RequireCache`/`MethodInfo`; фактически там лежит делегат, построенный через Expression compile, c H4-резолюции). IDEAS.md ссылка тоже устарела: Simulate Level 2 живёт на 817-889, не 972-1045. Fix сделан сразу, не defer — цена минимальна, закрывает dormant race до начала L2-работ.

**Файл:** `Runtime/AspectInjector.cs:13-14`
**Категория:** suspicious (dormant)

```csharp
private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();
private static readonly Dictionary<Type, Func<IEntity, object>> RequireDelegateCache = new();
```

Unity однопоточен → проблемы нет сейчас. `IDEAS.md:817-889` планирует Simulate Level 2 — headless .NET console app с Monte Carlo прогонами. Если симуляция пойдёт многопоточной — эти кеши без локов породят race.

**Фикс:** заменить на `ConcurrentDictionary` в момент, когда Simulate L2 станет реальностью.

---

## Что НЕ нашёл (перестрахуюсь)

Прочитал целиком и проблем не увидел:
- `Runtime/IEntity.cs`, `Runtime/IEntityAspect.cs`, `Runtime/ITickable.cs`, `Runtime/AspectAttribute.cs`, `Runtime/AssemblyInfo.cs`
- `Runtime/WorldCore.cs` — чистая композиция, работает как документировано
- `Runtime/ACS.Runtime.asmdef`, `Runtime/csc.rsp`, `package.json`

`Tests/` не проверялся — по явной просьбе.

---

## Топ на починку по ROI

1. **H4** (Expression-based AspectInjector) — большой рантайм-перф на spawn сцены, без смены API.
2. **H6** (Unregister с известными типами) — простой рефактор, заметный перф на деспавне.
3. **M7** (regex false positives в AspectUsageAnalyzer) — чистота инспектора.
