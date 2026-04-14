# ACS Persistence — Ideas

Post-v1 направления. Список не приоритизирован — очерёдность появится когда первый save-слой-консумер начнёт упираться в конкретный шов.

---

## 1. Versioning + migration

**Проблема.** Снапшот это `Dictionary<string, AspectData>`, ключ — `Type.FullName`. Сейчас:
- Переименовал аспект / переехал в другой namespace → старые сейвы не находят тип, `Restore` молча скипает аспект с warning'ом.
- Убрал поле → старый сейв его всё ещё пишет, binding не найдёт имени, поле молча теряется.
- Добавил поле → в старом сейве его нет, дефолт из конструктора остаётся (устраивает по умолчанию).
- Сменил тип поля (`int` → `float`) → `InvalidCastException` на `WriteValue`, ловим и логируем.

Это даёт базовый forward/backward compat, но не миграцию.

**Что нужно.**

1. **Стабильный ключ аспекта независимо от CLR-имени.** Варианты:
   - `[PersistedKey("hero.stats")]` на аспекте. Если атрибут есть — используется как ключ; если нет — `Type.FullName` как сейчас. Мигрируешь старые сейвы регистрацией alias'а.
   - `[PersistedAlias("Old.Namespace.HeroAspect")]` — допускать несколько "старых" ключей, мапится на новый тип при `Restore`.

2. **Версия на уровне пакета снапшота.** Добавить `int FormatVersion` в `AspectSnapshot` (или обёртку `VersionedSnapshot { int Version; AspectSnapshot Data; }`). Save-слой пишет/читает версию, pipeline миграций прогоняет `Data` через цепочку трансформеров `v1 → v2 → v3` перед `Restore`.

3. **Per-aspect версия.** `[PersistedVersion(3)]` на аспекте + `IPersistedMigrator<TAspect>` с `Migrate(int fromVersion, AspectData data)`. Точечнее, не требует переписывать весь формат при добавлении одного поля, но больше boilerplate.

4. **Где живёт pipeline миграций.** В самом пакете или в save-слое? Склоняюсь к "в пакете — интерфейс и цепочка, в save-слое — регистрация конкретных migrator'ов". ACS даёт механизм, save-слой даёт политику.

**Открытые вопросы.**
- Миграция коллекций (добавить поле в элемент `ObservableList<struct>`) — нетривиально, боксированный `List<T>` не даёт структурного доступа. Возможно, "coll migrations — ответственность save-слоя через кастомный сериализатор, ACS их не трогает".
- Миграция должна уметь удалять аспект целиком (аспект упразднён, его данные надо распределить по другим) — это уже кросс-аспектная операция, хочет API на уровне `AspectSnapshot`, не `AspectData`.
- Downgrade (из новой версии в старую) — за рамками. Сейвы только forward-compatible.

**Решение — отложено до первого реального консумера с сейвами в проде.** До этого момента API миграций будет угадан наугад.

---

## ~~2. WorldSnapshot + SnapshotAll/RestoreAll~~ ✅ Сделано

Закрыто в v1.1.0. `WorldSnapshot` — отдельное поле `World` + `SortedDictionary<string, AspectSnapshot> Entities`. `SnapshotAll(Func<IEntity,string> keyOf)` / `RestoreAll(snapshot, Func<string,IEntity> resolveOrSpawn, WorldRestoreOptions options)`. `FormatVersion` не добавлен — отложен до #1 (первый реальный консумер сейвов).

<details>
<summary>Исходный черновик</summary>

**Проблема.** Save-слой каждый раз пишет один и тот же цикл:

```csharp
foreach (var e in world.PersistedEntities())
    manifest.Add(getId(e), getPrefabId(e), e.Snapshot());

foreach (var entry in manifest.Entries)
    FindOrSpawn(entry.Id, entry.PrefabId).Restore(entry.Snapshot);
```

Это не "фича save-слоя" — это boilerplate, который любой консумер повторит одинаково. Имеет смысл дать готовый API и оставить save-слою только identity/prefab-делегаты.

**Предлагаемый API.**

```csharp
public sealed class WorldSnapshot
{
    public Dictionary<string, AspectSnapshot> ByKey { get; }
    // Возможно: int FormatVersion (пересечение с п.1).
}

public static class WorldPersistenceExtensions
{
    public static WorldSnapshot SnapshotAll(this World world, Func<IEntity, string> keyOf);

    public static void RestoreAll(
        this World world,
        WorldSnapshot snapshot,
        Func<string, IEntity> resolveOrSpawn,
        WorldRestoreOptions options = default);
}
```

- `keyOf` — save-слой выдаёт свой стабильный id. ACS не пытается угадать (см. "ACS не знает про prefab/id" из README).
- `resolveOrSpawn` — save-слой либо находит существующую сущность, либо спавнит новую (prefab lookup — его забота).
- `WorldRestoreOptions` — см. п.4.

