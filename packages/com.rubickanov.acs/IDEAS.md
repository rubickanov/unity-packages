# ACS — Ideas

Расширения поверх acs, которые переиспользуют существующую архитектуру (аспекты, ReactiveProperty, MonoEntity, сканеры).

> Core-фичи `MonoWorld` (синглтон-обёртка) и pure core (`IEntity` / `Entity` / `World` / `IEntityLogic` / `ITickable` / `EntityTickRunner` / `AttachLogic`) уже реализованы в пакете — см. `README.md`. Этот документ — про то, что ещё предстоит.

---

## 1. acs.persistence

Отдельный пакет-расширение. Даёт **только примитивы**: snapshot состояния аспектов и restore обратно. Ни слотов, ни автосейва, ни резолверов префабов, ни storage backend'ов. Куда писать snapshot, когда его делать, как резолвить id в префаб — всё это решает вышестоящий слой (save-система конкретной игры или отдельный `com.rubickanov.save`).

Причина — слоты, UI слот-селектора, тайминг, cloud sync, платформенные save API, метадата (timestamp, скриншот, playtime), persistent identity — это продуктовые концерны. ACS не должен иметь про них мнение. Его зона ответственности — стейт аспектов, и максимум что он должен уметь — сериализовать его и применить обратно.

### Использование

```csharp
public class PlayerAspect : IEntityAspect
{
    [PersistedState]
    [Replicated(Authority = AuthorityMode.Server)]
    public readonly ReactiveProperty<float> Health = new(100f);

    [PersistedState]
    public readonly ReactiveProperty<Vector3> Position = new();

    // Не помечен — runtime-only, не сохраняется
    public readonly ReactiveProperty<bool> IsInCombat = new(false);
}
```

Атрибут `[PersistedState]` — маркер для scanner'а. Два атрибута на одном поле (`[PersistedState]` + `[Replicated]`) — каждый работает в своём pipeline.

### API — snapshot / restore

Весь public API — это extension methods на `EntityContext` плюс итератор на `World`:

```csharp
namespace Rubickanov.ACS.Runtime.Persistence
{
    public static class EntityPersistenceExtensions
    {
        // Сериализованный стейт всех [PersistedState] полей всех аспектов сущности.
        // Чистый value object — без привязки к GameObject, можно передавать, хранить, пересылать.
        public static AspectSnapshot Snapshot(this EntityContext entity);

        // Применить snapshot обратно: пишет значения в ReactiveProperty.
        // Netcode/UI/правила реагируют как на обычный write.
        public static void Restore(this EntityContext entity, AspectSnapshot snapshot);

        // true — хотя бы у одного аспекта есть [PersistedState] поле. Кэш scanner'а.
        public static bool HasPersistedState(this EntityContext entity);
    }

    public static class WorldPersistenceExtensions
    {
        // Обход всех сущностей с persisted state. Без реестра slot'ов — просто enumerate.
        public static IEnumerable<EntityContext> PersistedEntities(this World world);
    }
}
```

`AspectSnapshot` — сериализуемый value object (структура или record) с таблицей `aspectType → fieldName → value` и версиями аспектов. Всё. ACS не знает про файлы, бэкенды, слоты, манифесты, prefabId.

### World — тоже сущность

World — это `MonoEntity`, так что `world.Snapshot()` / `world.Restore(snap)` работают на нём как и на любой другой сущности. Мировой стейт (TimeOfDay, Weather) сохраняется/восстанавливается тем же API.

### Как это выглядит на уровне игры

Save-систему пишет конкретная игра (или отдельный пакет). ACS только поставляет снепшоты — всё остальное выше:

```csharp
// Код в game layer или в отдельном com.rubickanov.save
public class SaveService
{
    private readonly IStorage _storage;
    private readonly Func<EntityContext, string> _getId;        // как получить стабильный id
    private readonly Func<EntityContext, string> _getPrefabId;  // как получить prefabId для респауна
    private readonly Func<string, GameObject> _resolvePrefab;   // как спавнить по prefabId

    public async Task SaveSlot(string slot)
    {
        var manifest = new Manifest();
        foreach (var entity in MonoWorld.Instance.PersistedEntities())
            manifest.Add(_getId(entity), _getPrefabId(entity), entity.Snapshot());
        await _storage.Write(slot, manifest);
    }

    public async Task LoadSlot(string slot)
    {
        var manifest = await _storage.Read<Manifest>(slot);
        foreach (var entry in manifest.Entries)
        {
            var entity = FindOrSpawn(entry.Id, entry.PrefabId);
            entity.Restore(entry.Snapshot);
        }
    }
}
```

