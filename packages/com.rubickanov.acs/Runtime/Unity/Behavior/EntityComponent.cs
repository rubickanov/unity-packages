using System;
using R3;
using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Base class for entity components. Override <see cref="OnSubscribe"/> to subscribe to aspect events;
    /// subscriptions are automatically disposed on <see cref="OnDisable"/>.
    /// Mark aspect fields with <see cref="AspectAttribute"/> for automatic injection in Awake.
    /// <para/>
    /// <see cref="Awake"/> is intentionally non-virtual: overriding it and forgetting
    /// <c>base.Awake()</c> skips <see cref="AspectAttribute"/> injection and leaves every
    /// aspect field as <c>null</c>, producing NREs on first use. To run logic at Awake time,
    /// override <see cref="OnAwake"/> instead — it is invoked after injection, so all
    /// <c>[Aspect]</c> fields are already populated.
    /// </summary>
    public abstract class EntityComponent : MonoBehaviour, IEntityComponent
    {
        private MonoEntity? _context;
        private DisposableBag _disposables;

        protected MonoEntity Context
        {
            get
            {
                if (_context != null) return _context;
                _context = GetComponentInParent<MonoEntity>();
                if (_context == null)
                    throw new InvalidOperationException(
                        $"EntityComponent '{GetType().Name}' on GameObject '{gameObject.name}' requires a MonoEntity in its parent hierarchy.");
                return _context;
            }
        }

        // Non-virtual on purpose: an overriding subclass that forgets `base.Awake()` would
        // silently skip [Aspect] injection. The C# compiler now rejects `override void Awake`
        // on any subclass — use OnAwake for custom init instead. Unity's magic-method
        // reflection still picks this up because access modifier does not matter to the
        // lifecycle dispatcher.
        protected virtual void Awake()
        {
            EntityInjector.Invoke(gameObject);
            AspectInjector.Inject(Context, this);
        }

        /// <summary>
        /// Override to subscribe to aspect events. All subscriptions added to <paramref name="disposables"/>
        /// are automatically disposed when the component is disabled.
        /// </summary>
        protected virtual void OnSubscribe(ref DisposableBag disposables) { }

        // R3's DisposableBag is a struct with an internal "disposed" latch. Once Dispose runs,
        // every subsequent AddTo against the same bag immediately disposes whatever is added —
        // so a component that has been disabled and re-enabled would silently lose all its
        // subscriptions on the second OnEnable. Reset the struct to a virgin state here so the
        // next OnSubscribe builds a fresh subscription set.
        protected virtual void OnEnable()
        {
            _disposables = default;
            OnSubscribe(ref _disposables);
        }

        protected virtual void OnDisable()
        {
            _disposables.Dispose();
        }
    }
}
