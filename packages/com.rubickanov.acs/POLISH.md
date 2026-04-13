# ACS — Polish Backlog

Known rough edges in the current design. None of them are bugs — they are
places where the code works, but the split between pure `Entity` / Unity
`MonoEntity` / world registry could be tighter. Listed here so the next
refactor pass has a checklist.

---

## 1. Дублирование `Entity` ↔ `MonoEntity`

Оба класса держат идентичный `Dictionary<Type, object> _aspects` и одинаково
реализуют `Require` / `TryGet` / `Has` / `GetAllAspects` / `AspectTypes`.
Разница — только в том, кому звонить при `Register` / `Unregister`:

- `MonoEntity.Require` — `World.Instance?.Register(this, type)` (`Runtime/MonoEntity.cs:46`)
- `Entity.Require` — `_core?.Register(this, type)` (`Runtime/Entity.cs:62`)

Просится общий `AspectStore` (pure C#), который оба композируют внутри.
Сэкономит ~60 строк и уберёт второй источник ошибок. Сейчас фикс в одном
месте надо помнить дублировать во второе.

---

## 2. `World` копирует все 8 `Query<>` перегрузок из `WorldCore`

`Runtime/World.cs:66–136` — 70 строк copy-paste. Достаточно было бы одного
`public static WorldCore Core => Instance?._core`, а `Query<...>` доступен
через него. Или extension-метод на `WorldCore`.

---

## 3. Асимметрия auto-register

`Entity` берёт `WorldCore` в ctor, а `MonoEntity` хардкодит `World.Instance`
(`Runtime/MonoEntity.cs:46`). Значит:

- `MonoEntity` нельзя зарегистрировать в отдельном `WorldCore` (например,
  для мини-игры в одной сцене или для интеграционного теста без синглтона
  `World`).
- Тесты на `MonoEntity` обязаны поднимать `World` в сцене.

Исправить: сделать в `MonoEntity` аналог `Entity(WorldCore)` — например,
`[SerializeField] WorldProvider` или `MonoEntity.Bind(WorldCore)` до первого
`Require`. Тогда API симметричный.

---

## 4. `IEntity.AspectTypes` выставляет `Dictionary<Type, object>.KeyCollection`

`Runtime/IEntity.cs:59`. Прагматично для zero-alloc, но протекла деталь
реализации: если когда-нибудь захочется заменить словарь на sparse-set или
массив, ломаются все консьюмеры. Альтернатива — кастомный struct-энумератор
в интерфейсе, но это overkill для сегодняшних нужд. Оставь, но держи в уме —
это якорь.

---

## 5. `World.Require<T>` — `new static`, затеняющий `MonoEntity.Require<T>`

`Runtime/World.cs:54`. `World.Instance.Require<T>()` и `World.Require<T>()`
делают одно и то же, но первое идёт через instance-API, второе — через
shadow. Читающий код может удивиться. Мелочь, но nitpick.

---

## 6. `AttachLogic` идемпотентный только по контракту

`Runtime/EntityExtensions.cs`, `Runtime/IEntityLogic.cs:18–30`. Если юзер
руками вызовет `logic.Dispose()`, а потом сущность умрёт — обработчик на
`Destroyed` дёрнет `Dispose` ещё раз. Требование «`Dispose` идемпотентный»
задокументировано, но footgun. Можно было сделать `AttachLogic` возвращающим
handle, по которому можно и отсоединиться; но для текущего use-case
(fire-and-forget) нынешний вариант норм.
