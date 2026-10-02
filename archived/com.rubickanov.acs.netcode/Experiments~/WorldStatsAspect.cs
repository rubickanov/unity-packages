using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Netcode;

namespace Experiments
{
    /// <summary>
    /// World-scoped aspect. Accessed via <c>World.Require&lt;WorldStatsAspect&gt;()</c>
    /// from any component. Purely local — not replicated — so each peer ticks its
    /// own HUD counters.
    /// </summary>
    public class WorldStatsAspect : IEntityAspect
    {
        public readonly ReactiveProperty<float> ElapsedSeconds = new(0f);
        [Replicated(Authority = AuthorityMode.Server)]
        public readonly ReactiveProperty<int> TotalDamageEvents = new(0);
        public readonly ReactiveProperty<int> EntitiesAlive = new(0);
        public readonly ReactiveProperty<float> TotalHealth = new(0f);
    }
}
