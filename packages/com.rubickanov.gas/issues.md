# GAS — issues & improvements

Аудит пакета `com.rubickanov.gas` от 2026-04-17.

Батчи подобраны так, чтобы внутри каждого — связанные изменения, которые имеет смысл делать одним PR. Внутри батча issue отсортированы по убыванию приоритета. Сложность каждого батча помечена: **easy** / **medium** / **hard**.

Формат issue:
```
### [ID] Заголовок — severity
**Где:** file:line
**Что:** короткое описание
**Почему это проблема:** реальный failure mode
**Предложение:** конкретное изменение (или DECISION NEEDED: варианты)
```

---

## BATCH 1 — Correctness: скрытые логические ошибки — **easy/medium**

### 1.1 Периодический тик срабатывает, когда эффект уже должен был истечь в этом же фрейме — correctness-risk
**Где:** `Runtime/Effects/EffectController.cs:140-167`
**Что:** В `Tick(deltaTime)` сначала обрабатывается periodic (lines 145-154), потом проверяется Duration (lines 157-166). При большом `deltaTime` (пауза, lag spike) эффект успеет несколько раз применить periodic, даже если его `RemainingDuration` уже ушёл в минус на первом же periodic.
**Почему:** DOT, который должен был нанести 2 тика, наносит 5 при лаге, потому что Duration проверяется *после* periodic, а не между тиками.
**Предложение:** на каждой итерации внутри `while`-цикла periodic также декрементить/проверять `RemainingDuration`. Псевдокод:
```
while periodTimer >= period AND remainingDuration > 0:
    apply periodic
    periodTimer -= period
```
Либо: сначала вычислить сколько тиков укладывается в `min(deltaTime, remainingDuration)`.

### 1.2 Несколько Override-модификаторов на один атрибут — «последний выигрывает» по порядку итерации — correctness-risk
**Где:** `Runtime/Calculation/ModifierAggregator.cs:37-39`
**Что:** `overrideValue` просто перезаписывается. Порядок = порядок в `_activeEffects` (порядок применения), внутри эффекта — порядок в списке модификаторов. Нет приоритетов, нет предупреждений.
**Почему:** Реальные сценарии, где это больно:
- `Invulnerable` (Override Health=999) vs `Execute` (Override Health=0) — порядок решает жизнь игрока.
- `Stun` (Override MoveSpeed=0) vs `CC-Immunity` (Override MoveSpeed=base) — Immunity должен выигрывать всегда.
- `GodMode` debug-чит vs любой Override-debuff — GodMode обязан иметь абсолютный приоритет.
- Конкурирующие бафы `Rage`/`Frenzy` на одну `Attack` — игрок ожидает «лучший».

**Предложение (ПРИНЯТО):** добавить `int Priority` в `Modifier` (default = 0).
- Сериализация ScriptableObject совместима: отсутствующее поле десериализуется в 0, старые ассеты работают как раньше.
- В `ModifierAggregator.Aggregate` среди активных Override выбирается модификатор с максимальным `Priority`, при равенстве — last-wins (документируем).
- В `SerializedModifier` + property drawer добавить поле Priority (int).
- Designer ставит `CC-Immunity.Priority = 100`, `Stun.Priority = 0` — immunity выигрывает детерминированно.
- Это строгое расширение, не breaking change.

### 1.3 `GameplayAttribute.BaseValue` публичный сеттер не триггерит пересчёт — bug
**Где:** `Runtime/Attributes/GameplayAttribute.cs:10-18`. Комментарий «CurrentValue will be recalculated by EffectController» вводит в заблуждение.
**Что:** Прямая запись `attr.BaseValue = 100` меняет base, но `CurrentValue` остаётся старым до следующего `Apply/Remove/Tick`. `ValueChanged` не срабатывает.
**Почему:** Любое место «level up bumps max health» ломается тихо — UI видит старое значение.

**Предложение (ПРИНЯТО):**
- Сеттер `GameplayAttribute.BaseValue` → `internal set`.
- `AttributeSet.SetBaseValue(GameplayTag tag, float value)` — единственная публичная точка записи base.
- `AttributeSet` поднимает событие `BaseValueChanged(GameplayTag tag, float newBaseValue)`.
- `EffectController` в конструкторе подписывается и в хендлере вызывает `RecalculateAttributes()` (ограниченный конкретным тегом, когда сделаем 4.1 dirty-tracking).
- Ownership чистый: writes идут через AttributeSet, recalc owned by controller. Test-friendly (AttributeSet тестируется без EffectController).

