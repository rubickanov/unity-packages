# UI Animations Package — Issues & Work Plan

Результаты аудита пакета `com.rubickanov.ui.animations`. Документ отслеживает все найденные проблемы и порядок их исправления. Ломающие изменения публичного API допустимы — делаем правильно.

**Зафиксированные решения по ключевым неопределённостям:**
- **M1 (`package.json dependencies`):** декларируем только `com.rubickanov.*` — третьесторонние (`UniTask`, `LitMotion`) оставляем на совести потребителя, консистентно с `com.rubickanov.ui`, `com.rubickanov.loading`, `com.rubickanov.localization`.
- **M2 (кеширование `ViewAnimations`):** `static readonly` поля на каждую фабричную анимацию — паттерн уже используется в `FadeAnimation.Instance` / `NoneAnimation.Instance`, не требует `Lazy<T>`.

---

## Находки

### Критические (реальные баги)

Нет. Runtime-логика чистая: классы `sealed`, LitMotion/UniTask берут на себя lifecycle тасков, LINQ в Runtime отсутствует. Все проблемы ниже — паттерны аллокации, контракт публичных границ, и отсутствие тестов / декларации зависимостей.

### Мажорные (M)

- **M1. `package.json` не декларирует `com.rubickanov.ui`** — `package.json:1-10`
  Runtime asmdef (`Runtime/UI.Animations.asmdef:4-8`) ссылается на `UI.Runtime` по GUID. При установке пакета через git-URL без локальных `file:`-ссылок UPM не подтянет `com.rubickanov.ui` — у консьюмера сломается компиляция asmdef. Остальные пакеты репо, использующие rubickanov-зависимости, корректно их декларируют (см. `com.rubickanov.localization/package.json:9-11`, `com.rubickanov.acs.netcode/package.json`).
  **Решение:**
  ```json
  {
      "name": "com.rubickanov.ui.animations",
      ...
      "dependencies": {
          "com.rubickanov.ui": "1.0.0"
      }
  }
  ```
  `UniTask` и `LitMotion` не декларируем — остальные пакеты репо их тоже не перечисляют, консьюмер ставит их отдельно.

- **M2. `ViewAnimations.Scale` / `SlideFrom*` / `FadeAndScale` аллоцируют объект на каждый доступ** — `Runtime/ViewAnimations.cs:8-13`
  Экспрешн-боди-свойства возвращают `new ScaleAnimation(0.8f)`, `new SlideAnimation(...)`, `new CompositeAnimation(Fade, Scale)`. Каждый Show view'а вида `ViewAnimations.FadeAndScale.PlayShowAsync(...)` — это **две** аллокации (новый `ScaleAnimation` + новый `CompositeAnimation`). В контрасте с `Fade`/`None`, которые возвращают singleton (`FadeAnimation.Instance`, `NoneAnimation.Instance`). На hot-path view-show/hide это накопительный GC-мусор.
  **Решение:** закешировать в приватные `static readonly` поля, как `FadeAnimation.Instance`:
  ```csharp
  public static class ViewAnimations
  {
      private static readonly ScaleAnimation _scale = new(0.8f);
      private static readonly SlideAnimation _slideFromLeft = new(SlideDirection.Left);
      private static readonly SlideAnimation _slideFromRight = new(SlideDirection.Right);
      private static readonly SlideAnimation _slideFromTop = new(SlideDirection.Top);
      private static readonly SlideAnimation _slideFromBottom = new(SlideDirection.Bottom);
      private static readonly CompositeAnimation _fadeAndScale = new(FadeAnimation.Instance, _scale);

      public static IViewAnimation Default { get; set; } = FadeAnimation.Instance;
      public static IViewAnimation None => NoneAnimation.Instance;
      public static IViewAnimation Fade => FadeAnimation.Instance;
      public static IViewAnimation Scale => _scale;
      public static IViewAnimation SlideFromLeft => _slideFromLeft;
      public static IViewAnimation SlideFromRight => _slideFromRight;
      public static IViewAnimation SlideFromTop => _slideFromTop;
      public static IViewAnimation SlideFromBottom => _slideFromBottom;
      public static IViewAnimation FadeAndScale => _fadeAndScale;

      public static IViewAnimation Combine(params IViewAnimation[] animations)
          => new CompositeAnimation(animations);
  }
  ```

- **M3. Нет валидации аргументов на публичных границах** — `Runtime/CompositeAnimation.cs:9-22, 24-32`, `Runtime/ScaleAnimation.cs:10-13, 15-26, 28-37`, `Runtime/SlideAnimation.cs:19-23, 25-43, 45-61`, `Runtime/FadeAnimation.cs:10-16, 18-23`
  Ни один из `PlayShowAsync` / `PlayHideAsync` не проверяет `target`. `new CompositeAnimation(null)` проходит без исключения, `NullReferenceException` всплывает только на `_animations.Length` — далеко от точки ошибки. Это контрактный баг: публичные границы обязаны валидировать вход.
  **Решение:**
  ```csharp
  public async UniTask PlayShowAsync(IAnimationTarget target, float duration)
  {
      if (target == null) throw new ArgumentNullException(nameof(target));
      // ...
  }

  public CompositeAnimation(params IViewAnimation[] animations)
  {
      _animations = animations ?? throw new ArgumentNullException(nameof(animations));
  }
  ```
  Для `duration` — документировать, что отрицательное значение допустимо (LitMotion схлопывает в instant), либо guard-ить через `ArgumentOutOfRangeException`. Выбрать в момент правки.

