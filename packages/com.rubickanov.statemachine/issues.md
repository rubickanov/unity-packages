# State Machine — Issues & Work Plan

Аудит пакета `com.rubickanov.statemachine`. Находки сгруппированы по батчам — сначала критичные баги, затем поведенческие вопросы (решения уже зафиксированы), расхождения sync/async, тесты, документация и nice-to-haves. Внутри батча — по связанности и лёгкости фикса.

Ссылки на строки актуальны на момент аудита.

---

## BATCH 1 — Критичные баги

### 1.1 `IsInState` игнорирует кастомный `IEqualityComparer<TKey>` — CRITICAL

**Файлы:**
- `Runtime/StateMachine.cs:59` — `EqualityComparer<TKey>.Default.Equals(_currentKey, key)`
- `Runtime.Async/AsyncStateMachine.cs:61` — то же самое

Конструкторы с компаратором (sync:28-30, async:30-32) используют его для `Dictionary`, но `IsInState` сравнивает через `EqualityComparer<TKey>.Default`. Баг известен — в тестах `CustomComparer_IsUsedForStateLookup` (`Tests/StateMachineTests.cs:346-359`, `Tests/AsyncStateMachineTests.cs:377-390`) стоят комментарии-алиби, пинающие lookup через `CurrentState` identity, потому что `IsInState` «не работает».

Пример поломки:
```csharp
var fsm = new StateMachine<string>(StringComparer.OrdinalIgnoreCase);
fsm.AddState("State", state);
fsm.Start("STATE");
fsm.IsInState("state");   // false, хотя текущее состояние — "STATE"
```

**Фикс:**
1. Сохранить компаратор в приватное поле `_comparer` (оба класса).
2. В конструкторе без компаратора — `_comparer = EqualityComparer<TKey>.Default`.
3. `IsInState` → `return _isStarted && _comparer.Equals(_currentKey, key);`.
4. В тестах `CustomComparer_IsUsedForStateLookup` убрать комментарий-алиби, добавить прямой `Assert.IsTrue(fsm.IsInState("state"))`.

---

### 1.2 Async: `CancellationToken` теряется на отложенных переходах — CRITICAL

**Файлы:** `Runtime.Async/AsyncStateMachine.cs:123-138`, `:94-98`, `:141-180`.

В `SetStateAsync` когда `_isTransitioning == true`, сохраняется только `_pendingKey`, а переданный `newCt` отбрасывается:
```csharp
if (_isTransitioning)
{
    _hasPendingTransition = true;
    _pendingKey = key;
    return;   // ← newCt потерян
}
```
В цикле `PerformTransitionAsync` на строке 178 (`nextKey = _pendingKey;`) используется `ct` исходного перехода — не того вызова, который queue’ил. Если у queue’щего был более короткий таймаут — он игнорируется.

Аналогичная проблема в `StartAsync` (строки 94-98): отложенный переход получает `ct` от `StartAsync`, не от `SetStateAsync`, дёрнутого из `OnEnterAsync`.

**Фикс:**
```csharp
private CancellationToken _pendingCancellationToken;

public async UniTask SetStateAsync(TKey key, CancellationToken ct = default)
{
    // ... guards ...
    if (_isTransitioning)
    {
        _hasPendingTransition = true;
        _pendingKey = key;
        _pendingCancellationToken = ct;   // preserve
        return;
    }
    await PerformTransitionAsync(key, ct);
}

private async UniTask PerformTransitionAsync(TKey key, CancellationToken ct)
{
    var nextKey = key;
    while (true)
    {
        // ... exit/enter ...
        if (!_hasPendingTransition) { _transitionDepth = 0; return; }
        _hasPendingTransition = false;
        nextKey = _pendingKey;
        ct = _pendingCancellationToken;   // switch to deferred token
    }
}
```
То же — в `StartAsync` (передать `_pendingCancellationToken` в `PerformTransitionAsync`).

---

### 1.3 Версия в `package.json` не совпадает с CLAUDE.md — MEDIUM (trivial)

`package.json:3` — `"version": "1.0.0"`. CLAUDE.md заявляет `statemachine` (**2.0.0**).

