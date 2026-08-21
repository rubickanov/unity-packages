using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Calls <see cref="IEntity.Require{T}"/> for an aspect type only known at runtime.
    /// The single place in the framework that bridges <c>Type</c> → the generic
    /// <c>Require&lt;T&gt;</c>; <see cref="AspectInjector"/> and the persistence package
    /// both route through here instead of each rolling their own reflection path.
    /// <para/>
    /// <b>Why not a compiled delegate.</b> The obvious implementation is
    /// <c>Expression.Lambda&lt;Func&lt;IEntity, object&gt;&gt;(…).Compile()</c>, and that is
    /// what this class replaced. IL2CPP has no runtime IL emitter, so <c>Compile()</c> falls
    /// back to an interpreter there — orders of magnitude more expensive on the cold path and
    /// it drags <c>System.Linq.Expressions</c> into the build, which matters most on the very
    /// platforms that use IL2CPP. The generic-dispatcher pattern below needs no code
    /// generation at all: <see cref="AspectRequirer{T}"/> is compiled ahead of time and the
    /// only runtime work is closing it over the aspect type. Same trade-off
    /// <c>ACS.Netcode</c> already makes in <c>ReplicatedFieldBinding</c>.
    /// <para/>
    /// <b>Cost.</b> Hot path is one dictionary lookup plus one virtual call — a virtual call
    /// and a delegate invocation are the same order of magnitude, so nothing was given up
    /// against the compiled-delegate version. Building an entry is a <c>MakeGenericType</c>
    /// plus an <c>Activator.CreateInstance</c>, both microseconds, against tens or hundreds
    /// of microseconds for an <c>Expression.Compile()</c>.
    /// <para/>
    /// <b>Thread safety:</b> the cache is a <see cref="ConcurrentDictionary{TKey,TValue}"/> so
    /// headless simulations can resolve aspects from several threads without racing on cache
    /// population. Entries are stateless (a <c>Type</c> keyed dispatcher holding no fields),
    /// so a duplicate build under contention is harmless and the cache never needs clearing
    /// between play sessions. Note this says nothing about <see cref="IEntity"/> itself —
    /// <see cref="AspectStore"/> is not thread-safe.
    /// </summary>
    public static class AspectResolver
    {
        private static readonly ConcurrentDictionary<Type, AspectRequirer> Cache = new();

        // Cached so GetOrAdd doesn't allocate a fresh delegate from the method group on every
        // call — C# 10 has no method-group conversion caching.
        private static readonly Func<Type, AspectRequirer> BuildRequirerDelegate = BuildRequirer;

        /// <summary>
        /// Returns the aspect of type <paramref name="aspectType"/> on <paramref name="context"/>,
        /// creating it if it doesn't exist yet — the runtime-typed equivalent of
        /// <c>context.Require&lt;T&gt;()</c>, with the same idempotency.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="aspectType"/> cannot satisfy the <c>where T : class, IEntityAspect, new()</c>
        /// constraint on <see cref="IEntity.Require{T}"/>, or IL2CPP stripped the closed generic.
        /// </exception>
        public static object Require(IEntity context, Type aspectType)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (aspectType == null) throw new ArgumentNullException(nameof(aspectType));

            return Cache.GetOrAdd(aspectType, BuildRequirerDelegate).Require(context);
        }

        private static AspectRequirer BuildRequirer(Type aspectType)
        {
            Validate(aspectType);

            try
            {
                var closed = typeof(AspectRequirer<>).MakeGenericType(aspectType);
                return (AspectRequirer)Activator.CreateInstance(closed, nonPublic: true)!;
            }
            catch (Exception ex) when (ex is NotSupportedException
                                          or MissingMethodException
                                          or TypeLoadException
                                          or TargetInvocationException)
            {
                // Under IL2CPP a stripped generic specialization surfaces as one of these
                // rather than as anything that names the real cause. Translate it into the
                // fix, mirroring ReplicatedFieldBindingFactory.InvokeCtorSafe in ACS.Netcode.
                throw new InvalidOperationException(
                    $"Could not resolve aspect '{aspectType.FullName}'. Most likely IL2CPP stripped the " +
                    $"closed generic — add the aspect type to Assets/link.xml with preserve=\"all\". " +
                    $"Inner: {ex}", ex);
            }
        }

        // Pre-flight the four things `where T : class, IEntityAspect, new()` demands. Without
        // this MakeGenericType throws a bare ArgumentException that names neither the aspect
        // nor which rule it broke, and the caller is left staring at a stack trace inside the
        // injector. Same guard shape as EntityPersistenceExtensions.RequireAspect.
        private static void Validate(Type aspectType)
        {
            if (aspectType.IsValueType)
                throw new InvalidOperationException(
                    $"Aspect type '{aspectType.FullName}' is a struct. Aspects must be reference types — " +
                    $"IEntity.Require<T>() constrains T to `class`.");

            if (aspectType.IsInterface || aspectType.IsAbstract)
                throw new InvalidOperationException(
                    $"Aspect type '{aspectType.FullName}' is abstract or an interface and cannot be instantiated. " +
                    $"Require a concrete aspect class.");

            if (aspectType.ContainsGenericParameters)
                throw new InvalidOperationException(
                    $"Aspect type '{aspectType.FullName}' is an open generic type. " +
                    $"Require a closed constructed type (e.g. MyAspect<int>, not MyAspect<>).");

            if (!typeof(IEntityAspect).IsAssignableFrom(aspectType))
                throw new InvalidOperationException(
                    $"Type '{aspectType.FullName}' does not implement {nameof(IEntityAspect)} and cannot be " +
                    $"used as an aspect.");

            if (aspectType.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException(
                    $"Aspect type '{aspectType.FullName}' has no public parameterless constructor. " +
                    $"Aspects are pure data and must be constructible by the framework — give it a " +
                    $"parameterless constructor or initialize its fields at declaration.");
        }

        /// <summary>
        /// Type-erased handle onto <c>Require&lt;T&gt;</c> for one aspect type. Abstract base
        /// so the cache can hold every closed <see cref="AspectRequirer{T}"/> under one
        /// non-generic type and the call site is a plain virtual dispatch.
        /// </summary>
        private abstract class AspectRequirer
        {
            internal abstract object Require(IEntity context);
        }

        /// <summary>
        /// The generic half: <typeparamref name="T"/> is baked in at construction, so the body
        /// is an ordinary statically-compiled call — no reflection, no emitted IL.
        /// </summary>
        private sealed class AspectRequirer<T> : AspectRequirer
            where T : class, IEntityAspect, new()
        {
            internal override object Require(IEntity context) => context.Require<T>();
        }
    }
}
