using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Binary test: 1 if clear line of sight from querier to item, 0 or filtered if blocked.
    /// </summary>
    [Serializable]
    public class LineOfSightTest : EQSTest
    {
        [SerializeField] private float _eyeHeight = 1.2f;
        [SerializeField] private float _targetHeight;
        [SerializeField] private LayerMask _obstacleMask = ~0;
        [SerializeField] private bool _filterOnFail = true;

        public override bool PreferBatch => true;
        public override int BatchChunkSize => 32;

        public override float Score(EQSQueryContext context, in EQSItem item)
        {
            Vector3 origin = context.QuerierPosition + Vector3.up * _eyeHeight;
            Vector3 target = item.Position + Vector3.up * _targetHeight;
            Vector3 dir = target - origin;
            float dist = dir.magnitude;

            if (dist < 0.01f) return 1f;

            bool blocked = Physics.Raycast(origin, dir.normalized, dist, _obstacleMask, QueryTriggerInteraction.Ignore);
            if (blocked) return _filterOnFail ? -1f : 0f;
            return 1f;
        }

        public override void ScoreBatch(
            EQSQueryContext context, IReadOnlyList<EQSItem> items,
            bool[] alive, float[] rawScores, int startIndex, int endExclusive)
        {
            // Count live items in the range; degenerate-distance items are scored inline below.
            int liveCount = 0;
            for (int i = startIndex; i < endExclusive; i++)
                if (alive[i]) liveCount++;

            if (liveCount == 0) return;

            // NativeArrays are not declared with `using var` because `using` locals are readonly,
            // which blocks NativeArray's indexer setter. Explicit try/finally keeps disposal safe
            // under exceptions.
            var commands = new NativeArray<RaycastCommand>(liveCount, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(liveCount, Allocator.TempJob);
            var liveIndices = new NativeArray<int>(liveCount, Allocator.TempJob);
            try
            {
                var queryParams = new QueryParameters(
                    layerMask: _obstacleMask.value,
                    hitMultipleFaces: false,
                    hitTriggers: QueryTriggerInteraction.Ignore,
                    hitBackfaces: false);

                Vector3 origin = context.QuerierPosition + Vector3.up * _eyeHeight;
                int commandCount = 0;

                for (int i = startIndex; i < endExclusive; i++)
                {
                    if (!alive[i]) continue;

                    Vector3 target = items[i].Position + Vector3.up * _targetHeight;
                    Vector3 dir = target - origin;
                    float dist = dir.magnitude;

                    if (dist < 0.01f)
                    {
                        // Degenerate — don't raycast, matches the Score() path.
                        rawScores[i] = 1f;
                        continue;
                    }

                    commands[commandCount] = new RaycastCommand(origin, dir / dist, queryParams, dist);
                    liveIndices[commandCount] = i;
                    commandCount++;
                }

                if (commandCount == 0) return;

                // ScheduleBatch walks the entire NativeArray; unused tail slots are zero-initialised
                // RaycastCommands with zero distance → guaranteed no-hit, harmless and ignored below.
                RaycastCommand.ScheduleBatch(commands, hits, minCommandsPerJob: 16).Complete();

                for (int j = 0; j < commandCount; j++)
                {
                    int idx = liveIndices[j];
                    bool blocked = hits[j].collider != null;
                    rawScores[idx] = blocked ? (_filterOnFail ? -1f : 0f) : 1f;
                }
            }
            finally
            {
                commands.Dispose();
                hits.Dispose();
                liveIndices.Dispose();
            }
        }
    }
}