### 1.4 `AttributeSet.Define(tag, newBaseValue)` тихо игнорирует новый baseValue для уже существующего тега — footgun
**Где:** `Runtime/Attributes/AttributeSet.cs:10-18`
**Что:** Если атрибут с таким тегом уже определён, возвращается старый, новый `baseValue` теряется без предупреждения.
**Почему:** Типичная ошибка настройки — повторная `Define(Health, 200)` вместо `SetBaseValue(Health, 200)`. Молча не применится.
**Предложение:** бросать `InvalidOperationException` при попытке повторного Define с отличным baseValue; переименовать в `GetOrDefine` при желании сохранить мягкое поведение. Альтернатива — `TryDefine` (возвращает bool) + отдельный `SetBaseValue` из issue 1.3.

---

## BATCH 2 — Валидация и «тихие провалы» — **easy**

### 2.1 `ModifierAggregator.ApplyInstant` молча выходит при отсутствующем атрибуте — API-footgun
**Где:** `Runtime/Calculation/ModifierAggregator.cs:50-51`
**Что:** `if (attribute == null) return;` — без лога, без исключения.
**Почему:** Опечатка в теге атрибута (`Attribute.Healt` вместо `Health`) → модификатор не применяется, никаких сигналов нигде.
**Предложение:** использовать логгер пакета `com.rubickanov.logging` (чтобы не тащить `UnityEngine.Debug` в `noEngineReferences` рантайм). Уровень — `Warning`. Альтернатива: бросать исключение в Debug-билдах (`#if UNITY_EDITOR || DEVELOPMENT_BUILD`).

### 2.2 `SerializedModifier` с незаполненным атрибутом в инспекторе даёт «мёртвый» модификатор — bug
**Где:** `Unity/SerializedModifier.cs:18-20` — эффект оказывается в `EffectDef.Modifiers` с `Modifier.Attribute = default GameplayTag`.
**Что:** В `Aggregate`/`ApplyInstant` такой модификатор не матчится ни с одним атрибутом, молча no-op.
**Почему:** Designer создаёт эффект, забывает прокликать атрибут в одной из строчек — эффект «работает», но применяет не все модификаторы.
**Предложение:** в `GameplayEffectAsset.ToDef()` перед копированием модификатора проверять `_modifiers[i].Attribute.IsValid`, в Debug-билдах бросать либо логировать. Плюс в `SerializedModifierPropertyDrawer` рисовать `HelpBox(MessageType.Error)` при незаполненном теге.

### 2.3 Нет `OnValidate` в `GameplayEffectAsset` — correctness-risk
**Где:** `Unity/GameplayEffectAsset.cs`
**Что:** Можно сохранить: `Period > DurationSeconds`, `DurationSeconds < 0`, `Period < 0`, `Duration=Instant + Period > 0`, `Duration=Instant + DurationSeconds > 0`, пустой список модификаторов.
**Почему:** Ничего из этого не бьёт в рантайме явно, но часть поведения становится undefined (например, periodic шире duration).
**Предложение:** добавить `OnValidate()`:
- `_durationSeconds = Mathf.Max(0f, _durationSeconds)`
- `_period = Mathf.Max(0f, _period)`
- если `_duration == DurationPolicy.Instant` → обнулить `_durationSeconds` и `_period`
- `Debug.LogWarning`, если `_period > _durationSeconds` при `DurationPolicy.Duration`

### 2.4 `EffectDef` конструктор без инвариантов — correctness-risk
**Где:** `Runtime/Effects/EffectDef.cs`
**Что:** Принимает что угодно — отрицательные длительности/периоды, `null` для любой коллекции.
**Почему:** `OnValidate` защищает только путь из `GameplayEffectAsset`. Код, который строит `EffectDef` напрямую (сеть, тесты, процедурные эффекты), может прокинуть мусор.
**Предложение:** добавить проверки в конструкторе: `durationSeconds >= 0`, `period >= 0`, `modifiers != null`. Null-коллекции тегов — заменять на `GameplayTagContainer.Empty` (или требовать не-null).

---

## BATCH 3 — API consistency и эргономика — **medium**

### 3.1 Возврат-типы методов удаления несогласованы — inconsistent-api
**Где:** `Runtime/Effects/EffectController.cs` — `RemoveEffect` (bool), `RemoveEffectsWithTag` (int), `RemoveAllEffects` (void)
**Что:** Три метода — три разных контракта.
**Предложение:** унифицировать на `int` (количество удалённых). `RemoveEffect` → 0 или 1, `RemoveAllEffects` → размер списка до очистки.

