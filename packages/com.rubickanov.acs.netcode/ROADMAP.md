# ACS Netcode — Fix Roadmap

План устранения тех долгов из `ISSUES.md`. Каждый батч — самодостаточный брифинг для агента: можно скопировать как промпт, агент выполнит по пунктам.

**Порядок батчей принципиален.** Не переставлять без причины: **safety → tests → architecture → perf**.

---

## Принципы

- **Safety first, architecture second, perf last.** Сначала дыры которые могут упасть, потом тесты под следующие изменения, потом всё остальное.
- **Не рефакторить без регрессионной сетки.** #16 и #19 требуют тестов из 3.2 заранее.
- **Perf-оптимизации — только после Profiler GC Alloc на реальной сцене.** Не угадывать узкие места.
- **Один батч = один PR**, внутри — по одному коммиту на issue. Коммит-месседж ссылается на номер issue.
- **Каждый фикс имеет хотя бы один регрессионный тест** (где тест вообще применим — не для UX-улучшений вроде #20/#21).
- **Не бандлить issues из разных батчей в одном PR** — ревью становится невозможным.

---

## Батч 3.1 — Quick Safety

**Goal:** закрыть 6 мелких Easy-issues с минимальным риском. Все изменения профилактические, никакого поведенческого эффекта для корректного usage.

**Prerequisites:** нет.

**Объём:** ~50-70 строк кода в `Runtime/`. Одна сессия, ~1-2 часа.

### Задачи

**1. #15 — защита RPC handler'ов от NRE после null-context early-return**

`AspectReplicator.cs:14-17, 27-30` — заменить `= null!` на `Array.Empty<...>()` для всех коллекционных полей:

```csharp
private ReplicatedFieldBinding[] _bindings = Array.Empty<ReplicatedFieldBinding>();
private AuthorityMode[] _bindingAuthorities = Array.Empty<AuthorityMode>();
private ReplicatedEventBinding[] _eventBindings = Array.Empty<ReplicatedEventBinding>();
```

`_interpolatedBindings` и `_ownerScopedComponents` уже инициализированы правильно — не трогать.

Broadcaster-делегаты (`_reliableBroadcaster` и др.) можно оставить `null!` — они читаются только из subscribe-loop, который не выполняется с пустым `_eventBindings`. `_statePayloadCap = 0` — безвреден, `OnServerTick` гардится `if (dirtyMask == 0) return;` раньше создания writer'а.

Проверить: если в `OnNetworkSpawn` сработает ранний return на `context == null`, все RPC (`BroadcastStateRpc`, `SendInitialStateRpc`, `SubmitOwnerStateRpc`, `DispatchEvent`, `HandleOwnerEvent`, `RequestInitialStateRpc`) должны отрабатывать no-op без NRE.

**2. #20 — null-check для значений аспект-полей**

`AspectReplicator.cs:64, 83` — после `info.Field.GetValue(aspect)` добавить проверку:

```csharp
var reactive = info.Field.GetValue(aspect);
if (reactive == null)
{
    Debug.LogError($"[AspectReplicator] Aspect '{aspect.GetType().Name}' field '{info.Field.Name}' is null on '{gameObject.name}'. Initialize it in the aspect constructor or field initializer.");
    continue;
}
```

Аналогично для subject на `:83`. Early-continue, не crash.

**3. #21 — валидация `unmanaged` constraint в `ReplicationScanner`**

`ReplicationScanner.cs` — добавить статический хелпер `IsUnmanagedType(Type)` с кэшем:
```csharp
private static readonly Dictionary<Type, bool> UnmanagedCache = new();

private static bool IsUnmanagedType(Type type)
{
    if (UnmanagedCache.TryGetValue(type, out var cached)) return cached;
    bool result = type.IsPrimitive
                || type.IsEnum
                || (type.IsValueType
                    && !type.IsGenericType
                    && type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .All(f => IsUnmanagedType(f.FieldType)));
    UnmanagedCache[type] = result;
    return result;
}
```

В `CollectReplicatedFields` (`:92`) и `CollectReplicatedEvents` (`:127`) после получения `valueType` добавить:
```csharp
if (!IsUnmanagedType(valueType))
{
    Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [ReplicatedState] but ReactiveProperty<{valueType.Name}> is not unmanaged. Only unmanaged types are supported.");
    continue;
}
```

**4. #18 — клемпинг `_eventBindings` при >256**

`AspectReplicator.cs:126-127` — после error-лога добавить `Array.Resize`, симметрично с полями (`:97-98`):

```csharp
if (_eventBindings.Length > 256)
{
    Debug.LogError(...);
    Array.Resize(ref _eventBindings, 256);
}
```

На `:136` убрать `Math.Min` — теперь тривиально `_eventBindings.Length`:
```csharp
for (int i = 0; i < _eventBindings.Length; i++)
```

**5. #12b — удалить dead `_suppressNotification` из `ReplicatedEventBinding<T>`**

`ReplicatedEventBinding.cs:33` — удалить поле `private bool _suppressNotification;`.

`ReplicatedEventBinding.cs:65-79` — упростить `ApplyFromNetwork`:
```csharp
public override unsafe void ApplyFromNetwork(FastBufferReader reader)
{
    T value = default;
    reader.ReadBytesSafe((byte*)&value, sizeof(T));
    _subject.OnNext(value);
}
```

**Важно:** не трогать `_suppressNotification` в `ReplicatedFieldBinding.cs` — он живой (см. #12a в `ISSUES.md`).

**6. #23 — `List.Sort` вместо `.OrderBy` в `OnNetworkSpawn`**

`AspectReplicator.cs:57-58` — заменить LINQ на ручную сортировку:
```csharp
var aspectList = new List<object>();
foreach (var a in context.GetAllAspects()) aspectList.Add(a);
aspectList.Sort((a, b) => string.Compare(
    a.GetType().FullName, b.GetType().FullName, StringComparison.Ordinal));
```

Можно вынести `aspectList` в инстанс-поле для переиспользования между спавнами — но это оптимизация, не обязательная.

`ReplicationScanner.cs:105, 140` — **не трогать**, их `.OrderBy().ToArray()` кэшируется per-type, отрабатывает один раз.

### Files to touch

- `Runtime/AspectReplicator.cs` — #15, #18, #20, #23
- `Runtime/ReplicationScanner.cs` — #21
- `Runtime/ReplicatedEventBinding.cs` — #12b

### Verification

1. Проект компилируется без warnings (особенно проверить #15 — нет ли теперь NullReference warnings от анализатора).
2. Ручной playtest: сущность спавнится, реплицируется, owner пишет → сервер релеит → клиенты видят.
3. Ручной негативный тест: убрать `EntityContext` с префаба — в логах ровно один `LogError` про missing context, никаких NRE при RPC.
4. Ручной негативный тест: создать аспект с `[ReplicatedState] public ReactiveProperty<string>` — в логах `LogError` про non-unmanaged до запуска сцены (или на первом spawn'е).

### Definition of Done

- [ ] Все 6 issues закоммичены отдельными коммитами, каждый ссылается на номер в `ISSUES.md`.
- [ ] `ISSUES.md` обновлён: #15/#18/#20/#21/#23/#12b помечены как `fixed` с датой.
- [ ] `ISSUES.md` #12 переписан — оставить только #12a (fields), убрать #12b (закрыт).
- [ ] PR создан, компилируется, ручная проверка из Verification пройдена.

---

## Батч 3.2 — Test Foundation (партия 1)

**Goal:** создать регрессионную сетку из pure-unit тестов. Блокер для батчей 3.3 и 3.4 — без тестов трогать lifecycle и owner-auth слишком рискованно.

**Prerequisites:** Батч 3.1 замержен.

**Объём:** ~300-500 строк тестов. Одна сессия.

### Подготовка

Создать структуру:
```
com.rubickanov.acs.netcode/
  Tests/
    Runtime/
      ACS.Runtime.Netcode.Tests.asmdef   (EditMode, ссылается на ACS.Runtime.Netcode, nunit, R3)
      ReplicatedFieldBindingTests.cs
      InterpolatedFieldBindingTests.cs
      ReplicationScannerTests.cs
      ApplyStateBufferRoundTripTests.cs
      NetworkScopeScannerTests.cs
```

Шаблон asmdef взять у `com.rubickanov.acs/Tests/` — там уже есть `EntityContextTests.cs` и компания.

### Задачи (тесты)

**1. `ReplicatedFieldBindingTests`** — byte round-trip для unmanaged типов

На каждый тип (`int`, `float`, `bool`, `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Color`, кастомный unmanaged struct) — отдельный тест:
1. Создать `ReactiveProperty<T>` с известным значением.
2. Создать `ReplicatedFieldBinding<T>` через factory.
3. `WriteTo(FastBufferWriter)` → `ReadFrom(FastBufferReader)` → прочитанное значение совпадает с исходным **побайтово**.
4. `Skip(FastBufferReader)` продвигает позицию reader'а ровно на `sizeof(T)`.
5. `IsDirty` становится true после подписки authority и изменения `_reactive.Value`; `ClearDirty` сбрасывает.

**Запрет:** не писать тесты вроде `Assert.IsNotNull(binding)`. Каждый assert проверяет реальный инвариант.

**2. `InterpolatedFieldBindingTests`** — edge cases буфера и лерпа

- Пустой буфер: `TickRender(0)` — ничего не крэшится, `_reactive.Value` не меняется.
- 1 snapshot: `TickRender` возвращает это значение на любой `renderTime`.
- 2 snapshots, `renderTime` между ними: результат = lerp(a, b, t) с ожидаемым `t`.
- `renderTime` до oldest: удерживается oldest.
- `renderTime` после newest: удерживается newest.
- Wraparound: запушить 40 snapshots в 32-capacity буфер, проверить что oldest корректно сместился, `_count` остался 32.
- Bootstrap: первый snapshot сразу применяется (не ждёт interpolation delay).
- Lerp корректности для float/Vector3/Quaternion: ручные значения с known expected.

**3. `ReplicationScannerTests`** — ordering, кэш, наследование, негативные

- Два аспекта одного типа дают идентичный `ReplicatedFieldInfo[]` (instance-независимо).
- Порядок полей стабилен между вызовами (отсортирован по имени).
- Кэш: второй `Scan(aspect)` возвращает **тот же** массив (ReferenceEquals).
- Наследование: поле на base aspect'е попадает в scan derived'а.
- **Негативный:** аспект с `[ReplicatedState] ReactiveProperty<string>` — `Scan` возвращает пустой массив, в логах `LogError` (`LogAssert.Expect`).
- **Негативный:** аспект с `[ReplicatedEvent] Subject<List<int>>` — то же самое для events.
- **Регрессия #5:** два аспекта в разном порядке регистрации дают один и тот же bitmask-порядок после scan.

**4. `ApplyStateBufferRoundTripTests`** — payload write → read

Это немного хитрее, т.к. `ApplyStateBuffer` — private. Варианты:
- Сделать `internal` + `InternalsVisibleTo("ACS.Runtime.Netcode.Tests")` в `AssemblyInfo.cs`.
- Или протестировать через publicly доступный путь (spawn с mock'ом NGO — сложнее).

Выбрать первый вариант.

Сценарии:
- Full payload round-trip: сервер пишет N dirty полей через `OnServerTick` логику (собрать вручную writer), затем клиент читает через `ApplyStateBuffer` — значения совпадают, dirty mask применилась корректно.
- `skipOwnerFields: true` — owner-auth поля пропущены, server-auth применены.
- `skipOwnerFields: false` — все поля применены.
- Mixed binding array: server-auth + owner-auth в одном buffer — ни один не испорчен.
- **Регрессия #2:** bitmask с битом >63 не влияет на поля 0-63.

**5. `NetworkScopeScannerTests`**

- Компонент без атрибута → `NetworkScope.Everywhere`.
- Компонент с `[NetworkScope(ServerOnly)]` → `ServerOnly`.
- Компонент с `[NetworkScope(OwnerOnly)]` → `OwnerOnly`.
- Кэш: второй `GetScope` возвращает то же значение без reflection (проверить инвариант через side-effect или просто по корректности возврата).
- Inheritance: атрибут на base классе наследуется в derived (если `inherit: true` — что сейчас в коде).

### Качество

Из memory feedback: **каждый assert проверяет реальный инвариант, никаких `Assert.IsNotNull` ради галочки, никаких тавтологий.** Для каждого теста задавать себе вопрос: "если удалить эту строку кода в проде, упадёт ли этот тест?". Если нет — тест бесполезен.

### Files to touch

- `Tests/Runtime/ACS.Runtime.Netcode.Tests.asmdef` (новый)
- `Tests/Runtime/*.cs` (5 новых файлов)
- `Runtime/ACS.Runtime.Netcode.asmdef` — добавить `InternalsVisibleTo` если выбран этот путь
- Возможно `Runtime/AspectReplicator.cs` — метод `ApplyStateBuffer` поменять с `private` на `internal`

### Verification

- `dotnet test` или Unity Test Runner (Edit Mode) — все тесты зелёные.
- Coverage не обязателен, но желательно запустить покрытие и убедиться что unit-тестами покрыто минимум 80% `ReplicatedFieldBinding`, `InterpolatedFieldBinding`, `ReplicationScanner`.

### Definition of Done

- [ ] `Tests/Runtime/` создан с 5 файлами тестов.
- [ ] Все тесты зелёные в Unity Test Runner.
- [ ] Для каждого зафиксированного issue #1-#14 (где применимо unit-тест) добавлен регрессионный тест.
- [ ] Ни одного `Assert.IsNotNull` без реального инварианта (проверить вручную).
- [ ] PR создан, CI зелёный.

---

## Батч 3.3 — Architectural Fixes (#22, #16)

**Goal:** закрыть два архитектурных issues — lifecycle и аллокацию в scope-apply.

**Prerequisites:** Батч 3.2 замержен. Без тестов #16 слишком рискованно.

**Объём:** средний. #22 — разминка, #16 — основное.

### Задачи

**1. #22 — `GetComponentsInChildren` pool (делать первым как warm-up)**

`AspectReplicator.cs:196` — завести инстанс-поле:
```csharp
private readonly List<IEntityComponent> _scopeComponentsBuffer = new();
```

Заменить вызов:
```csharp
_scopeComponentsBuffer.Clear();
GetComponentsInChildren(includeInactive: true, _scopeComponentsBuffer);
// теперь _scopeComponentsBuffer — rent'нутый список, использовать его вместо `components`
```

Обновить цикл на `_scopeComponentsBuffer.Count` / `_scopeComponentsBuffer[i]`.

Никаких тестов не нужно — это невидимый перф-фикс. Проверить только что scope всё ещё корректно применяется на ручном playtest'е.

**2. #16 — перенос `OnSubscribe` в `OnEnable`/`OnDisable`**

Это основной кусок батча. Риск: ломает неявный контракт жизненного цикла.

`EntityNetworkComponent.cs` — рефактор:

```csharp
public abstract class EntityNetworkComponent : NetworkBehaviour, IEntityComponent
{
    private EntityContext? _context;
    private DisposableBag _disposables;
    private bool _subscribed;

    protected EntityContext Context => _context ??= GetComponentInParent<EntityContext>();

    protected virtual void Awake()
    {
        EntityInjector.Inject?.Invoke(gameObject);
        AspectInjector.Inject(Context, this);
    }

    protected virtual void OnEnable()
    {
        if (_subscribed) return;
        if (!IsSpawned) return; // подождём OnNetworkSpawn
        OnSubscribe(ref _disposables);
        _subscribed = true;
    }

    protected virtual void OnDisable()
    {
        if (!_subscribed) return;
        _disposables.Dispose();
        _disposables = default;
        _subscribed = false;
    }

    protected virtual void OnSubscribe(ref DisposableBag disposables) { }

    public override void OnNetworkSpawn()
    {
        // Если scope-disable уже поставил enabled=false — подпись не создаём.
        if (!_subscribed && enabled)
        {
            OnSubscribe(ref _disposables);
            _subscribed = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_subscribed)
        {
            _disposables.Dispose();
            _disposables = default;
            _subscribed = false;
        }
    }
}
```

**Нюансы:**
- `OnEnable` Unity вызывает до `OnNetworkSpawn` (при spawn'е объекта). В этот момент `IsSpawned == false` — subscribe нельзя, подождать `OnNetworkSpawn`.
- `OnNetworkSpawn` вызывается после того как `AspectReplicator.ApplyNetworkScopes` мог выставить `enabled=false`. В этом случае `OnNetworkSpawn` триггерится, но `enabled` уже false → subscribe skip'ается. Хорошо.
- `OnDisable` освобождает `_disposables` при любом disable'е (scope или gameplay) — корректное поведение.
- Runtime re-enable (scope или gameplay): `OnEnable` → если `IsSpawned` → subscribe заново. Корректно.
- `_disposables = default` после Dispose — сброс состояния struct'а для возможного повторного использования.
- `OnNetworkDespawn` ещё раз диспозит если подписка была — защита от двойного диспоза.

**Регрессионный тест** (добавить в `Tests/Runtime/`):
- `EntityNetworkComponentLifecycleTests.cs`:
  - Create mock EntityNetworkComponent, Spawn mock → `OnSubscribe` вызывается ровно один раз.
  - Disable → `_disposables` диспозятся, но `OnSubscribe` больше не вызван.
  - Enable → `OnSubscribe` вызывается второй раз.
  - Despawn → финальный dispose.
  - **Регрессия #16:** если до `OnNetworkSpawn` вызвать `enabled = false`, `OnSubscribe` не должен вызваться при `OnNetworkSpawn`.

### Files to touch

- `Runtime/AspectReplicator.cs` — #22
- `Runtime/EntityNetworkComponent.cs` — #16
- `Tests/Runtime/EntityNetworkComponentLifecycleTests.cs` (новый)

### Verification

- Тесты из 3.2 всё ещё зелёные (регрессия не появилась).
- Новые тесты #16 зелёные.
- Ручной playtest: ServerOnly компонент на pure-client'е disabled и НЕ реагирует на изменения аспектов (проверить logging с breakpoint'ом в `OnSubscribe`).
- Ручной playtest: ownership transfer — OwnerOnly компонент корректно реагирует на изменения ownership.

### Definition of Done

- [ ] #22 и #16 закоммичены отдельно.
- [ ] Новый lifecycle-тест покрывает #16.
- [ ] `ISSUES.md` — #22 и #16 помечены `fixed`.
- [ ] Все существующие тесты зелёные.
- [ ] Ручные сценарии пройдены.

---

## Батч 3.4 — Owner-auth Cleanup (#19 + #12a docs)

**Goal:** закрыть owner-auth initial-sync race и задокументировать контракт suppression.

**Prerequisites:** Батч 3.2 (тесты для ReplicatedFieldBinding нужны для верификации).

**Объём:** небольшой-средний, одна сессия.

### Задачи

**1. #19 — `_ownerWroteSinceSpawn` флаг**

`ReplicatedFieldBinding.cs` — добавить в `ReplicatedFieldBinding<T>`:
```csharp
public bool OwnerWroteSinceSpawn { get; private set; }

public override void SubscribeAsAuthority(ref DisposableBag disposables)
{
    _reactive.Subscribe(value =>
    {
        if (_suppressNotification) return;
        IsDirty = true;
        OwnerWroteSinceSpawn = true;  // NEW
    }).AddTo(ref disposables);
}

public void ResetOwnerWroteSinceSpawn()
{
    OwnerWroteSinceSpawn = false;
}
```

Выставить абстрактно в `ReplicatedFieldBinding` (base):
```csharp
public virtual bool OwnerWroteSinceSpawn => false;
public virtual void ResetOwnerWroteSinceSpawn() { }
```

`AspectReplicator.ApplyStateBuffer` — обновить сигнатуру, чтобы принимать predicate вместо bool:
```csharp
private void ApplyStateBuffer(byte[] payload, Func<int, bool>? shouldSkip)
```

Или дешевле — два отдельных метода / inline вариант для initial-sync без дженериков.

`SendInitialStateRpc` (`:385`) — заменить `ApplyStateBuffer(payload, skipOwnerFields: false)` на predicate-версию:
```csharp
ApplyStateBuffer(payload, shouldSkip: i =>
    _bindingAuthorities[i] == AuthorityMode.Owner
    && _bindings[i].OwnerWroteSinceSpawn);
```

`OnGainedOwnership` / `OnLostOwnership` — при gained ownership сбросить флаг для всех owner-auth bindings:
```csharp
public override void OnGainedOwnership()
{
    for (int i = 0; i < _bindings.Length; i++)
    {
        if (_bindingAuthorities[i] == AuthorityMode.Owner)
            _bindings[i].ResetOwnerWroteSinceSpawn();
    }
    ReapplyOwnerScope();
}
```

**Аккуратно:** `OnGainedOwnership` сейчас есть в `AspectReplicator.cs:190` как одностроковый `=> ReapplyOwnerScope();` — раскрыть в полный блок.

**2. Тесты**

Добавить в `Tests/Runtime/ReplicatedFieldBindingTests.cs`:
- `OwnerWroteSinceSpawn` изначально false.
- После `_reactive.Value = ...` когда authority и `!_suppressNotification` — становится true.
- После `WriteSuppressed(...)` — НЕ становится true (`_suppressNotification` гардит).
- `ResetOwnerWroteSinceSpawn` сбрасывает в false.

Добавить в `Tests/Runtime/ApplyStateBufferRoundTripTests.cs`:
- **Регрессия #19:** initial-sync с `shouldSkip` predicate пропускает owner-auth поле, если флаг true; применяет если false.

**3. #12a — документация в `DESIGN.md`**

Найти секцию "Layer 0 — Replication" в `DESIGN.md`, добавить врезку про suppression контракт:

> **Suppression contract.** Subscribe callback в `ReplicatedFieldBinding<T>` обязан уважать `_suppressNotification` флаг. Это нужно для owner-auth initial-sync: когда pure-client owner получает `SendInitialStateRpc`, `WriteSuppressed` пишет в `_reactive.Value`, что триггерит subscribe callback; без suppression owner пометил бы поле dirty и отправил snapshot обратно на сервер — echo loop. В `ReplicatedEventBinding<T>` suppression удалён (не нужен — см. `ISSUES.md#12b`).

### Files to touch

- `Runtime/ReplicatedFieldBinding.cs`
- `Runtime/AspectReplicator.cs` — `ApplyStateBuffer`, `SendInitialStateRpc`, `OnGainedOwnership`
- `Tests/Runtime/ReplicatedFieldBindingTests.cs` — новые тесты
- `Tests/Runtime/ApplyStateBufferRoundTripTests.cs` — регрессия #19
- `DESIGN.md` — документация контракта

### Verification

- Все тесты зелёные.
- Ручной integration: late-join owner который уже локально писал — запись сохраняется, снапшот не перетирает. Это сложно воспроизвести ручками, но минимум проверить что обычный late-join flow всё ещё работает.

### Definition of Done

- [ ] #19 закоммичен.
- [ ] Документация #12a добавлена в DESIGN.md.
- [ ] `ISSUES.md` — #19 помечен `fixed`, #12 → #12a помечен как `documented`, вся запись #12 обновлена.
- [ ] Новые тесты зелёные.
- [ ] Все существующие тесты зелёные.

---

## Батч 3.5 — Integration Tests (#17 партия 2)

**Goal:** покрыть network-specific сценарии которые нельзя тестировать pure unit'ами.

**Prerequisites:** Батчи 3.3 и 3.4 замержены (архитектура стабилизировалась).

**Объём:** большой, отдельная сессия (или две). Требует NGO test fixtures setup — может быть не тривиально.

### Подготовка

Выбрать подход к NGO fixtures. Варианты:
1. **Unity Multiplayer Play Mode tests** — встроенный NGO testing framework. `NetcodeIntegrationTest` base class. Умеет создавать multi-instance network в одном editor процессе.
2. **Самодельный fixture** — `NetworkManager.Singleton` + `StartHost` + manual tick. Проще но менее реалистично.

Рекомендую вариант 1 если NGO версия поддерживает (проверить в `Packages/com.unity.netcode.gameobjects`).

### Задачи (тесты)

**1. `AspectReplicatorLifecycleTests`**
- Spawn с корректным `EntityContext` → binding'и создаются, tick subscriptions активны.
- Spawn без `EntityContext` → LogError, NO NRE при любом RPC (регрессия #15).
- Despawn → tick unsubscribed, disposables диспозятся.
- Re-spawn того же NetworkObject (если NGO поддерживает pooling) → всё работает.
- Спавн аспекта с >64 полями → clamp, нет out-of-range (регрессия #2).
- Спавн аспекта с >256 событиями → clamp (регрессия #18).

**2. `AspectReplicatorStateSyncTests`**
- Server пишет в server-auth поле → broadcast → client применил (полное совпадение).
- Server пишет несколько полей в один tick → один RPC с корректной dirty mask.
- Late-join: новый клиент получил snapshot всех полей (в том числе не-dirty никогда) (регрессия #1).
- Multiple late-joins подряд не ломают состояние (регрессия #1 edge case).
- `ClearDirty` после broadcast — повторных сообщений нет.

**3. `AspectReplicatorOwnerAuthTests`**
- Pure-client owner пишет owner-auth поле → SubmitOwnerStateRpc → server получил → broadcast → third client применил.
- Owner пишет в server-auth поле → LogWarning, поле не релеится.
- Host-owner пишет owner-auth поле → broadcast напрямую без SubmitOwnerStateRpc (pattern из кода).
- **Регрессия #19:** owner пишет локально, потом получает SendInitialStateRpc → локальное значение сохранено.

**4. `AspectReplicatorScopeTests`**
- Компонент с `[NetworkScope(ServerOnly)]` на pure-client → `enabled == false`.
- Компонент с `[NetworkScope(OwnerOnly)]` на non-owner → `enabled == false`.
- Ownership transfer → OwnerOnly перевключается.
- **Регрессия #3:** nested `NetworkObject` → его компоненты не задеты scope'ом родителя.
- **Регрессия #16:** если ServerOnly компонент подписался до `ApplyNetworkScopes`, после disable его подписки не срабатывают.

**5. `AspectReplicatorEventTests`**
- Server-auth event: server fires → все клиенты получили OnNext.
- Owner-auth event: owner fires → server → non-owner clients получили, owner получил только локально (без двойного).
- Reliable vs Unreliable routing (проверить что RPC атрибуты отрабатывают).
- Host fires → только non-host peers получают (IsHost return guard).

### Files to touch

- `Tests/Runtime/AspectReplicatorLifecycleTests.cs` (новый)
- `Tests/Runtime/AspectReplicatorStateSyncTests.cs` (новый)
- `Tests/Runtime/AspectReplicatorOwnerAuthTests.cs` (новый)
- `Tests/Runtime/AspectReplicatorScopeTests.cs` (новый)
- `Tests/Runtime/AspectReplicatorEventTests.cs` (новый)
- Возможно shared fixture `NetcodeFixture.cs` с helper'ами для spawn/despawn/tick.

### Verification

- Все тесты зелёные в Unity Test Runner (Play Mode).
- Coverage replication path ≥ 70%.

### Definition of Done

- [ ] NGO test fixture работает.
- [ ] 5 test suites созданы, все тесты зелёные.
- [ ] Каждый зафиксированный issue #1-#19 имеет либо unit, либо integration регрессионный тест.
- [ ] `ISSUES.md` — #17 помечен `fixed` (или `partial` если какие-то категории отложены).

---

## Батч 3.6 — GC Hotpath Refactor (#6, #7, #8, #10, #11)

**Goal:** единый серверный replication system, убирающий per-tick/per-event аллокации и лишние подписки на Tick.

**Prerequisites:**
1. Батч 3.5 замержен (integration тесты нужны для верификации).
2. **Profiler GC Alloc на реальной сцене показал, что это реальный bottleneck.** Без этого пункта — НЕ начинать. Возможно MVP вообще не упрётся в этот перф.

**Объём:** Hard × Hard. Multi-session. Самый рискованный батч.

### Задачи

**1. Design phase**

Спроектировать `AspectReplicationSystem` (pure C# class, VContainer singleton). Решения принять на этапе:
- Один раз подписывается на `NetworkManager.NetworkTickSystem.Tick`.
- Держит `List<AspectReplicator>` активных replicator'ов.
- `AspectReplicator.OnNetworkSpawn/OnNetworkDespawn` регистрируется/снимается.
- На каждом tick'е собирает dirty от всех replicator'ов.
- Отправляет **одно** сообщение на tick через `CustomMessagingManager.SendNamedMessage` (или `NativeArray`-based RPC если NGO поддерживает).
- Внутри сообщения — список `(NetworkObjectId, dirtyMask, payload)` для каждого dirty replicator'а.
- Клиент роутит обратно по `NetworkObjectId` в конкретный `AspectReplicator.ApplyStateBuffer`.

Аналогично для events: события буферизуются и шлются batched.

**Это — отдельный design doc, не просто код.** Обновить `DESIGN.md` с новой архитектурой Layer 0.

**2. Implementation phase**

В зависимости от выбранного подхода (см. `ISSUES.md#6` варианты 1/2/3):
- **Вариант 2 (NativeArray RPC):** меньше рефакторинг, но всё ещё per-entity RPC. Решает #7, частично #8, не решает #10.
- **Вариант 3 (CustomMessagingManager + centralized system):** полный рефактор. Решает всё сразу. Больше работы.

Рекомендую **вариант 3** — раз уж взялись, делать до конца.

Удалить из `AspectReplicator`:
- `OnServerTick` / `OnOwnerTick` (переезжают в систему).
- Tick subscriptions.
- `BroadcastStateRpc` / `SendInitialStateRpc` (переезжают в систему).
- Broadcaster делегаты (закрывает #11).

Оставить в `AspectReplicator`:
- Binding collection (scan и init).
- `ApplyStateBuffer` (принимает от системы).
- Ownership handling.

**3. Тесты**

Запустить все тесты из 3.2 и 3.5. Ничего не должно упасть.

Добавить тесты специфичные для системы:
- Batching: два dirty replicator'а в один tick → одно сообщение, оба применились.
- Registration: register/unregister работает корректно.
- Нет memory leak'ов (проверить через repeated spawn/despawn).

Дополнительно замерить **GC alloc после** рефактора — должно быть заметно меньше, иначе рефактор бесполезен.

### Verification

- Все тесты из 3.2, 3.5 + новые — зелёные.
- Profiler: GC Alloc per tick снизился минимум в 10x на сцене с 50 replicated сущностями.
- Playtest на 50+ сущностях — визуально нет проблем с репликацией.

### Definition of Done

- [ ] `DESIGN.md` обновлён с новой архитектурой Layer 0.
- [ ] `AspectReplicationSystem` реализован.
- [ ] `AspectReplicator` упрощён.
- [ ] Все тесты зелёные.
- [ ] Профайлер подтвердил улучшение.
- [ ] `ISSUES.md` — #6, #7, #8, #10, #11 помечены `fixed`.

---

## Батч 3.7 — IL2CPP Full Fix (#14)

**Goal:** закрыть AOT-инстанциацию для кастомных unmanaged типов на IL2CPP.

**Prerequisites:** нет технических. Но желательно — первый IL2CPP билд проекта, чтобы увидеть реальные падения.

**Объём:** средний. Одна сессия.

### Задачи

**1. Expression.Compile фабрики**

Заменить `Activator.CreateInstance` в `ReplicatedFieldBindingFactory.Create` и `ReplicatedEventBindingFactory.Create` на compiled expressions:

```csharp
private static readonly Dictionary<Type, Func<object, ReplicatedFieldBinding>> FieldFactories = new();
private static readonly Dictionary<Type, Func<object, Lerp<float>, ReplicatedFieldBinding>> InterpFactories = new();

private static Func<object, ReplicatedFieldBinding> BuildFieldFactory(Type valueType)
{
    var bindingType = typeof(ReplicatedFieldBinding<>).MakeGenericType(valueType);
    var reactiveType = typeof(ReactiveProperty<>).MakeGenericType(valueType);
    var ctor = bindingType.GetConstructor(new[] { reactiveType })!;
    
    var param = Expression.Parameter(typeof(object), "reactive");
    var casted = Expression.Convert(param, reactiveType);
    var newExpr = Expression.New(ctor, casted);
    var lambda = Expression.Lambda<Func<object, ReplicatedFieldBinding>>(newExpr, param);
    return lambda.Compile();
}
```

Плюс: быстрее, чем Activator (после первого вызова).

**2. AOT hints**

Создать `AotHints.cs`:
```csharp
[Preserve]
internal static class AotHints
{
    [Preserve]
    private static void HintsNeverCalled()
    {
        // Force IL2CPP to emit instantiations for common types
        _ = new ReplicatedFieldBinding<int>(null!);
        _ = new ReplicatedFieldBinding<float>(null!);
        _ = new ReplicatedFieldBinding<bool>(null!);
        _ = new ReplicatedFieldBinding<Vector2>(null!);
        _ = new ReplicatedFieldBinding<Vector3>(null!);
        _ = new ReplicatedFieldBinding<Vector4>(null!);
        _ = new ReplicatedFieldBinding<Quaternion>(null!);
        _ = new ReplicatedFieldBinding<Color>(null!);
        _ = new InterpolatedFieldBinding<float>(null!, null!);
        _ = new InterpolatedFieldBinding<Vector3>(null!, null!);
        _ = new InterpolatedFieldBinding<Quaternion>(null!, null!);
        _ = new ReplicatedEventBinding<int>(null!, default, default);
        _ = new ReplicatedEventBinding<float>(null!, default, default);
        // ... все поддерживаемые встроенные типы
    }
}
```

**3. Документация**

В README.md добавить секцию "IL2CPP Support":
> Если вы используете кастомный unmanaged struct в `[ReplicatedState]` или `[ReplicatedEvent]`, добавьте hint в `link.xml` или в собственный `AotHints.cs` в user-коде. Встроенные Unity value-типы (int, float, Vector3 и т.д.) закрыты автоматически.

С примером `link.xml`.

### Files to touch

- `Runtime/ReplicatedFieldBinding.cs` — factory замена
- `Runtime/ReplicatedEventBinding.cs` — factory замена
- `Runtime/AotHints.cs` (новый)
- `README.md` — секция IL2CPP

### Verification

- Все существующие тесты всё ещё зелёные.
- Если возможно — сделать IL2CPP билд проекта и проверить что Replication работает.

### Definition of Done

- [ ] Factories переведены на Expression.Compile.
- [ ] AotHints.cs создан.
- [ ] README обновлён.
- [ ] `ISSUES.md` — #14 помечен `fixed`.

---

## Что НЕ делать

- **Не бандлить issues из разных батчей в одном PR.** Каждый батч — отдельный PR.
- **Не начинать 3.6 без профайлера.** Может оказаться не bottleneck'ом.
- **Не трогать #16 и #19 без батча 3.2.** Регрессионная сетка обязательна.
- **Не переставлять 3.3 и 3.4 местами** — 3.3 меняет lifecycle, 3.4 зависит от стабильного lifecycle.
- **Не писать "на всякий случай" тесты в 3.2.** Только критические пути из `ISSUES.md`. Остальное — в 3.5.
- **Не трогать `_suppressNotification` в `ReplicatedFieldBinding`** при удалении из events (#12b). Поле живое.

---

## Использование этого файла

Скопируй целый батч в промпт агенту:
> Выполни батч 3.1 из `com.rubickanov.acs.netcode/ROADMAP.md`. Все prerequisites выполнены. Верни список созданных коммитов.

Агент должен прочитать ROADMAP.md, относящийся батч, ISSUES.md с детальными описаниями проблем, и начать работу.
