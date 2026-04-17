using System.Reflection;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    // Focused coverage for the bit-level routing inside ApplyStateBuffer. The round-trip
    // suite exercises common masks; this suite walks the individual indices that sit on
    // a byte boundary (0, 7, 8, 15, 16, and the upper cap at 255) to pin down any
    // off-by-one in `mask[i >> 3] & (1 << (i & 7))`.
    [TestFixture]
    public class ApplyStateBufferMaskBoundaryTests
    {
        private GameObject _go = null!;
        private EntityReplicator _replicator = null!;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject(nameof(ApplyStateBufferMaskBoundaryTests));
            _go.AddComponent<NetworkObject>();
            _replicator = _go.AddComponent<EntityReplicator>();
            SetPrivate(_replicator, "_tickInterval", 0.05);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static void SetPrivate(object target, string name, object value)
        {
            var f = typeof(EntityReplicator).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"EntityReplicator must have a private field '{name}' — rename detected?");
            f!.SetValue(target, value);
        }

        private static ReplicatedFieldBinding<int> MakeIntBinding(ReactiveProperty<int> reactive)
        {
            return (ReplicatedFieldBinding<int>)
                ReplicatedFieldBindingFactory.Create(reactive, typeof(int), FieldBindingKind.Plain);
        }

        private (ReplicatedFieldBinding[] bindings, AuthorityMode[] authorities, ReactiveProperty<int>[] reactives)
            BuildBindings(int count)
        {
            var reactives = new ReactiveProperty<int>[count];
            var bindings = new ReplicatedFieldBinding[count];
            var authorities = new AuthorityMode[count];
            for (int i = 0; i < count; i++)
            {
                reactives[i] = new ReactiveProperty<int>(-1);
                bindings[i] = MakeIntBinding(reactives[i]);
                authorities[i] = AuthorityMode.Server;
            }
            SetPrivate(_replicator, "_bindings", bindings);
            SetPrivate(_replicator, "_bindingAuthorities", authorities);
            SetPrivate(_replicator, "_maskByteCount", (count + 7) / 8);
            return (bindings, authorities, reactives);
        }

        // Constructs a mask with exactly one bit set at `bitIndex`.
        private static byte[] MaskWithBit(int bitIndex, int maskByteCount)
        {
            var mask = new byte[maskByteCount];
            mask[bitIndex >> 3] |= (byte)(1 << (bitIndex & 7));
            return mask;
        }

        private static unsafe byte[] BuildPayloadForSingleField(int serverTick, byte[] mask, int value)
        {
            var w = new FastBufferWriter(256, Unity.Collections.Allocator.Temp);
            try
            {
                w.WriteValueSafe(serverTick);
                fixed (byte* p = mask) w.WriteBytesSafe(p, mask.Length);
                w.WriteBytesSafe((byte*)&value, sizeof(int));
                return w.ToArray();
            }
            finally { w.Dispose(); }
        }

        // Data source covers every meaningful boundary in the `(i >> 3, 1 << (i & 7))`
        // decomposition: LSB of a byte (0), MSB of a byte (7), LSB of the next byte (8),
        // MSB of byte 1 (15), LSB of byte 2 (16), and the upper cap (255, LSB of byte 31).
        [TestCase(0)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(255)]
        public void ApplyStateBuffer_MaskBitSetAtBoundary_OnlyThatBindingUpdated(int bitIndex)
        {
            int count = bitIndex + 1;
            var (_, _, reactives) = BuildBindings(count);

            int maskByteCount = (count + 7) / 8;
            var mask = MaskWithBit(bitIndex, maskByteCount);
            var payload = BuildPayloadForSingleField(serverTick: 1, mask, value: 4242);

            _replicator.ApplyStateBuffer(payload, StateApplyMode.ApplyAll);

            Assert.AreEqual(4242, reactives[bitIndex].Value,
                $"binding at index {bitIndex} must be updated when its mask bit is set");
            for (int i = 0; i < count; i++)
            {
                if (i == bitIndex) continue;
                Assert.AreEqual(-1, reactives[i].Value,
                    $"binding at index {i} must stay untouched when only bit {bitIndex} is set");
            }
        }
    }
}
