using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Rubickanov.Loading;

namespace Rubickanov.UI.Loading
{
    /// <summary>
    /// Loading operation that registers a set of views into the active <see cref="SceneViewScopeService"/> scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <see cref="Add{T}"/> to declare registrations, then pass the instance to a loading pipeline.
    /// The operation opens a new scope via <see cref="SceneViewScopeService.Begin"/> and registers each view in order.
    /// </para>
    /// <para>
    /// <b>Scope ownership:</b> the returned <see cref="ScopedViewRegistration"/> is owned by the service, not by this
    /// operation. <see cref="Execute"/> does not dispose the scope on success OR on exception — partial registrations
    /// remain valid until the service opens another scope (which auto-disposes the previous one) or the service is
    /// disposed. This is intentional: scope lifetime is bound to the scene, not to the loading operation.
    /// </para>
    /// <para>
    /// <b>Single-use:</b> each instance is single-use. Calling <see cref="Execute"/> more than once throws.
    /// </para>
    /// </remarks>
    public sealed class RegisterViewsOperation : ILoadingOperation
    {
        private readonly SceneViewScopeService _scopeService;
        private readonly List<Func<ScopedViewRegistration, UniTask>> _registrations = new();
        private readonly HashSet<Type> _registeredTypes = new();
        private bool _executed;

        /// <summary>Human-readable description surfaced to loading presenters.</summary>
        public string Description { get; }

        /// <param name="scopeService">Service that owns the view scope opened by <see cref="Execute"/>.</param>
        /// <param name="description">Presenter-visible description. Defaults to <c>"Loading UI..."</c>.</param>
        public RegisterViewsOperation(SceneViewScopeService scopeService, string description = "Loading UI...")
        {
            _scopeService = scopeService ?? throw new ArgumentNullException(nameof(scopeService));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        /// <summary>Queues a view type for registration on the given layer.</summary>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> was already added to this operation.</exception>
        public RegisterViewsOperation Add<T>(UILayer layer) where T : class, IView
        {
            if (!_registeredTypes.Add(typeof(T)))
                throw new InvalidOperationException($"View type {typeof(T).Name} already added to this operation.");

            _registrations.Add(scope => scope.Register<T>(layer));
            return this;
        }

        /// <summary>Opens a new scope on the service and registers every queued view in order.</summary>
        /// <exception cref="InvalidOperationException">Thrown when invoked more than once on the same instance.</exception>
        public async UniTask Execute(IProgress<float> progress, CancellationToken ct)
        {
            if (_executed)
                throw new InvalidOperationException("RegisterViewsOperation is single-use; create a new instance for a new scope.");
            _executed = true;

            progress.Report(0f);

            var scope = _scopeService.Begin();

            for (int i = 0; i < _registrations.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _registrations[i](scope);
                progress.Report((float)(i + 1) / _registrations.Count);
            }

            if (_registrations.Count == 0)
                progress.Report(1f);
        }
    }
}
