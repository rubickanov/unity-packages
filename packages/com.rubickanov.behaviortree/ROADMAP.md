# Behavior Tree — Roadmap

Что добавить, чтобы догнать фичи Opsive Behavior Designer, оставаясь лёгким и без зависимостей. Порядок приоритетный: сверху — то, без чего самопис реально жмёт, снизу — полезные удобства.

## Приоритет 1 — без этого БТ неполноценный

### 1. BTParallel (composite)
Запускает всех детей одновременно за один тик. Без этого нельзя выразить «двигайся И стреляй И следи за таймером».

Параметры политики завершения:
- `SuccessPolicy`: `RequireOne` / `RequireAll`
- `FailurePolicy`: `RequireOne` / `RequireAll`

При завершении по политике — `Abort()` всем ещё `Running` детям.

Файл: `Runtime/Nodes/Composites/BTParallel.cs`.

### 2. Conditional Abort (самая важная фича)
Декоратор-условие, которое может **прервать ветку**, если его статус изменился, пока другая ветка `Running`. В Opsive это «Lower Priority» / «Self» abort — то, ради чего БТ вообще отличается от плоского switch.

Подход:
- Ввести `BTAbortType { None, Self, LowerPriority, Both }` на `BTDecorator`-условиях
- `BTSelector` / `BTSequence` перед тиком активной ветки перепроверяют conditional-decorator'ы слева от неё (`LowerPriority`) и внутри неё (`Self`)
- Если условие изменилось — abort текущей ветки, перезапуск селектора

Требует:
- Трек «текущего активного индекса» в композитах (уже есть в `BTSequence._currentIndex`)
- Метод `BTDecorator.IsConditionalAbort()` + кеш условных декораторов на уровне композита
- Регрессионные тесты на каждый abort-type

Это самый нетривиальный пункт, стоит делать отдельным PR.

### 3. Repeat / UntilSuccess / UntilFailure (decorators)
Тривиальные, но нужны постоянно:
- `BTRepeat(count)` — тикает ребёнка N раз (или бесконечно при `count <= 0`)
- `BTUntilSuccess` — тикает пока ребёнок не вернёт `Success`
- `BTUntilFailure` — симметрично

Файлы: `Runtime/Nodes/Decorators/BTRepeat.cs`, `BTUntilSuccess.cs`, `BTUntilFailure.cs`.

---

## Приоритет 2 — сильно повышает удобство

### 4. Random composites
- `BTRandomSelector` — случайный порядок детей при старте
- `BTRandomSequence` — симметрично

Использовать `Rubickanov.Utils.DeterministicRandom` из пакета `utils`, чтобы можно было сидить для тестов/реплеев.

### 5. Runtime debugger в редакторе
Подсветка активных нод в `BehaviorTreeGraphView` во время play mode:
- `BehaviorTreeRunner` публикует событие `OnTicked(BTNode root)` (только в `#if UNITY_EDITOR`)
- `BehaviorTreeEditorWindow` подписывается и красит ноды по `BTNode.LastStatus`:
  - Running → жёлтый
  - Success → зелёный
  - Failure → красный
- Активный ранер выбирается из выделенного GameObject в сцене

Без этого отлаживать большие деревья — боль.

### 6. Succeeder / Failer decorators
Однострочные — всегда возвращают `Success` или `Failure` независимо от ребёнка. Нужны для склейки условий в `BTSequence`.

---

## Приоритет 3 — удобства, без которых можно жить

### 7. BTWait (leaf)
`BTWait(seconds)` — возвращает `Running` пока не пройдёт таймер, потом `Success`. Каждый проект пишет его сам — лучше иметь в пакете.

### 8. Shared Blackboard между деревьями
Сейчас `Blackboard` создаётся внутри `BehaviorTreeRunner`. Добавить конструктор `BehaviorTreeRunner.Initialize(BTNode root, Blackboard shared)` — позволит нескольким агентам/деревьям шарить данные (squad state, global alerts).

### 9. BTNodeDescription в nested categories
Сейчас `[BTNodeDescription]` берёт плоскую строку категории. Поддержать вложенность через `/`: `"AI/Combat/Attack"`. Правка в `BehaviorTreeSearchWindow`.

### 10. Async leaves (опционально)
Если в проекте активно UniTask — отдельный `com.rubickanov.behaviortree.unitask` с `BTAsyncAction : BTLeafAction`, который хранит `UniTask` и возвращает `Running` пока не завершится. По аналогии с `eqs.UniTask`. **Не тащить UniTask в рантайм-асембли основного пакета.**

---

## Чего НЕ делать

- **Не переписывать сериализацию на ScriptableObject-per-node.** `[SerializeReference]` + один ассет — это осознанное решение (см. README, "Design Decisions"). Оно лучше масштабируется в VCS.
- **Не тащить рефлексию ради SharedVariable-like системы.** Блэкборд с типизированными ключами уже даёт всё, что нужно, без GC-мусора.
- **Не добавлять встроенную библиотеку «готовых задач» (NavMesh/Seek/Flee/…).** Это роль consumer-проекта. Пакет должен оставаться core-only.
- **Не добавлять визуальный дебагер в Runtime-асембли.** Только `#if UNITY_EDITOR` или отдельный Editor-файл.

---

## Порядок реализации (предложение)

1. **PR 1 — Simple nodes.** `BTParallel` + `BTRepeat` + `BTUntilSuccess` + `BTUntilFailure` + `BTSucceeder` + `BTFailer` + `BTWait`. Тесты на каждую ноду. ~день работы, покрывает 80% случаев, когда тянешься за Opsive.
2. **PR 2 — Random + Shared Blackboard.** Мелкие, но требуют отдельного ревью.
3. **PR 3 — Conditional Abort.** Большой, нетривиальный, отдельный PR с плотной регрессионной сеткой. Делать только после того, как появится реальный AI, который этого просит.
4. **PR 4 — Runtime debugger.** UX-улучшение, делать когда дерево станет достаточно большим, чтобы отлаживать глазами стало тяжело.