### 3.2 Асимметрия проверки тегов в `ApplyEffect` vs `RemoveEffectsWithTag` — inconsistent-api
**Где:** `Runtime/Effects/EffectController.cs:41` (`HasTag`) vs `:115` (`Matches`)
**Что:** При применении эффекта — «container has exact tag», при удалении по тегу — «effect tag matches query (hierarchy)». Тесты документируют, но пользователь интуитивно ожидает одинаковую семантику.

**Предложение (ПРИНЯТО):** применить hierarchy-aware (`Matches`) везде.
- В `ApplyEffect` заменить `def.RemoveEffectsWithTags.HasTag(existing.Def.EffectTag)` на логику «существует ли в `def.RemoveEffectsWithTags` тег, для которого `existing.Def.EffectTag.Matches(tag)` истинно». Если в `GameplayTagContainer` уже есть `MatchesAny` — использовать его; если нет — добавить.
- Поведение согласуется с Unreal GAS: `Dispel [Buff]` удаляет всё поддерево `Buff.*`.
- Тесты в `EffectControllerConditionsTests` и `EffectControllerStackingTests` обновить — старый кейс «container has exact tag» заменить на кейсы с иерархией.

### 3.3 `ActiveEffect.RemainingDuration` / `PeriodTimer` — public getter + internal setter — inconsistent-api
**Где:** `Runtime/Effects/ActiveEffect.cs:9-10`
**Что:** Эти поля — деталь реализации для `EffectController.Tick`. Снаружи им делать нечего.
**Предложение:** `{ get; private set; }` и вынести мутацию в internal-методы класса (`DecrementDuration`, `AdvancePeriod`), которые вызывает контроллер.

### 3.4 Sentinel `-1f` в `RemainingDuration` для Infinite мёртвый — style
**Где:** `Runtime/Effects/ActiveEffect.cs:18`
**Что:** Значение никогда не читается — `Tick` (line 157) проверяет `DurationPolicy.Duration`, не значение. Для Infinite поле бесполезно.
**Предложение:** убрать тернарный оператор, присваивать `0f`.

### 3.5 Дублирующее поле `_activeEffectsReadOnly` — style
**Где:** `Runtime/Effects/EffectController.cs:11-12, 24`
**Что:** `_activeEffectsReadOnly = _activeEffects` — та же самая ссылка. Индирекция без эффекта — `IReadOnlyList<T>` на интерфейсном уровне уже запрещает мутацию.
**Предложение:** удалить `_activeEffectsReadOnly`, `ActiveEffects => _activeEffects`.

### 3.6 `RemoveEffect` итерирует вперёд, остальные — назад — style
**Где:** `Runtime/Effects/EffectController.cs:92` (forward) vs `:37, :60, :112, :129` (backward)
**Что:** Функционально безопасно (break на первом match), но стилистически выбивается.
**Предложение:** переписать на backward для консистентности.

### 3.7 `GameplayAttribute.ValueChanged` отдаёт только новое значение — inconsistent-api
**Где:** `Runtime/Attributes/GameplayAttribute.cs:22, 34`
**Что:** `Action<float>` — нет `oldValue`. UI/логика часто хочет знать delta.
**Предложение:** заменить на `Action<float oldValue, float newValue>`. Breaking, но API молодое — сейчас проще поменять.

### 3.8 `Modifier` не реализует `IEquatable<Modifier>` — low priority
**Где:** `Runtime/Effects/Modifier.cs`
**Что:** Struct без явного Equals/GetHashCode. В текущем коде не используется как ключ словаря — проблема потенциальная.
**Предложение:** добавить `IEquatable<Modifier>` когда (если) появится необходимость. Пока оставить — YAGNI.

---

## BATCH 4 — Performance — **medium/hard**

### 4.1 `RecalculateAttributes` — O(A × E × M) на каждое Apply/Remove/периодический тик — perf
**Где:** `Runtime/Effects/EffectController.cs:222-230` + `ModifierAggregator.Aggregate`
**Что:** Полный перебор всех атрибутов × всех эффектов × всех модификаторов при любом изменении.
**Почему:** При 100 атрибутах × 50 эффектах × 3 модификатора — 15 000 сравнений на апдейт. При множестве DOT-эффектов — заметно.
**Предложение:**
- **A (easy, рекомендую сейчас):** dirty-tracking — recalc только тех атрибутов, которые входят в `Modifiers` изменённого эффекта. Собрать `HashSet<GameplayTag>` dirty-атрибутов при Apply/Remove, чистить в конце `RecalculateAttributes`.
- **B (hard, отложить до профилирования):** `Dictionary<GameplayTag, List<ActiveEffect>>` — per-attribute индекс активных эффектов, обновлять на Apply/Remove.