ACS не участвует в решениях "куда писать", "когда писать", "как резолвить". Игра сама подставляет id-провайдер и prefab-резолвер. Всё что ACS экспонирует — snapshot/restore.

### Сценарии (glue со стороны игры)

**Дисконнект/реконнект (DayZ-style):**

```csharp
// Отключился
await saveService.WritePlayer(playerId, player.Snapshot());

// Реконнект
var entity = Instantiate(playerPrefab).GetComponent<EntityContext>();
var snapshot = await saveService.ReadPlayer(playerId);
entity.Restore(snapshot);
// Restore пишет в ReactiveProperty → netcode marks dirty → реплицируется клиенту
```

**Чекпоинты:** save-слой пишет снепшоты под нужным именем слота. ACS про слоты не знает.

**Автосейв:** таймер живёт в save-слое или в game loop, дёргает `SaveService.SaveSlot("autosave")` с нужной периодичностью.

### Persistent identity — не здесь

Стабильный id сущности через рестарты — это не ACS концерн. Скорее всего компонент `PersistentIdentity` живёт в save-пакете или в `utils`, save-слой конфигурирует `SaveService` чтобы он знал как получить id из сущности. ACS просто оперирует снепшотами без привязки.

### Архитектура

```
AspectSnapshot (struct/record, public)
└── aspectType → fieldName → value, плюс версии аспектов

PersistenceScanner (internal, зеркало ReplicationScanner)
├── reflection + кэш per-type: находит [PersistedState] поля
└── строит PersistedFieldBinding<T> для каждого поля

Extensions
├── entity.Snapshot()   — проходит по аспектам, собирает AspectSnapshot
├── entity.Restore(s)   — читает snapshot, пишет в ReactiveProperty
└── world.PersistedEntities() — enumerator по реестру World с фильтром HasPersistedState
```

Никаких MonoBehaviour на сущностях, никаких глобальных систем, никаких таймеров, никаких реестров слотов. Чистая библиотека функций поверх аспектов.

### Версионирование (schema migration)

Принадлежит acs.persistence: это про эволюцию стейта аспектов, а не про save-систему.

```csharp
[AspectVersion(3)]
public class PlayerAspect : IEntityAspect
{
    [PersistedState] public readonly ReactiveProperty<float> Health = new(100f);
    [PersistedState] public readonly ReactiveProperty<float> MaxHealth = new(100f);  // добавлено в v2
    [PersistedState] public readonly ReactiveProperty<int> ArmorClass = new(0);      // добавлено в v3

    // Мигратор v1 → v2: MaxHealth не существовал, ставим дефолт
    static void Migrate_1_to_2(AspectData data) => data.SetIfMissing("MaxHealth", 100f);

    // Мигратор v2 → v3: ArmorClass не существовал
    static void Migrate_2_to_3(AspectData data) => data.SetIfMissing("ArmorClass", 0);
}
```

При `Restore`:
1. Читаем версию аспекта из snapshot
2. Если старше текущей — прогоняем `Migrate_N_to_M` по цепочке
3. Применяем результат

Формат snapshot со строковыми ключами полей помогает — неизвестные поля игнорируются, отсутствующие заполняются дефолтами. Мигратор нужен только когда дефолт недостаточен (переименование, преобразование типа, перенос данных между полями).

### Интеграция с netcode

`Restore` пишет в ReactiveProperty → netcode видит dirty → реплицирует. Ноль клея:

```
Restore(snapshot) → Health.Value = 73 → ReactiveProperty fires
                                      → ReplicatedFieldBinding marks dirty
                                      → server tick broadcasts to clients
```

### Что вынесено наружу

- **Storage backends** (file, PlayerPrefs, encrypted, cloud) — `com.rubickanov.storage` или save-пакет
- **Слоты, манифест, UI слот-селектора, cloud sync** — save-слой игры
- **Автосейв, чекпоинты, триггеры сохранения** — game loop
- **Persistent identity** — save-пакет или utils
- **Prefab resolution** — save-слой (ACS не знает что такое префаб)
- **Сериализатор конкретного формата** (JSON / MsgPack / бинарный) — save-слой (acs.persistence может дать дефолтный JSON для удобства, но не навязывает)

### Что переиспользуется из netcode

- `ReplicationScanner` — паттерн сканирования атрибутов (reflection + кэш per-type)
- `ReplicatedFieldBinding` — паттерн чтения/записи полей аспекта

Основная работа — сканер и field bindings. Всё остальное, что было в старом плане (AspectPersistor, AspectPersistenceSystem, manifest, PersistentIdentity, auto-save), перенесено на уровень выше и из ACS убрано.

---

## 2. acs.codegen

Source generators вместо runtime reflection. Compile-time кодогенерация для:

### Что генерируем

