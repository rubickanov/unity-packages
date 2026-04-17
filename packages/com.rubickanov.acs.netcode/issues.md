# ACS Netcode Package — Issues & Work Plan

Результаты аудита пакета `com.rubickanov.acs.netcode`. Документ отслеживает все найденные проблемы и порядок их исправления. Ломающие изменения публичного API допустимы — делаем правильно.

---

## Ложные срабатывания (отброшены при ручной проверке)

Эти флаги подняли автоматические аудит-агенты, но ручная верификация кода их не подтвердила. Фиксирую здесь чтобы не вернуться к ним повторно:

- **«Null broadcaster в `ReplicatedEventBinding.OnLocalEvent`»** — `_broadcaster` присваивается в `SubscribeAsAuthority` до `subject.Subscribe(...)`. `OnLocalEvent` не может сработать, пока subscribe не отработал → `!` стоит корректно. `ReplicatedEventBinding.cs:50-53`.
- **«Event dispatch to unknown entity портит tail батча»** — каждый `SendNamedMessage` в NGO CustomMessagingManager — отдельное сообщение, `FastBufferReader` содержит ровно одно событие. `return` без seek ничего не ломает. `EntityReplicationSystem.cs:484-493`.
- **«OnLostOwnership disposes before unsubscribing → race»** — порядок (`Dispose` → `ClearInterpolationState`) намеренный, объяснён комментарием в коде (строки 351-356): sampler удалён, `ClearInterpolationState` должен сбросить `_samplesFromSubscribe`, чтобы входящие снепшоты снова пошли в `RecordSample` через `ApplyFromNetwork`. Не баг. Отдельная, настоящая проблема (видимый snap из-за сброса буфера) — см. **M7** ниже.

## Находки

### Критические (реальные баги)

Нет. Архитектура надёжная: cap 256 на field/event bindings, инварианты проверяются через `Debug.Assert`, per-entity fallback `Seek` по `payloadBytes` при unknown entity, правильная последовательность OnNetworkSpawn с ApplyNetworkScopes первым. Все "critical"-находки агентов развалились при верификации (см. выше).

### Мажорные (M)

- **M1. `ISimulate<TInput>.Simulate` контракт не документирует идемпотентность** — `Runtime/Prediction/ISimulate.cs`
  `PredictionManager<T>.Reconcile` (PredictionManager.cs:400-413) вызывает `Simulate` повторно в цикле replay. Пользователь, у которого `Simulate` делает глобальные side-effects, использует RNG без seed или читает неинициализированные поля, получит расхождение при реконсиляции. Решение: xmldoc на интерфейс — «MUST be idempotent within a tick frame: no external side-effects outside the aspect, no unseeded randomness, no frame-local cache reads».

- **M2. IL2CPP: reflection `Invoke` без try/catch** — `PredictionBinder.cs:191`, `ReplicatedFieldBinding.cs:247-264`, `ReplicatedEventBinding.cs:115-116`
  Factory-methods вызывают `ConstructorInfo.Invoke` / `MethodInfo.Invoke` напрямую. В IL2CPP при отсутствии типа в link.xml получаем `TargetInvocationException` без понятного контекста какой тип не смог инстанцироваться. Решение: обернуть каждый Invoke в `try/catch (TargetInvocationException or MissingMethodException)`, лог с названием целевого типа и подсказкой про `[Preserve]` / link.xml.

- **M3. `[NetworkScope]` компоненты не покрыты IL2CPP-хинтами** — `Runtime/Authority/NetworkScopeScanner.cs`
  Scanner зовёт `GetCustomAttribute<NetworkScopeAttribute>(inherit: true)` на пользовательских `MonoBehaviour`-ах. `AotHints.cs` preserves только replicated-типы, не компоненты. В IL2CPP-билде тип стрипается → атрибут не найден → компонент молча работает как `Everywhere` (включая `ServerOnly` на клиентах). Решение: добавить в README секцию IL2CPP с явным предупреждением — любой `[NetworkScope]`-помеченный тип должен быть preserved в link.xml пользовательского проекта.