### 4.2 `IsTagGrantedByOtherEffect` — O(N) per granted tag при удалении — perf
**Где:** `Runtime/Effects/EffectController.cs:211-220`
**Что:** Удаление эффекта с M granted-тегами и N активными эффектами даёт O(N × M).
**Предложение:** если `GameplayTagContainer` поддерживает ref-count на `AddTag`/`RemoveTag` (нужно проверить в `com.rubickanov.gameplaytags`), заменить проверку на простой `_tags.RemoveTag(tag)`. Если нет — оставить, на N,M < 50 это шум.

### 4.3 `dirty` флаг в `Tick` — это хорошо, оставляем
**Где:** `Runtime/Effects/EffectController.cs:138, 169`
**Статус:** GOOD, не трогать.

---

## BATCH 5 — Event / reentrancy semantics — **medium**

### 5.1 `EffectApplied` срабатывает после того как теги уже гранированы и атрибуты пересчитаны — API-footgun
**Где:** `Runtime/Effects/EffectController.cs:77-83`
**Что:** Если подписчик внутри `EffectApplied` вызывает `RemoveEffect` / `ApplyEffect`, он видит уже изменённое состояние тегов и атрибутов. Логически корректно, но неочевидно.
**Предложение:** задокументировать порядок в XML doc на `EffectApplied`:
```
/// Fires AFTER: tags granted, attributes recalculated, effect added to ActiveEffects.
/// Safe to call ApplyEffect/RemoveEffect from handler; effects from handlers are processed immediately.
```

### 5.2 `EffectRemoved` срабатывает в `RemoveEffectInternal` **после** снятия тегов, но **до** удаления из списка — API-footgun
**Где:** `Runtime/Effects/EffectController.cs:199-209`
**Что:** В `RemoveEffect`/`RemoveEffectsWithTag`/`RemoveAllEffects` сначала вызывается `RemoveEffectInternal` (стреляет событием), потом `_activeEffects.RemoveAt(i)`. В момент события эффект ещё в списке `ActiveEffects`, но теги уже сняты.
**Почему:** Несогласованное состояние, видимое подписчику.
**Предложение:** вынести `EffectRemoved?.Invoke` из `RemoveEffectInternal` в вызывающие методы — после `_activeEffects.RemoveAt(i)`. Либо наоборот: тeги снимать в вызывающих методах, `RemoveEffectInternal` превратить в финализирующий хук.

### 5.3 Нет reentrancy guard для `ApplyEffect`/`RemoveEffect` во время `Tick` — correctness-risk
**Где:** `Runtime/Effects/EffectController.cs:136-170`
**Что:** Если подписчик `EffectRemoved` вызывает `ApplyEffect` во время `Tick`, новый эффект добавляется в `_activeEffects` пока мы итерируем. Backward-iteration спасает (индекс только уменьшается), но новый эффект в этом тике не тикнется — не документировано.
**Предложение:** задокументировать контракт («new effects applied during Tick begin ticking next frame»). Рекомендую именно документацию. Альтернатива — defer-очередь `_pendingApplies`/`_pendingRemoves`, обрабатывать после основного цикла, но это усложнение без реальной потребности.

---

## BATCH 6 — Documentation & UX — **easy**

### 6.1 README использует несуществующие константы `Attribute.Health` в примерах — docs
**Где:** `README.md`
**Что:** Примеры вида `attributes.Define(Attribute.Health, 100f);` ссылаются на класс `Attribute`, которого нет в пакете.
**Предложение:** добавить preamble «Определите свои теги атрибутов через `com.rubickanov.gameplaytags` (`public static readonly GameplayTag Health = ...`)» и/или показать полный snippet до первого использования.

### 6.2 Нет XML doc comments на публичной поверхности — docs
**Где:** все публичные типы: `GameplayAttribute`, `AttributeSet`, `EffectController`, `EffectDef`, `EffectSpec`, `ActiveEffect`, `ActiveEffectHandle`, `Modifier`, `ModifierOp`, `DurationPolicy`, `StackingPolicy`, `GameplayEffectAsset`.
**Предложение:** пройтись одним проходом, `/// <summary>` на всё публичное. Для энумов описать каждое значение.