**Aspect injection (замена AspectInjector):**

Сейчас:
```csharp
// Runtime: reflection ищет [Aspect] поля, кэширует per-type, инжектит через SetValue
AspectInjector.Inject(context, component);
```

Source generator создаёт:
```csharp
// Сгенерировано в compile-time
partial class OwnerInputMover
{
    private void __InjectAspects(EntityContext context)
    {
        _aspect = context.Require<ExperimentAspect>();
    }
}
```

Нулевой runtime cost — нет reflection, нет Dictionary lookup, нет кэша.

**Replication scanner (замена ReplicationScanner):**

Сейчас:
```csharp
// Runtime: reflection сканирует [Replicated] поля, сортирует, кэширует
var fields = ReplicationScanner.Scan(aspect);
```

Source generator создаёт:
```csharp
// Сгенерировано: стабильный порядок полей, типы известны
static class ExperimentAspect__ReplicationInfo
{
    public static readonly ReplicatedFieldInfo[] Fields = new[]
    {
        new ReplicatedFieldInfo("Health", typeof(float), AuthorityMode.Server, InterpolationMode.None),
        new ReplicatedFieldInfo("Position", typeof(Vector3), AuthorityMode.Owner, InterpolationMode.Linear),
        new ReplicatedFieldInfo("Rotation", typeof(Quaternion), AuthorityMode.Server, InterpolationMode.Linear),
    };
}
```

**Persistence scanner (если acs.persistence реализован):**

Аналогично — `[PersistedState]` поля сканируются в compile-time.

### Что это даёт

- Нулевой runtime cost — reflection на старте полностью убран
- IL2CPP safe — нет `MakeGenericType`, нет `GetValue`/`SetValue`, нет stripping проблем
- Ошибки в compile-time — забыл `[Aspect]` на неправильном типе → ошибка компиляции, а не LogError в рантайме
- `AotHints.cs` больше не нужен

### Сложность

Высокая. Unity + source generators = нетривиальный setup (отдельный .NET Standard 2.0 проект, интеграция с asmdef). Делать последним, когда reflection станет реальным bottleneck или IL2CPP начнёт ломаться на кастомных типах.

---

## 3. acs.queries (spatial)

Базовые queries (по типам аспектов, фильтрация, итерация) — уже в core через `World.Query<T>()`. Этот пакет — только про **расширенные spatial queries**, которых в core нет:

### Spatial queries

`WithinRadius` / `Nearest` / spatial hash grid — для больших миров (500+ сущностей):

```csharp
// Расширение поверх World.Query
var targets = World.Query<HealthAspect, PositionAspect>()
    .WithinRadius(origin, 20f)    // ← spatial extension
    .Nearest(origin);              // ← spatial extension
```

Реализация: spatial hash grid, обновляется при изменении `ReactiveProperty<Vector3>` Position. Без spatial extensions — `World.Query<T>()` итерирует линейно (O(n)), с extensions — O(1) lookup по ячейке.

### Интеграция с EQS

`com.rubickanov.eqs` уже делает spatial queries для AI. Spatial queries могут стать его data source:

```csharp
public class EntityQueryGenerator : EQSGenerator
{
    public override IEnumerable<Vector3> Generate(Vector3 origin)
    {
        return World.Query<PositionAspect>()
            .WithinRadius(origin, 30f)
            .Select(e => e.Position.Value);
    }
}
```

Возможно не отдельный пакет, а часть EQS. Решить при реализации.

---

## 4. acs.debug

Runtime визуализатор аспектов. Оверлей в play mode — все сущности, их аспекты, значения полей в реалтайме.

### Что показывает

- Список сущностей с EntityContext, фильтр по аспектам
- Значения всех ReactiveProperty на выбранной сущности, обновляются live
- Кто подписан на какое поле (subscription count)
- Dirty state — какие поля помечены dirty прямо сейчас
- Сетевой трафик per field — сколько байт ушло на репликацию конкретного поля за секунду
- Authority — кто пишет (server/owner), визуально отличается
- Timeline — график значения поля за последние N секунд (Health over time)

### Реализация

EditorWindow + runtime компонент. В play mode подписывается на все `ReactiveProperty` выбранной сущности, рисует через IMGUI или UI Toolkit. Для сетевых метрик — hook в `EntityReplicationSystem` (счётчик байт per binding).

Не влияет на production — `#if UNITY_EDITOR` или отдельный Editor asmdef.

---

## 5. acs.replay

Запись и воспроизведение изменений аспектов. Третье переиспользование паттерна сериализации (после netcode и persistence).

### Использование