- **M4. Отсутствует `Tests/`** — корень пакета
  Нет ни одного теста на ~200 строк runtime-кода. Минимально ценные покрытия:
  - `CompositeAnimation` с нулевым / одним / несколькими дочерними animations (guard поведение после M3).
  - `SlideAnimation.GetOffset` для каждого `SlideDirection` (private метод — проверять через публичный `PlayShowAsync` на фейковом `IAnimationTarget`, фиксирующем значения `TranslateX` / `TranslateY` в начале и конце).
  - `ScaleAnimation` с кастомным `startScale`.
  - Null-валидация (после M3): `Assert.Throws<ArgumentNullException>`.
  **Решение:** создать `Tests/UI.Animations.Tests.asmdef` (гейт `UNITY_INCLUDE_TESTS` + `includePlatforms: [Editor]`), плюс тест-файлы по одному на класс. Фейковый `IAnimationTarget` — простой record-class с publics, фиксирующий значения свойств.

### Минорные (m)

- **m1. `CompositeAnimation` аллоцирует новый `UniTask[]` на каждый Show/Hide** — `Runtime/CompositeAnimation.cs:14-22, 24-32`
  `new UniTask[_animations.Length]` на каждый вызов. Размер массива фиксирован после конструктора — логично переиспользовать поле. `UniTask.WhenAll` требует массив, альтернативы нет.
  **Решение:**
  ```csharp
  public sealed class CompositeAnimation : IViewAnimation
  {
      private readonly IViewAnimation[] _animations;
      private readonly UniTask[] _buffer;

      public CompositeAnimation(params IViewAnimation[] animations)
      {
          _animations = animations ?? throw new ArgumentNullException(nameof(animations));
          _buffer = new UniTask[_animations.Length];
      }

      public async UniTask PlayShowAsync(IAnimationTarget target, float duration)
      {
          if (target == null) throw new ArgumentNullException(nameof(target));
          for (var i = 0; i < _animations.Length; i++)
              _buffer[i] = _animations[i].PlayShowAsync(target, duration);
          await UniTask.WhenAll(_buffer);
      }
      // аналогично для PlayHideAsync
  }
  ```
  **НЮАНС:** если один и тот же `CompositeAnimation` вызывается рекурсивно (Show внутри Show), буфер перепишется. Для текущего UI-фреймворка это исключено (`UIToolkitView` не реентерабельна), но стоит задокументировать.

- **m2. `CompositeAnimation` и `SlideAnimation` без guard-ов на edge inputs** — `Runtime/CompositeAnimation.cs:9-12`, `Runtime/SlideAnimation.cs:19-23`
  `new CompositeAnimation()` (без аргументов) → молчаливый no-op (массив пустой, `WhenAll` мгновенно завершается). `new SlideAnimation(dir, -100f)` → слайд в противоположную сторону. Ни то, ни другое не кидает.
  **Решение:**
  ```csharp
  public CompositeAnimation(params IViewAnimation[] animations)
  {
      if (animations == null) throw new ArgumentNullException(nameof(animations));
      if (animations.Length == 0)
          throw new ArgumentException("At least one animation required", nameof(animations));
      _animations = animations;
      _buffer = new UniTask[animations.Length];
  }

  public SlideAnimation(SlideDirection direction, float offset = 100f)
  {
      if (offset < 0f)
          throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be non-negative");
      _direction = direction;
      _offset = offset;
  }
  ```

- **m3. README не предупреждает о необходимости `await` при ручном вызове** — `README.md:11-26, 43-52`
  Примеры используют `=> ViewAnimations.FadeAndScale.PlayShowAsync(...)` (expression-body возвращает UniTask — автоматически awaited в `OnShowAsync`). Но в "Custom Composites" / `Per-Element Animations` пользователь может написать `ViewAnimations.Fade.PlayShowAsync(...)` как отдельный вызов без `await` — анимация тихо не запустится. Никаких предупреждений в README нет.
  **Решение:** в Quick Start добавить одну строку:
  > **Важно:** `PlayShowAsync` / `PlayHideAsync` возвращают `UniTask`. Если вызываете их вручную (не возвращая из `OnShowAsync` / `OnHideAsync`), **обязательно `await`** — иначе задача никогда не запустится.

- **m4. README-таблица не отражает singleton-семантику фабрики `ViewAnimations`** — `README.md:30-41`
  После фикса M2 `ViewAnimations.Scale` / `SlideFrom*` / `FadeAndScale` станут singletons. Стоит одной строкой упомянуть в "Built-in Animations": *все перечисленные свойства возвращают один и тот же экземпляр на каждый доступ — можно безопасно использовать в hot-path*.
  **Решение:** добавить одну строку перед или после таблицы:
  > Все свойства `ViewAnimations.*` (кроме `Combine(...)`) возвращают кешированные singleton-инстансы — аллокаций на каждый доступ нет.

- **m5. `SlideDirection` и публичные API без XML-doc** — `Runtime/SlideAnimation.cs:6-12`, `Runtime/ViewAnimations.cs:3-18`, `Runtime/CompositeAnimation.cs:5`, `Runtime/ScaleAnimation.cs:6-13`
  Ни один публичный тип/метод не имеет `/// <summary>`. Потребитель видит только API-подсказку IDE без описания. Extension-пакет маленький — добавить одну-две строки на каждый публичный элемент.
  **Решение:** XML-doc на `SlideDirection`, `ViewAnimations` (класс + каждое свойство фабрики), конструкторы анимаций (особенно параметр `startScale` / `offset` с границами). 