**Фикс:** поднять `package.json` до `2.0.0`. API переработан (async, SubStateMachine, deferred transitions) — это явный major bump.

---

## BATCH 2 — Поведенческие решения (уже зафиксированы)

### 2.1 Self-transitions → NO-OP

**Файлы:** `Runtime/StateMachine.cs:116-132`, `Runtime.Async/AsyncStateMachine.cs:123-139`.

Текущее поведение: `SetState(CurrentKey)` дёргает полный Exit→Enter. Ни теста, ни комментария, ни упоминания в README.

**Решение:** `SetState(CurrentKey)` — тихий no-op.

**Фикс:** в начале `SetState`/`SetStateAsync`, **после** guards (`!_isStarted`, `!ContainsKey`), **до** проверки `_isTransitioning`:
```csharp
if (_comparer.Equals(_currentKey, key))
    return;                     // sync
    // return UniTask.CompletedTask;   // async
```
Использует `_comparer` из фикса 1.1.

**Примечание:** проверка сравнивает с `_currentKey`. Если в момент вызова уже идёт переход в другое состояние (`_isTransitioning == true`), цель сравнения — текущий key, а не пункт назначения. В экзотических цепочках возможно «по итогу» прийти в self-transition (например, `A→B`, в `B.OnEnter` позвали `SetState(A)`). Это приемлемо — поведение симметрично обычному deferred transition. Закрепляется тестом 4.1.

---

### 2.2 Multiple deferred `SetState` → LAST-WRITE-WINS (задокументировать + тест)

**Файлы:** `Runtime/StateMachine.cs:18, 126-127`, `Runtime.Async/AsyncStateMachine.cs:20, 133-134`.

Поле `_pendingKey` — одно. Цепочка `SetState(B); SetState(C); SetState(D)` в одном Enter/Exit исполнит только переход в `D`.

**Решение:** оставить как есть, задокументировать, закрепить тестом.

**Фикс:**
1. Правок в коде нет (после 1.2 `_pendingCancellationToken` перезаписывается в той же логике — это согласуется).
2. README, секция deferred transitions: одна строка — `If multiple SetState calls happen during OnEnter/OnExit, only the last one executes — earlier queued keys are overwritten.`
3. Тест `SetState_MultipleCallsDuringOnEnter_LastWriteWins` (sync + async) — см. 4.2.

---

### 2.3 `CurrentKey` vs `CurrentState` → ОБЕ МЯГКИЕ (breaking change)

**Файлы:** `Runtime/StateMachine.cs:33-48`, `Runtime.Async/AsyncStateMachine.cs:35-50`.

`CurrentKey` кидает `InvalidOperationException` при `!_isStarted`; `CurrentState` возвращает `null` без guard’а. Асимметрия.

**Решение:** обе мягкие. `CurrentKey` возвращает `default(TKey)`, `CurrentState` остаётся `null`. Юзер сам проверяет `IsStarted` при необходимости.

**Фикс:**
```csharp
public TKey CurrentKey
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _currentKey;
}
```
Тесты `CurrentKey_BeforeStart_Throws` (`Tests/StateMachineTests.cs:103-106`, `Tests/AsyncStateMachineTests.cs:106-109`) переписать в `CurrentKey_BeforeStart_ReturnsDefault`:
```csharp
Assert.AreEqual(default(Key), _fsm.CurrentKey);
```
⚠️ **Breaking change.** Отметить в `CHANGELOG`/commit message вместе с бампом версии 1.3.

---

### 2.4 Per-state `CancellationTokenSource` → НЕ ВНЕДРЯТЬ, задокументировать

**Файлы:** `Runtime.Async/AsyncStateMachine.cs:77-121, 141-180`.

Текущее поведение: токен прокидывается насквозь; fire-and-forget задачи состояния не отменяются автоматически при Exit.

**Анализ:**
- Аллокация: +1 CTS на переход (~100 байт). Против zero-alloc policy пакета.
- Сложность: +1 поле, правильный Cancel→Dispose порядок, идемпотентность в `Stop`.
- Польза узкая: страхует только от анти-паттерна «fire-and-forget без владения токеном». При обычном `await UniTask.Delay(ct)` пользовательский `ct` всё отменит сам.