**Плюсы.**
- Консумер пишет 2 строчки вместо цикла.
- `WorldSnapshot` — один сериализуемый объект на всё, save-слой прогоняет одним вызовом сериализатора.
- Готовая точка для версионирования (FormatVersion на уровне мира, а не каждой сущности).

**Минусы / вопросы.**
- Требует доопределить, что делать с World-scoped аспектами (сам `World` как `IEntity`). Вероятно — `keyOf(world)` возвращает зарезервированный ключ вроде `"__world__"`, или отдельное поле `WorldSnapshot.World`.
- Размывает границу "пакет не знает про storage". Нет, не размывает: `WorldSnapshot` это всё ещё POCO, save-слой всё ещё сам выбирает сериализатор.

**Склоняюсь к: сделать в v1.1.** Стоимость низкая, пользы много, scope не расширяется.

</details>

---

## ~~3. Детерминизм в порядке полей и аспектов~~ ✅ Сделано

Закрыто: `AspectSnapshot.Aspects` и `AspectData.Fields` переведены на `SortedDictionary<string, …>` с `StringComparer.Ordinal`. Гарантия от BCL, culture-invariant. Детерминизм сериализованного блоба — всё ещё ответственность save-слоя (зависит от того, уважает ли его сериализатор порядок `IDictionary`). См. README → Design Decisions.

---

## ~~4. Политика "entity есть в мире, но нет в снапшоте"~~ ✅ Сделано

Закрыто в v1.1.0. `WorldRestoreOptions.Missing` + `enum MissingEntityPolicy { Ignore, DisposeMissing }`. Дефолт — `Ignore`. World из кандидатов на Dispose исключён по ссылке, runtime-only сущности фильтруются через `HasPersistedState()`. Teardown по умолчанию: `IDisposable.Dispose()` → `UnityEngine.Object.Destroy(component.gameObject)` → LogError; override через `WorldRestoreOptions.DisposeMissing`.

<details>
<summary>Исходный черновик</summary>

**Проблема.** `RestoreAll` (п.2) применяет снапшот к существующему миру. Возможные состояния:

| В снапшоте | В мире | Текущее поведение           | Ожидаемое поведение?        |
|------------|--------|------------------------------|------------------------------|
| есть       | есть   | перезаписываем                | всегда перезаписываем        |
| есть       | нет    | `resolveOrSpawn` спавнит      | OK                           |
| нет        | есть   | **не тронута**                | **зависит от use case**      |

Третья строка — spawner-ная. Типовые сценарии:

- **Load slot "с нуля".** Игрок был в квесте, сейв сделан до встречи с боссом, после сейва игрок заспавнил NPC-помощника. На load хотим полностью вернуться к состоянию сейва → **лишние сущности должны быть удалены**.
- **Load checkpoint внутри сцены.** Уровень стоит, игрок умер, загружаем мидсейв. Декорации и статические сущности уже заспавнены сценой, им перезагрузка не нужна → **оставляем лишние как есть**.
- **Merge snapshot.** Применяем partial-снапшот к живому миру (например, бекап одного аспекта). Опять же, **оставляем**.

**Варианты API.**

```csharp
public readonly struct WorldRestoreOptions
{
    public MissingEntityPolicy Missing { get; init; }
    // Возможно: UnknownAspectPolicy (сейчас: warn+skip).
}

public enum MissingEntityPolicy
{
    Ignore,         // оставить как есть (дефолт — наименьшее удивление)
    DisposeMissing  // Dispose() каждой сущности, которая не упомянута в снапшоте
}
```

Дефолт — `Ignore`, потому что Dispose — деструктивный, лишний спавн максимум выдаёт визуальный косяк, а лишний Dispose уничтожает легитимные runtime-only сущности (частицы, ownership-игрушки), что дебажить больно.

**Открытые вопросы.**
- Как `DisposeMissing` отличает "runtime-only сущность, которая никогда и не должна была быть в снапшоте" от "удалённая со времени сейва"? Вариант: диспоузим только те, что прошли `HasPersistedState()`. Runtime-only выживают по определению.
- Как быть с World-scoped аспектами? Если аспект на World есть в снапшоте, но после снапшота World получил ещё один runtime-only аспект — его не трогаем (аспекты не Dispose'ятся по-отдельности у World, это не тот же lifecycle).
- Нужно ли событие `EntityRestored` / `EntityDisposedByRestore` для UI-hooks (flash, fade)? Возможно — но это уже ближе к save-слою.

**Склоняюсь к: реализовать в рамках п.2 (WorldRestoreOptions), дефолт `Ignore`, `DisposeMissing` — opt-in.**

</details>

---

## Связность пунктов

- **#2 (WorldSnapshot)** — сделано. Готовая точка для `FormatVersion` когда возьмёмся за #1.
- **#1 (миграции)** — самый крупный, ждёт реального save-слоя.

Открытый пункт: **#1**.