```csharp
// Начать запись
var recording = ReplaySystem.StartRecording(entity);

// Остановить, получить данные
var clip = recording.Stop();

// Воспроизвести на другой сущности (ghost)
var ghost = Instantiate(ghostPrefab);
ReplaySystem.Play(ghost, clip, speed: 1f);

// Сохранить на диск
await clip.SaveAsync("killcam_2026-04-11.replay");
```

### Формат записи

```
[timestamp, entityId, aspectType, fieldName, value]
[0.000, "player_1", "PlayerAspect", "Position", (0, 0, 0)]
[0.016, "player_1", "PlayerAspect", "Position", (0.1, 0, 0.2)]
[0.350, "player_1", "PlayerAspect", "Health", 85.0]
```

Подписывается на `ReactiveProperty.Subscribe` — каждое изменение записывается с timestamp. Воспроизведение — пишет значения обратно в ReactiveProperty по таймлайну.

### Сценарии

- **Killcam** — запись последних 10 секунд жертвы, воспроизведение после смерти
- **Spectator mode** — запись матча целиком, просмотр с любой камеры
- **Баг-репорты** — приложить replay к тикету, разработчик видит точное состояние
- **Балансировка** — графики Health/DPS/позиции за бой, анализ post-mortem
- **Обучение** — воспроизвести эталонное прохождение как ghost

### Интеграция

Сериализация та же что в netcode (бинарная для компактности) и persistence (JSON для анализа). Один `ReplayFieldBinding<T>` по образцу `ReplicatedFieldBinding<T>`.

---

## 6. acs.reactive

Computed properties — производные значения, автоматически пересчитываемые при изменении источников.

### Проблема

Сейчас derived state = ручной `Subscribe` + `CombineLatest`:

```csharp
// Boilerplate на каждое вычисляемое значение
_health.Health.CombineLatest(_health.MaxHealth, (h, max) => h / max)
    .Subscribe(v => _health.HealthPercent.Value = v)
    .AddTo(ref _disposables);
```

### С acs.reactive

```csharp
public class HealthAspect : IEntityAspect
{
    [Replicated] public readonly ReactiveProperty<float> Health = new(100f);
    [Replicated] public readonly ReactiveProperty<float> MaxHealth = new(100f);

    // Auto-computed, read-only для внешнего кода
    [Computed] public readonly IReadOnlyReactiveProperty<float> HealthPercent;
    [Computed] public readonly IReadOnlyReactiveProperty<bool> IsDead;

    public HealthAspect()
    {
        HealthPercent = Health.CombineLatest(MaxHealth, (h, max) => max > 0 ? h / max : 0f)
            .ToReadOnlyReactiveProperty();
        IsDead = Health.Select(h => h <= 0f)
            .ToReadOnlyReactiveProperty();
    }
}
```

`[Computed]` — маркер для сканеров: не реплицировать, не сохранять, не включать в dirty mask. Значение всегда вычисляется из источников локально.

### Расширенные паттерны

```csharp
// Debounce — не спамить подписчиков при быстрых изменениях
[Debounced(0.1f)]
public readonly ReactiveProperty<Vector3> SmoothedPosition;

// Throttle — максимум N обновлений в секунду
[Throttled(10)]
public readonly ReactiveProperty<float> UIHealth; // UI не нужно 60 fps обновлений
```

### Реализация

Если делать через codegen (acs.codegen) — source generator видит `[Computed]` и генерирует subscription wiring. Без codegen — runtime helper:

```csharp
ComputedProperty.Bind(HealthPercent, Health, MaxHealth, (h, max) => h / max);
```

---

## 7. acs.pooling

Интеграция entity pooling с lifecycle аспектов.

### Проблема

При object pooling (NGO или свой) сущность возвращается в пул, но аспекты остаются в грязном состоянии — Health = 0 от прошлой жизни, Position от места смерти, подписки живые. При повторном использовании нужен ручной reset каждого поля.

### Использование

```csharp
public class ZombieAspect : IEntityAspect
{
    [ResetOnRecycle] public readonly ReactiveProperty<float> Health = new(100f);
    [ResetOnRecycle] public readonly ReactiveProperty<Vector3> Position = new();
    [ResetOnRecycle] public readonly ReactiveProperty<bool> IsAggro = new(false);

    // Не ресетится — сохраняет значение между жизнями
    public readonly ReactiveProperty<int> TotalKills = new(0);
}
```

`[ResetOnRecycle]` — при возврате в пул значение сбрасывается к initial (тому что в `new(...)`). Без атрибута — поле сохраняется между переиспользованиями.

### Архитектура