**Решение:** оставить как есть. В README (секция про async и `CancellationToken`) добавить явное разъяснение: `The FSM does not create per-state tokens. If your state spawns fire-and-forget background work, manage its lifecycle explicitly in OnExitAsync.`

---

## BATCH 3 — Расхождение sync/async (drift)

### 3.1 Разный control-flow для отложенных переходов

- Sync: рекурсия. `PerformTransition()` → `ProcessPendingTransition()` → `PerformTransition()` (`Runtime/StateMachine.cs:134-174`).
- Async: `while (true)` цикл (`Runtime.Async/AsyncStateMachine.cs:141-180`).

Оба работают корректно благодаря `MaxTransitionDepth = 16`, но это поддерживаемый drift — фикс в одной не попадёт в другую без внимания.

**Фикс:** переписать sync через `while (true)`, как в async. Тесты `SetState_PingPongingOnEnter_ThrowsAtMaxTransitionDepth` и цепочки (`StateMachineTests.cs:237-316`) уже покрывают сценарии.

---

### 3.2 `SubStateMachine._initialState` фиксирован в конструкторе — LOW

**Файлы:** `Runtime/SubStateMachine.cs:7`, `Runtime.Async/AsyncSubStateMachine.cs:9`.

Sub-FSM всегда стартует в том состоянии, что задано при создании. Нельзя «прыгнуть» в другое при активации из родителя.

**Фикс (опционально):** либо добавить проперти/второй конструктор, либо признать фичей и добавить строку в README про «sub starts at fixed initial state — by design; reset intermediate child state before re-entering». Обычно достаточно второго.

---

## BATCH 4 — Пробелы в тестах

Добавлять симметрично в sync (`Tests/StateMachineTests.cs`, `Tests/SubStateMachineTests.cs`) и async (`Tests/AsyncStateMachineTests.cs`, `Tests/AsyncSubStateMachineTests.cs`).

### 4.1 Self-transition — закрепляет 2.1

```csharp
[Test]
public void SetState_ToCurrentKey_IsNoOp()
{
    _fsm.AddState(Key.A, NewState("A"));
    _fsm.Start(Key.A);
    _log.Clear();

    _fsm.SetState(Key.A);

    CollectionAssert.IsEmpty(_log);
    Assert.AreEqual(Key.A, _fsm.CurrentKey);
}
```

### 4.2 Multiple deferred `SetState` — закрепляет 2.2

```csharp
[Test]
public void SetState_MultipleCallsDuringOnEnter_LastWriteWins()
{
    var a = NewState("A");
    var b = NewState("B");
    var c = NewState("C");
    var d = NewState("D");
    b.OnEnterHook = () =>
    {
        _fsm.SetState(Key.C);
        _fsm.SetState(Key.D);
    };

    _fsm.AddState(Key.A, a);
    _fsm.AddState(Key.B, b);
    _fsm.AddState(Key.C, c);
    _fsm.AddState(Key.D, d);
    _fsm.Start(Key.A);
    _log.Clear();

    _fsm.SetState(Key.B);

    CollectionAssert.AreEqual(new[] { "A:Exit", "B:Enter", "B:Exit", "D:Enter" }, _log);
    Assert.AreEqual(Key.D, _fsm.CurrentKey);
    Assert.AreEqual(0, c.EnterCount);
}
```

### 4.3 `Update()` во время перехода (sync)

```csharp
[Test]
public void Update_CalledDuringOnEnter_TicksNewState()
{
    var a = NewState("A");
    var b = NewState("B");
    b.OnEnterHook = () => _fsm.Update(0.016f);

    _fsm.AddState(Key.A, a);
    _fsm.AddState(Key.B, b);
    _fsm.Start(Key.A);
    _log.Clear();

    _fsm.SetState(Key.B);

    CollectionAssert.AreEqual(new[] { "A:Exit", "B:Enter", "B:Update" }, _log);
}
```

### 4.4 Async: отмена реально соблюдается

Текущий `StartAsync_WithCancellationToken_PassesSameTokenToStateCallbacks` (`Tests/AsyncStateMachineTests.cs:327-351`) проверяет только *передачу*, не *уважение*. Нужно:

