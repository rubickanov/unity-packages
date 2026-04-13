using R3;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// Ticks <see cref="WorldStatsAspect"/>: advances elapsed time, aggregates live
    /// entity totals, and counts DamageDealt events across all <see cref="ExperimentAspect"/>s.
    /// </summary>
    public class WorldStatsLogic : EntityComponent
    {
        [Aspect] private readonly WorldStatsAspect _stats = default!;
        private DisposableBag _damageSubs;
        private int _subscribedCount;

        private void Update()
        {
            _stats.ElapsedSeconds.Value += Time.deltaTime;

            // Single pass: refresh live aggregates in the aspect and wire DamageDealt subs.
            // World.Query is a live type-bucket lookup — destroyed entities drop out automatically.
            float totalHealth = 0f;
            int count = 0;
            foreach (var aspect in World.Query<ExperimentAspect>())
            {
                totalHealth += aspect.Health.Value;
                count++;
            }

            _stats.EntitiesAlive.Value = count;
            _stats.TotalHealth.Value = totalHealth;

            if (count != _subscribedCount)
            {
                _damageSubs.Clear();
                foreach (var aspect in World.Query<ExperimentAspect>())
                {
                    aspect.DamageDealt
                        .Subscribe(_ => _stats.TotalDamageEvents.Value++)
                        .AddTo(ref _damageSubs);
                }

                _subscribedCount = count;
            }
        }

        private void OnDestroy() => _damageSubs.Dispose();
    }
}