```csharp
// При возврате в пул
AspectPoolManager.OnRecycle(entity);
// → scanner находит все [ResetOnRecycle] поля
// → записывает initial values (кэшированы per-type при первом scan)
// → suppressed write (как в netcode — без триггера подписок)
// → отписка компонентов через OnDisable lifecycle

// При повторном spawn
AspectPoolManager.OnReuse(entity);
// → аспекты уже в чистом состоянии
// → OnEnable → подписки заново
```

### Интеграция с NGO

NGO поддерживает `INetworkPrefabInstanceHandler` для кастомного pooling. `AspectPoolManager` реализует этот интерфейс — NGO дёргает Instantiate/Destroy, менеджер подставляет pool + aspect reset.

---

## 8. acs.animation

Reactive binding аспектов к Animator. Убирает ручной `animator.SetFloat`/`SetBool`/`SetTrigger` в Update.

### Проблема

В каждом проекте:
```csharp
// Этот код пишется на каждую сущность с аниматором
void Update()
{
    _animator.SetFloat("Speed", _movement.Speed.Value);
    _animator.SetBool("IsGrounded", _movement.Grounded.Value);
    _animator.SetFloat("Health", _health.Health.Value);
}
```

### Использование

```csharp
public class MovementAspect : IEntityAspect
{
    [AnimatorParam("Speed")]
    public readonly ReactiveProperty<float> MoveSpeed = new(0f);

    [AnimatorParam("IsGrounded")]
    public readonly ReactiveProperty<bool> Grounded = new(true);

    [AnimatorTrigger("Jump")]
    public readonly Subject<Unit> Jump = new(); // OnNext → SetTrigger
}

public class CombatAspect : IEntityAspect
{
    [AnimatorParam("AttackType")]
    public readonly ReactiveProperty<int> AttackType = new(0);

    [AnimatorTrigger("Attack")]
    public readonly Subject<Unit> Attack = new();
}
```

`ReactiveProperty` → `SetFloat`/`SetBool`/`SetInt` при изменении. `Subject` → `SetTrigger` при OnNext. Нет Update, нет bridge-компонента.

### Архитектура

`AspectAnimatorBinder` — компонент рядом с `Animator`. Сканирует аспекты, строит подписки. Кэш per-type. При spawn подписывается, при despawn отписывается.

Маппинг типов:
- `ReactiveProperty<float>` → `SetFloat`
- `ReactiveProperty<int>` → `SetInteger`
- `ReactiveProperty<bool>` → `SetBool`
- `Subject<Unit>` → `SetTrigger`
- `Subject<T>` → `SetTrigger` (payload игнорируется)

### Валидация

Editor-time проверка: если `[AnimatorParam("Speed")]` а в AnimatorController нет параметра "Speed" — warning в консоли при spawn, а не молчаливый no-op.

---

## 9. acs.rules

Декларативный rules engine поверх реактивных аспектов. Data-driven игровые правила без хардкода.

### Проблема

Игровая логика типа "когда здоровье < 20% и персонаж горит — включить панику" живёт внутри компонентов как if/else. Геймдизайнер хочет поменять порог с 20% на 30% — нужен программист.

### Использование

```csharp
// Правило как ScriptableObject — редактируется в инспекторе
[CreateAssetMenu]
public class PanicRule : AspectRule
{
    [SerializeField] float healthThreshold = 0.2f;
    [SerializeField] GameplayTag requiredTag = "Status.Burning";

    public override void Configure(RuleBuilder builder)
    {
        builder
            .When<HealthAspect>(h => h.HealthPercent.Value < healthThreshold)
            .And<TagsAspect>(t => t.Has(requiredTag))
            .Apply<StatusAspect>((status) => status.Panic.Value = true)
            .Remove<StatusAspect>((status) => status.Panic.Value = false); // когда условие перестаёт выполняться
    }
}
```

```csharp
// Регистрация
RuleSystem.Register(panicRule);
// Всё. Проверяется реактивно — при изменении Health или Tags, не в Update.
```

### Ключевое — реактивность

Правила **не** проверяются каждый кадр. `RuleSystem` подписывается на `ReactiveProperty` полей в условии. Правило вычисляется только когда одно из условий изменилось. 100 правил × 500 сущностей — нагрузка только при реальных изменениях.

### Интеграция

- `com.rubickanov.gameplaytags` — условия на теги (`Has`, `HasAny`, `HasAll`)
- `com.rubickanov.gas` — правила как альтернатива GameplayEffects для простых случаев
- `com.rubickanov.behaviortree` — BT ноды могут проверять active rules

### Уровни сложности

**Simple** — ScriptableObject с полями-порогами, без кода. Геймдизайнер справится.

**Advanced** — кастомные условия и действия через C#:

```csharp
builder
    .When<PositionAspect, ZoneAspect>((pos, zone) =>
        Vector3.Distance(pos.Position.Value, zone.Center.Value) < zone.Radius.Value)
    .Apply<BuffAspect>(buff => buff.InSafeZone.Value = true);
```