- `SetStateAsync_CancelledDuringOnEnterAwait_ThrowsOperationCanceledException`
- `SetStateAsync_CancelledDuringOnExitAwait_ThrowsOperationCanceledException`
- `StopAsync_CancelledDuringOnExitAwait_PropagatesCancellation`

Каждый — через `AsyncCallbackState` с `await UniTask.Delay(5000, ct)` внутри, `cts.CancelAfter(50)`, ожидание `OperationCanceledException`.

### 4.5 Async: deferred transition использует новый `ct` (проверка фикса 1.2)

```csharp
[Test]
public async Task SetStateAsync_CalledDuringOnEnter_UsesDeferredCallerToken()
{
    var cts1 = new CancellationTokenSource();
    var cts2 = new CancellationTokenSource();
    CancellationToken receivedAtC = default;

    var a = NewState("A");
    var b = new AsyncCallbackState(onEnterAsync: _ =>
    {
        // Deferred call with *different* token
        _ = _fsm.SetStateAsync(Key.C, cts2.Token);
        return UniTask.CompletedTask;
    });
    var c = new AsyncCallbackState(onEnterAsync: ct =>
    {
        receivedAtC = ct;
        return UniTask.CompletedTask;
    });

    _fsm.AddState(Key.A, a);
    _fsm.AddState(Key.B, b);
    _fsm.AddState(Key.C, c);
    await _fsm.StartAsync(Key.A);

    await _fsm.SetStateAsync(Key.B, cts1.Token);

    Assert.AreEqual(cts2.Token, receivedAtC);
}
```

### 4.6 Async sub: отмена каскадит в ребёнка при переходе родителя

```csharp
[Test]
public async Task ParentTransitionOutOfSub_CancellingTokenAbortsChildEnter()
{
    var cts = new CancellationTokenSource();
    var childStarted = false;
    var childCompleted = false;

    var slowChild = new AsyncCallbackState(onEnterAsync: async ct =>
    {
        childStarted = true;
        await UniTask.Delay(5000, cancellationToken: ct);
        childCompleted = true;
    });

    var parent = new AsyncStateMachine<Parent>();
    var sub = new AsyncSubStateMachine<Combat>(Combat.Aiming);
    sub.AddState(Combat.Aiming, slowChild);
    parent.AddState(Parent.Menu, NewState("Menu"));
    parent.AddState(Parent.Combat, sub);
    await parent.StartAsync(Parent.Menu);

    var go = parent.SetStateAsync(Parent.Combat, cts.Token);
    cts.CancelAfter(50);

    Assert.ThrowsAsync<OperationCanceledException>(async () => await go);
    Assert.IsTrue(childStarted);
    Assert.IsFalse(childCompleted);
}
```

### 4.7 CallbackState / AsyncCallbackState с null-коллбэками — полный FSM-контекст

Поверхностно покрыто в `CallbackStateTests`/`AsyncCallbackStateTests`. Добавить интеграционный тест через `StateMachine`/`AsyncStateMachine`: зарегистрировать `new CallbackState()` без коллбэков, выполнить полный цикл Start → SetState → Stop, убедиться что никаких `NullReferenceException`.

---

## BATCH 5 — Документация

### 5.1 README игнорирует async-часть — HIGH

`README.md` описывает только sync. Async-типы (`AsyncStateMachine<TKey>`, `AsyncSubStateMachine<TKey>`, `IAsyncState`, `AsyncStateBase`, `AsyncCallbackState`) — в публичном API, но в README их нет.

Добавить секцию `## Async State Machines` со следующим содержанием:

- **When to use:** long-running Enter/Exit — загрузка ассетов, установка сетевых сессий, cleanup с awaitable teardown.
- **Differences from sync:** `OnEnterAsync(CancellationToken)` и `OnExitAsync(CancellationToken)` возвращают `UniTask`. `OnUpdate(float)` остаётся sync (per-frame, нельзя await).
- **CancellationToken semantics:**
  - Токен прокидывается из `StartAsync`/`SetStateAsync`/`StopAsync` в `OnEnterAsync`/`OnExitAsync`.
  - Для отложенных переходов (queued во время другого перехода) используется токен *caller’а deferred-вызова*, не изначального перехода (после фикса 1.2).
  - FSM не создаёт per-state токены — fire-and-forget задачи состояния нужно отменять самому в `OnExitAsync`.