- **M4. Tick rate фиксируется на spawn, рантайм-смена не отслеживается** — `EntityReplicator.cs:147-149`
  `_tickInterval` и `_interpolationDelaySeconds` кэшируются один раз. Если `NetworkTickSystem.TickRate` меняется в рантайме (теоретически), `AuthorityRenderBinding` coalesce/stale пороги, render delay, `PredictionManager._tickDelta` расходятся с фактическим tick-темпом. Решение (два шага):
  1. README — зафиксировать контракт «TickRate должен быть задан до spawn первого entity и не меняться».
  2. `EntityReplicator.Update`: одноразовая проверка `NetworkManager.NetworkTickSystem.TickRate` против закэшированного, при обнаружении изменения — один `Debug.LogWarning` через static `bool _tickRateMismatchLogged` флаг (чтобы не спамить).

- **M5. LINQ в `ReplicationScanner` — оставить, обновить корневой CLAUDE.md** — `ReplicationScanner.cs:160, 485, 529`
  `.All(...)` в `IsUnmanagedType`, `.OrderBy(...).ToArray()` в `CollectReplicatedFields` / `CollectReplicatedEvents`. Путь: один раз на тип аспекта (scan-path, результат кэшируется). Реальной per-tick нагрузки нет. Решение: не трогать код; обновить `/home/alex/projects/pet/unity-packages/CLAUDE.md` — переформулировать правило как «No LINQ in runtime hot paths (tick/Update/frame loops)», с явным исключением для scan/spawn-time кода, удовлетворяющего: (1) вызов гарантированно ≤1 раз на тип за сессию, (2) не в tick-цикле, (3) результат кэшируется.

- **M6. `OnGainedOwnership`: `ReapplyOwnerScope()` в конце метода** — `EntityReplicator.cs:322-342`
  Порядок сейчас: SubscribeOwnerFieldBindings → SubscribeEventBindingsAsAuthority → ResetOwnerSubmitTickSync → ClearInterpolationState → ReapplyOwnerScope. `OwnerOnly` компоненты просыпаются в самом конце — между subscribe-ми и scope re-apply они пропускают первый event. В `OnNetworkSpawn` (строки 122-132) scope применяется первым — симметричное поведение должно быть и здесь. Решение: перенести `ReapplyOwnerScope()` в начало `OnGainedOwnership`.

- **M7. Owner-auth interpolation при ownership transfer — split `ClearInterpolationState` → `OnAuthorityLost`** — `EntityReplicator.cs:333-339, 357-361`, `AuthorityRenderBinding.cs`
  Сейчас `ClearInterpolationState` сбрасывает сразу `_prev`, `_curr`, `_times` и `_samplesFromSubscribe`. Между `OnLostOwnership` старого владельца и первым `Simulate`/`RecordSample` нового буфер пустой → первый render-сэмпл пройдёт через stale-bootstrap путь, визуально даст snap. Решение:
  1. Добавить `virtual void OnAuthorityLost()` на `ReplicatedFieldBinding` (пустой по умолчанию).
  2. В `AuthorityRenderBinding` override: опустить только `_samplesFromSubscribe = false`, буфер `_prev`/`_curr`/`_times` сохранить — render продолжится на последних known-good значениях до первого нового сэмпла.
  3. В `EntityReplicator.OnLostOwnership` и `OnGainedOwnership` заменить `ClearInterpolationState` на `OnAuthorityLost` для owner-auth полей.
  4. `ClearInterpolationState` оставить, но вызывать только в `OnNetworkDespawn` (полная очистка при uninstall).
  5. Тест в Batch 6 должен покрыть: два последовательных ownership transfer не ломают render.

- **M8. `Reliability` enum: нет exhaustiveness default** — `EntityReplicationSystem.cs:541-565`
  Тернарник `reliability == Reliable ? ... : ...`. Если в enum добавят `Unreliable_Unordered` или `ReliableUnsequenced` — молча пойдёт через Unreliable ветку. Решение: переписать на `switch` с `default: throw new ArgumentOutOfRangeException(...)` или `Debug.LogError + fallback`. Защита от будущих maintenance-ошибок.

- **M9. `InterpolationRegistry` double-register только в Debug** — `InterpolationRegistry.cs:72-80`
  `Debug.Assert` компилится из Release-билдов. Если в будущем появится lifecycle-баг с двойной регистрацией, Release build молча перезапишет первый биндинг вторым и `Smooth()` вернёт stale значения. Решение: заменить `Debug.Assert` на безусловный `if + Debug.LogError` (или `throw InvalidOperationException` — fail-fast предпочтительнее для invariant-ошибок).