---

## 10. acs.testing

Тест-утилиты для ACS. Fluent builder для сборки сущностей и assert-хелперы без GameObject, сцены и NetworkManager.

### Проблема

Сейчас чтобы протестировать компонент нужно руками создавать GameObject, вешать EntityContext, создавать аспекты, мокать подписки. Много boilerplate на каждый тест.

### Использование

```csharp
// Fluent builder — собрать сущность для теста
var entity = ACSTestFixture.Create()
    .WithAspect(new HealthAspect { Health = { Value = 50f } })
    .WithAspect(new PositionAspect())
    .WithComponent<DamageReceiver>()
    .Build();

// Assert — значение поля
entity.AssertAspect<HealthAspect>(h => h.Health.Value == 50f);

// Assert — поле изменилось после действия
entity.AssertChanged<HealthAspect>(h => h.Health, () =>
{
    entity.GetComponent<DamageReceiver>().TakeDamage(10f);
});

// Assert — событие сработало
entity.AssertFired<CombatAspect>(c => c.OnDeath, () =>
{
    entity.Require<HealthAspect>().Health.Value = 0f;
});

// Assert — поле НЕ изменилось
entity.AssertNotChanged<HealthAspect>(h => h.Health, () =>
{
    entity.GetComponent<DamageReceiver>().TakeDamage(0f);
});
```

### Что под капотом

`ACSTestFixture.Create()` создаёт реальный GameObject с EntityContext (нужен для AspectInjector), но без сцены и без MonoBehaviour lifecycle. `Build()` возвращает обёртку с доступом к аспектам и компонентам.

`AssertChanged` — подписывается на ReactiveProperty, выполняет action, проверяет что callback сработал. `AssertFired` — то же для Subject.

### Для netcode-тестов

```csharp
// Сборка сущности с network-мокой
var entity = ACSTestFixture.Create()
    .WithAspect(new StateTestAspect())
    .AsNetworked(isServer: true, isOwner: false)
    .Build();

// Проверить что binding пометился dirty
entity.AssertDirty<StateTestAspect>(s => s.Health);
```

`AsNetworked` подставляет мок-флаги `IsServer`/`IsOwner` без реального NetworkManager.

---

## 11. acs.live

Веб-панель для живой настройки аспектов в реалтайме. Из игры стримится состояние по WebSocket в браузер — на телефоне, планшете, втором мониторе.

### Как работает

```
Unity (game) ←— WebSocket —→ Browser (панель)
```

Игра поднимает lightweight WebSocket сервер. Панель в браузере подключается, получает список сущностей и аспектов, показывает значения live. Слайдеры, чекбоксы, инпуты — меняешь значение в браузере, оно пишется в ReactiveProperty в игре.

### Сценарии

- **Плейтест** — геймдизайнер сидит рядом с тестером, на планшете тюнит Health, DamageMultiplier, SpawnRate в реалтайме. Нашли хороший баланс — записали значения.
- **Демо/стриминг** — показываешь игру, параллельно на втором экране видишь все внутренности. "Вот смотрите, Health падает когда горит, вот Regeneration поднимает".
- **Удалённый dedicated сервер** — подключаешься к серверу из дома, видишь состояние всех игроков, можешь вмешаться.

### Архитектура

```
ACSLiveServer (MonoBehaviour, singleton)
├── WebSocket server (порт конфигурируется)
├── EntityRegistry — список сущностей с EntityContext
├── AspectSerializer — JSON snapshot аспектов (переиспользуется из persistence)
├── Change listener — подписки на ReactiveProperty, push при изменении
└── Write handler — входящие изменения → ReactiveProperty.Value = ...
```

### Протокол

```json
// Сервер → браузер: snapshot сущности
{ "type": "snapshot", "entityId": "player_1", "aspects": { "HealthAspect": { "Health": 73.5 } } }

// Сервер → браузер: поле изменилось
{ "type": "changed", "entityId": "player_1", "aspect": "HealthAspect", "field": "Health", "value": 65.0 }

// Браузер → сервер: изменить значение
{ "type": "set", "entityId": "player_1", "aspect": "HealthAspect", "field": "Health", "value": 100.0 }
```

### Веб-панель

Простой SPA (можно на vanilla JS, без фреймворков). Встроен в пакет как WebGL ресурс — открываешь `localhost:7777` и панель уже там. Никаких npm/node зависимостей.

### Безопасность

Dev-only. `#if DEVELOPMENT_BUILD || UNITY_EDITOR` — в release билде сервер не компилируется. Опционально: пароль на подключение.

