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
    /// <see cref="Awake"/> is <c>virtual</c> and performs <see cref="AspectAttribute"/> injection.
    /// To run logic at Awake time, override it and call <c>base.Awake()</c> first — forgetting the
    /// base call skips injection and leaves every aspect field <c>null</c>, producing NREs on first
    /// use.
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

        // Virtual so subclasses can add Awake-time init — they MUST call base.Awake() first,
        // otherwise [Aspect] injection is skipped and aspect fields stay null. Unity's
        // magic-method reflection picks this up regardless of access modifier.
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
