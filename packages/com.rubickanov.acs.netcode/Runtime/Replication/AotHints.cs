using ObservableCollections;
using R3;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Explicit AOT generic instantiation hints for IL2CPP.
    /// This class is never called at runtime — it exists solely to ensure IL2CPP
    /// generates native code for common generic binding specializations.
    /// For custom unmanaged structs, users must add a link.xml entry.
    /// <para>
    /// Prediction pipeline (<see cref="PredictionManager{TInput}"/>) is instantiated
    /// via reflection on the user's <see cref="IInputCommand"/> struct, which IL2CPP
    /// cannot discover statically. Games using IL2CPP must preserve their concrete
    /// <c>PredictionManager&lt;MyInput&gt;</c> and <c>ISimulate&lt;MyInput&gt;</c>
    /// specializations through their own link.xml.
    /// </para>
    /// </summary>
    [Preserve]
    internal static class AotHints
    {
        [Preserve]
        private static void UsedOnlyForAOTCodeGeneration()
        {
            // ReplicatedFieldBinding<T>
            new ReplicatedFieldBinding<int>(default!, default!);
            new ReplicatedFieldBinding<float>(default!, default!);
            new ReplicatedFieldBinding<bool>(default!, default!);
            new ReplicatedFieldBinding<Vector2>(default!, default!);
            new ReplicatedFieldBinding<Vector3>(default!, default!);
            new ReplicatedFieldBinding<Vector4>(default!, default!);
            new ReplicatedFieldBinding<Quaternion>(default!, default!);
            new ReplicatedFieldBinding<Color>(default!, default!);
            new ReplicatedFieldBinding<EntityRef>(default!, default!);

            // InterpolatedFieldBinding<T> — types with registered lerpers
            new InterpolatedFieldBinding<float>(default!, default!, default!);
            new InterpolatedFieldBinding<double>(default!, default!, default!);
            new InterpolatedFieldBinding<Vector2>(default!, default!, default!);
            new InterpolatedFieldBinding<Vector3>(default!, default!, default!);
            new InterpolatedFieldBinding<Vector4>(default!, default!, default!);
            new InterpolatedFieldBinding<Quaternion>(default!, default!, default!);
            new InterpolatedFieldBinding<Color>(default!, default!, default!);

            // AuthorityRenderBinding<T> — types with registered lerpers
            new AuthorityRenderBinding<float>(default!, default!, default, default!);
            new AuthorityRenderBinding<double>(default!, default!, default, default!);
            new AuthorityRenderBinding<Vector2>(default!, default!, default, default!);
            new AuthorityRenderBinding<Vector3>(default!, default!, default, default!);
            new AuthorityRenderBinding<Vector4>(default!, default!, default, default!);
            new AuthorityRenderBinding<Quaternion>(default!, default!, default, default!);
            new AuthorityRenderBinding<Color>(default!, default!, default, default!);

            // RawCodec<T> — fallback codec for QuantizationMode.None on every supported T.
            // Constructed by CodecRegistry.GetOrCreateRaw via reflection; IL2CPP needs the
            // closed generic ctor preserved.
            new RawCodec<int>();
            new RawCodec<float>();
            new RawCodec<double>();
            new RawCodec<bool>();
            new RawCodec<Vector2>();
            new RawCodec<Vector3>();
            new RawCodec<Vector4>();
            new RawCodec<Quaternion>();
            new RawCodec<Color>();

            // Quantizing codecs — registered as singletons in CodecRegistry, but listed
            // here so [Preserve] propagates and the types are kept by IL2CPP regardless of
            // discovery path.
            _ = new FloatHalfCodec();
            _ = new Vector2HalfCodec();
            _ = new Vector3HalfCodec();
            _ = new Vector4HalfCodec();
            _ = new QuaternionSmallestThreeCodec();

            // ReplicatedEventBinding<T>
            new ReplicatedEventBinding<int>(default!, default, default);
            new ReplicatedEventBinding<float>(default!, default, default);
            new ReplicatedEventBinding<bool>(default!, default, default);

            // ObservableListBinding<T> — Phase 1 collection replication. Same IL2CPP
            // concern as ReplicatedFieldBinding<T>: closed generic ctors are produced
            // reflectively by ReplicatedFieldBindingFactory.BuildObservableListFactory,
            // so the common specialisations must be preserved here for IL2CPP builds.
            new ObservableListBinding<int>(default!, default!);
            new ObservableListBinding<float>(default!, default!);
            new ObservableListBinding<bool>(default!, default!);
            new ObservableListBinding<Vector2>(default!, default!);
            new ObservableListBinding<Vector3>(default!, default!);
            new ObservableListBinding<EntityRef>(default!, default!);

            // ObservableDictionaryBinding<K,V> — Phase 2. Primary use case is
            // <string, float> (CooldownsAspect-style cooldown maps). <string, int> kept
            // as the common int-valued counterpart. Other closed generics need a
            // user-supplied link.xml entry — the factory builds them reflectively.
            new ObservableDictionaryBinding<string, float>(default!, default!, default!);
            new ObservableDictionaryBinding<string, int>(default!, default!, default!);

            // Preserve closed generics of the dictionary key codec family for IL2CPP:
            // StringKeyCodec is a singleton used directly by the factory; UnmanagedKeyCodec<K>
            // is built reflectively for unmanaged-keyed dictionaries. <int> covers the
            // common case; other unmanaged keys need user link.xml entries.
            _ = new StringKeyCodec();
            _ = new UnmanagedKeyCodec<int>(default!);

            // ObservableHashSetBinding<T> — Phase 3. Element rules match ObservableList<T>
            // (unmanaged OR EntityRef). Preserved defaults cover common discrete-value use
            // cases (tag-enum ids, EntityRef sets); other closed generics need a user
            // link.xml entry.
            new ObservableHashSetBinding<int>(default!, default!);
            new ObservableHashSetBinding<EntityRef>(default!, default!);

            // ObservableRingBufferBinding<T> — Phase 3. Primary use cases are damage
            // logs / event histories (int / float) and positional trails (Vector3).
            new ObservableRingBufferBinding<int>(default!, default!);
            new ObservableRingBufferBinding<float>(default!, default!);
            new ObservableRingBufferBinding<Vector3>(default!, default!);

            // ReactivePropertyExtensions.Smooth<T> — ensure IL2CPP generates the
            // InterpolationRegistry.TryGetInterpolatedValue<T> specialization.
            ReactivePropertyExtensions.Smooth(default(ReactiveProperty<float>)!);
            ReactivePropertyExtensions.Smooth(default(ReactiveProperty<double>)!);
            ReactivePropertyExtensions.Smooth(default(ReactiveProperty<Vector2>)!);
            ReactivePropertyExtensions.Smooth(default(ReactiveProperty<Vector3>)!);
            ReactivePropertyExtensions.Smooth(default(ReactiveProperty<Vector4>)!);
            ReactivePropertyExtensions.Smooth(default(ReactiveProperty<Quaternion>)!);
            ReactivePropertyExtensions.Smooth(default(ReactiveProperty<Color>)!);
        }
    }
}
