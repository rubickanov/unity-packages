using Unity.Collections;
using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    // Test-only convenience for the byte[]-based state payload pattern that
    // existing fixtures use. Production EntityReplicator only carries the
    // FastBufferReader overload (out serverTick); tests that don't need the
    // tick value go through this wrapper.
    internal static class EntityReplicatorTestExtensions
    {
        internal static void ApplyStateBuffer(
            this EntityReplicator replicator, byte[] payload, StateApplyMode mode)
        {
            var reader = new FastBufferReader(payload, Allocator.Temp);
            try
            {
                replicator.ApplyStateBuffer(reader, mode, out _);
            }
            finally
            {
                reader.Dispose();
            }
        }
    }
}