- **Example:** `LoadingState` который `await LoadAssets(ct)` в `OnEnterAsync`.

### 5.2 Self-transition не задокументировано (после решения 2.1)

В секцию `### Transitions`:
```markdown
Calling `SetState(CurrentKey)` — a self-transition — is a no-op; the state is not re-entered.
```

### 5.3 Last-write-wins для multiple-deferred (после решения 2.2)

В ту же секцию, после строки про max depth 16:
```markdown
If multiple `SetState` calls happen during `OnEnter`/`OnExit`, only the last one executes — earlier queued keys are overwritten.
```

### 5.4 Thread-safety

В `## Design Decisions` добавить пункт:
```markdown
- **Not thread-safe** — designed for single-threaded access (game main loop). Do not call concurrently from multiple threads.
```

### 5.5 `StateChanged` — порядок

В секцию `### State Change Events`:
```markdown
The event fires after the new state's `OnEnter` completes. For chained deferred transitions, the event fires once per hop (e.g. A→B→C fires `StateChanged(A, B)` then `StateChanged(B, C)`).
```

### 5.6 «Zero-allocation runtime» — уточнить формулировку

`README.md:3` — переформулировать: `Zero allocations per update and transition; setup allocates once (Dictionary backing store).`

---

## BATCH 6 — Nice-to-haves (низкий приоритет)

### 6.1 `GetCurrentState<T>()`

Шорткат в `StateMachine<TKey>` и `AsyncStateMachine<TKey>`:
```csharp
public T? GetCurrentState<T>() where T : class
    => _currentState as T;
```

### 6.2 `TrySetState(key)`

После решения 2.1 (no-op self-transition) — возможно, избыточно. `SetState` уже «безопасен» к повторным вызовам. Если нужен bool-результат «был ли реальный переход» — добавить:
```csharp
public bool TrySetState(TKey key)
{
    if (_comparer.Equals(_currentKey, key)) return false;
    SetState(key);
    return true;
}
```
Оценить после внедрения 2.1 — возможно, не нужен.

### 6.3 `HasPendingTransition` / `PendingKey`

Для дебага и для логики в `OnExit`, которой нужно знать точку назначения:
```csharp
public bool HasPendingTransition => _hasPendingTransition;
public TKey? PendingKey => _hasPendingTransition ? _pendingKey : default;
```

### 6.4 XML-комментарии на публичный API

Сейчас XML-доков нет вообще. Минимум — на `StateMachine<TKey>`, `AsyncStateMachine<TKey>`, `IState`, `IAsyncState`, `SubStateMachine`, `AsyncSubStateMachine` и их публичные методы/проперти. Особенно критичны семантические моменты: `SetState` deferred behavior, `CurrentKey` после Stop, `CancellationToken` lifecycle.

---

## Порядок внедрения (предложение)

1. **Batch 1** — без обсуждения, фиксить.
2. **Batch 2** — одновременно с 1 (решения уже приняты).
3. **Batch 3.1** — вместе с 1.2 (всё равно трогаем `PerformTransitionAsync`, заодно сглаживаем sync control-flow).
4. **Batch 4** — одним коммитом после 1-3 (тесты закрепляют все изменения).
5. **Batch 5** — отдельным коммитом (чистая документация).
6. **Batch 6** — опционально, по желанию, после стабилизации 1-5.

## Что сознательно оставлено как есть

- **Single-threaded FSM без блокировок** — соответствует контракту и zero-alloc policy. Документируется в 5.4.
- **`[MethodImpl(AggressiveInlining)]`** на hot-path — корректное использование.
- **LINQ в Runtime** — отсутствует, правило CLAUDE.md соблюдено.
- **UniTask как зависимость async-части** — правильный выбор (struct ValueTask-like, без аллокаций на completed path).
- **`sealed` на `CallbackState`/`AsyncCallbackState`** — корректно, запрещает наследование от helper’ов.
- **`SubStateMachine` через explicit interface implementation** (`void IState.OnEnter()` вместо `public virtual`) — сознательно: не даёт наследникам сломать семантику Start/Stop.
