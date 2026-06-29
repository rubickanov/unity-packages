namespace Rubickanov.Motor
{
    /// <summary>
    /// Optional interface for modules with persistent internal state
    /// that must be saved/restored for prediction and reconciliation.
    /// Modules without meaningful state (e.g. SprintModule) do not need this.
    /// </summary>
    public interface IStatefulModule
    {
        void SaveState(ref ModuleStateWriter writer);
        void RestoreState(ref ModuleStateReader reader);
    }
}