---

## 12. acs.mirror

Зеркало аспектов во внешнюю базу данных. Состояние сущностей доступно из внешних инструментов — админ-панели, аналитика, GM-тулзы.

### Отличие от live

`acs.live` — dev-time, WebSocket, прямое подключение к процессу игры. Для разработки и плейтестов.

`acs.mirror` — production, через базу данных (Redis, Firebase, PostgreSQL). Для live-сервисов, GM-инструментов, аналитики.

### Как работает

```
Game Server → MirrorSystem → Database ← Admin Panel / Analytics / GM Tools
```

`MirrorSystem` подписывается на `[Mirrored]` поля, при изменении пишет в базу. Внешние инструменты читают из базы. Обратная связь: GM записал в базу → MirrorSystem подхватил → записал в ReactiveProperty → игра отреагировала.

### Использование

```csharp
public class PlayerAspect : IEntityAspect
{
    [Mirrored]  // синхронизируется с внешней базой
    [Replicated(Authority = AuthorityMode.Server)]
    public readonly ReactiveProperty<float> Health = new(100f);

    [Mirrored]
    [PersistedState]
    public readonly ReactiveProperty<int> Gold = new(0);

    // Не зеркалится — только внутри игры
    public readonly ReactiveProperty<Vector3> Velocity = new();
}
```

### Сценарии

- **GM-панель** — саппорт видит "игрок X застрял с Health=0, Gold=-500". Кликает "починить" → Health = 100, Gold = 0 → изменения прилетают в игру через mirror.
- **Аналитика** — дашборд показывает среднее Health по всем игрокам, экономику сервера (суммарный Gold), популярные предметы. Всё в реалтайме.
- **Античит** — внешний сервис мониторит Speed, Position. Аномалия → автоматический бан.
- **Межсерверная коммуникация** — два game-сервера зеркалят в одну базу. Игрок перешёл на другой сервер → его состояние уже в базе.

### Бэкенды

```
IMirrorBackend
├── RedisMirrorBackend      — быстрый, pub/sub для обратной связи
├── FirebaseMirrorBackend   — для мобилок, real-time database
└── PostgresMirrorBackend   — для аналитики, SQL-запросы по состоянию
```

### Throttling

Не каждое изменение ReactiveProperty нужно писать в базу. `[Mirrored(IntervalMs = 1000)]` — максимум раз в секунду. Для Position при 60fps без throttle база умрёт.

---

## 13. acs.simulate

Headless симуляция логики аспектов без рендера. Прогон тысяч боёв за секунды для балансировки, или dedicated сервер без GPU.

### Два уровня

**Уровень 1: Unity headless build** — Unity без рендера. MonoBehaviour, GameObject — всё работает. Подходит для dedicated серверов. Простой — не нужен рефактор, `BuildTarget.Server` и готово.

**Уровень 2: чистый .NET без Unity** — console app, прогон 10000 боёв за секунду. Это сложнее, требует отделения логики от MonoBehaviour.

### Подход для уровня 2

Интерфейс `IEntityLogic` — контракт на чистую тикаемую логику:

```csharp
public interface IEntityLogic
{
    void Tick(float dt);
}
```

Компоненты в Unity реализуют интерфейс, оставаясь MonoBehaviour:

```csharp
[NetworkScope(NetworkScope.ServerOnly)]
public class BurnDamage : EntityComponent, IEntityLogic
{
    [Aspect] private HealthAspect _health;
    [Aspect] private StatusAspect _status;

    public void Tick(float dt)
    {
        if (_status.IsBurning.Value)
            _health.Health.Value -= 10f * dt;
    }

    void Update() => Tick(Time.deltaTime);
}
```

В Unity — работает как обычно. Для headless без Unity — нужен рефактор: вынести логику в pure C# классы, MonoBehaviour остаётся тонкой обёрткой. Или писать новые логики сразу как pure C# + отдельный MonoBehaviour-раннер.

### Headless runner

```csharp
// Console app — без Unity
var entity = new SimulationContext();
var health = entity.Require<HealthAspect>();
var status = entity.Require<StatusAspect>();

var burn = new BurnDamageLogic(); // pure C# версия
AspectInjector.Inject(entity, burn);

status.IsBurning.Value = true;
health.Health.Value = 100f;

// Прогон
for (int tick = 0; tick < 10000; tick++)
    burn.Tick(0.016f);

Console.WriteLine($"Health after 160s of burn: {health.Health.Value}");
```

### Сценарии