- **M10. Симметрия: `ReplicatedEventBinding` не имеет `OnDespawn()` virtual** — `Runtime/Replication/Events/ReplicatedEventBinding.cs`
  `ReplicatedFieldBinding` имеет `OnDespawn()` virtual, event-биндинг — нет. Сейчас event-subscribes чистятся через `_disposables`/`_ownerDisposables` (R3 Disposable), так что не баг. Но при будущем добавлении stateful cleanup (например, кэш broadcaster'а) легко пропустить. Решение: добавить `virtual void OnDespawn()` пустым дефолтом на `ReplicatedEventBinding`, вызывать из `EntityReplicator.OnNetworkDespawn` симметрично полям.

- **M11. Per-entity `PredictionBinder` вместо prefab-scoped кэша** — `EntityReplicator.cs:91`
  Binder инстанцируется на каждый replicator. Reflection-результаты кэшируются per-type через `PredictionHookCache`, повторный cost ограничен. Решение: оставить как есть, добавить комментарий над полем объясняющий почему per-entity (хранит owner-specific state: bag, tick sync, etc.).

- **F2. Runtime invariant check для parity `_bindings.Length == _bindingAuthorities.Length`** — `EntityReplicator.cs:244-252`
  Сейчас только `Debug.Assert` (компилится из Release). Для production builds заменить на `throw InvalidOperationException` при фейле — silent state corruption при промахе страшнее чем crash. Под `Debug.Assert` оставить только нестрогие invariants (например, предупреждения).

### Минорные (m)

- **m1. `ExceedsEventBindingCap` error message «max is 256» сбивает с толку** — `EntityReplicator.cs:274, 284`
  Cap корректен (count=256, индексы 0..255), но сообщение читается неоднозначно. Переформулировать: «max count is 256 (indices 0..255)».

- **m2. `NetworkScopeController`: компоненты на nested NetworkObject молча игнорируются** — `NetworkScopeController.cs:46-51`
  Разработчик, поставивший `[NetworkScope]` на компонент на вложенном NetworkObject, не получит обратной связи. Решение: `Debug.LogWarning` когда `[NetworkScope]`-помеченный компонент скипается из-за nested boundary.

- **m3. Stackalloc bounds не утверждаются явно** — `EntityReplicator.StateApply.cs:22`
  `stackalloc byte[_maskByteCount]` безопасен: `_maskByteCount ≤ 32` гарантируется cap-check в `OnNetworkSpawn`. Добавить короткий комментарий «stack-safe by field cap (≤256/8=32 bytes)» для ясности.

- **m4. `Predicted = true` требует Server authority — не в xmldoc атрибута** — `Runtime/Attributes/ReplicatedAttribute.cs`
  Scanner warn'ит при `Predicted + Owner`, но doc-comment атрибута молчит об этом. Добавить: «Requires `Authority = AuthorityMode.Server`. Owner-auth predicted fields are unsupported; the flag is ignored with a warning».

- **m5. Reliability ordering guarantees не задокументированы** — `README.md:105-121`
  Секция Reliability объясняет Reliable/Unreliable по смыслу «доставлено / best-effort», но не упоминает что оба варианта идут через `NetworkDelivery.ReliableFragmentedSequenced` / `NetworkDelivery.Unreliable` NGO-каналы и сохраняют порядок в рамках одного канала. Прояснить.

- **m6. `AuthorityRenderBinding.Clock` — static per closed type** — `AuthorityRenderBinding.cs:40`
  Инжектируется per-type (для тестов). Два независимых instance одного типа разделяют clock. Не трогать, задокументировать в комментарии класса: «`Clock` is static per closed generic type — tests injecting fakes must restore it in `TearDown`».

- **m7. Тесты: AAA phase separation местами без пустых строк** — `Tests/Runtime/Replication/Events/ReplicatedEventBindingTests.cs` и другие
  CLAUDE.md требует blank line separation между Arrange/Act/Assert. Местами строки слипшиеся. Косметика, пройтись по файлам в рамках Batch 6.

### Отсутствующие фичи / пробелы (F)

- **F1. Comprehensive test coverage batch.** Текущий список пробелов:
  - Mask bit boundary tests: индексы 0, 7, 8 (transition), 15, 16, 255.
  - EntityRef spawn-order race integration test: A ссылается на B до spawn B; после spawn B первое dirty-пересылание поля должно разрешить ref на корректный `EntityId` (сейчас silent `EntityRef.None` forever если поле больше не меняется).
  - Ownership transfer + initial-sync race: ownership флипается между `RequestInitialSync` и `ApplySyncReply` — новый owner не должен потерять локальные записи.
  - Host-owner event double-fire guard: сейчас `PureClientOwnerFiresOwnerAuthEvent_OwnerDoesNotDoubleReceive` покрывает только pure-client. Добавить `HostOwnerFiresOwnerAuthEvent_HostDoesNotDoubleReceive`.
  - Ownership transfer + event subscription cleanup: старый owner не ловит server-relay-ы после потери владения.
  - Authority mismatch: pure client пытается зафаерить server-auth event → warn+drop (проверить что `HandleOwnerEvent` логирует и не релеит).
  - `AuthorityRenderBinding` coalesce/stale state machine unit tests с инжектированным clock (bootstrap → coalesce <10ms → stale-gap >83ms).
  - `ReplicatedEventAttribute` unit tests (default Authority/Reliability, ctor варианты).
  - Late joiner: events, отстрелянные до его spawn, не должны ретранслироваться через initial-sync.
  - Interpolation ring buffer overflow stress-test (>32 снепшотов за тик — не должно крэшить, count clamp).
  - Batch 3 tests: два последовательных ownership transfer с owner-auth render полем не вызывают visible snap (проверка сохранения `_prev/_curr`).

- **F2. Runtime invariant check для parity** — см. M-блок выше (вынесено туда как fix, не как фича).

---

## Порядок работы (батчи)

Батчи упорядочены по зависимости: pure-docs первым, рантайм-гард, lifecycle, hardening, tests последним. Каждый батч — логически связанный коммит.

### Batch 1 — Docs & контракты (pure docs, no runtime code)
**Решает:** M1, M3, M4 (doc-часть), M5 (CLAUDE.md часть), m4, m5, m6

- [ ] `Runtime/Prediction/ISimulate.cs` — xmldoc idempotency contract.
- [ ] `Runtime/Attributes/ReplicatedAttribute.cs` — xmldoc: `Predicted = true` требует `Server` authority.
- [ ] `Runtime/Authority/AuthorityRenderBinding.cs` — class-level комментарий про static `Clock`.
- [ ] `README.md` — секции:
  - «IL2CPP / AOT» — требование preserve для `[NetworkScope]`-компонентов + пользовательских TInput.
  - «TickRate» — контракт «задан до spawn первого entity, не меняется в рантайме».
  - «Reliability» — уточнить что оба варианта сохраняют ordering в рамках канала.
- [ ] `/home/alex/projects/pet/unity-packages/CLAUDE.md` — переформулировать правило LINQ с явным cold-path исключением.

### Batch 2 — TickRate runtime-guard
**Решает:** M4 (code-часть)

- [ ] `EntityReplicator.cs`:
  - Static `bool _tickRateMismatchLogged` (один лог на всю сессию).
  - В `Update` (до `TickRender` цикла) сравнить `NetworkManager.NetworkTickSystem.TickRate` с закэшированным `1.0 / _tickInterval`.
  - При несоответствии: `Debug.LogWarning` один раз, подсказка «TickRate changed at runtime; respawn entities or reset interpolation windows».

### Batch 3 — Lifecycle cleanup + `OnAuthorityLost`
**Решает:** M6, M7, M10

- [ ] `ReplicatedFieldBinding.cs` — добавить `public virtual void OnAuthorityLost() {}`.
- [ ] `AuthorityRenderBinding.cs` — override `OnAuthorityLost`: опустить только `_samplesFromSubscribe`, буфер не трогать.
- [ ] `EntityReplicator.cs`:
  - `OnGainedOwnership`: перенести `ReapplyOwnerScope()` в самое начало.
  - `OnGainedOwnership` и `OnLostOwnership`: заменить `ClearInterpolationState` на `OnAuthorityLost` для owner-auth полей.
  - `ClearInterpolationState` оставить; она зовётся только из `OnNetworkDespawn` (полный teardown).
- [ ] `ReplicatedEventBinding.cs` — добавить `public virtual void OnDespawn() {}`.
- [ ] `EntityReplicator.cs` (`OnNetworkDespawn`) — цикл по `_eventBindings` с вызовом `OnDespawn()` симметрично полям.

### Batch 4 — AOT/IL2CPP hardening
**Решает:** M2, m1, m3

- [ ] `PredictionBinder.cs:191` — try/catch (`TargetInvocationException` / `MissingMethodException`) с логом указывающим тип hook + подсказкой link.xml.
- [ ] `ReplicatedFieldBinding.cs:247-264` (Factory) — тот же паттерн для ctor.Invoke.
- [ ] `ReplicatedEventBinding.cs:115-116` (Factory) — тот же паттерн.
- [ ] `EntityReplicator.cs:276, 286` — переформулировать error messages: «max count is 256 (indices 0..255)».
- [ ] `EntityReplicator.StateApply.cs:22` — короткий inline-комментарий «stack-safe by field cap (≤32 bytes)».

### Batch 5 — Defensive guards в Release
**Решает:** M8, M9, m2, F2

- [ ] `EntityReplicationSystem.SendEvent` — переписать Reliability-dispatch через `switch` с `default: throw ArgumentOutOfRangeException`.
- [ ] `InterpolationRegistry.cs:72-80` — `Debug.Assert` → `if (_entries.ContainsKey(...)) throw InvalidOperationException(...)`.
- [ ] `NetworkScopeController.cs:46-51` — `Debug.LogWarning` когда `[NetworkScope]`-помеченный компонент скипается из-за nested-NO boundary.
- [ ] `EntityReplicator.OnNetworkSpawn:244` — parity-check (`_bindings.Length == _bindingAuthorities.Length`) превратить из `Debug.Assert` в runtime `throw`. Остальные три assert'а (mask byte count, dirty buffer, predicted index) оставить как есть — они вычисляются рядом с проверяемыми значениями, invariant сохраняется by construction.

### Batch 6 — Tests
**Решает:** F1 (весь список), m7 (AAA косметика), регрессии под Batch 3 (`OnAuthorityLost` не ломает render).

- [ ] `Tests/Runtime/Replication/MaskBitBoundaryTests.cs` — индексы 0, 7, 8, 15, 16, 255.
- [ ] `Tests/Runtime/Integration/EntityRefSpawnOrderTests.cs` — ref без dirty-trigger не разрешается после spawn цели.
- [ ] `Tests/Runtime/Integration/OwnershipTransferInitialSyncRaceTests.cs`.
- [ ] Расширить `EntityReplicatorEventTests.cs`: host-owner double-fire guard, ownership transfer + event cleanup, authority mismatch warn+drop.
- [ ] `Tests/Runtime/Integration/LateJoinerEventsNotReplayedTests.cs`.
- [ ] `Tests/Runtime/Authority/AuthorityRenderBindingStateMachineTests.cs` — coalesce/stale state machine через injected clock.
- [ ] `Tests/Runtime/Attributes/ReplicatedEventAttributeTests.cs` — defaults, ctor варианты.
- [ ] `Tests/Runtime/Replication/InterpolationBufferOverflowTests.cs` — >32 snapshots/tick stress.
- [ ] `Tests/Runtime/Integration/OwnershipTransferRenderContinuityTests.cs` — два последовательных transfer, owner-auth render поле не snap'ает (регрессия Batch 3).
- [ ] AAA cosmetic pass по existing test files — пустые строки между фазами.

### Batch 7 (опционально) — Perf polish
**Решает:** M11

- [ ] `EntityReplicator.cs:91` — комментарий над `_predictionBinder` объясняющий per-entity state + причину отсутствия prefab-cache.
- [ ] (Если профайлинг показывает нагрузку) — batched OwnerSubmit: собирать все owner-auth dirty entities этого пира в один `FastBufferWriter` за tick. Пока — только комментарий-TODO в `OwnerTick`.

---

## Статус

План зафиксирован. Начинать с Batch 1 (pure docs). Верификация каждого батча: `unity-project-pckgs` открывается без ошибок компиляции, `ACS.Runtime.Netcode.Tests` зелёные в Test Runner. Smoke-тест host+client с одним predicted owner-auth entity — initial sync, ownership transfer без snap'а, event fire обоими путями.
