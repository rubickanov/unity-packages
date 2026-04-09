# ACS Netcode — Known Issues

Только по уже реализованным шагам (Layer 0 state, Layer 0.5 events, Layer 1.5 NetworkScope).
Owner-auth, интерполяция, prediction — отдельно, в DESIGN.md.

Приоритет = важность × сложность. Чем выше в таблице — тем раньше стоит делать.

| # | Проблема | Важность | Сложность | Статус | Файл |
|---|----------|----------|-----------|--------|------|
| 2 | >64 полей молча портят dirty-mask | Критично | Easy | **fixed** | `AspectReplicator.cs:79-84` |
| 5 | Порядок аспектов в bitmask не стабилизирован | Критично | Easy | **fixed** | `AspectReplicator.cs:45-50` |
| 3 | `ApplyNetworkScopes` пересекает nested `NetworkObject` | Критично | Easy | **fixed** | `AspectReplicator.cs:148-150` |
| 1 | Нет initial-sync для поздних клиентов | Критично | Medium | **fixed** | `AspectReplicator.RequestInitialStateRpc / SendInitialStateRpc` |
| 4 | `(Behaviour)component` NRE/InvalidCast | Высокая | Easy | **fixed** | `AspectReplicator.cs:145-146` |
| 9 | `FastBufferWriter` autogrow, не pre-sized | Высокая | Easy | **fixed** | `AspectReplicator._statePayloadCap`, `ReplicatedFieldBinding.Size` |
| 7 | Аллокация `byte[]` на каждый event OnNext | Высокая | Medium | open | `ReplicatedEventBinding.cs:48-63` |
| 6 | Аллокация `byte[]` на каждый server tick | Высокая | Hard | open | `AspectReplicator.cs:265, 301, 375` |
| 15 | RPC на полу-инициализированном replicator'е (NRE при missing EntityContext) | Высокая | Medium | open | `AspectReplicator.cs:14-17, 42-47` |
| 16 | Подписки NetworkScope-компонентов срабатывают после disable (NGO spawn-order race) | Высокая | Medium | open | `AspectReplicator.cs:32-40`, `EntityNetworkComponent.cs:31-34` |
| 17 | 0% покрытие тестами | Высокая | Hard | open | весь пакет |
| 8 | `FastBufferReader(payload, Temp)` копирует managed→native | Средняя | Medium | open | `AspectReplicator.cs:311, 405, 483, 511` |
| 14 | `Activator.CreateInstance` vs IL2CPP | Средняя | Easy | **partial** | `ReplicatedFieldBinding.cs`, `ReplicatedEventBinding.cs` |
| 10 | Per-entity `NetworkTickSystem.Tick` подписка | Средняя | Medium | open | `AspectReplicator.cs:154, 159` |
| 18 | `_eventBindings` не обрезается при >256 (ломает симметрию с #2) | Средняя | Easy | open | `AspectReplicator.cs:126-127` |
| 19 | Owner-auth late-join race (`_ownerWroteSinceSpawn` флаг) | Средняя | Medium | open | `AspectReplicator.cs:384-399` |
| 20 | Нет null-check для `ReactiveProperty`/`Subject` field values | Средняя | Easy | open | `AspectReplicator.cs:64, 83` |
| 21 | Scanner не валидирует `unmanaged` constraint — криптический error | Средняя | Easy | open | `ReplicationScanner.cs:92, 127` |
| 22 | `GetComponentsInChildren` аллокация в `ApplyNetworkScopes` | Средняя | Medium | open | `AspectReplicator.cs:196` |
| 23 | LINQ `.OrderBy` в `OnNetworkSpawn` | Средняя | Easy | open | `AspectReplicator.cs:57-58` |
| 11 | Четыре broadcaster-делегата на каждый spawn | Низкая | Easy | open | `AspectReplicator.cs:131-134` |
| 13 | Нет null-check `EntityContext` | Низкая | Easy | **fixed** | `AspectReplicator.cs:34-39` |
| 12 | `_suppressNotification` — живой для fields, dead для events | Низкая | Easy | **partial** | `ReplicatedFieldBinding.cs:36`, `ReplicatedEventBinding.cs:33` |

---

## Критично

### #2. >64 полей молча портят dirty-mask — **fixed (2026-04-09)**
**Проблема.** `AspectReplicator.cs:61` логирует error про лимит 64, но выполнение не прерывается. В `OnServerTick` делается `dirtyMask |= 1UL << i`; C# shift-оператор для `i >= 64` работает как `i & 63`, то есть поле 64 алиасит бит 0, поле 65 — бит 1, и т.д. Клиент будет писать значения не в те поля.

**Фикс.** `AspectReplicator.OnNetworkSpawn` после error-лога делает `Array.Resize` на 64 элемента сразу для двух массивов: `_bindings` и `_bindingAuthorities` (появился вместе с owner-auth). Поля сверх лимита детерминированно отбрасываются — порядок уже стабилен за счёт сортировки аспектов и внутриаспектной сортировки полей по имени.

---

### #5. Порядок аспектов в dirty-mask не стабилизирован — **fixed (2026-04-09)**
**Проблема.** Внутри одного аспекта поля сортируются по имени (`ReplicationScanner.cs:105/140`) — корректно. Но внешний цикл `context.GetAllAspects()` возвращает `Dictionary<Type,object>.Values` в insertion-order, и insertion-order зависит от того, в каком порядке компоненты вызывают `Context.Require<T>()` в `Awake()`. Сейчас работает по совпадению (одинаковый префаб → одинаковый Awake-порядок), но:
- Любой условный `Require` (компонент присутствует только на одной стороне) смещает индексы.
- `NetworkScope.ServerOnly` компоненты всё равно получают `Awake` до `ApplyNetworkScopes` — ок сейчас, но ломкая инвариантность.
- Нет никакой документации, что этот порядок обязан быть стабильным.

**Фикс.** `AspectReplicator.OnNetworkSpawn` перед foreach-ом оборачивает `context.GetAllAspects()` в `OrderBy(a => a.GetType().FullName, StringComparer.Ordinal)`. Внутриаспектная сортировка по имени полей в `ReplicationScanner` осталась нетронутой. Теперь порядок полностью детерминирован от типов, а не от порядка `Awake`.

---

### #3. `ApplyNetworkScopes` пересекает nested `NetworkObject` — **fixed (2026-04-09)**
**Проблема.** `AspectReplicator.cs:102` — `GetComponentsInChildren<IEntityComponent>(includeInactive: true)` идёт по всей иерархии без остановки на child `NetworkObject`. Если в префабе есть вложенные сетевые сущности (пушка как отдельный NO, vehicle seats, attachments) — scope родителя будет отключать компоненты на чужой сетевой identity по чужому `IsServer`/`IsOwner`.

**Фикс.** `ApplyNetworkScopes` теперь сохраняет `var myNetworkObject = NetworkObject;` в начале и внутри цикла отбрасывает любой компонент, у которого `GetComponentInParent<NetworkObject>() != myNetworkObject`. Компоненты на корне и на Visual-детях без собственного NO пробегают фильтр (у них ближайший родительский NO — это и есть `this`); компоненты внутри вложенных NO — отсекаются. Защищает на будущее (сейчас nested NO в проекте нет, фикс профилактический).

---

### #1. Нет initial-sync для поздних клиентов — **fixed (2026-04-09)**
**Проблема.** `OnServerTick` шлёт только *dirty* поля. Клиент, который спавнит существующую сущность (join in progress, respawn, network relevancy), получает `default(T)` до первой последующей модификации. Поля типа `MaxHealth`, `WeaponId`, `TeamColor` могут уже никогда не меняться → на этом клиенте они так и останутся дефолтными.

**Фикс.** Вариант 1 из исходного плана (client-side pull).

- В `OnNetworkSpawn` не-сервер после вычисления `_statePayloadCap` шлёт `RequestInitialStateRpc` на сервер (гвард: только `!IsServer && _bindings.Length > 0`).
- Сервер собирает полный snapshot (`fullMask = (1UL << N) - 1`, со спец-кейсом `N == 64 → ulong.MaxValue`), кладёт `serverTick` и `dirtyMask` в тот же формат, что и `BroadcastStateRpc`, и отвечает через `SendInitialStateRpc` с `SendTo.SpecifiedInParams` (таргет — `RpcTarget.Single(senderClientId, Temp)`).
- Клиент применяет snapshot через общий хелпер `ApplyStateBuffer`, извлечённый из `BroadcastStateRpc`.

**Владельческие поля.** `SendInitialStateRpc` вызывает `ApplyStateBuffer(payload, skipOwnerFields: false)` — в отличие от broadcast-пути, где `skipOwnerFields: IsOwner`. Причина: на спавне pure-client owner имеет локально `default(T)` у owner-auth полей (`ReactiveProperty<T>` только что создан), а сервер может держать непустое значение, выставленное до передачи ownership (напр. `WeaponId`, заданный сервером). Скип заставил бы owner'a навечно остаться в `default`. Плата за честный apply — теоретический race ~RTT, где owner успел локально записать до прихода snapshot-а и snapshot перезапишет его. Эта часть помечена TODO и должна быть закрыта вместе с Owner-auth re-analysis (#12) через per-binding флаг `ownerHasWrittenLocally`.

**Ordering.** NGO гарантирует per-connection send order. Если между client-spawn и request'ом сервер успел послать `BroadcastStateRpc` — client применит broadcast первым, потом snapshot; snapshot собирается на сервере в момент обработки запроса (после broadcast-а) → значения в snapshot-е ≥ broadcast-а, перезапись корректна.

**Async-природа.** Initial-sync асинхронный — несколько ms (RTT ×2) клиент видит `default(T)`. Компоненты не должны полагаться на актуальные значения реплицированных полей в `OnEnable` / первом `Update` на поздних клиентах; подписываться на `ReactiveProperty.Subscribe(...)` — единственный корректный способ.

---

## Высокая важность

### #4. `(Behaviour)component` NRE/InvalidCast — **fixed (2026-04-09)**
**Проблема.** `AspectReplicator.cs:111` кастит `IEntityComponent` в `Behaviour` без проверки. Интерфейс пустой, и в ACS нигде не форсится, что `IEntityComponent` обязан быть `MonoBehaviour`. Pure-C# компонент, реализующий интерфейс, уронит spawn.

**Фикс.** В `ApplyNetworkScopes` жёсткий каст заменён на `if (component is not Behaviour behaviour) continue;` — не-Behaviour реализации просто пропускаются. В текущем коде все реализации IEntityComponent — это `MonoBehaviour`/`NetworkBehaviour`, так что поведение не изменилось, но теперь поломать spawn невозможно.

---

### #9. `FastBufferWriter` autogrow, не pre-sized — **fixed (2026-04-09)**
**Проблема.** `AspectReplicator` три раза создавал writer как `new FastBufferWriter(256, Temp, int.MaxValue)`. Если payload больше 256 байт (много полей или крупные структуры), writer реаллоцирует внутри. Размер каждого binding известен на scan-е (`sizeof(T)`).

**Фикс.**
- В `ReplicatedFieldBinding` добавлен абстрактный `public abstract int Size { get; }`. В `ReplicatedFieldBinding<T>` (где `T : unmanaged`) реализован как `public override unsafe int Size => sizeof(T);` — компайл-тайм константа для каждой инстанциации. `InterpolatedFieldBinding<T>` наследует.
- В `AspectReplicator` закэшировано `_statePayloadCap = sizeof(int) + sizeof(ulong) + Σ(_bindings[i].Size)` (worst case = все поля dirty, для server-broadcast формата с `serverTick + dirtyMask`). Вычисляется сразу после `>64` clamp и до tick-subscribe / initial-sync request.
- `OnServerTick`, `OnOwnerTick` и `RequestInitialStateRpc` используют `new FastBufferWriter(_statePayloadCap, Allocator.Temp)` — fixed capacity без autogrow (третий аргумент `maxSize = -1` по умолчанию → `MaxCapacity = size`, writer кинет exception при overflow вместо реаллока).
- `OnOwnerTick` слегка over-allocate'ит (включает `sizeof(int) serverTick`, хотя owner не пишет serverTick) — 4 байта slack'а на Temp-аллокацию, игнорируемо.

---

### #7. Аллокация `byte[]` на каждый event OnNext
**Проблема.** `ReplicatedEventBinding<T>.OnLocalEvent` (cs:49, 54) — `new FastBufferWriter` + `writer.ToArray()` каждое срабатывание `Subject.OnNext`. Для частых событий (footsteps, hit markers, bullet traces) это постоянный GC.

**Решение (в порядке усложнения).**
1. **Pool writer.** Один thread-local `FastBufferWriter` на `AspectReplicator`, `Seek(0)` перед записью. Убирает аллокацию writer-а, но `ToArray()` остаётся, т.к. NGO RPC принимает managed `byte[]`.
2. **Поменять сигнатуру RPC на `NativeArray<byte>` или `byte*`+length.** NGO поддерживает `NativeArray` как параметр RPC — его можно передавать без copy. Уберёт и второй allocation.
3. **Перейти на `CustomMessagingManager.SendNamedMessage`** — принимает `FastBufferWriter` напрямую, без промежуточного managed-массива. Но теряется удобство `[Rpc]` IL-кодегена; придётся вручную роутить по `NetworkObjectId`.

Старт с варианта 2: самое большое улучшение за наименьший рефактор.

---

### #6. Аллокация `byte[]` на каждый server tick
**Проблема.** `AspectReplicator.cs:265` (OnServerTick), `:301` (OnOwnerTick), `:375` (RequestInitialStateRpc) — `writer.ToArray()` каждый tick на каждую сущность. 60 Hz × 50 replicated сущностей = ~3000 массивов/сек только из OnServerTick. DESIGN.md знает как TODO (Layer 5), но уже сейчас hot-path.

**Решение.** То же, что #7, вариант 2 или 3. Вариант 3 (`CustomMessagingManager`) тут практичнее, т.к. state-broadcast и так один на entity — можно свести всё в один серверный менеджер, который раз в tick собирает dirty от всех зарегистрированных replicator-ов и шлёт одним сообщением. Это же частично покрывает #10.

**Важно:** прежде чем делать, замерить (Profiler GC Alloc). Возможно, текущего уровня достаточно для MVP, и стоит отложить до реальных нагрузочных тестов.

---

### #15. RPC на полу-инициализированном `AspectReplicator` — **fixed (2026-04-09)**
**Проблема.** Фикс #13 добавил `if (context == null) { LogError; return; }` сразу после `GetComponent<EntityContext>()` (`AspectReplicator.cs:42-47`). Но поля `_bindings`, `_bindingAuthorities`, `_eventBindings`, `_reliableBroadcaster` и т.д. объявлены как `= null!` (`cs:14-17, 27-30`) и в момент раннего return остаются **null**.

`NetworkObject` при этом уже заспавнен на сети — сервер и другие клиенты могут прислать RPC на эту сущность:
- `BroadcastStateRpc` (:339) → `ApplyStateBuffer` (:309) → `_bindings.Length` → **NRE**
- `SendInitialStateRpc` (:385) → то же самое → **NRE**
- `SubmitOwnerStateRpc` (:403) → итерация `_bindings` → **NRE**
- `DispatchEvent` (:494) / `HandleOwnerEvent` (:459) → `_eventBindings.Length` → **NRE**
- `RequestInitialStateRpc` (:352) → `if (_bindings.Length == 0)` → **NRE** прямо в guard

Полезный `LogError` про missing context затеряется в стеке повторяющихся NRE'шек на каждый incoming RPC.

**Решение.** Заинициализировать массивы в field declarations, как уже сделано с `_interpolatedBindings` и `_ownerScopedComponents`:
```csharp
private ReplicatedFieldBinding[] _bindings = Array.Empty<ReplicatedFieldBinding>();
private AuthorityMode[] _bindingAuthorities = Array.Empty<AuthorityMode>();
private ReplicatedEventBinding[] _eventBindings = Array.Empty<ReplicatedEventBinding>();
```
Ранний return оставит их пустыми; RPC handler'ы отработают no-op (итерация по пустому массиву). `_statePayloadCap` останется 0, но `OnServerTick` гардится через `if (dirtyMask == 0) return;` раньше создания writer'а, так что fixed-capacity writer с нулевым размером не аллоцируется. Broadcaster-делегаты остаются `null!`, но они читаются только из subscribe-loop'а, который с пустым `_eventBindings` не выполняется.

---

### #16. Подписки `[NetworkScope]`-компонентов срабатывают после disable
**Проблема.** `ApplyNetworkScopes` отрабатывает в начале `AspectReplicator.OnNetworkSpawn` (`cs:40`) и выставляет `behaviour.enabled = false` для компонентов с `ServerOnly`/`OwnerOnly` на "не тех" peer'ах. Но NGO **не гарантирует порядок вызова `OnNetworkSpawn`** между `NetworkBehaviour`'ами на одном `NetworkObject`. Если `EntityNetworkComponent.OnNetworkSpawn` (`EntityNetworkComponent.cs:31-34`) отработал **до** `AspectReplicator.OnNetworkSpawn`, его `OnSubscribe(ref _disposables)` уже вызван — подписки на R3 аспекты активны в `_disposables`. Дальше `AspectReplicator` выставляет `enabled = false`.

Комментарий в `AspectReplicator.cs:36-39` говорит "Update() will still be suppressed, and its DisposableBag is released on OnNetworkDespawn". Это неполная правда: **R3 подписки не проверяют `behaviour.enabled`** — они продолжают срабатывать на каждое изменение аспекта вплоть до `OnNetworkDespawn`. Логика, которая должна бежать только на сервере, может отработать на pure-client, и наоборот.

**Пример десинка.** Компонент с `[NetworkScope(ServerOnly)]` подписан на `HealthAspect.IncomingDamage` и применяет урон к `HealthAspect.Health`. На клиенте компонент disabled, но подписка активна → при получении `IncomingDamage` через network клиент сам уменьшает `Health` локально. Параллельно сервер тоже уменьшает и шлёт broadcast с новым `Health`. На клиенте здоровье уменьшается дважды.

**Важно:** сценарий сейчас "работает по совпадению" — если все network-компоненты на префабе добавлены *после* `AspectReplicator`, NGO в большинстве случаев вызывает их `OnNetworkSpawn` в порядке компонентов в объекте. Это хрупкий инвариант, который никак не форсится.

**Решение (варианты, в порядке предпочтения).**

1. **Defer subscribe в `OnEnable`/`OnDisable`.** Перенести `OnSubscribe(ref _disposables)` из `OnNetworkSpawn` в `OnEnable`, а `_disposables.Dispose()` — в `OnDisable`. Тогда scope-disable (выставленный синхронно из `ApplyNetworkScopes`) вызовет `OnDisable` → подписки освободятся. Плюс: совпадает с тем, как это работает в не-network `EntityComponent`. Минус: нужно аккуратно отличать scope-disable от gameplay-disable (обе идут через `OnDisable`), но по факту обе должны освобождать подписки, так что разницы нет.

2. **Script Execution Order.** Выставить `DefaultExecutionOrder` на `AspectReplicator` на значение меньше дефолтного (например −1000), а на `EntityNetworkComponent` — больше дефолтного (+100). Это **не гарантирует** порядок `OnNetworkSpawn` (NGO дёргает их в своём цикле), но гарантирует порядок `Awake`/`OnEnable`. Сработает только в комбинации с вариантом 1, сам по себе — нет.

3. **Централизованный spawn gate.** Отдельный интерфейс `INetworkScopeAware`, `AspectReplicator` вызывает scope-apply синхронно из своего `Awake`, а не в `OnNetworkSpawn`. Плюс: гарантированно до любых подписок на аспекты в `OnNetworkSpawn` компонентов. Минус: ломает API.

Рекомендую вариант 1 (+ опционально script order как страховку).

---

### #17. Покрытие тестами — 0%
**Проблема.** В `com.rubickanov.acs.netcode` **нет папки `Tests/`**, нет тест-asmdef, нет ни одного теста. Все фиксы из первого и второго батча 2026-04-09 держатся исключительно на ручном playtest'е. Core-пакет `com.rubickanov.acs` имеет юнит-тесты (`EntityContextTests`, `AspectInjectorTests`, `EntityInjectorTests`) — шаблон уже есть, можно копировать структуру.

**Критичные непокрытые зоны.**
- **Сериализация round-trip.** `ReplicatedFieldBinding<T>.WriteTo`/`ReadFrom`/`Skip` для всех unmanaged типов. Байтовый round-trip `struct → FastBufferWriter → FastBufferReader → struct` тривиально тестируется без NGO.
- **`ReplicationScanner`.** Стабильность порядка полей, наследование через base type, кэширование, негативные тесты с managed `T` (см. #21).
- **`InterpolatedFieldBinding<T>`.** Edge cases: пустой буфер, 1 snapshot, 2 snapshots, render time до oldest/после newest/точно на snapshot'е, 32+ snapshot'а (wraparound). Lerp корректности: `(a=0, b=10, t=0.5) → 5` для float/Vector3/Quaternion (Slerp).
- **Dirty mask construction.** Поле становится dirty при write, `ClearDirty` сбрасывает, >64 поля корректно обрезаются (регрессия #2), skipOwnerFields пропускает только owner-auth (регрессия #1 owner split).
- **`ApplyStateBuffer`.** Round-trip payload → apply на втором binding[].
- **`NetworkScopeScanner`.** Cache + дефолт `Everywhere` + наследование attribute'а.

**Непокрытые integration-зоны** (требуют NGO test fixtures — сложнее).
- spawn → dirty → broadcast → apply на втором peer;
- late-join → RequestInitialStateRpc → SendInitialStateRpc → полный snapshot совпадает с серверным состоянием;
- owner-auth write → SubmitOwnerStateRpc → Broadcast → apply на third peer (owner skip, other no-skip);
- ownership transfer → `ReapplyOwnerScope` → корректный `enabled`;
- nested NetworkObject (регрессия #3).

**План.**
1. Создать `Tests/Runtime/` с `ACS.Runtime.Netcode.Tests.asmdef` (EditMode + опциональный PlayMode).
2. **Партия 1 — pure unit, без NGO:**
   - `ReplicatedFieldBindingTests`: round-trip всех Unity value-типов (`int`, `float`, `bool`, `Vector2/3/4`, `Quaternion`, `Color`, кастомный unmanaged struct).
   - `InterpolatedFieldBindingTests`: все snapshot edge cases через публичные `ApplyFromNetwork`/`TickRender`.
   - `ReplicationScannerTests`: стабильность порядка, наследование, кэширование; **негативный** тест для managed ReactiveProperty (совместно с #21).
   - `DirtyMaskTests` / `ApplyStateBufferTests`: логика формирования и применения payload'а.
3. **Партия 2 — NGO integration через `NetworkManager.Singleton` в fixture:**
   - `AspectReplicatorLifecycleTests`: spawn, despawn, re-spawn, null context (регрессия #13/#15), >64 полей, >256 событий.
   - `AspectReplicatorStateSyncTests`: server broadcast, late-join snapshot, owner submit/relay, ownership transfer.

**Требования к качеству.**
- Не `Assert.IsNotNull` ради галочки — каждое утверждение должно проверять настоящий инвариант.
- Не тавтологии ("if X then X").
- Не тесты, которые падают только при изменении строковых констант.
- На каждый зафиксированный bug (регрессия) — хотя бы один тест, который бы поймал его до фикса.

Оцениваю как `Hard` — по объёму сравнимо с #6, но это критично для любых дальнейших изменений в replication-пути. Без тестов каждый батч фиксов — это ставка на удачу.

---

## Средняя важность

### #8. `FastBufferReader(payload, Temp)` копирует managed→native
**Проблема.** `AspectReplicator.cs:311` (`ApplyStateBuffer`), `:405` (`SubmitOwnerStateRpc`), `:483` (`HandleOwnerEvent`), `:511` (`DispatchEvent`) — конструктор `FastBufferReader` из managed `byte[]` копирует его в native Temp-буфер. То есть на каждом RPC: NGO аллоцирует managed array + reader делает ещё копию.

**Решение.** Если перейти на `NativeArray<byte>` в сигнатуре RPC (см. #7), reader можно будет построить без копии. Уменьшается вместе с #6/#7; отдельно чинить не стоит.

---

### #14. `Activator.CreateInstance` vs IL2CPP — **partial (2026-04-09)**
**Проблема.** `ReplicatedFieldBindingFactory.Create` / `ReplicatedEventBindingFactory.Create` используют `Activator.CreateInstance(MakeGenericType(...))`. На IL2CPP generic-код с value-типами может быть stripped, если код не упомянул их явно.

**Фикс (partial, вариант 1 из исходного плана).** На все binding-типы (`ReplicatedFieldBinding`, `ReplicatedFieldBinding<T>`, `InterpolatedFieldBinding<T>`, `ReplicatedEventBinding`, `ReplicatedEventBinding<T>`) и на статические factory-классы навешен `[UnityEngine.Scripting.Preserve]`. Это защищает template metadata от managed-линкера в проектах с агрессивным stripping.

**Честное ограничение.** `[Preserve]` на open generic **не форсит** AOT-инстанциацию для всех `T` на IL2CPP — только сохраняет определение типа. Для встроенных value-типов (`int`/`float`/`Vector3`/`Quaternion` etc.) IL2CPP инстанциации обычно выводит сам, потому что на них уже ссылаются `ReactiveProperty<T>` из пользовательского кода. **Для кастомных unmanaged типов в `[ReplicatedState]` пользователю всё равно нужен свой `link.xml` или explicit AOT hint** — это не закрывается `[Preserve]` и должно быть задокументировано.

**Полный фикс (варианты 2/3) — отдельная задача.**
1. Заменить `Activator.CreateInstance` на `Expression.Compile()` фабрики, собираемые один раз на тип (заодно быстрее).
2. Добавить AOT hint-метод `[Preserve] static void Hints()` с явными `new ReplicatedFieldBinding<T>(...)` для распространённых типов, чтобы IL2CPP видел инстанциации даже без ссылки из user-кода.
3. Документировать для юзеров: "если используете кастомный unmanaged тип в `[ReplicatedState]` — добавьте его в `link.xml` или в AOT hint'ы".

---

### #10. Per-entity `NetworkTickSystem.Tick` подписка
**Проблема.** `AspectReplicator.cs:154, 159` — каждая replicated сущность подписывается на глобальный `Tick` event отдельно (server-tick + owner-tick). На 200 сущностях это 200+ delegate invocations/tick, + захват `this` в каждой подписке. Профилируется как overhead даже до dirty-сканирования.

**Решение.** Один серверный `AspectReplicationSystem` (pure C# + VContainer Singleton), который держит `List<AspectReplicator>` активных и сам подписывается на `Tick`. `AspectReplicator.OnNetworkSpawn` регистрируется в системе, `OnNetworkDespawn` — снимает.

Бонус: удобная точка для #6 (единый writer, одно общее сообщение, batching по сущностям).

---

### #18. `_eventBindings` не обрезается при >256 (несимметрично с #2) — **fixed (2026-04-09)**
**Проблема.** Симметрия сломана:
- Для полей (`AspectReplicator.cs:94-99`) при `>64` делается `Array.Resize` обоих массивов. Излишек гарантированно удалён из памяти, `_bindings.Length == реально рабочих bindings`.
- Для событий (`:126-127`) при `>256` логируется только error, массив `_eventBindings` остаётся полной длины. Фактически только subscribe-loop ограничен через `int subscribeEventCount = Math.Min(_eventBindings.Length, 256);` на `:136`.

Функционально сейчас безопасно (индекс в RPC — `byte`, поэтому приёмник не может обратиться к `_eventBindings[256..]`), но:
- Память расходуется на mostly-dead bindings.
- Инвариант "длина массива = количество реально работающих bindings" нарушен — легко натурально "починить" это случайно во время будущего рефакторинга.
- Если завтра refactor случайно уберёт `Math.Min`, индексы за 255 станут скрытой дырой без видимого крэша.

**Решение.** После error-лога на `:127` добавить `Array.Resize(ref _eventBindings, 256);`. Убрать `Math.Min` на `:136` — теперь тривиально `_eventBindings.Length`. Один `Array.Resize` + одна замена, симметрично с #2.

---

### #19. Owner-auth late-join race — per-binding "owner has written locally" флаг
**Проблема.** Помечено TODO'шкой прямо в коде `AspectReplicator.SendInitialStateRpc` (`:387-398`), ссылается на `#12 в ISSUES.md`, но отдельным пунктом в таблице не трекается — легко забыть.

Когда pure-client owner поздно джойнит, он шлёт `RequestInitialStateRpc`. Между отправкой запроса и получением `SendInitialStateRpc` есть окно в ~RTT, где пользовательский компонент на owner'е может локально записать в owner-auth поле (например `InputVector` от первого обработанного кадра инпута). Snapshot прилетит и будет применён **без** `skipOwnerFields` — перетрёт свежую локальную запись старым серверным значением.

Сейчас в `SendInitialStateRpc` (`:399`) стоит `ApplyStateBuffer(payload, skipOwnerFields: false)` — приоритет у серверного значения (чтобы owner не остался навечно в `default(T)` для полей, заранее выставленных сервером, вроде `WeaponId`). Race принят как меньшее зло.

**Решение.** Добавить в `ReplicatedFieldBinding<T>` флаг `bool _ownerWroteSinceSpawn`. Выставляется из subscribe callback'а (`ReplicatedFieldBinding.cs:47-51`), когда узел — authority и `!_suppressNotification`. В `SendInitialStateRpc` передавать кастомный predicate в `ApplyStateBuffer`:
```
skipOwnerField(i) = _bindingAuthorities[i] == Owner
                    && _bindings[i].OwnerWroteSinceSpawn;
```
Серверный snapshot применяется к owner-auth полям **только если owner ещё не писал локально**. Если owner успел записать — локальное значение сохраняется.

**Открытые вопросы.**
- Сбрасывать ли флаг после первого успешного apply? Да — initial-sync происходит один раз, флаг больше не нужен, а без сброса он заблокирует все последующие корректные apply'и (хотя в broadcast-пути owner-auth фильтруется через `skipOwnerFields: IsOwner`, так что практически нет разницы).
- Что при ownership transfer? Новый owner должен начать с `_ownerWroteSinceSpawn == false`, чтобы принять серверное состояние. Потребуется сбрасывать в `OnGainedOwnership` для всех owner-auth bindings.

Связано с #12a (fields suppression alive) — обе проблемы живут на одном и том же initial-sync пути.

---

### #20. Нет null-check для значений аспект-полей до построения binding — **fixed (2026-04-09)**
**Проблема.** `AspectReplicator.cs:64`: `var reactive = info.Field.GetValue(aspect);` — если юзер объявил `[ReplicatedState] public ReactiveProperty<int> Health;` и забыл инициализировать, `GetValue` вернёт `null`. Дальше:
- `ReplicatedFieldBindingFactory.Create(null, ...)` → `Activator.CreateInstance(bindingType, null)` → конструктор `ReplicatedFieldBinding<T>` присваивает `_reactive = null`.
- Крэш прилетит позже в `WriteTo` (`_reactive.Value` → NRE), `SubscribeAsAuthority` (`_reactive.Subscribe` → NRE) или даже в `Interpolators.TryGetRaw` факторки. Стек трейс ничего не скажет про "поле `Health` на аспекте `HealthAspect` не инициализировано".

Аналогично для событий на `:83` (`Subject<T>` тоже не проверяется на null).

**Решение.** После `GetValue` проверить на null:
```csharp
var reactive = info.Field.GetValue(aspect);
if (reactive == null)
{
    Debug.LogError($"[AspectReplicator] Aspect '{aspect.GetType().Name}' field '{info.Field.Name}' is null on '{gameObject.name}'. Initialize it in the aspect constructor or field initializer.");
    continue;
}
```
Early-continue пропускает поле без крэша, ошибка указывает на точное место. Это дешёвая defensive-проверка, которая сэкономит часы дебага при забытой инициализации.

---

### #21. `ReplicationScanner` не валидирует `unmanaged` constraint — криптический error — **fixed (2026-04-09)**
**Проблема.** Scanner принимает любой `ReactiveProperty<T>` (`ReplicationScanner.cs:88-92`) — проверяет только что generic definition == `ReactiveProperty<>`, без валидации что `T` удовлетворяет constraint `where T : unmanaged` целевого binding'а. Если юзер напишет `[ReplicatedState] public ReactiveProperty<string> Name;`, scan пройдёт, а потом `ReplicatedFieldBindingFactory.Create` вызовет `typeof(ReplicatedFieldBinding<>).MakeGenericType(typeof(string))` — `MakeGenericType` проверяет constraint'ы в рантайме и бросает:
```
ArgumentException: GenericArguments[0], 'System.String', on
'Rubickanov.ACS.Runtime.Netcode.ReplicatedFieldBinding`1[T]'
violates the constraint of type 'T'.
```
Trace показывает `MakeGenericType`, юзер должен догадаться, что проблема в конкретном поле на конкретном аспекте. Диагностика — плохая.

**Решение.** В `CollectReplicatedFields` после получения `valueType` (`:92`) добавить валидацию:
```csharp
if (!IsUnmanagedType(valueType))
{
    Debug.LogError($"[ReplicationScanner] Aspect '{aspectType.Name}' field '{fields[i].Name}' has [ReplicatedState] but ReactiveProperty<{valueType.Name}> is not unmanaged. Only unmanaged types (primitives, Unity value types, unmanaged structs) are supported.");
    continue;
}
```
`IsUnmanagedType(Type)` реализуется через кэш + рекурсивную проверку: `type.IsPrimitive || type.IsEnum || (type.IsValueType && !type.IsGenericType && all fields are IsUnmanagedType)`. Аналогично для событий в `CollectReplicatedEvents` (`:127`).

Бонус: ошибка видна на первом `Scan(aspectType)` — то есть при первом спавне первого владельца аспекта, а не отложенно только на том клиенте, где это поле впервые пытаются сериализовать.

---

### #22. `GetComponentsInChildren` аллокация в `ApplyNetworkScopes`
**Проблема.** `AspectReplicator.cs:196` — `GetComponentsInChildren<IEntityComponent>(includeInactive: true)` аллоцирует новый массив на каждый spawn. На spawn-heavy сценариях (волны ботов, respawn-цикл, быстрый join) это прямая GC-нагрузка на hot path спавна.

Замечание: `ApplyNetworkScopes` вызывается один раз на spawn, не per-tick. Не hot-hot path, но GC во время спавна даёт микро-hitch на клиенте в самый неудобный момент — когда новая сущность появляется в поле зрения игрока.

**Решение.** Использовать list-overload: `GetComponentsInChildren<IEntityComponent>(true, _scopeComponentsBuffer)` где `_scopeComponentsBuffer` — re-used `List<IEntityComponent>` field на `AspectReplicator`. `List.Clear()` перед использованием (Unity overload сам делает Clear + Add). Один alloc списка при первом use на инстанс, дальше 0 alloc'ов.

Если хочется быть уверенным в нуле аллокаций — сделать список статичным thread-local (всё на main thread, так что простой `static List<...>` достаточно).

---

### #23. LINQ `.OrderBy` в `OnNetworkSpawn` — **fixed (2026-04-09)**
**Проблема.** `AspectReplicator.cs:57-58`:
```csharp
var aspects = context.GetAllAspects()
    .OrderBy(a => a.GetType().FullName, StringComparer.Ordinal);
```
Аллоцирует `OrderedEnumerable` + enumerator + sorting buffer на каждый spawn. Плюс делегат `a => a.GetType().FullName` захватывается каждый раз.

Аналогичные `.OrderBy(f => f.Field.Name).ToArray()` в `ReplicationScanner.cs:105, 140` выполняются только при первом `Scan()` на тип (результат кэшируется per-type), так что для них альфа по LINQ аллокациям низкая и оставить можно.

**Решение.** Заменить на ручную сортировку через `List<object>`:
```csharp
var aspectList = new List<object>(); // или re-used field
foreach (var a in context.GetAllAspects()) aspectList.Add(a);
aspectList.Sort((a, b) => string.Compare(
    a.GetType().FullName, b.GetType().FullName, StringComparison.Ordinal));
```
Ноль enumerator'ов (только прямая итерация), re-used список. Дёшево и устраняет одну из немногих spawn-time LINQ дыр.

---

## Низкая важность

### #11. Четыре broadcaster-делегата на каждый spawn
**Проблема.** `AspectReplicator.cs:131-134` — `_reliableBroadcaster`, `_unreliableBroadcaster`, `_submitOwnerReliableBroadcaster`, `_submitOwnerUnreliableBroadcaster` создаются как method-group делегаты на каждый spawn. Не closure'ы (нет захвата переменных кроме `this`), но всё равно четыре delegate instance'а — одноразовая аллокация на entity, не критично, но накапливается на spawn-heavy сценах.

**Решение.** Перенести вызов RPC внутрь самого binding через `IEventBroadcaster` интерфейс, который реализует `AspectReplicator`. Binding хранит `_broadcaster` как `this`-ссылку на replicator, не нужны четыре делегата. Косметика.

---

### #13. Нет null-check `EntityContext` — **fixed (2026-04-09)**
**Проблема.** `AspectReplicator.cs:30` — `GetComponent<EntityContext>()` без проверки. Если `EntityContext` забыли повесить, клиент получит NRE без понятного сообщения.

**Фикс.** `AspectReplicator.OnNetworkSpawn` теперь проверяет `context == null` сразу после `GetComponent`, пишет `Debug.LogError` с именем GameObject и ранним `return`-ом полностью отключает репликацию для этой сущности (не подписывается на tick, не строит bindings).

---

### #12a. `_suppressNotification` в `ReplicatedFieldBinding<T>` — **ALIVE, не stale**

(Изначально #12 включал также sub-пункт #12b для событий — он был помечен как dead code и удалён в батче 3.1 (2026-04-09), теперь живёт только #12a для полей.)

**Путь.** Pure-client owner получает `SendInitialStateRpc` → `ApplyStateBuffer(skipOwnerFields: false)` → `ReadFrom` → `ApplyFromNetwork` → `WriteSuppressed` → `_reactive.Value = ...` → subscribe callback (`ReplicatedFieldBinding.cs:47-51`) срабатывает → `if (_suppressNotification) return;`.

**Почему нужен.** Без флага initial-sync триггернул бы `IsDirty = true` на только что применённом значении. В следующий `OnOwnerTick` owner отправил бы серверу обратно тот же snapshot, который только что получил — бесконечный echo на каждом late-join'е. На host-owner'е и server-auth полях путь мёртв, но оставлять поле нельзя — pure-client owner'у оно нужно.

**Действие.** Задокументировать в DESIGN.md как часть owner-auth initial-sync контракта (subscribe callback обязан уважать suppression).

---

## План по минимуму (до первого playtest'а)

Первый батч сделан 2026-04-09:
- [x] #2 — clamp `_bindings`+`_bindingAuthorities` до 64.
- [x] #5 — сортировка аспектов по `Type.FullName`.
- [x] #3 — фильтр компонентов по `NetworkObject`.
- [x] #4 — `is not Behaviour → continue`.
- [x] #13 — null-check контекста.

Второй батч сделан 2026-04-09:
- [x] #1 — initial-sync через `RequestInitialStateRpc` / `SendInitialStateRpc` (client-pull).
- [x] #9 — `FastBufferWriter` pre-sizing: `ReplicatedFieldBinding.Size` + `_statePayloadCap` в `AspectReplicator`.
- [~] #14 — `[Preserve]` на binding-типы и factory (partial: закрывает managed-linker stripping, но не AOT-инстанциации для custom value types; варианты 2/3 — отдельная задача).

GC-оптимизации (#6/#7/#8) — после профайлинга на реальных сценах, не раньше.
#10 — имеет смысл делать вместе с #6, как единый рефактор в серверную систему.

Re-analysis Owner-auth пути **выполнен** — см. обновлённый #12 (fields live, events dead) и новый #19 (late-join race).

---

## Третий батч (после срезика 2026-04-09 — TODO)

Независимый audit всего пакета выявил 9 новых пунктов (#15-#23) и переанализ #12.

**Quick wins (Easy — закрываются меньшими правками, делать первыми):**
- [ ] #15 — `Array.Empty<>()` в field declarations, защита от NRE на RPC после null-context early-return.
- [ ] #18 — `Array.Resize(_eventBindings, 256)` симметрично с #2.
- [ ] #20 — null-check для `ReactiveProperty`/`Subject` field values с указанием конкретного поля.
- [ ] #21 — `IsUnmanagedType` валидация в `ReplicationScanner` с диагностическим error.
- [ ] #23 — ручная `List.Sort` вместо `.OrderBy` в `OnNetworkSpawn`.
- [ ] #12b — удалить `_suppressNotification` + suppression try/finally из `ReplicatedEventBinding<T>` (dead code).

**Архитектурные (Medium — требуют продумывания):**
- [ ] #16 — перенести `OnSubscribe` в `OnEnable`/`OnDisable` в `EntityNetworkComponent`.
- [ ] #19 — `_ownerWroteSinceSpawn` флаг в `ReplicatedFieldBinding<T>` + predicate в `SendInitialStateRpc`.
- [ ] #22 — re-used `List<IEntityComponent>` buffer в `ApplyNetworkScopes`.

**Долгосрочные (Hard — большие задачи):**
- [ ] #17 — создать `Tests/Runtime/` с unit + integration тестами. Блокер для уверенных изменений в replication-пути.
- [ ] #6/#7/#8 — единый серверный replication system + `NativeArray` или `CustomMessagingManager` (откладывается до реальных нагрузок).
- [ ] #10 — объединить с #6 как часть серверной системы.

#11 (четыре broadcaster-делегата) и #14 (полный IL2CPP fix) остаются на своих позициях — косметика / AOT edge cases.