- **Балансировка** — прогнать 10000 боёв "воин vs маг", собрать winrate, средний TTK, распределение DPS. Без Unity, без рендера, за секунды.
- **Dedicated сервер** — уровень 1 (Unity headless). Всё работает из коробки.
- **CI/CD** — автотест "после патча баланса средний TTK не упал ниже 5 секунд". Прогоняется в pipeline как console app.
- **Monte Carlo** — рандомизированные параметры, тысячи прогонов, статистика. "При каком значении ArmorClass выживаемость > 70%?"

### Ограничения

Для уровня 2 (без Unity): аспекты используют `Vector3`, `Quaternion` из `UnityEngine.dll`. Это просто структуры — можно зареференсить `UnityEngine.CoreModule.dll` из console app, работает в любом .NET рантайме. "Без Unity" = без GameObject/MonoBehaviour/рендера, математика остаётся.

---

## 14. acs.bindings

Декларативная привязка аспектов к UI элементам.

### Использование

```csharp
public class PlayerHudView : MonoBehaviour
{
    [SerializeField] Slider healthBar;
    [SerializeField] TMP_Text goldLabel;

    [BindTo(nameof(healthBar))]         // ReactiveProperty<float> → Slider.value
    [Aspect] private HealthAspect _health;

    [BindTo(nameof(goldLabel), format: "Gold: {0}")]  // ReactiveProperty<int> → TMP_Text.text
    [Aspect] private InventoryAspect _inventory;
}
```

Сканер находит `[BindTo]` атрибуты, подписывается на ReactiveProperty, пишет в UI. Bridge-компонент исчезает.

### Сомнения

Честно — непонятно, насколько это реально нужно. Ui пакет (`com.rubickanov.ui`) уже решает свои задачи по-своему, и привязка к конкретным элементам (Slider, TMP_Text, Image.fillAmount) — это кастомный код для каждого типа таргета. Получится пухлый маппинг "тип поля + тип UI элемента + форматтер".

Альтернатива — писать ViewModel-подобные классы в `ui` пакете которые знают про аспекты. Возможно это чище.

Делать последним, если вообще.

---

## 15. acs.commands

Командный паттерн для мутаций аспектов. Вместо прямого `Health.Value = x` — через типизированные команды.

### Использование

```csharp
public readonly struct DealDamageCommand : IAspectCommand
{
    public EntityRef Target;
    public float Amount;
    public DamageType Type;

    public void Execute(CommandContext ctx)
    {
        var health = ctx.Get<HealthAspect>(Target);
        health.Health.Value -= Amount;
    }
}

// Выполнение
CommandBus.Send(new DealDamageCommand { Target = enemy, Amount = 10f });
```

### Что даёт

- **Лог команд** — replay через команды вместо значений, меньше размер
- **Валидация** — `IValidator<DealDamageCommand>` проверяет на сервере до execute (античит)
- **Undo/redo** — для GM-тулзов и редакторов уровней
- **Батчинг** — несколько команд в одной транзакции, atomic apply
- **Сетевая отправка** — команда = готовое ServerRpc (для предсказания)

### Сомнения

Это шаг в сторону от реактивного подхода. Сейчас весь фреймворк построен на "пишем в ReactiveProperty, подписчики реагируют". Команды добавляют слой между намерением и изменением, и тогда ломается красота прямого `Health.Value -= 10`.

Есть риск что это превратится в архитектурный мандат — "теперь ВСЕ мутации через команды". Это уже не ACS, это CQRS на Unity.

Возможно полезно только для конкретных use case (античит, GM-откат), и тогда это не отдельный пакет, а паттерн который юзер применяет где нужно. В core ACS включать нет смысла.

---

## Приоритет

**Пакеты-расширения:**
1. **persistence** — минимальные примитивы Snapshot/Restore, переиспользует scanner + field bindings из netcode. Save-систему пишет игра сверху
2. **animation** — убирает слой bridge-кода между аспектами и Animator
3. **debug** — помогает при разработке всего остального, окупается сразу
4. **rules** — мощный инструмент для геймдизайнеров, хорошо ложится на reactive
5. **pooling** — нужен при масштабе (десятки AI с респавном)
6. **replay** — нишевый но эффектный, переиспользует существующую сериализацию
7. **reactive** — quality of life, сокращает boilerplate
8. **testing** — окупается при написании тестов для новых пакетов
9. **live** — dev-time remote inspector, кайф для плейтестов
10. **mirror** — production, GM-тулзы, аналитика
11. **simulate** — headless прогон для балансировки и CI
12. **queries (spatial)** — spatial hash, WithinRadius/Nearest. Возможно часть EQS
13. **codegen** — высокая сложность, делать когда reflection станет bottleneck
14. **bindings** — под вопросом, пересекается с `ui` пакетом. Может не понадобиться
15. **commands** — под вопросом, ломает реактивный стиль. Скорее паттерн чем пакет