### 6.3 Нет `[Tooltip]` на полях `GameplayEffectAsset` — UX
**Где:** `Unity/GameplayEffectAsset.cs:10-19`
**Предложение:** `[Tooltip("...")]` каждому полю + `[Min(0)]` на `_durationSeconds` и `_period`.

### 6.4 `SerializedModifierPropertyDrawer` не подсвечивает незаполненный тег — UX
**Где:** `Editor/SerializedModifierPropertyDrawer.cs`
**Предложение:** при `!tag.IsValid` рисовать `HelpBox(MessageType.Error)` или иконку рядом со строкой (связано с issue 2.2).

### 6.5 Семантика `Magnitude` не задокументирована — docs
**Где:** `Runtime/Effects/EffectSpec.cs`, `README.md`
**Что:** `Magnitude` умножает **значение модификатора**, не конечный результат. Для `Multiply` модификатор `*2.0` с `magnitude=0.5` даёт `*1.0` (т.е. выключает), а не `*1.41`.
**Предложение:** короткий параграф в README + XML doc на `EffectSpec.Magnitude` с примером.

### 6.6 Не задокументировано, что periodic мутирует `BaseValue`, не `CurrentValue` — docs
**Где:** `Runtime/Effects/EffectController.cs:192-197` — `ApplyPeriodicModifiers` → `ApplyInstant` → `BaseValue += ...`
**Что:** DOT-урон навсегда снижает максимум здоровья, если Health смоделирован как один атрибут. Это стандартное Unreal GAS-поведение (periodic = instant execution on tick), но в README не прописано.
**Предложение:** секция «Instant vs Persistent modifications» в README с примером «Health как MaxHealth + CurrentHealth» для DOT-сценариев.

---

## BATCH 7 — Test coverage gaps — **easy**

### 7.1 Нет теста на reentrancy внутри event handlers
**Что тестировать:** `ApplyEffect` внутри `EffectApplied`, `RemoveEffect` внутри `EffectRemoved`, `ApplyEffect` внутри `ValueChanged`. Проверить отсутствие исключений и корректное финальное состояние.

### 7.2 Нет теста для `Period == 0` на `DurationPolicy.Duration` эффекте
**Ожидание:** periodic не применяется, эффект просто отсчитывает длительность.

### 7.3 Нет теста для `DurationSeconds == 0` на `DurationPolicy.Duration`
**Ожидание:** после решения по 2.3 — либо мгновенное удаление на следующем `Tick`, либо запрещённое состояние (валидация в `EffectDef`).

### 7.4 Нет теста на stale `CurrentValue` после прямой записи `BaseValue`
Связано с issue 1.3. После внедрения `SetBaseValue` — проверить, что `CurrentValue` пересчитывается.

### 7.5 Нет теста на `AttributeSet.Define` того же тега с другим baseValue
Связано с issue 1.4.

### 7.6 Нет теста на независимость порядка модификаторов внутри `EffectDef`
**Что тестировать:** `{Add 10, Multiply 2}` и `{Multiply 2, Add 10}` дают одинаковый результат (по формуле `(base + addSum) * mulProduct`).

### 7.7 Нет теста на взаимодействие Infinite-эффектов с `RemoveEffectsWithTag`
**Что тестировать:** infinite эффект с `EffectTag` удаляется через `RemoveEffectsWithTag`, granted-теги корректно снимаются.

### 7.8 Нет теста на многократный Override внутри одного EffectDef и между эффектами
Связано с issue 1.2. После внедрения `Priority` — проверить:
- Два Override на одном атрибуте, разные Priority — выигрывает максимальный.
- Два Override с равным Priority — last-wins (детерминированно).

---

## Что в пакете сделано ХОРОШО (не трогать)

- `GAS.Runtime.asmdef` — `noEngineReferences: true`, никакого `UnityEngine` в рантайме.
- Ноль LINQ в рантайме, только `for`/`foreach` по `IReadOnlyList`.
- Чёткое разделение `EffectDef` (immutable config) / `EffectSpec` (runtime) / `ActiveEffect` (tracked).
- Backward-iteration для безопасного удаления в `Tick`, `RemoveAllEffects`, `RemoveEffectsWithTag`.
- Детерминированная формула `hasOverride ? override : (base + addSum) * mulProduct` — явная и тестируемая.
- `IsTagGrantedByOtherEffect` корректно не удаляет granted-тег, если другой эффект его тоже даёт.
- `dirty` флаг в `Tick`, чтобы избежать лишних пересчётов.
- 11 тестовых файлов — широкое покрытие базового поведения.
